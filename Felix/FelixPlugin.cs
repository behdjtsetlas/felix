using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Felix.Models;
using Felix.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Character = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace Felix;

public sealed class FelixPlugin : IDalamudPlugin
{
    private const string CommandName = "/felix";
    private unsafe delegate void ShowMiniTalkPlayerDelegate(
        RaptureLogModule* raptureLogModule,
        ushort logKindId,
        Utf8String* sender,
        Utf8String* message,
        ushort worldId,
        bool isLocalPlayer);

    private static readonly string[] TribalKeywords =
    [
        "tribe",
        "tribal",
        "reputation",
        "recognized",
        "friendly",
        "trusted",
        "respected",
        "honored",
        "sworn",
        "bloodsworn",
        "allied",
        "pelupelu",
        "omicron",
        "loporrit",
        "arkasodara",
        "pixie",
        "qitari",
        "dwarf",
        "namazu",
        "kojin",
        "ananta",
        "vath",
        "vanu",
        "moogle",
        "sylph",
        "amalj",
        "kobold",
        "sahagin",
    ];

    private static readonly string[] HuntKeywords =
    [
        "hunt",
        "mark bill",
        "elite mark",
        "clan mark",
        "daily mark",
        "mob hunt",
        "bounty",
    ];

    private static readonly string[] GcClassNames =
    [
        "Carpenter",
        "Blacksmith",
        "Armorer",
        "Goldsmith",
        "Leatherworker",
        "Weaver",
        "Alchemist",
        "Culinarian",
        "Miner",
        "Botanist",
        "Fisher",
    ];

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IDutyState dutyState;
    private readonly IChatGui chatGui;
    private readonly IGameGui gameGui;
    private readonly IGameInteropProvider gameInteropProvider;
    private readonly IObjectTable objectTable;
    private readonly IPartyList partyList;
    private readonly ITargetManager targetManager;
    private readonly IPluginLog pluginLog;
    private readonly IDataManager dataManager;
    private readonly IClientState clientState;
    private readonly Configuration configuration;
    private readonly PluginUi pluginUi;
    private readonly FelixApiClient apiClient;
    private readonly SnapshotBuilder snapshotBuilder;
    private readonly Timer syncTimer;
    private readonly SemaphoreSlim syncGate = new(1, 1);
    private readonly List<string> recentFreeCompanyDiscovery = [];
    private readonly List<string> recentDailyDiscovery = [];
    private readonly List<string> recentNativeBubbleDiscovery = [];
    private readonly List<string> recentWeeklyDiscovery = [];
    private readonly List<RoleplayBubbleMessage> recentRoleplayBubbleMessages = [];
    private readonly object freeCompanyDiscoveryGate = new();
    public readonly record struct FocusTargetMapMarker(Vector2 ScreenPosition, float Radius);
    public readonly record struct MapAddonOverlayMarker(Vector2 ScreenPosition, float Radius, Vector4 Color, int Priority);
    private readonly record struct MapAddonTrackedTarget(ulong ObjectId, Vector3 Position, float HitboxRadius, Vector4 Color, int Priority, string SourceLabel);
    private readonly object dailyDiscoveryGate = new();
    private readonly object nativeBubbleDiscoveryGate = new();
    private readonly object weeklyDiscoveryGate = new();
    private readonly object roleplayBubbleGate = new();
    private readonly object rpSceneGate = new();
    private readonly List<RpScenePendingLine> rpScenePendingLines = [];
    private readonly SemaphoreSlim rpSceneFlushSemaphore = new(1, 1);
    private readonly Timer rpSceneAutoFlushTimer;
    private string rpSceneFlushStatus = string.Empty;
    private bool rpSceneSessionFirstLineNotified;
    private int rpSceneConsecutiveUploadFailures;
    private readonly IPlayerState playerState;
    private readonly Dictionary<string, string> nativeBubbleEntryTextSnapshots = [];
    private readonly Dictionary<string, DateTimeOffset> nativeBubbleEntryTextUpdatedAt = [];
    private readonly string pluginVersion;
    private Hook<ShowMiniTalkPlayerDelegate>? showMiniTalkPlayerHook;
    private PendingNativeSpeechBubbleResize? pendingNativeSpeechBubbleResize;
    private string lastNativeBubbleDiscoverySignature = string.Empty;
    private string lastMapFocusMarkerDiscoverySignature = string.Empty;
    private ulong lastNativeFocusTargetObjectId;
    private Vector3 lastNativeFocusTargetPosition;
    private DateTimeOffset lastNativeFocusTargetMarkerRefreshAt = DateTimeOffset.MinValue;
    private bool nativeFocusTargetMarkerInjected;

    private sealed class RpScenePendingLine
    {
        public required string LineId { get; init; }
        public required string CreatedAtIso { get; init; }
        public required string ChannelLabel { get; init; }
        public required string SpeakerName { get; init; }
        public required string Text { get; init; }
        public bool IsOwnMessage { get; init; }
        public required string SpeakerContentId { get; init; }
    }

    private sealed class PendingNativeSpeechBubbleResize
    {
        public DateTimeOffset CreatedAt { get; init; }
        public string MessageText { get; init; } = string.Empty;
        public string SenderText { get; init; } = string.Empty;
        public bool IsLocalPlayer { get; init; }
        public int MessageLength { get; init; }
        public int DesiredWidth { get; init; }
        public int DesiredHeight { get; init; }
        public int DesiredTextWidth { get; init; }
        public int AppliedFrames { get; set; }
        public DateTimeOffset LastLoggedAt { get; set; }
        public DateTimeOffset LastStatusLoggedAt { get; set; }
        public bool BaselineCaptured { get; set; }
        public Dictionary<string, string> BaselineTexts { get; } = [];
    }

    public FelixPlugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        IAddonLifecycle addonLifecycle,
        IDutyState dutyState,
        IChatGui chatGui,
        IGameGui gameGui,
        IGameInteropProvider gameInteropProvider,
        IPluginLog pluginLog,
        IDataManager dataManager,
        IUnlockState unlockState,
        IClientState clientState,
        IPlayerState playerState,
        IObjectTable objectTable,
        IPartyList partyList,
        ITargetManager targetManager)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.framework = framework;
        this.addonLifecycle = addonLifecycle;
        this.dutyState = dutyState;
        this.chatGui = chatGui;
        this.gameGui = gameGui;
        this.gameInteropProvider = gameInteropProvider;
        this.objectTable = objectTable;
        this.partyList = partyList;
        this.targetManager = targetManager;
        this.pluginLog = pluginLog;
        this.dataManager = dataManager;
        this.clientState = clientState;
        this.playerState = playerState;

        this.configuration = this.pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.configuration.Initialize(this.pluginInterface);

        this.pluginVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0";

        this.apiClient = new FelixApiClient(this.pluginLog);
        this.snapshotBuilder = new SnapshotBuilder(dataManager, unlockState, clientState, playerState, objectTable, partyList);
        this.pluginUi = new PluginUi(this);
        this.syncTimer = new Timer(this.OnSyncTimerTick, null, Timeout.Infinite, Timeout.Infinite);
        this.rpSceneAutoFlushTimer = new Timer(this.OnRpSceneAutoFlushTick, null, Timeout.Infinite, Timeout.Infinite);

        this.commandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open Felix. Args: rp | record | pause | sync | help",
        });

        this.pluginInterface.UiBuilder.Draw += this.pluginUi.Draw;
        this.pluginInterface.UiBuilder.OpenMainUi += this.OpenUi;
        this.pluginInterface.UiBuilder.OpenConfigUi += this.OpenUi;
        this.addonLifecycle.RegisterListener(AddonEvent.PostSetup, this.OnAddonLifecycleEvent);
        this.addonLifecycle.RegisterListener(AddonEvent.PostRefresh, this.OnAddonLifecycleEvent);
        this.InitializeNativeSpeechBubbleHook();
        this.framework.Update += this.OnFrameworkUpdate;
        this.dutyState.DutyCompleted += this.OnDutyCompleted;
        this.chatGui.ChatMessage += this.OnChatMessage;
        this.clientState.Logout += this.OnClientLogout;

        this.UpdateSyncTimer();
        this.UpdateRpSceneAutoFlushTimer();
        this.pluginLog.Information("Felix initialized");
    }

    public string Name => "Felix";

    public Configuration Configuration => this.configuration;

    public void Dispose()
    {
        this.clientState.Logout -= this.OnClientLogout;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            this.FlushRpSceneLinesAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            this.pluginLog.Warning(ex, "Felix RP scene flush during dispose skipped");
        }

        this.RestoreNativeFocusTargetMarkers();
        this.syncTimer.Dispose();
        this.rpSceneAutoFlushTimer.Dispose();
        this.rpSceneFlushSemaphore.Dispose();
        this.syncGate.Dispose();
        this.commandManager.RemoveHandler(CommandName);
        this.pluginInterface.UiBuilder.Draw -= this.pluginUi.Draw;
        this.pluginInterface.UiBuilder.OpenMainUi -= this.OpenUi;
        this.pluginInterface.UiBuilder.OpenConfigUi -= this.OpenUi;
        this.addonLifecycle.UnregisterListener(this.OnAddonLifecycleEvent);
        this.showMiniTalkPlayerHook?.Dispose();
        this.framework.Update -= this.OnFrameworkUpdate;
        this.dutyState.DutyCompleted -= this.OnDutyCompleted;
        this.chatGui.ChatMessage -= this.OnChatMessage;
        this.pluginUi.Dispose();
    }

    public void SaveConfiguration()
    {
        this.configuration.Save();
        this.UpdateSyncTimer();
        this.UpdateRpSceneAutoFlushTimer();
        this.pluginLog.Information("Felix configuration saved");
    }

    public IReadOnlyList<string> GetRecentFreeCompanyDiscovery()
    {
        lock (this.freeCompanyDiscoveryGate)
        {
            return this.recentFreeCompanyDiscovery.ToList();
        }
    }

    public IReadOnlyList<string> GetRecentWeeklyDiscovery()
    {
        lock (this.weeklyDiscoveryGate)
        {
            return this.recentWeeklyDiscovery.ToList();
        }
    }

    public IReadOnlyList<string> GetRecentDailyDiscovery()
    {
        lock (this.dailyDiscoveryGate)
        {
            return this.recentDailyDiscovery.ToList();
        }
    }

    public IReadOnlyList<string> GetRecentNativeBubbleDiscovery()
    {
        lock (this.nativeBubbleDiscoveryGate)
        {
            return this.recentNativeBubbleDiscovery.ToList();
        }
    }

    public IReadOnlyList<RoleplayBubbleMessage> GetRoleplayBubbleMessages()
    {
        lock (this.roleplayBubbleGate)
        {
            this.TrimExpiredRoleplayBubbleMessagesLocked();
            var maxVisible = Math.Clamp(this.configuration.RoleplayBubbleMaxVisible, 1, 8);
            return this.recentRoleplayBubbleMessages
                .OrderByDescending((entry) => entry.CreatedAt)
                .Take(maxVisible)
                .ToList();
        }
    }

    public IReadOnlyList<RoleplayBubbleMessage> GetRoleplayBubbleMessagesForSnapshot(int maxCount)
    {
        lock (this.roleplayBubbleGate)
        {
            this.TrimExpiredRoleplayBubbleMessagesLocked();
            var max = Math.Clamp(maxCount, 1, 48);
            return this.recentRoleplayBubbleMessages
                .OrderByDescending((entry) => entry.CreatedAt)
                .Take(max)
                .ToList();
        }
    }

    public RoleplayBubbleMessage? GetLatestRoleplayBubbleMessage()
    {
        lock (this.roleplayBubbleGate)
        {
            this.TrimExpiredRoleplayBubbleMessagesLocked();
            return this.recentRoleplayBubbleMessages
                .OrderByDescending((entry) => entry.CreatedAt)
                .FirstOrDefault();
        }
    }

    public RoleplayBubbleMessage? GetLatestRoleplayOverheadBubbleMessage()
    {
        lock (this.roleplayBubbleGate)
        {
            this.TrimExpiredRoleplayBubbleMessagesLocked();
            var lifetime = TimeSpan.FromSeconds(Math.Clamp(this.configuration.RoleplayOverheadBubbleLifetimeSeconds, 3, 30));
            var cutoff = DateTimeOffset.UtcNow - lifetime;
            return this.recentRoleplayBubbleMessages
                .Where((entry) => entry.CreatedAt >= cutoff)
                .OrderByDescending((entry) => entry.CreatedAt)
                .FirstOrDefault();
        }
    }

    public IReadOnlyList<RoleplayBubbleMessage> GetRoleplayOverheadBubbleMessages()
    {
        lock (this.roleplayBubbleGate)
        {
            this.TrimExpiredRoleplayBubbleMessagesLocked();
            var lifetime = TimeSpan.FromSeconds(Math.Clamp(this.configuration.RoleplayOverheadBubbleLifetimeSeconds, 3, 30));
            var cutoff = DateTimeOffset.UtcNow - lifetime;
            return this.recentRoleplayBubbleMessages
                .Where((entry) => entry.CreatedAt >= cutoff)
                .GroupBy((entry) => $"{entry.SpeakerName}|{entry.IsOwnMessage}")
                .Select((group) => group.OrderByDescending((entry) => entry.CreatedAt).First())
                .OrderByDescending((entry) => entry.CreatedAt)
                .Take(Math.Clamp(this.configuration.RoleplayBubbleMaxVisible, 1, 8))
                .ToList();
        }
    }

    public bool TryGetFocusTargetAreaMapMarker(out FocusTargetMapMarker marker)
    {
        marker = default;
        return false;
    }

    public bool TryGetFocusTargetMiniMapMarker(out FocusTargetMapMarker marker)
    {
        marker = default;
        return false;
    }

    public IReadOnlyList<MapAddonOverlayMarker> GetAreaMapOverlayMarkers()
    {
        if (!this.TryGetMapAddonTrackedTargets(out var localPlayer, out var trackedTargets))
        {
            return [];
        }

        var markers = new List<MapAddonOverlayMarker>(trackedTargets.Count);
        ulong focusedObjectId = 0;
        var usedNativeFocusPartyAreaMarker = false;
        if (this.configuration.MapAddonFocusTargetColorEnabled)
        {
            var focusTarget = this.targetManager.FocusTarget;
            if (focusTarget is not null && focusTarget.IsValid())
            {
                focusedObjectId = focusTarget.GameObjectId;
            }

            if (this.TryGetFocusedPartyAreaMapOverlayMarker(localPlayer, out var focusedPartyMarker))
            {
                markers.Add(focusedPartyMarker);
                usedNativeFocusPartyAreaMarker = true;
            }
        }

        foreach (var trackedTarget in trackedTargets.OrderBy((entry) => entry.Priority))
        {
            // Party focus uses the game's map marker when available; otherwise fall through and
            // project like friends/FC when the native party marker path does not apply.
            if (focusedObjectId != 0
                && trackedTarget.ObjectId == focusedObjectId
                && usedNativeFocusPartyAreaMarker)
            {
                continue;
            }

            if (!this.TryProjectActorToAreaMap(localPlayer.Position, trackedTarget.Position, trackedTarget.HitboxRadius, out var marker))
            {
                continue;
            }

            markers.Add(new MapAddonOverlayMarker(marker.ScreenPosition, marker.Radius, trackedTarget.Color, trackedTarget.Priority));
        }

        return markers;
    }

    public IReadOnlyList<MapAddonOverlayMarker> GetMiniMapOverlayMarkers()
    {
        if (!this.TryGetMapAddonTrackedTargets(out var localPlayer, out var trackedTargets))
        {
            return [];
        }

        var markers = new List<MapAddonOverlayMarker>(trackedTargets.Count);
        foreach (var trackedTarget in trackedTargets.OrderBy((entry) => entry.Priority))
        {
            if (!this.TryProjectActorToMiniMap(localPlayer, trackedTarget.Position, trackedTarget.HitboxRadius, out var marker))
            {
                continue;
            }

            markers.Add(new MapAddonOverlayMarker(marker.ScreenPosition, marker.Radius, trackedTarget.Color, trackedTarget.Priority));
        }

        return markers;
    }

    public bool TryGetRoleplayBubbleScreenPosition(RoleplayBubbleMessage bubble, out Vector2 screenPosition)
    {
        screenPosition = default;

        var anchor = this.ResolveRoleplayBubbleAnchor(bubble);
        var yOffset = Math.Clamp(this.configuration.RoleplayOverheadBubbleYOffset, 0.1f, 0.9f);

        if (anchor is not null)
        {
            if (this.TryProjectRoleplayBubblePosition(anchor, bubble.IsOwnMessage, yOffset, out screenPosition))
            {
                return true;
            }
        }

        if (bubble.SpeakerWorldPosition.HasValue
            && this.TryProjectRoleplayBubblePosition(
                bubble.SpeakerWorldPosition.Value,
                Math.Max(0.5f, bubble.SpeakerHitboxRadius),
                MathF.Max(1.2f, Math.Max(0.5f, bubble.SpeakerHitboxRadius) * 2f),
                bubble.IsOwnMessage,
                yOffset,
                out screenPosition))
        {
            return true;
        }

        return false;
    }

    public void ClearFreeCompanyDiscovery()
    {
        lock (this.freeCompanyDiscoveryGate)
        {
            this.recentFreeCompanyDiscovery.Clear();
        }
        this.configuration.LastSyncStatus = "FC discovery log cleared.";
        this.SaveConfiguration();
    }

    public void ClearWeeklyDiscovery()
    {
        lock (this.weeklyDiscoveryGate)
        {
            this.recentWeeklyDiscovery.Clear();
        }
        this.configuration.LastSyncStatus = "Weekly discovery log cleared.";
        this.SaveConfiguration();
    }

    public void ClearDailyDiscovery()
    {
        lock (this.dailyDiscoveryGate)
        {
            this.recentDailyDiscovery.Clear();
        }

        this.configuration.LastSyncStatus = "Daily discovery log cleared.";
        this.SaveConfiguration();
    }

    public void ClearRoleplayBubbleMessages()
    {
        lock (this.roleplayBubbleGate)
        {
            this.recentRoleplayBubbleMessages.Clear();
        }
    }

    public void ClearNativeBubbleDiscovery()
    {
        lock (this.nativeBubbleDiscoveryGate)
        {
            this.recentNativeBubbleDiscovery.Clear();
            this.lastNativeBubbleDiscoverySignature = string.Empty;
        }

        this.configuration.LastSyncStatus = "Native bubble discovery log cleared.";
        this.SaveConfiguration();
    }

    public void InspectFreeCompanyFields()
    {
        this.framework.RunOnTick(() =>
        {
            try
            {
                var entries = this.snapshotBuilder.InspectFreeCompanyFields();
                if (entries.Count == 0)
                {
                    this.AppendFreeCompanyDiscovery("No FC-related local player fields were found.");
                    this.pluginLog.Information("Felix FC discovery: no matching fields found.");
                }
                else
                {
                    foreach (var entry in entries)
                    {
                        this.AppendFreeCompanyDiscovery(entry);
                        this.pluginLog.Information("Felix FC discovery: {Entry}", entry);
                    }
                }

                this.configuration.LastSyncStatus = $"FC discovery inspected ({entries.Count} entries).";
                this.SaveConfiguration();
            }
            catch (Exception ex)
            {
                this.pluginLog.Error(ex, "Felix FC discovery failed");
                this.configuration.LastSyncStatus = $"FC discovery failed: {ex.Message}";
                this.SaveConfiguration();
            }
        });
    }

    public void ClearDeviceToken()
    {
        this.configuration.DeviceToken = string.Empty;
        this.configuration.SelectedGuildId = string.Empty;
        this.configuration.LastSyncStatus = "Device token cleared";
        this.SaveConfiguration();
    }

    public async Task PairAsync()
    {
        if (string.IsNullOrWhiteSpace(this.configuration.PairingToken))
        {
            this.configuration.LastSyncStatus = "Paste a pairing token from the Felix dashboard first.";
            this.SaveConfiguration();
            return;
        }

        try
        {
            var request = new FelixPairRequest
            {
                PairingToken = this.configuration.PairingToken.Trim(),
                InstallationId = this.configuration.InstallationId,
                Label = this.configuration.DeviceLabel,
                PluginVersion = this.pluginVersion,
                ApiBaseUrl = this.configuration.DashboardBaseUrl,
                Platform = Environment.OSVersion.Platform.ToString(),
                SyncConfig = new FelixSyncConfigPayload
                {
                    AutoSyncEnabled = this.configuration.AutoSyncEnabled,
                    SyncIntervalSeconds = this.configuration.SyncIntervalSeconds,
                    IncludeCollections = this.configuration.IncludeCollections,
                    IncludeCurrencies = this.configuration.IncludeCurrencies,
                    IncludeCombat = this.configuration.IncludeCombat,
                },
            };

            var response = await this.apiClient.PairAsync(this.configuration.DashboardBaseUrl, request, CancellationToken.None).ConfigureAwait(false);
            this.configuration.DeviceToken = response.Device.DeviceToken;
            this.configuration.SelectedGuildId = string.IsNullOrWhiteSpace(response.Device.GuildName)
                ? "Global Felix Profile"
                : response.Device.GuildName;
            this.configuration.PairingToken = string.Empty;
            this.configuration.LastPairedAtUtc = DateTimeOffset.UtcNow.ToString("O");
            this.configuration.LastSyncStatus = "Paired to Global Felix Profile";
            this.SaveConfiguration();
        }
        catch (Exception ex)
        {
            this.pluginLog.Error(ex, "Felix pairing failed");
            this.configuration.LastSyncStatus = $"Pairing failed: {ex.Message}";
            this.SaveConfiguration();
        }
    }

    public async Task SyncNowAsync()
    {
        await this.syncGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (string.IsNullOrWhiteSpace(this.configuration.DeviceToken))
            {
                this.configuration.LastSyncStatus = "Pair the plugin with the Felix dashboard before syncing.";
                this.SaveConfiguration();
                return;
            }

            var payload = await this.BuildSnapshotOnFrameworkThreadAsync().ConfigureAwait(false);
            await this.apiClient.UploadSnapshotAsync(
                this.configuration.DashboardBaseUrl,
                this.configuration.DeviceToken,
                payload,
                CancellationToken.None).ConfigureAwait(false);

            await this.FlushRpSceneLinesAsync(CancellationToken.None).ConfigureAwait(false);

            await this.ProcessMirrorRenderQueueAsync().ConfigureAwait(false);

            var wtEntry = payload.Tracker?.Weeklies?.Entries?
                .FirstOrDefault((entry) => string.Equals(entry.Id, "wondrous_tails", StringComparison.Ordinal));

            this.pluginLog.Information(
                "Felix snapshot uploaded for {Character} | Gil={Gil} | Mounts={MountsOwned}/{MountsTotal} | Minions={MinionsOwned}/{MinionsTotal} | Emotes={EmotesOwned}/{EmotesTotal} | Roulettes={CompletedRouletteCount}/{AvailableRouletteCount} | Weeklies={WeeklyEntries} | WT={WtProgress} | WTDuties={WtDutyCount} | WTSample={WtSample}",
                payload.Character.Name,
                payload.Currencies.Gil,
                payload.Collections.MountsOwned,
                payload.Collections.MountsTotal,
                payload.Collections.MinionsOwned,
                payload.Collections.MinionsTotal,
                payload.Collections.EmotesOwned,
                payload.Collections.EmotesTotal,
                payload.Tracker?.Dailies?.CompletedRouletteCount ?? 0,
                payload.Tracker?.Dailies?.AvailableRouletteCount ?? 0,
                payload.Tracker?.Weeklies?.Entries?.Count ?? 0,
                wtEntry?.ProgressLabel ?? "not-found",
                wtEntry?.Items?.Count ?? 0,
                wtEntry?.Items?.FirstOrDefault() ?? string.Empty);

            if (this.configuration.FcDiscoveryMode)
            {
                var fcSummary = $"FC snapshot => Name={payload.FreeCompany.Name} | Tag={payload.FreeCompany.Tag} | Rank={payload.FreeCompany.Rank} | CompanyRank={payload.FreeCompany.CompanyRank} | Leader={payload.FreeCompany.LeaderCharacterName} | Members={payload.FreeCompany.MemberCount}/{payload.FreeCompany.ActiveMemberCount} | Estate={payload.FreeCompany.Estate} | Location={payload.FreeCompany.EstateLocation} | Type={payload.FreeCompany.HousingType} | IsLeader={payload.FreeCompany.IsLeader}";
                this.AppendFreeCompanyDiscovery(fcSummary);
                this.pluginLog.Information(fcSummary);
            }

            this.configuration.LastSyncAtUtc = DateTimeOffset.UtcNow.ToString("O");
            this.configuration.LastSyncStatus = $"Snapshot synced for {payload.Character.Name}";
            this.SaveConfiguration();
        }
        catch (Exception ex)
        {
            this.pluginLog.Error(ex, "Felix sync failed");
            this.configuration.LastSyncStatus = $"Sync failed: {ex.Message}";
            this.SaveConfiguration();
        }
        finally
        {
            this.syncGate.Release();
        }
    }

    private void OnCommand(string command, string arguments)
    {
        var args = arguments.Trim();
        if (string.IsNullOrEmpty(args))
        {
            this.pluginUi.IsOpen = true;
            return;
        }

        var parts = args.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
        if (verb is "rp" or "scene")
        {
            this.pluginUi.IsOpen = true;
            return;
        }

        if (verb == "help")
        {
            this.FelixChatNotify(
                "/felix — window · rp · record [on|off] · pause (break) · sync · help");
            return;
        }

        if (verb == "pause")
        {
            if (!this.configuration.RpSceneJoined || string.IsNullOrWhiteSpace(this.configuration.RpSceneJoinCode))
            {
                this.FelixChatNotify("Join an RP scene first, then /felix pause toggles a recording break.");
                this.pluginUi.IsOpen = true;
                return;
            }

            if (!this.configuration.RpSceneRecordingEnabled)
            {
                this.FelixChatNotify("Turn on scene recording in /felix first; pause only affects active recording.");
                this.pluginUi.IsOpen = true;
                return;
            }

            this.configuration.RpSceneRecordingPaused = !this.configuration.RpSceneRecordingPaused;
            this.SaveConfiguration();
            this.FelixChatNotify(
                this.configuration.RpSceneRecordingPaused
                    ? "RP scene recording paused (break). Lines are not queued until you resume."
                    : "RP scene recording resumed.");
            return;
        }

        if (verb == "record")
        {
            if (!this.configuration.RpSceneJoined || string.IsNullOrWhiteSpace(this.configuration.RpSceneJoinCode))
            {
                this.FelixChatNotify("Join an RP scene in the Felix window first, then use record.");
                this.pluginUi.IsOpen = true;
                return;
            }

            var mode = parts.Length > 1 ? parts[1].ToLowerInvariant() : "toggle";
            var on = mode switch
            {
                "on" or "1" or "true" or "yes" => true,
                "off" or "0" or "false" or "no" => false,
                _ => !this.configuration.RpSceneRecordingEnabled,
            };
            this.configuration.RpSceneRecordingEnabled = on;
            this.SaveConfiguration();
            this.FelixChatNotify(
                on
                    ? "RP scene recording ON — lines go to the scene transcript (bubble capture must be on)."
                    : "RP scene recording OFF.");
            return;
        }

        if (verb == "sync")
        {
            _ = this.SyncNowAsync();
            this.FelixChatNotify("Felix sync started (snapshot + scene line upload when queued).");
            return;
        }

        this.pluginUi.IsOpen = true;
    }

    private void FelixChatNotify(string message)
    {
        try
        {
            this.chatGui.Print($"[Felix] {message}");
        }
        catch (Exception ex)
        {
            this.pluginLog.Warning(ex, "Felix chat notify failed");
        }
    }

    public string RpSceneFlushStatus => this.rpSceneFlushStatus;

    public int RpSceneConsecutiveUploadFailures => this.rpSceneConsecutiveUploadFailures;

    public void ResetRpSceneUploadFailureCount()
    {
        this.rpSceneConsecutiveUploadFailures = 0;
    }

    public int GetRpScenePendingLineCount()
    {
        lock (this.rpSceneGate)
        {
            return this.rpScenePendingLines.Count;
        }
    }

    public async Task UploadRpSceneLinesNowAsync()
    {
        if (string.IsNullOrWhiteSpace(this.configuration.DeviceToken))
        {
            this.FelixChatNotify("Pair the plugin before uploading scene lines.");
            return;
        }

        if (!this.configuration.RpSceneJoined || string.IsNullOrWhiteSpace(this.configuration.RpSceneJoinCode))
        {
            this.FelixChatNotify("Join an RP scene first.");
            return;
        }

        this.ResetRpSceneUploadFailureCount();
        for (var i = 0; i < 32; i++)
        {
            await this.FlushRpSceneLinesAsync(CancellationToken.None).ConfigureAwait(false);
            if (this.GetRpScenePendingLineCount() == 0)
            {
                break;
            }
        }
    }

    public async Task UploadQueuedLinesThenLeaveRpSceneAsync()
    {
        await this.UploadRpSceneLinesNowAsync().ConfigureAwait(false);
        this.framework.RunOnTick(() => this.LeaveRpScene());
    }

    private async Task ProcessMirrorRenderQueueAsync()
    {
        if (string.IsNullOrWhiteSpace(this.configuration.DeviceToken))
        {
            return;
        }

        try
        {
            var job = await this.apiClient.GetMirrorRenderJobAsync(
                this.configuration.DashboardBaseUrl,
                this.configuration.DeviceToken,
                CancellationToken.None).ConfigureAwait(false);

            if (job is null || string.IsNullOrWhiteSpace(job.Id))
            {
                return;
            }

            this.pluginLog.Information(
                "Felix mirror render job claimed for {Character} with {OverrideCount} overrides",
                job.SourceCharacterName,
                job.Overrides?.Count ?? 0);

            this.configuration.LastSyncStatus = $"Mirror render queued for {job.SourceCharacterName}";
            this.SaveConfiguration();
        }
        catch (Exception ex)
        {
            this.pluginLog.Warning(ex, "Felix mirror render queue check failed");
        }
    }

    private void OpenUi()
    {
        this.pluginUi.IsOpen = true;
    }

    private void UpdateSyncTimer()
    {
        if (!this.configuration.AutoSyncEnabled || string.IsNullOrWhiteSpace(this.configuration.DeviceToken))
        {
            this.syncTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Clamp(this.configuration.SyncIntervalSeconds, 10, 600));
        this.syncTimer.Change(interval, interval);
    }

    private void OnSyncTimerTick(object? state)
    {
        _ = this.SyncNowAsync();
    }

    private void OnClientLogout(int type, int code)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await this.FlushRpSceneLinesAsync(cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.pluginLog.Warning(ex, "Felix RP scene flush on logout skipped");
            }
        });
    }

    private void OnRpSceneAutoFlushTick(object? state)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await this.FlushRpSceneLinesAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.pluginLog.Warning(ex, "Felix RP scene auto flush failed");
            }
        });
    }

    private void UpdateRpSceneAutoFlushTimer()
    {
        var sec = Math.Clamp(this.configuration.RpSceneAutoFlushIntervalSeconds, 0, 300);
        if (sec <= 0 || string.IsNullOrWhiteSpace(this.configuration.DeviceToken))
        {
            this.rpSceneAutoFlushTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        var interval = TimeSpan.FromSeconds(sec);
        this.rpSceneAutoFlushTimer.Change(interval, interval);
    }

    public string GetRpSceneNormalizedJoinDraftPreview() =>
        NormalizeRpSceneJoinCode(this.configuration.RpSceneJoinDraft);

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        try
        {
            var shouldTrackRoleplayOverlay =
                (this.configuration.RoleplayBubbleChatEnabled || this.configuration.RoleplayOverheadBubbleEnabled)
                && ShouldTrackRoleplayBubbleChatType(type, this.configuration.RoleplayBubbleCaptureAllChat);
            var shouldSeedNativeBubble =
                this.configuration.NativeSpeechBubbleResizeEnabled
                && ShouldTrackNativeSpeechBubbleChatType(type);

            if (!shouldTrackRoleplayOverlay && !shouldSeedNativeBubble)
            {
                this.TryEnqueueRpSceneEmoteWhileOverlayDisabled(type, sender, message);
                return;
            }

            var messageText = NormalizeRoleplayBubbleText(message.TextValue);
            if (string.IsNullOrWhiteSpace(messageText))
            {
                return;
            }

            var localPlayerName = ExtractRoleplayBubbleCharacterName(this.objectTable.LocalPlayer?.Name.TextValue ?? string.Empty);
            var payloadSpeakerName = StripRoleplayBubbleCompanyTagsSafe(ExtractRoleplayBubbleSpeaker(sender));
            var speakerName = payloadSpeakerName;
            var isOwnMessage = !string.IsNullOrWhiteSpace(localPlayerName)
                && !string.IsNullOrWhiteSpace(speakerName)
                && string.Equals(speakerName, localPlayerName, StringComparison.OrdinalIgnoreCase);

            if (this.configuration.DailyDiscoveryMode && shouldTrackRoleplayOverlay)
            {
                this.AppendDailyDiscovery(
                    $"RP sender resolve => Type={GetRoleplayBubbleChannelLabel(type)} | RawSender={sender.TextValue} | Payload={payloadSpeakerName} | Local={localPlayerName} | Normalized={speakerName} | Own={isOwnMessage}");
            }

            if (string.IsNullOrWhiteSpace(speakerName))
            {
                speakerName = isOwnMessage ? "You" : GetRoleplayBubbleChannelLabel(type);
            }

            if (shouldSeedNativeBubble && isOwnMessage)
            {
                var nativeSender = !string.IsNullOrWhiteSpace(localPlayerName) ? localPlayerName : speakerName;
                this.pendingNativeSpeechBubbleResize = BuildPendingNativeSpeechBubbleResize(messageText, nativeSender, true);
                if (this.configuration.NativeSpeechBubbleDiscoveryMode)
                {
                    this.AppendNativeBubbleDiscovery(
                        $"Seeded native bubble from chat => Channel={GetRoleplayBubbleChannelLabel(type)} | Sender={nativeSender} | chars={messageText.Length}");
                }
            }

            if (!shouldTrackRoleplayOverlay)
            {
                this.TryEnqueueRpSceneEmoteWhileOverlayDisabled(type, sender, message);
                return;
            }

            var initialAnchor = this.ResolveRoleplayBubbleAnchor(speakerName, isOwnMessage);

            var entry = new RoleplayBubbleMessage
            {
                Id = DateTimeOffset.UtcNow.Ticks,
                CreatedAt = DateTimeOffset.UtcNow,
                ChannelLabel = GetRoleplayBubbleChannelLabel(type),
                SpeakerName = speakerName,
                Message = messageText,
                IsOwnMessage = isOwnMessage,
                SpeakerGameObjectId = initialAnchor?.GameObjectId,
                SpeakerWorldPosition = initialAnchor?.Position,
                SpeakerHitboxRadius = initialAnchor?.HitboxRadius ?? 0.5f,
            };

            lock (this.roleplayBubbleGate)
            {
                this.TrimExpiredRoleplayBubbleMessagesLocked();
                this.recentRoleplayBubbleMessages.Add(entry);
                this.recentRoleplayBubbleMessages.Sort((left, right) => right.CreatedAt.CompareTo(left.CreatedAt));

                var maxMessages = Math.Max(Math.Clamp(this.configuration.RoleplayBubbleMaxVisible, 1, 8) * 3, 12);
                if (this.recentRoleplayBubbleMessages.Count > maxMessages)
                {
                    this.recentRoleplayBubbleMessages.RemoveRange(maxMessages, this.recentRoleplayBubbleMessages.Count - maxMessages);
                }
            }

            this.TryEnqueueRpSceneLine(entry, type);
        }
        catch (Exception ex)
        {
            this.pluginLog.Warning(ex, "Felix RP bubble chat capture failed");
        }
    }

    private void TryEnqueueRpSceneEmoteWhileOverlayDisabled(XivChatType type, SeString sender, SeString message)
    {
        if (type is not (XivChatType.StandardEmote or XivChatType.CustomEmote))
        {
            return;
        }

        if (!this.configuration.RpSceneRecordingEnabled
            || this.configuration.RpSceneRecordingPaused
            || !this.configuration.RpSceneJoined
            || string.IsNullOrWhiteSpace(this.configuration.RpSceneJoinCode))
        {
            return;
        }

        if (!ShouldTrackRoleplayBubbleChatType(type, this.configuration.RoleplayBubbleCaptureAllChat))
        {
            return;
        }

        var messageText = NormalizeRoleplayBubbleText(message.TextValue);
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return;
        }

        var localPlayerName = ExtractRoleplayBubbleCharacterName(this.objectTable.LocalPlayer?.Name.TextValue ?? string.Empty);
        var payloadSpeakerName = StripRoleplayBubbleCompanyTagsSafe(ExtractRoleplayBubbleSpeaker(sender));
        var speakerName = payloadSpeakerName;
        var isOwnMessage = !string.IsNullOrWhiteSpace(localPlayerName)
            && !string.IsNullOrWhiteSpace(speakerName)
            && string.Equals(speakerName, localPlayerName, StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(speakerName))
        {
            speakerName = isOwnMessage ? "You" : GetRoleplayBubbleChannelLabel(type);
        }

        var initialAnchor = this.ResolveRoleplayBubbleAnchor(speakerName, isOwnMessage);
        var entry = new RoleplayBubbleMessage
        {
            Id = DateTimeOffset.UtcNow.Ticks,
            CreatedAt = DateTimeOffset.UtcNow,
            ChannelLabel = GetRoleplayBubbleChannelLabel(type),
            SpeakerName = speakerName,
            Message = messageText,
            IsOwnMessage = isOwnMessage,
            SpeakerGameObjectId = initialAnchor?.GameObjectId,
            SpeakerWorldPosition = initialAnchor?.Position,
            SpeakerHitboxRadius = initialAnchor?.HitboxRadius ?? 0.5f,
        };

        this.TryEnqueueRpSceneLine(entry, type);
    }

    private string ResolveRpSceneLocalCharacterName()
    {
        try
        {
            var lp = this.objectTable.LocalPlayer;
            if (lp is null || !lp.IsValid())
            {
                return string.Empty;
            }

            return ExtractRoleplayBubbleCharacterName(lp.Name.TextValue);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatRpSceneEmoteTranscriptLine(RoleplayBubbleMessage entry, string localCharacterName)
    {
        var local = (localCharacterName ?? string.Empty).Trim();
        var speaker = (entry.SpeakerName ?? string.Empty).Trim();
        var character = entry.IsOwnMessage && local.Length > 0
            ? local
            : speaker.Length > 0 && !speaker.Equals("You", StringComparison.OrdinalIgnoreCase)
                ? speaker
                : local.Length > 0
                    ? local
                    : speaker;
        if (string.IsNullOrWhiteSpace(character))
        {
            character = "Unknown";
        }

        var m = (entry.Message ?? string.Empty).Trim();
        if (m.Length == 0)
        {
            return $"Emote: \"{character}\"";
        }

        string inner;
        if (m.StartsWith(character + " ", StringComparison.OrdinalIgnoreCase))
        {
            var tail = m[(character.Length + 1)..].Trim();
            inner = tail.Length > 0 ? $"{character} {tail}" : character;
        }
        else if (m.Equals(character, StringComparison.OrdinalIgnoreCase))
        {
            inner = character;
        }
        else if (entry.IsOwnMessage && m.StartsWith("You ", StringComparison.OrdinalIgnoreCase) && local.Length > 0)
        {
            inner = $"{local} {m[4..].Trim()}".Trim();
        }
        else if (m.IndexOf(' ') < 0 && m.IndexOf('\u3000') < 0)
        {
            inner = $"{character} {m}".Trim();
        }
        else
        {
            inner = m;
        }

        if (inner.Length > 400)
        {
            inner = inner[..400];
        }

        return $"Emote: \"{inner}\"";
    }

    private void OnDutyCompleted(object? sender, ushort contentFinderConditionId)
    {
        try
        {
            this.configuration.EnsureCurrentDailyTrackerDay();
            var dutyName = this.snapshotBuilder.ResolveDutyName(contentFinderConditionId);
            if (string.IsNullOrWhiteSpace(dutyName))
            {
                dutyName = $"Duty {contentFinderConditionId}";
            }

            this.configuration.DailyTracker ??= new FelixDailyTrackerState();
            var existing = this.configuration.DailyTracker.CompletedDuties
                .FirstOrDefault((entry) => entry.Id == contentFinderConditionId);

            var completedAtUtc = DateTimeOffset.UtcNow.ToString("O");
            if (existing is null)
            {
                this.configuration.DailyTracker.CompletedDuties.Add(new FelixCompletedDutyState
                {
                    Id = contentFinderConditionId,
                    Name = dutyName,
                    CompletedAtUtc = completedAtUtc,
                });
            }
            else
            {
                existing.Name = dutyName;
                existing.CompletedAtUtc = completedAtUtc;
            }

            this.configuration.DailyTracker.LastDutyCompletedAtUtc = completedAtUtc;
            this.pluginLog.Information("Felix daily tracker marked duty complete: {DutyName} ({DutyId})", dutyName, contentFinderConditionId);
            this.SaveConfiguration();
        }
        catch (Exception ex)
        {
            this.pluginLog.Warning(ex, "Felix daily tracker duty completion hook failed");
        }
    }

    private Task<FelixSnapshotPayload> BuildSnapshotOnFrameworkThreadAsync()
    {
        var tcs = new TaskCompletionSource<FelixSnapshotPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        this.framework.RunOnTick(() =>
        {
            try
            {
                var payload = this.snapshotBuilder.Build(this.configuration, this.pluginVersion);
                this.ApplyRoleplayFeedToPayload(payload);
                tcs.TrySetResult(payload);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    private void ApplyRoleplayFeedToPayload(FelixSnapshotPayload payload)
    {
        var captureEnabled = this.configuration.RoleplayBubbleChatEnabled || this.configuration.RoleplayOverheadBubbleEnabled;
        var joinedScene = this.configuration.RpSceneJoined
            && !string.IsNullOrWhiteSpace(this.configuration.RpSceneJoinCode);
        var activelyRecording = joinedScene
            && this.configuration.RpSceneRecordingEnabled
            && !this.configuration.RpSceneRecordingPaused;

        if (!this.configuration.IncludeRoleplayFeedInSync && !joinedScene)
        {
            payload.Roleplay = null;
            return;
        }

        if (joinedScene)
        {
            var note = TrimRpSceneSessionNote(this.configuration.RpSceneSessionNote);
            payload.Roleplay = new FelixRoleplayPayload
            {
                SyncEnabled = true,
                CaptureEnabled = captureEnabled,
                MessageCount = 0,
                Messages = [],
                SceneRecording = activelyRecording,
                ScenePaused = this.configuration.RpSceneRecordingEnabled && this.configuration.RpSceneRecordingPaused,
                SceneId = this.configuration.RpSceneId ?? string.Empty,
                SceneJoinCode = this.configuration.RpSceneJoinCode ?? string.Empty,
                SceneSessionNote = note,
            };
            if (activelyRecording)
            {
                return;
            }

            return;
        }

        if (!this.configuration.IncludeRoleplayFeedInSync)
        {
            payload.Roleplay = null;
            return;
        }

        const int maxMessages = 32;
        const int maxChars = 480;
        var source = this.GetRoleplayBubbleMessagesForSnapshot(maxMessages);
        var messages = new List<FelixRoleplayMessagePayload>(source.Count);
        foreach (var entry in source)
        {
            var text = entry.Message ?? string.Empty;
            if (text.Length > maxChars)
            {
                text = text[..maxChars];
            }

            messages.Add(new FelixRoleplayMessagePayload
            {
                Id = entry.Id.ToString(CultureInfo.InvariantCulture),
                CreatedAt = entry.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                ChannelLabel = entry.ChannelLabel ?? string.Empty,
                SpeakerName = entry.SpeakerName ?? string.Empty,
                Text = text,
                IsOwnMessage = entry.IsOwnMessage,
            });
        }

        payload.Roleplay = new FelixRoleplayPayload
        {
            SyncEnabled = true,
            CaptureEnabled = captureEnabled,
            MessageCount = messages.Count,
            Messages = messages,
        };
    }

    private static string TrimRpSceneSessionNote(string? raw)
    {
        var t = (raw ?? string.Empty).Trim();
        return t.Length <= 200 ? t : t[..200];
    }

    private void TryEnqueueRpSceneLine(RoleplayBubbleMessage entry, XivChatType type)
    {
        if (!this.configuration.RpSceneRecordingEnabled
            || this.configuration.RpSceneRecordingPaused
            || !this.configuration.RpSceneJoined
            || string.IsNullOrWhiteSpace(this.configuration.RpSceneJoinCode))
        {
            return;
        }

        var overlayCapture = this.configuration.RoleplayBubbleChatEnabled || this.configuration.RoleplayOverheadBubbleEnabled;
        var isEmote = type is XivChatType.StandardEmote or XivChatType.CustomEmote;
        if (!overlayCapture && !isEmote)
        {
            return;
        }

        if (!ShouldTrackRoleplayBubbleChatType(type, this.configuration.RoleplayBubbleCaptureAllChat))
        {
            return;
        }

        var speakerCid = string.Empty;
        try
        {
            if (entry.IsOwnMessage)
            {
                speakerCid = this.playerState.ContentId.ToString();
            }
        }
        catch
        {
        }

        var localName = this.ResolveRpSceneLocalCharacterName();
        var channelLabel = entry.ChannelLabel ?? string.Empty;
        var speakerName = entry.SpeakerName ?? string.Empty;
        var text = entry.Message ?? string.Empty;
        if (isEmote)
        {
            channelLabel = "Emote";
            text = FormatRpSceneEmoteTranscriptLine(entry, localName);
            if (entry.IsOwnMessage && localName.Length > 0)
            {
                speakerName = localName;
            }
        }

        if (text.Length > 520)
        {
            text = text[..520];
        }

        var line = new RpScenePendingLine
        {
            LineId = entry.Id.ToString(CultureInfo.InvariantCulture),
            CreatedAtIso = entry.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            ChannelLabel = channelLabel,
            SpeakerName = speakerName,
            Text = text,
            IsOwnMessage = entry.IsOwnMessage,
            SpeakerContentId = speakerCid,
        };

        lock (this.rpSceneGate)
        {
            this.rpScenePendingLines.Add(line);
            while (this.rpScenePendingLines.Count > 200)
            {
                this.rpScenePendingLines.RemoveAt(0);
            }
        }

        if (this.configuration.RpSceneNotifyFirstCapturedLine && !this.rpSceneSessionFirstLineNotified)
        {
            this.rpSceneSessionFirstLineNotified = true;
            this.FelixChatNotify("First line captured for this RP scene — it will upload on the next sync or auto-flush.");
        }
    }

    private async Task FlushRpSceneLinesAsync(CancellationToken cancellationToken)
    {
        await this.rpSceneFlushSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.IsNullOrWhiteSpace(this.configuration.DeviceToken)
                || string.IsNullOrWhiteSpace(this.configuration.RpSceneJoinCode)
                || !this.configuration.RpSceneJoined)
            {
                return;
            }

            List<RpScenePendingLine> batch;
            lock (this.rpSceneGate)
            {
                if (this.rpScenePendingLines.Count == 0)
                {
                    return;
                }

                var take = Math.Min(40, this.rpScenePendingLines.Count);
                batch = this.rpScenePendingLines.Take(take).ToList();
                this.rpScenePendingLines.RemoveRange(0, take);
            }

            var lines = batch.ConvertAll(
                (l) => new FelixRpSceneLineUpload
                {
                    LineId = l.LineId,
                    CreatedAt = l.CreatedAtIso,
                    ChannelLabel = l.ChannelLabel,
                    SpeakerName = l.SpeakerName,
                    Text = l.Text,
                    IsOwnMessage = l.IsOwnMessage,
                    ContentId = l.SpeakerContentId,
                });

            var (ok, status) = await this.apiClient.PostRpSceneLinesAsync(
                this.configuration.DashboardBaseUrl,
                this.configuration.DeviceToken,
                new FelixRpSceneLinesRequest
                {
                    JoinCode = NormalizeRpSceneJoinCode(this.configuration.RpSceneJoinCode),
                    Lines = lines,
                },
                cancellationToken).ConfigureAwait(false);

            if (!ok)
            {
                if (status == 403)
                {
                    var dropped = batch.Count;
                    this.rpSceneConsecutiveUploadFailures = 0;
                    this.framework.RunOnTick(() =>
                    {
                        this.LeaveRpScene(
                            "This RP scene ended or this device is no longer a member. "
                            + $"The last upload failed and {dropped} queued line(s) were not saved.");
                    });
                    return;
                }

                this.rpSceneConsecutiveUploadFailures = Math.Min(99, this.rpSceneConsecutiveUploadFailures + 1);
                var hint = status == 429
                    ? " (rate limited — wait ~1 min)"
                    : string.Empty;
                this.rpSceneFlushStatus =
                    $"Last scene upload failed ({batch.Count} lines re-queued) · failures x{this.rpSceneConsecutiveUploadFailures}{hint} · {DateTime.Now:t}";
                lock (this.rpSceneGate)
                {
                    this.rpScenePendingLines.InsertRange(0, batch);
                }
            }
            else
            {
                this.rpSceneConsecutiveUploadFailures = 0;
                this.rpSceneFlushStatus = $"Last upload OK · {batch.Count} line(s) · {DateTime.Now:t}";
            }
        }
        finally
        {
            this.rpSceneFlushSemaphore.Release();
        }
    }

    private static string NormalizeRpSceneJoinCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[raw.Length];
        var n = 0;
        foreach (var c in raw.ToUpperInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c) && n < 8)
            {
                buffer[n++] = c;
            }
        }

        return n == 0 ? string.Empty : new string(buffer[..n]);
    }

    public async Task JoinRpSceneAsync()
    {
        var draft = NormalizeRpSceneJoinCode(this.configuration.RpSceneJoinDraft);
        if (draft.Length != 8)
        {
            this.configuration.RpSceneLastJoinError = "Enter the 8-character join code from the dashboard.";
            this.SaveConfiguration();
            return;
        }

        var tcs = new TaskCompletionSource<(string ContentId, string Name)?>(TaskCreationOptions.RunContinuationsAsynchronously);
        this.framework.RunOnTick(() =>
        {
            try
            {
                var lp = this.objectTable.LocalPlayer;
                if (lp is null || !lp.IsValid())
                {
                    tcs.TrySetResult(null);
                    return;
                }

                var cid = this.playerState.ContentId.ToString();
                var name = lp.Name.TextValue;
                tcs.TrySetResult((cid, name));
            }
            catch (Exception ex)
            {
                this.pluginLog.Warning(ex, "Felix RP scene join identity read failed");
                tcs.TrySetResult(null);
            }
        });

        var ids = await tcs.Task.ConfigureAwait(false);
        if (ids is null)
        {
            this.configuration.RpSceneLastJoinError = "Log in with a character before joining a scene.";
            this.SaveConfiguration();
            return;
        }

        try
        {
            var response = await this.apiClient.JoinRpSceneAsync(
                this.configuration.DashboardBaseUrl,
                this.configuration.DeviceToken,
                new FelixRpSceneJoinRequest
                {
                    JoinCode = draft,
                    ContentId = ids.Value.ContentId,
                    CharacterName = ids.Value.Name,
                },
                CancellationToken.None).ConfigureAwait(false);

            if (!response.Ok)
            {
                this.configuration.RpSceneLastJoinError = string.IsNullOrWhiteSpace(response.Error) ? "Join failed." : response.Error;
                this.configuration.RpSceneJoined = false;
                this.SaveConfiguration();
                return;
            }

            this.configuration.RpSceneJoined = true;
            this.configuration.RpSceneJoinCode = response.JoinCode;
            this.configuration.RpSceneId = response.SceneId;
            this.configuration.RpSceneDisplayTitle = response.Title;
            this.configuration.RpSceneLastJoinError = string.Empty;
            this.configuration.RpSceneJoinDraft = draft;
            this.rpSceneSessionFirstLineNotified = false;
            this.SaveConfiguration();
        }
        catch (Exception ex)
        {
            this.pluginLog.Warning(ex, "Felix RP scene join request failed");
            this.configuration.RpSceneLastJoinError = ex.Message;
            this.SaveConfiguration();
        }
    }

    public void LeaveRpScene(string? sessionEndedMessage = null)
    {
        this.configuration.RpSceneJoined = false;
        this.configuration.RpSceneJoinCode = string.Empty;
        this.configuration.RpSceneId = string.Empty;
        this.configuration.RpSceneDisplayTitle = string.Empty;
        this.configuration.RpSceneLastJoinError = sessionEndedMessage ?? string.Empty;
        this.configuration.RpSceneRecordingPaused = false;
        this.rpSceneFlushStatus = string.Empty;
        this.rpSceneSessionFirstLineNotified = false;
        lock (this.rpSceneGate)
        {
            this.rpScenePendingLines.Clear();
        }

        this.SaveConfiguration();
    }

    private void OnAddonLifecycleEvent(AddonEvent type, AddonArgs args)
    {
        if (args is null)
        {
            return;
        }

        var addonName = args.AddonName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(addonName))
        {
            return;
        }

        var isFreeCompanyAddon = ContainsFreeCompanyKeyword(addonName);
        var isDailyAddon = ContainsDailyKeyword(addonName);
        var isWeeklyAddon = ContainsWeeklyKeyword(addonName);
        var canCaptureDailyState = args is AddonSetupArgs || args is AddonRefreshArgs;
        var canCaptureWeeklyState = isWeeklyAddon && (args is AddonSetupArgs || args is AddonRefreshArgs);
        if ((!this.configuration.FcDiscoveryMode && !this.configuration.DailyDiscoveryMode && !this.configuration.WeeklyDiscoveryMode && !canCaptureDailyState && !canCaptureWeeklyState)
            || (!isFreeCompanyAddon && !isDailyAddon && !isWeeklyAddon && !this.configuration.DailyDiscoveryMode))
        {
            return;
        }

        if (this.configuration.FcDiscoveryMode && isFreeCompanyAddon)
        {
            var entry = $"Addon {type}: {addonName}";
            this.AppendFreeCompanyDiscovery(entry);
            this.pluginLog.Information("Felix FC addon discovery: {Entry}", entry);

            if (addonName.StartsWith("FreeCompany", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (args is AddonSetupArgs setupArgs)
                    {
                        this.LogFreeCompanyAtkValues(addonName, "Setup", setupArgs.AtkValueSpan);
                    }
                    else if (args is AddonRefreshArgs refreshArgs)
                    {
                        this.LogFreeCompanyAtkValues(addonName, "Refresh", refreshArgs.AtkValueSpan);
                    }
                }
                catch (Exception ex)
                {
                    this.pluginLog.Error(ex, "Felix FC addon parse failed");
                    this.AppendFreeCompanyDiscovery($"FC addon parse failed: {ex.Message}");
                }
            }
        }

        if (canCaptureDailyState)
        {
            try
            {
                if (args is AddonSetupArgs setupArgs)
                {
                    var captured = this.TryCaptureDailyAddonState(addonName, setupArgs.AtkValueSpan);
                    if (this.configuration.DailyDiscoveryMode && (captured || isDailyAddon))
                    {
                        var entry = $"Addon {type}: {addonName}";
                        this.AppendDailyDiscovery(entry);
                        this.pluginLog.Information("Felix daily addon discovery: {Entry}", entry);
                        this.LogDailyAtkValues(addonName, "Setup", setupArgs.AtkValueSpan);
                    }
                }
                else if (args is AddonRefreshArgs refreshArgs)
                {
                    var captured = this.TryCaptureDailyAddonState(addonName, refreshArgs.AtkValueSpan);
                    if (this.configuration.DailyDiscoveryMode && (captured || isDailyAddon))
                    {
                        var entry = $"Addon {type}: {addonName}";
                        this.AppendDailyDiscovery(entry);
                        this.pluginLog.Information("Felix daily addon discovery: {Entry}", entry);
                        this.LogDailyAtkValues(addonName, "Refresh", refreshArgs.AtkValueSpan);
                    }
                }
            }
            catch (Exception ex)
            {
                this.pluginLog.Error(ex, "Felix daily addon parse failed");
                this.AppendDailyDiscovery($"Daily addon parse failed: {ex.Message}");
            }
        }

        if (canCaptureWeeklyState)
        {
            try
            {
                if (args is AddonSetupArgs setupArgs)
                {
                    this.TryCaptureWeeklyAddonState(addonName, args, setupArgs.AtkValueSpan);
                    if (this.configuration.WeeklyDiscoveryMode)
                    {
                        var entry = $"Addon {type}: {addonName}";
                        this.AppendWeeklyDiscovery(entry);
                        this.pluginLog.Information("Felix weekly addon discovery: {Entry}", entry);
                        this.LogWeeklyAtkValues(addonName, "Setup", setupArgs.AtkValueSpan);
                    }
                }
                else if (args is AddonRefreshArgs refreshArgs)
                {
                    this.TryCaptureWeeklyAddonState(addonName, args, refreshArgs.AtkValueSpan);
                    if (this.configuration.WeeklyDiscoveryMode)
                    {
                        var entry = $"Addon {type}: {addonName}";
                        this.AppendWeeklyDiscovery(entry);
                        this.pluginLog.Information("Felix weekly addon discovery: {Entry}", entry);
                        this.LogWeeklyAtkValues(addonName, "Refresh", refreshArgs.AtkValueSpan);
                    }
                }
            }
            catch (Exception ex)
            {
                this.pluginLog.Error(ex, "Felix weekly addon parse failed");
                this.AppendWeeklyDiscovery($"Weekly addon parse failed: {ex.Message}");
            }
        }
    }

    private void OnFrameworkUpdate(IFramework frameworkInstance)
    {
        this.UpdateNativeFocusTargetMarkers();

        if (!this.configuration.NativeSpeechBubbleResizeEnabled)
        {
            return;
        }

        this.TryApplyPendingNativeSpeechBubbleResize();
    }

    private unsafe void UpdateNativeFocusTargetMarkers()
    {
        // Map Addon now uses a custom overlay path, so native marker mutation stays disabled.
        this.RestoreNativeFocusTargetMarkers();
    }

    private bool TryGetFocusedPartyMember(IGameObject focusTarget, out IPartyMember focusedPartyMember, out int focusedPartySlot)
    {
        focusedPartyMember = null!;
        focusedPartySlot = -1;

        if (focusTarget is null || !focusTarget.IsValid())
        {
            return false;
        }

        var focusObjectId = focusTarget.GameObjectId;
        var focusName = ExtractRoleplayBubbleCharacterName(focusTarget.Name.TextValue);

        // First, trust the actual object id if the party member exposes it.
        for (var slot = 0; slot < this.partyList.Length; slot++)
        {
            var candidate = this.partyList[slot];
            if (candidate is null)
            {
                continue;
            }

            var candidateGameObject = candidate.GameObject;
            var candidateObjectId = candidateGameObject?.GameObjectId ?? candidate.EntityId;
            if (candidateObjectId != 0 && candidateObjectId == focusObjectId)
            {
                focusedPartyMember = candidate;
                focusedPartySlot = slot;
                return true;
            }
        }

        // Next, match on normalized name. This is much safer than proximity and
        // avoids selecting the local player just because both characters are close.
        if (!string.IsNullOrWhiteSpace(focusName))
        {
            for (var slot = 0; slot < this.partyList.Length; slot++)
            {
                var candidate = this.partyList[slot];
                if (candidate is null)
                {
                    continue;
                }

                var candidateName = ExtractRoleplayBubbleCharacterName(candidate.Name.TextValue);
                if (string.Equals(candidateName, focusName, StringComparison.Ordinal))
                {
                    focusedPartyMember = candidate;
                    focusedPartySlot = slot;
                    return true;
                }
            }
        }

        var localPosition = this.objectTable.LocalPlayer?.Position ?? Vector3.Zero;
        var bestDistance = float.MaxValue;

        // Last-resort fallback only: use proximity if we could not match by id or name.
        for (var slot = 0; slot < this.partyList.Length; slot++)
        {
            var candidate = this.partyList[slot];
            if (candidate is null)
            {
                continue;
            }

            var candidateGameObject = candidate.GameObject;
            var candidateObjectId = candidateGameObject?.GameObjectId ?? candidate.EntityId;
            if (candidateObjectId == 0)
            {
                continue;
            }

            var samePosition = Vector3.DistanceSquared(candidate.Position, focusTarget.Position) <= 0.25f;
            if (!samePosition)
            {
                continue;
            }

            var distance = Vector3.DistanceSquared(candidate.Position, localPosition);
            if (distance >= bestDistance)
            {
                continue;
            }

            focusedPartyMember = candidate;
            focusedPartySlot = slot;
            bestDistance = distance;
        }

        return focusedPartySlot >= 0;
    }

    private unsafe bool TryHighlightFocusedPartyMemberMarkers(AgentMap* agentMap, IPartyMember focusedPartyMember, int focusedPartySlot)
    {
        var highlightedFullMap = this.TryHighlightFocusedPartyFullMapMarker(agentMap->MapMarkers, focusedPartyMember, focusedPartySlot, out var fullMapMarkerSummary);
        var highlightedMiniMap = this.TryHighlightFocusedPartyMiniMapMarker(agentMap->MiniMapMarkers, focusedPartyMember, focusedPartySlot, out var miniMapMarkerSummary);

        if (this.configuration.DailyDiscoveryMode)
        {
            var signature =
                $"Focus={focusedPartyMember.Name.TextValue}|Slot={focusedPartySlot}|FullMap={fullMapMarkerSummary}|MiniMap={miniMapMarkerSummary}";
            if (!string.Equals(signature, this.lastMapFocusMarkerDiscoverySignature, StringComparison.Ordinal))
            {
                this.lastMapFocusMarkerDiscoverySignature = signature;
                this.AppendDailyDiscovery($"Map focus party marker => {signature}");
            }
        }

        return highlightedFullMap || highlightedMiniMap;
    }

    private unsafe bool TryHighlightFocusedPartyFullMapMarker(Span<MapMarkerInfo> markers, IPartyMember focusedPartyMember, int focusedPartySlot, out string summary)
    {
        summary = "none";
        if (markers.IsEmpty)
        {
            return false;
        }

        var bestIndex = -1;
        var bestScore = int.MinValue;

        for (var index = 0; index < markers.Length; index++)
        {
            ref var candidate = ref markers[index];
            var score = this.ScoreFocusedPartyMapMarker(candidate.MapMarker, candidate.DataKey, candidate.MapMarkerSubKey, focusedPartyMember, focusedPartySlot);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestIndex = index;
        }

        if (bestIndex < 0 || bestScore <= 0)
        {
            return false;
        }

        ref var marker = ref markers[bestIndex];
        marker.MapMarker.Scale = Math.Max(marker.MapMarker.Scale, 160);
        summary =
            $"idx={bestIndex} type={marker.DataType} key={marker.DataKey} sub={marker.MapMarkerSubKey} icon={marker.MapMarker.IconId} icon2={marker.MapMarker.SecondaryIconId} flags={marker.MapMarker.IconFlags} scale={marker.MapMarker.Scale} x={marker.MapMarker.X} y={marker.MapMarker.Y} score={bestScore}";
        return true;
    }

    private unsafe bool TryHighlightFocusedPartyMiniMapMarker(Span<MiniMapMarker> markers, IPartyMember focusedPartyMember, int focusedPartySlot, out string summary)
    {
        summary = "none";
        if (markers.IsEmpty)
        {
            return false;
        }

        var bestIndex = -1;
        var bestScore = int.MinValue;

        for (var index = 0; index < markers.Length; index++)
        {
            ref var candidate = ref markers[index];
            var score = this.ScoreFocusedPartyMapMarker(candidate.MapMarker, candidate.DataKey, 0, focusedPartyMember, focusedPartySlot);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestIndex = index;
        }

        if (bestIndex < 0 || bestScore <= 0)
        {
            return false;
        }

        ref var marker = ref markers[bestIndex];
        marker.MapMarker.Scale = Math.Max(marker.MapMarker.Scale, 160);
        summary =
            $"idx={bestIndex} key={marker.DataKey} icon={marker.MapMarker.IconId} icon2={marker.MapMarker.SecondaryIconId} flags={marker.MapMarker.IconFlags} scale={marker.MapMarker.Scale} x={marker.MapMarker.X} y={marker.MapMarker.Y} score={bestScore}";
        return true;
    }

    private unsafe bool TryFindFocusedPartyFullMapMarker(Span<MapMarkerInfo> markers, IPartyMember focusedPartyMember, int focusedPartySlot, out MapMarkerBase marker)
    {
        marker = default;
        if (markers.IsEmpty)
        {
            return false;
        }

        var bestIndex = -1;
        var bestScore = int.MinValue;
        for (var index = 0; index < markers.Length; index++)
        {
            ref var candidate = ref markers[index];
            var score = this.ScoreFocusedPartyMapMarker(candidate.MapMarker, candidate.DataKey, candidate.MapMarkerSubKey, focusedPartyMember, focusedPartySlot);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestIndex = index;
        }

        if (bestIndex < 0 || bestScore <= 0)
        {
            return false;
        }

        marker = markers[bestIndex].MapMarker;
        return true;
    }

    private unsafe bool TryFindFocusedPartyMiniMapMarker(Span<MiniMapMarker> markers, IPartyMember focusedPartyMember, int focusedPartySlot, out MapMarkerBase marker)
    {
        marker = default;
        if (markers.IsEmpty)
        {
            return false;
        }

        var bestIndex = -1;
        var bestScore = int.MinValue;
        for (var index = 0; index < markers.Length; index++)
        {
            ref var candidate = ref markers[index];
            var score = this.ScoreFocusedPartyMapMarker(candidate.MapMarker, candidate.DataKey, 0, focusedPartyMember, focusedPartySlot);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestIndex = index;
        }

        if (bestIndex < 0 || bestScore <= 0)
        {
            return false;
        }

        marker = markers[bestIndex].MapMarker;
        return true;
    }

    private int ScoreFocusedPartyMapMarker(MapMarkerBase marker, ushort dataKey, byte markerSubKey, IPartyMember focusedPartyMember, int focusedPartySlot)
    {
        var score = 0;
        var focusedEntityIdLow = unchecked((ushort)focusedPartyMember.EntityId);

        if (dataKey == focusedEntityIdLow)
        {
            score += 900;
        }

        if (marker.Index == focusedPartySlot)
        {
            score += 450;
        }

        if (markerSubKey == focusedPartySlot)
        {
            score += 350;
        }

        if (this.TryGetPreciseMapCoordinates(focusedPartyMember.Position, out var focusedMapCoords))
        {
            var expectedX = (int)MathF.Round(focusedMapCoords.X * 100f);
            var expectedY = (int)MathF.Round(focusedMapCoords.Y * 100f);
            var delta = Math.Abs(marker.X - expectedX) + Math.Abs(marker.Y - expectedY);
            score += Math.Max(0, 300 - delta);
        }

        if (marker.IconId != 0)
        {
            score += 25;
        }

        if (marker.Scale > 0)
        {
            score += 10;
        }

        return score;
    }

    private unsafe void RestoreNativeFocusTargetMarkers()
    {
        if (!this.nativeFocusTargetMarkerInjected)
        {
            return;
        }

        var agentMap = AgentMap.Instance();
        if (agentMap != null)
        {
            agentMap->ResetMapMarkers();
            agentMap->CreateMapMarkers(false);
            agentMap->ResetMiniMapMarkers();
            agentMap->CreateMiniMapMarkers(false);
        }

        this.nativeFocusTargetMarkerInjected = false;
        this.lastNativeFocusTargetObjectId = 0;
        this.lastNativeFocusTargetPosition = default;
        this.lastNativeFocusTargetMarkerRefreshAt = DateTimeOffset.MinValue;
    }

    private unsafe bool TryGetNativeFocusTargetMarkerScale(AgentMap* agentMap, out int scale)
    {
        scale = 100;
        if (agentMap == null)
        {
            return false;
        }

        var zoom = MathF.Max(0.35f, agentMap->SelectedMapSizeFactorFloat > 0.01f ? agentMap->SelectedMapSizeFactorFloat / 100f : 1f);
        scale = (int)Math.Clamp(100f * zoom, 60f, 160f);
        return true;
    }

    private unsafe bool TryGetNativeFocusTargetMiniMapScale(AgentMap* agentMap, out int scale)
    {
        scale = 100;
        if (agentMap == null)
        {
            return false;
        }

        var zoom = MathF.Max(0.5f, agentMap->CurrentMapSizeFactorFloat > 0.01f ? agentMap->CurrentMapSizeFactorFloat / 100f : 1f);
        scale = (int)Math.Clamp(100f * zoom, 55f, 140f);
        return true;
    }

    private void TryResizeNativeSpeechBubbleByName(string addonName)
    {
        if (!this.configuration.NativeSpeechBubbleResizeEnabled
            || !string.Equals(addonName, "_MiniTalk", StringComparison.Ordinal))
        {
            return;
        }

        this.TryApplyPendingNativeSpeechBubbleResize();
    }

    private unsafe bool TryResizeNativeSpeechBubble(nint addonAddress, int addonInstanceIndex)
    {
        if (addonAddress == nint.Zero)
        {
            return false;
        }

        var addon = (AddonMiniTalk*)addonAddress;
        if (addon == null)
        {
            return false;
        }

        var pending = this.pendingNativeSpeechBubbleResize;
        if (pending is null)
        {
            return false;
        }

        var talkBubbles = addon->TalkBubbles;
        var discoveredEntries = new List<string>();
        var selectedIndex = -1;
        var selectedScore = int.MinValue;
        var selectedText = string.Empty;
        var selectedUsedFallback = false;
        AtkTextNode* selectedTextNode = null;
        var sawPendingMatch = false;

        for (var index = 0; index < talkBubbles.Length; index++)
        {
            ref var bubble = ref talkBubbles[index];
            var componentTextNode = FindBestBubbleComponentTextNode(bubble);
            var preferredTextNode = componentTextNode != null ? componentTextNode : bubble.BubbleTextNode;
            var originalText = NormalizeRoleplayBubbleText(ExtractBestNativeBubbleText(bubble, componentTextNode));
            var activityScore = GetNativeBubbleActivityScore(bubble, preferredTextNode);
            var matchScore = ScorePendingBubbleTextMatch(originalText, pending.MessageText);
            var entryKey = $"{addonInstanceIndex}:{index}";
            var recentChangeBonus = this.UpdateNativeBubbleEntryRecency(entryKey, originalText, pending.CreatedAt);
            var baselineChangeBonus = ScorePendingBaselineChange(pending, entryKey, originalText);
            var selectionScore = activityScore + matchScore + recentChangeBonus;
            var effectiveText = !string.IsNullOrWhiteSpace(pending.MessageText)
                ? pending.MessageText
                : originalText;

            if (this.configuration.NativeSpeechBubbleDiscoveryMode)
            {
                var entrySummary = DescribeNativeBubbleEntry(index, bubble, preferredTextNode, originalText, selectionScore);
                if (!string.IsNullOrWhiteSpace(entrySummary))
                {
                    discoveredEntries.Add(entrySummary);
                }
            }

            if (preferredTextNode == null || string.IsNullOrWhiteSpace(effectiveText))
            {
                continue;
            }

            if ((matchScore > 0 || recentChangeBonus > 0 || baselineChangeBonus > 0) && !sawPendingMatch)
            {
                selectedIndex = -1;
                selectedScore = int.MinValue;
                sawPendingMatch = true;
            }

            if (sawPendingMatch && matchScore <= 0 && recentChangeBonus <= 0 && baselineChangeBonus <= 0)
            {
                continue;
            }

            selectionScore += baselineChangeBonus;

            if (selectionScore <= selectedScore)
            {
                continue;
            }

            selectedIndex = index;
            selectedScore = selectionScore;
            selectedText = effectiveText;
            selectedUsedFallback = matchScore <= 0;
            selectedTextNode = preferredTextNode;
        }

        if (this.configuration.NativeSpeechBubbleDiscoveryMode && discoveredEntries.Count > 0)
        {
            this.AppendNativeBubbleDiscovery($"Addon[{addonInstanceIndex}] raw entries => {string.Join(" | ", discoveredEntries)}");
        }

        if (selectedIndex < 0)
        {
            return false;
        }

        ref var targetBubble = ref talkBubbles[selectedIndex];
        var targetTextNode = selectedTextNode != null ? selectedTextNode : targetBubble.BubbleTextNode;
        if (targetTextNode == null || string.IsNullOrWhiteSpace(selectedText))
        {
            return false;
        }

        targetTextNode->SetText(selectedText);

        var textFlags = targetTextNode->TextFlags;
        textFlags |= TextFlags.AutoAdjustNodeSize;
        textFlags &= ~TextFlags.MultiLine;
        textFlags &= ~TextFlags.WordWrap;
        textFlags &= ~TextFlags.Ellipsis;
        textFlags &= ~TextFlags.OverflowHidden;
        targetTextNode->TextFlags = textFlags;

        var seedTextWidth = (ushort)Math.Clamp(Math.Max(pending.DesiredTextWidth, pending.DesiredWidth - 24), 600, 6000);
        targetTextNode->Width = seedTextWidth;
        targetTextNode->Height = 80;
        targetTextNode->ResizeNodeForCurrentText();

        ushort drawWidth = 0;
        ushort drawHeight = 0;
        targetTextNode->GetTextDrawSize(&drawWidth, &drawHeight);

        var innerWidth = (ushort)Math.Clamp(Math.Max(Math.Max(drawWidth + 24, pending.DesiredTextWidth), 220), 220, 6000);
        var innerHeight = (ushort)Math.Clamp(Math.Max(drawHeight + 18, 32), 32, 240);
        var width = (ushort)Math.Clamp(Math.Max(innerWidth + 18, pending.DesiredWidth), 240, 6020);
        var height = (ushort)Math.Clamp(innerHeight + 18, 50, 260);

        targetTextNode->Width = innerWidth;
        targetTextNode->Height = innerHeight;

        if (targetBubble.BubbleTextNode != null && targetBubble.BubbleTextNode != targetTextNode)
        {
            targetBubble.BubbleTextNode->Width = innerWidth;
            targetBubble.BubbleTextNode->Height = innerHeight;
        }

        if (targetBubble.BubbleResNode != null)
        {
            targetBubble.BubbleResNode->Width = width;
            targetBubble.BubbleResNode->Height = height;
        }

        if (targetBubble.BubbleNineGridNode != null)
        {
            targetBubble.BubbleNineGridNode->Width = width;
            targetBubble.BubbleNineGridNode->Height = height;
        }

        if (targetBubble.BubbleImageNode != null)
        {
            targetBubble.BubbleImageNode->Width = width;
            targetBubble.BubbleImageNode->Height = height;
        }

        if (targetBubble.ComponentNode != null)
        {
            targetBubble.ComponentNode->Width = width;
            targetBubble.ComponentNode->Height = height;
            ExpandBubbleComponentLayout(targetBubble.ComponentNode, width, height, innerWidth, innerHeight);
        }

        if (targetBubble.ComponentNode2 != null)
        {
            targetBubble.ComponentNode2->Width = width;
            targetBubble.ComponentNode2->Height = height;
            ExpandBubbleComponentLayout(targetBubble.ComponentNode2, width, height, innerWidth, innerHeight);
        }

        addon->SetSize(width, height);
        ExpandMiniTalkAddonLayout(addon, width, height, innerWidth, innerHeight);
        addon->UpdateCollisionNodeList(false);
        pending.AppliedFrames++;

        if (this.configuration.NativeSpeechBubbleDiscoveryMode)
        {
            this.LogNativeBubbleState(addonInstanceIndex, selectedIndex, targetBubble, targetTextNode, selectedText, drawWidth, drawHeight);
        }

        if ((DateTimeOffset.UtcNow - pending.LastLoggedAt).TotalMilliseconds >= 500)
        {
            pending.LastLoggedAt = DateTimeOffset.UtcNow;
            var source = selectedUsedFallback ? "detour" : "node";
            this.AppendNativeBubbleDiscovery($"Resized native bubble => Addon[{addonInstanceIndex}] Bubble[{selectedIndex}] Score={selectedScore} Source={source} | {width}x{height} | text={innerWidth}x{innerHeight} | chars={pending.MessageLength} | frame={pending.AppliedFrames}");
        }

        return true;
    }

    private unsafe void InitializeNativeSpeechBubbleHook()
    {
        try
        {
            var address = (nint)RaptureLogModule.MemberFunctionPointers.ShowMiniTalkPlayer;
            if (address == nint.Zero)
            {
                this.pluginLog.Warning("Felix native speech bubble hook address was zero.");
                return;
            }

            this.showMiniTalkPlayerHook = this.gameInteropProvider.HookFromAddress<ShowMiniTalkPlayerDelegate>(
                address,
                this.ShowMiniTalkPlayerDetour);
            this.showMiniTalkPlayerHook.Enable();
            this.pluginLog.Information("Felix native speech bubble hook initialized.");
        }
        catch (Exception ex)
        {
            this.pluginLog.Error(ex, "Felix failed to initialize the native speech bubble hook");
            this.configuration.LastSyncStatus = $"Native bubble hook failed: {ex.Message}";
            this.SaveConfiguration();
        }
    }

    private unsafe void ShowMiniTalkPlayerDetour(
        RaptureLogModule* raptureLogModule,
        ushort logKindId,
        Utf8String* sender,
        Utf8String* message,
        ushort worldId,
        bool isLocalPlayer)
    {
        var messageText = string.Empty;
        try
        {
            messageText = message != null ? message->ToString() ?? string.Empty : string.Empty;
            if (this.configuration.NativeSpeechBubbleResizeEnabled && !string.IsNullOrWhiteSpace(messageText))
            {
                var normalized = NormalizeRoleplayBubbleText(messageText);
                var senderText = sender != null ? sender->ToString() ?? string.Empty : string.Empty;
                this.pendingNativeSpeechBubbleResize = BuildPendingNativeSpeechBubbleResize(normalized, senderText, isLocalPlayer);

                if (this.configuration.NativeSpeechBubbleDiscoveryMode)
                {
                    this.AppendNativeBubbleDiscovery(
                        $"ShowMiniTalkPlayer => LogKind={logKindId} | Sender={senderText} | World={worldId} | Local={isLocalPlayer} | chars={normalized.Length}");
                }
            }
        }
        catch (Exception ex)
        {
            this.pluginLog.Warning(ex, "Felix native speech bubble detour pre-processing failed");
        }

        if (this.configuration.RoleplayOverheadBubbleEnabled
            && this.configuration.SuppressNativeSpeechBubbleWhenOverheadEnabled)
        {
            return;
        }

        this.showMiniTalkPlayerHook!.Original(raptureLogModule, logKindId, sender, message, worldId, isLocalPlayer);
    }

    private void TryApplyPendingNativeSpeechBubbleResize()
    {
        var pending = this.pendingNativeSpeechBubbleResize;
        if (pending is null)
        {
            return;
        }

        if ((DateTimeOffset.UtcNow - pending.CreatedAt).TotalSeconds > 6)
        {
            this.pendingNativeSpeechBubbleResize = null;
            return;
        }

        var foundAnyAddon = false;
        var readyVisibleAddonIndexes = new List<int>();
        var discoveredAddonIndexes = new List<int>();
        var resizedAny = false;

        if (!pending.BaselineCaptured)
        {
            this.CapturePendingNativeBubbleBaseline(pending);
            pending.BaselineCaptured = true;
            if (this.configuration.NativeSpeechBubbleDiscoveryMode)
            {
                this.AppendNativeBubbleDiscovery(
                    $"Captured native bubble baseline => Sender={pending.SenderText} | Local={pending.IsLocalPlayer} | chars={pending.MessageLength} | entries={pending.BaselineTexts.Count}");
            }

            return;
        }

        for (var addonIndex = 1; addonIndex <= 8; addonIndex++)
        {
            var addon = this.gameGui.GetAddonByName("_MiniTalk", addonIndex);
            if (addon.IsNull)
            {
                continue;
            }

            foundAnyAddon = true;
            discoveredAddonIndexes.Add(addonIndex);
            if (addon.IsReady && addon.IsVisible)
            {
                readyVisibleAddonIndexes.Add(addonIndex);
            }

            if (this.TryResizeNativeSpeechBubble(addon.Address, addonIndex))
            {
                resizedAny = true;
            }
        }

        if (this.configuration.NativeSpeechBubbleDiscoveryMode
            && !resizedAny
            && (DateTimeOffset.UtcNow - pending.LastStatusLoggedAt).TotalMilliseconds >= 500)
        {
            pending.LastStatusLoggedAt = DateTimeOffset.UtcNow;
            if (!foundAnyAddon)
            {
                this.AppendNativeBubbleDiscovery("No _MiniTalk addon instances were found yet.");
            }
            else if (readyVisibleAddonIndexes.Count == 0)
            {
                this.AppendNativeBubbleDiscovery($"MiniTalk addon instances found at indices [{string.Join(", ", discoveredAddonIndexes)}], but none report ready+visible. Inspecting them anyway.");
            }
            else
            {
                this.AppendNativeBubbleDiscovery($"Ready+visible _MiniTalk addons found at indices [{string.Join(", ", readyVisibleAddonIndexes)}], but no populated bubble text nodes were detected.");
            }
        }
    }

    private unsafe void CapturePendingNativeBubbleBaseline(PendingNativeSpeechBubbleResize pending)
    {
        pending.BaselineTexts.Clear();
        for (var addonIndex = 1; addonIndex <= 8; addonIndex++)
        {
            var addon = this.gameGui.GetAddonByName("_MiniTalk", addonIndex);
            if (addon.IsNull)
            {
                continue;
            }

            var miniTalk = (AddonMiniTalk*)addon.Address;
            if (miniTalk == null)
            {
                continue;
            }

            var talkBubbles = miniTalk->TalkBubbles;
            for (var bubbleIndex = 0; bubbleIndex < talkBubbles.Length; bubbleIndex++)
            {
                ref var bubble = ref talkBubbles[bubbleIndex];
                var componentTextNode = FindBestBubbleComponentTextNode(bubble);
                var preferredTextNode = componentTextNode != null ? componentTextNode : bubble.BubbleTextNode;
                var text = NormalizeRoleplayBubbleText(ExtractBestNativeBubbleText(bubble, componentTextNode));
                if (preferredTextNode == null)
                {
                    continue;
                }

                pending.BaselineTexts[$"{addonIndex}:{bubbleIndex}"] = text;
            }
        }
    }

    private static PendingNativeSpeechBubbleResize BuildPendingNativeSpeechBubbleResize(string messageText, string senderText, bool isLocalPlayer)
    {
        var normalized = NormalizeRoleplayBubbleText(messageText);
        var messageLength = normalized.Length;
        var desiredTextWidth = 600 + Math.Min(messageLength * 18, 5400);
        var desiredWidth = desiredTextWidth + 20;
        var estimatedLineCount = Math.Max(1, (int)Math.Ceiling(messageLength / 120f));
        var desiredHeight = 90 + (estimatedLineCount * 30);

        return new PendingNativeSpeechBubbleResize
        {
            CreatedAt = DateTimeOffset.UtcNow,
            MessageText = normalized,
            SenderText = senderText ?? string.Empty,
            IsLocalPlayer = isLocalPlayer,
            MessageLength = messageLength,
            DesiredWidth = desiredWidth,
            DesiredHeight = desiredHeight,
            DesiredTextWidth = desiredTextWidth,
            AppliedFrames = 0,
            LastLoggedAt = DateTimeOffset.MinValue,
            LastStatusLoggedAt = DateTimeOffset.MinValue,
        };
    }

    private unsafe void LogNativeBubbleState(
        int addonInstanceIndex,
        int index,
        AddonMiniTalk.TalkBubbleEntry bubble,
        AtkTextNode* activeTextNode,
        string text,
        ushort drawWidth,
        ushort drawHeight)
    {
        if (activeTextNode == null)
        {
            return;
        }

        var signature = string.Join(
            "|",
            index,
            text,
            activeTextNode->TextFlags,
            activeTextNode->Width,
            activeTextNode->Height,
            drawWidth,
            drawHeight,
            bubble.BubbleResNode != null ? bubble.BubbleResNode->Width : 0,
            bubble.BubbleResNode != null ? bubble.BubbleResNode->Height : 0,
            bubble.BubbleNineGridNode != null ? bubble.BubbleNineGridNode->Width : 0,
            bubble.BubbleNineGridNode != null ? bubble.BubbleNineGridNode->Height : 0);

        if (string.Equals(signature, this.lastNativeBubbleDiscoverySignature, StringComparison.Ordinal))
        {
            return;
        }

        this.lastNativeBubbleDiscoverySignature = signature;
        var preview = text.Length > 80 ? $"{text[..80]}..." : text;
        var resWidth = bubble.BubbleResNode != null ? bubble.BubbleResNode->Width : (ushort)0;
        var resHeight = bubble.BubbleResNode != null ? bubble.BubbleResNode->Height : (ushort)0;
        var nineGridWidth = bubble.BubbleNineGridNode != null ? bubble.BubbleNineGridNode->Width : (ushort)0;
        var nineGridHeight = bubble.BubbleNineGridNode != null ? bubble.BubbleNineGridNode->Height : (ushort)0;
        this.AppendNativeBubbleDiscovery(
            $"Addon[{addonInstanceIndex}] Bubble[{index}] TextFlags={FormatTextFlags(activeTextNode->TextFlags)} | TextNode={activeTextNode->Width}x{activeTextNode->Height} | Draw={drawWidth}x{drawHeight} | Res={resWidth}x{resHeight} | NineGrid={nineGridWidth}x{nineGridHeight} | Text=\"{preview}\"");
    }

    private static unsafe string ExtractBestNativeBubbleText(AddonMiniTalk.TalkBubbleEntry bubble, AtkTextNode* componentTextNode)
    {
        var componentText = ExtractTextNodeText(componentTextNode);
        if (!string.IsNullOrWhiteSpace(componentText))
        {
            return componentText;
        }

        return ExtractNativeBubbleText(bubble);
    }

    private static unsafe string ExtractNativeBubbleText(AddonMiniTalk.TalkBubbleEntry bubble)
    {
        return ExtractTextNodeText(bubble.BubbleTextNode);
    }

    private static unsafe string ExtractTextNodeText(AtkTextNode* textNode)
    {
        if (textNode == null)
        {
            return string.Empty;
        }

        var nodeText = textNode->NodeText.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(nodeText))
        {
            return nodeText;
        }

        var nativeText = textNode->GetText().ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(nativeText))
        {
            return nativeText;
        }

        var originalPointerText = textNode->OriginalTextPointer.ToString() ?? string.Empty;
        return originalPointerText;
    }

    private static unsafe string DescribeNativeBubbleEntry(
        int index,
        AddonMiniTalk.TalkBubbleEntry bubble,
        AtkTextNode* activeTextNode,
        string text,
        int activityScore)
    {
        var hasAnyPointers =
            bubble.ComponentNode != null
            || bubble.ComponentNode2 != null
            || bubble.BubbleResNode != null
            || bubble.BubbleTextNode != null
            || bubble.BubbleNineGridNode != null
            || bubble.BubbleImageNode != null;

        if (!hasAnyPointers)
        {
            return string.Empty;
        }

        var preview = string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : (text.Length > 24 ? $"{text[..24]}..." : text);

        var textLength = string.IsNullOrWhiteSpace(text) ? 0 : text.Length;
        var textNodeSize = activeTextNode != null ? $"{activeTextNode->Width}x{activeTextNode->Height}" : "0x0";
        var resSize = bubble.BubbleResNode != null ? $"{bubble.BubbleResNode->Width}x{bubble.BubbleResNode->Height}" : "0x0";
        var source = activeTextNode == bubble.BubbleTextNode ? "root" : "child";
        return $"[{index}:Src={source} C={(bubble.ComponentNode != null ? 1 : 0)} C2={(bubble.ComponentNode2 != null ? 1 : 0)} R={(bubble.BubbleResNode != null ? 1 : 0)} T={(bubble.BubbleTextNode != null ? 1 : 0)} N={(bubble.BubbleNineGridNode != null ? 1 : 0)} I={(bubble.BubbleImageNode != null ? 1 : 0)} Len={textLength} Score={activityScore} TN={textNodeSize} RZ={resSize}{(textLength > 0 ? $" Text=\"{preview}\"" : string.Empty)}]";
    }

    private static unsafe int GetNativeBubbleActivityScore(AddonMiniTalk.TalkBubbleEntry bubble, AtkTextNode* activeTextNode)
    {
        var score = 0;

        if (activeTextNode != null)
        {
            score += 1000 + activeTextNode->Width + activeTextNode->Height + (ExtractTextNodeText(activeTextNode).Length * 100);
        }

        if (bubble.BubbleResNode != null)
        {
            score += 500 + bubble.BubbleResNode->Width + bubble.BubbleResNode->Height;
        }

        if (bubble.BubbleNineGridNode != null)
        {
            score += 300 + bubble.BubbleNineGridNode->Width + bubble.BubbleNineGridNode->Height;
        }

        if (bubble.ComponentNode != null)
        {
            score += 200 + bubble.ComponentNode->Width + bubble.ComponentNode->Height;
        }

        if (bubble.ComponentNode2 != null)
        {
            score += 100 + bubble.ComponentNode2->Width + bubble.ComponentNode2->Height;
        }

        return score;
    }

    private static unsafe AtkTextNode* FindBestBubbleComponentTextNode(AddonMiniTalk.TalkBubbleEntry bubble)
    {
        var bestNode = bubble.BubbleTextNode;
        var bestScore = ScoreBubbleTextNode(bestNode);

        bestNode = ConsiderBubbleComponentTextNodes(bubble.ComponentNode, bestNode, ref bestScore);
        bestNode = ConsiderBubbleComponentTextNodes(bubble.ComponentNode2, bestNode, ref bestScore);
        return bestNode;
    }

    private static unsafe AtkTextNode* ConsiderBubbleComponentTextNodes(
        AtkComponentNode* componentNode,
        AtkTextNode* currentBest,
        ref int currentBestScore)
    {
        if (componentNode == null || componentNode->Component == null)
        {
            return currentBest;
        }

        for (uint nodeId = 1; nodeId <= 40; nodeId++)
        {
            var candidate = componentNode->Component->GetTextNodeById(nodeId);
            var score = ScoreBubbleTextNode(candidate);
            if (score <= currentBestScore)
            {
                continue;
            }

            currentBest = candidate;
            currentBestScore = score;
        }

        return currentBest;
    }

    private static unsafe void ExpandBubbleComponentLayout(
        AtkComponentNode* componentNode,
        ushort width,
        ushort height,
        ushort innerWidth,
        ushort innerHeight)
    {
        if (componentNode == null || componentNode->Component == null)
        {
            return;
        }

        var component = componentNode->Component;
        for (uint nodeId = 1; nodeId <= 64; nodeId++)
        {
            ResizeBubbleLayoutNode(component->GetNodeById(nodeId), width, height, innerWidth, innerHeight);

            var textNode = component->GetTextNodeById(nodeId);
            if (textNode != null)
            {
                textNode->Width = innerWidth;
                textNode->Height = innerHeight;
            }

            var collisionNode = component->GetCollisionNodeById(nodeId);
            if (collisionNode != null)
            {
                collisionNode->Width = width;
                collisionNode->Height = height;
            }

            ResizeBubbleLayoutNode((AtkResNode*)component->GetClippingMaskNodeById(nodeId), width, height, innerWidth, innerHeight);
        }
    }

    private static unsafe void ExpandMiniTalkAddonLayout(
        AddonMiniTalk* addon,
        ushort width,
        ushort height,
        ushort innerWidth,
        ushort innerHeight)
    {
        if (addon == null)
        {
            return;
        }

        for (uint nodeId = 1; nodeId <= 128; nodeId++)
        {
            ResizeBubbleLayoutNode(addon->GetNodeById(nodeId), width, height, innerWidth, innerHeight);

            var textNode = addon->GetTextNodeById(nodeId);
            if (textNode != null)
            {
                textNode->Width = Math.Max(textNode->Width, innerWidth);
                textNode->Height = Math.Max(textNode->Height, innerHeight);
            }
        }
    }

    private static unsafe void ResizeBubbleLayoutNode(
        AtkResNode* node,
        ushort width,
        ushort height,
        ushort innerWidth,
        ushort innerHeight)
    {
        if (node == null)
        {
            return;
        }

        var targetWidth = node->Type == NodeType.Text ? innerWidth : width;
        var targetHeight = node->Type == NodeType.Text ? innerHeight : height;
        if (node->Width < targetWidth)
        {
            node->Width = targetWidth;
        }

        if (node->Height < targetHeight)
        {
            node->Height = targetHeight;
        }
    }

    private static unsafe int ScoreBubbleTextNode(AtkTextNode* textNode)
    {
        if (textNode == null)
        {
            return int.MinValue;
        }

        return textNode->Width + textNode->Height + (ExtractTextNodeText(textNode).Length * 100);
    }

    private static int ScorePendingBubbleTextMatch(string candidateText, string pendingText)
    {
        if (string.IsNullOrWhiteSpace(pendingText))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(candidateText))
        {
            return 2_000;
        }

        if (string.Equals(candidateText, pendingText, StringComparison.Ordinal))
        {
            return 100_000;
        }

        if (candidateText.Contains(pendingText, StringComparison.Ordinal) || pendingText.Contains(candidateText, StringComparison.Ordinal))
        {
            return 50_000;
        }

        var sharedPrefixLength = 0;
        var maxPrefixLength = Math.Min(candidateText.Length, pendingText.Length);
        while (sharedPrefixLength < maxPrefixLength && candidateText[sharedPrefixLength] == pendingText[sharedPrefixLength])
        {
            sharedPrefixLength++;
        }

        if (sharedPrefixLength >= 16)
        {
            return 10_000 + (sharedPrefixLength * 100);
        }

        return -50_000;
    }

    private int UpdateNativeBubbleEntryRecency(string entryKey, string currentText, DateTimeOffset pendingCreatedAt)
    {
        var normalized = currentText ?? string.Empty;
        if (!this.nativeBubbleEntryTextSnapshots.TryGetValue(entryKey, out var previousText)
            || !string.Equals(previousText, normalized, StringComparison.Ordinal))
        {
            this.nativeBubbleEntryTextSnapshots[entryKey] = normalized;
            this.nativeBubbleEntryTextUpdatedAt[entryKey] = DateTimeOffset.UtcNow;
        }

        if (!this.nativeBubbleEntryTextUpdatedAt.TryGetValue(entryKey, out var updatedAt))
        {
            return 0;
        }

        if (updatedAt >= pendingCreatedAt.AddMilliseconds(-250))
        {
            return 120_000;
        }

        return 0;
    }

    private static int ScorePendingBaselineChange(PendingNativeSpeechBubbleResize pending, string entryKey, string currentText)
    {
        if (!pending.BaselineTexts.TryGetValue(entryKey, out var baselineText))
        {
            return 0;
        }

        var normalizedCurrent = currentText ?? string.Empty;
        if (string.Equals(baselineText, normalizedCurrent, StringComparison.Ordinal))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(normalizedCurrent))
        {
            return 0;
        }

        return 80_000;
    }

    private static string FormatTextFlags(TextFlags value)
    {
        if (value == TextFlags.None)
        {
            return nameof(TextFlags.None);
        }

        var flags = Enum.GetValues<TextFlags>()
            .Where((flag) => flag != TextFlags.None && value.HasFlag(flag))
            .Select(static (flag) => flag.ToString())
            .ToList();

        return flags.Count > 0 ? string.Join(",", flags) : value.ToString();
    }

    private void AppendFreeCompanyDiscovery(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return;
        }

        var value = $"[{DateTimeOffset.Now:HH:mm:ss}] {entry}";
        lock (this.freeCompanyDiscoveryGate)
        {
            this.recentFreeCompanyDiscovery.Insert(0, value);
            if (this.recentFreeCompanyDiscovery.Count > 80)
            {
                this.recentFreeCompanyDiscovery.RemoveRange(80, this.recentFreeCompanyDiscovery.Count - 80);
            }
        }
    }

    private void AppendNativeBubbleDiscovery(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return;
        }

        var value = $"[{DateTimeOffset.Now:HH:mm:ss}] {entry}";
        lock (this.nativeBubbleDiscoveryGate)
        {
            this.recentNativeBubbleDiscovery.Insert(0, value);
            if (this.recentNativeBubbleDiscovery.Count > 80)
            {
                this.recentNativeBubbleDiscovery.RemoveRange(80, this.recentNativeBubbleDiscovery.Count - 80);
            }
        }
    }

    private static bool ContainsFreeCompanyKeyword(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var lowered = value.ToLowerInvariant();
        return lowered.Contains("free")
            || lowered.Contains("company")
            || lowered.Contains("fc")
            || lowered.Contains("housing")
            || lowered.Contains("estate");
    }

    private static bool ContainsWeeklyKeyword(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var lowered = value.ToLowerInvariant();
        return lowered.Contains("weekly")
            || lowered.Contains("bingo")
            || lowered.Contains("wonder")
            || lowered.Contains("khloe")
            || lowered.Contains("satisfaction")
            || lowered.Contains("delivery")
            || lowered.Contains("collectable")
            || lowered.Contains("custom");
    }

    private static bool ContainsDailyKeyword(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var lowered = value.ToLowerInvariant();
        return lowered.Contains("cactpot")
            || lowered.Contains("lottery")
            || lowered.Contains("tribe")
            || lowered.Contains("beast")
            || lowered.Contains("hunt")
            || lowered.Contains("mob")
            || lowered.Contains("bill")
            || lowered.Contains("supply")
            || lowered.Contains("provision")
            || lowered.Contains("grandcompany")
            || lowered.Contains("gc");
    }

    private void LogFreeCompanyAtkValues(string addonName, string phase, ReadOnlySpan<AtkValue> values)
    {
        var limit = Math.Min(values.Length, 60);
        for (var index = 0; index < limit; index++)
        {
            var value = values[index];
            var rendered = RenderAtkValue(value);
            var entry = $"{addonName} {phase}[{index}] {rendered}";
            this.AppendFreeCompanyDiscovery(entry);
            this.pluginLog.Information("Felix FC addon value: {Entry}", entry);
        }
    }

    private void AppendWeeklyDiscovery(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return;
        }

        var value = $"[{DateTimeOffset.Now:HH:mm:ss}] {entry}";
        lock (this.weeklyDiscoveryGate)
        {
            this.recentWeeklyDiscovery.Insert(0, value);
            if (this.recentWeeklyDiscovery.Count > 80)
            {
                this.recentWeeklyDiscovery.RemoveRange(80, this.recentWeeklyDiscovery.Count - 80);
            }
        }
    }

    private void AppendDailyDiscovery(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return;
        }

        var value = $"[{DateTimeOffset.Now:HH:mm:ss}] {entry}";
        lock (this.dailyDiscoveryGate)
        {
            this.recentDailyDiscovery.Insert(0, value);
            if (this.recentDailyDiscovery.Count > 80)
            {
                this.recentDailyDiscovery.RemoveRange(80, this.recentDailyDiscovery.Count - 80);
            }
        }
    }

    private void LogDailyAtkValues(string addonName, string phase, ReadOnlySpan<AtkValue> values)
    {
        var limit = Math.Min(values.Length, 24);
        for (var index = 0; index < limit; index++)
        {
            var value = values[index];
            var rendered = RenderAtkValue(value);
            var entry = $"{addonName} {phase}[{index}] {rendered}";
            this.AppendDailyDiscovery(entry);
            this.pluginLog.Information("Felix daily addon value: {Entry}", entry);
        }
    }

    private bool TryCaptureDailyAddonState(string addonName, ReadOnlySpan<AtkValue> values)
    {
        var readableStrings = ExtractReadableDailyStrings(values);
        var system = ResolveDailySystem(addonName, readableStrings);
        if (system is null)
        {
            return false;
        }

        var (id, name, state) = system.Value;
        var now = DateTimeOffset.UtcNow.ToString("O");
        var detail = $"Native source detected from {addonName}.";
        var progressLabel = "Native source detected";
        var items = new List<string>();

        switch (id)
        {
            case "gc_turn_ins":
                items = ExtractMatchingStrings(readableStrings, GcClassNames);
                progressLabel = items.Count > 0 ? $"{items.Count} requested classes detected" : "Supply board detected";
                detail = items.Count > 0
                    ? $"Supply & Provisioning board detected. Requested classes: {string.Join(", ", items)}."
                    : "Supply & Provisioning board detected from the Grand Company daily turn-in UI.";
                break;
            case "tribal_quests":
                items = ExtractMatchingStrings(readableStrings, TribalKeywords);
                progressLabel = items.Count > 0 ? "Tribal quests detected" : "Tribal UI detected";
                detail = items.Count > 0
                    ? $"Tribal quests UI detected. Related labels: {string.Join(", ", items)}."
                    : $"Tribal quests UI detected from {addonName}.";
                break;
            case "hunt_bills":
                items = readableStrings
                    .Where((value) => ContainsAny(value, HuntKeywords))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(6)
                    .ToList();
                progressLabel = items.Count > 0 ? "Hunt board detected" : "Hunt source detected";
                detail = items.Count > 0
                    ? $"Hunt board detected. Visible labels: {string.Join(", ", items)}."
                    : $"Hunt board detected from {addonName}.";
                break;
            case "mini_cactpot":
                items = readableStrings
                    .Where((value) => value.Contains("Cactpot", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToList();
                progressLabel = "Mini Cactpot detected";
                detail = "Mini Cactpot UI detected from the Gold Saucer daily lottery screen.";
                break;
            default:
                items = readableStrings.Take(4).ToList();
                break;
        }

        var changed = !state.Synced
            || !string.Equals(state.NativeSource, addonName, StringComparison.Ordinal)
            || !string.Equals(state.ProgressLabel, progressLabel, StringComparison.Ordinal)
            || !string.Equals(state.Detail, detail, StringComparison.Ordinal)
            || !state.Items.SequenceEqual(items, StringComparer.Ordinal);

        state.Synced = true;
        state.SyncedAtUtc = now;
        state.NativeSource = addonName;
        state.ProgressLabel = progressLabel;
        state.Detail = detail;
        state.Items = items;

        var summary = $"{name} native source => Addon={addonName} | Labels={items.Count}";
        this.AppendDailyDiscovery(summary);
        this.pluginLog.Information("Felix daily tracker: {Summary}", summary);

        if (changed)
        {
            this.configuration.LastSyncStatus = $"{name} native source detected";
            this.SaveConfiguration();
        }

        return true;
    }

    private (string Id, string Name, FelixNativeDailySystemState State)? ResolveDailySystem(string addonName, IReadOnlyList<string> readableStrings)
    {
        if (string.IsNullOrWhiteSpace(addonName))
        {
            addonName = string.Empty;
        }

        var daily = this.configuration.DailyNative ??= new FelixDailyNativeState();
        var lowered = addonName.ToLowerInvariant();

        if (lowered.Contains("cactpot") || lowered.Contains("lottery"))
        {
            return ("mini_cactpot", "Mini Cactpot", daily.MiniCactpot ??= new FelixNativeDailySystemState());
        }

        if (lowered.Contains("tribe") || lowered.Contains("beast"))
        {
            return ("tribal_quests", "Tribal Quests", daily.TribalQuests ??= new FelixNativeDailySystemState());
        }

        if (readableStrings.Any((value) => ContainsAny(value, TribalKeywords)))
        {
            return ("tribal_quests", "Tribal Quests", daily.TribalQuests ??= new FelixNativeDailySystemState());
        }

        if (lowered.Contains("hunt") || lowered.Contains("mob") || lowered.Contains("bill"))
        {
            return ("hunt_bills", "Hunt Bills", daily.HuntBills ??= new FelixNativeDailySystemState());
        }

        if (readableStrings.Any((value) => ContainsAny(value, HuntKeywords)))
        {
            return ("hunt_bills", "Hunt Bills", daily.HuntBills ??= new FelixNativeDailySystemState());
        }

        if (lowered.Contains("supply") || lowered.Contains("provision") || lowered.Contains("grandcompany") || lowered.Contains("gc"))
        {
            return ("gc_turn_ins", "GC Turn-ins", daily.GcTurnIns ??= new FelixNativeDailySystemState());
        }

        if (readableStrings.Any((value) => GcClassNames.Contains(value, StringComparer.OrdinalIgnoreCase)))
        {
            return ("gc_turn_ins", "GC Turn-ins", daily.GcTurnIns ??= new FelixNativeDailySystemState());
        }

        if (lowered.Contains("cactpot") || lowered.Contains("lottery") || readableStrings.Any((value) => value.Contains("Cactpot", StringComparison.OrdinalIgnoreCase)))
        {
            return ("mini_cactpot", "Mini Cactpot", daily.MiniCactpot ??= new FelixNativeDailySystemState());
        }

        return null;
    }

    private static List<string> ExtractReadableDailyStrings(ReadOnlySpan<AtkValue> values)
    {
        var results = new List<string>();
        var limit = Math.Min(values.Length, 40);
        for (var index = 0; index < limit; index++)
        {
            var value = ReadAtkString(values, index);
            value = NormalizeDailyText(value);
            if (!LooksLikeReadableDailyString(value))
            {
                continue;
            }

            if (results.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            results.Add(value);
        }

        return results;
    }

    private static string NormalizeDailyText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static bool LooksLikeReadableDailyString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Length < 2 || value.Length > 80)
        {
            return false;
        }

        if (value.StartsWith("Duty ID ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return value.Any(char.IsLetter);
    }

    private static bool ContainsAny(string value, IEnumerable<string> terms)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var term in terms)
        {
            if (value.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> ExtractMatchingStrings(IEnumerable<string> source, IEnumerable<string> terms)
    {
        var results = new List<string>();
        foreach (var value in source)
        {
            var matched = false;
            foreach (var term in terms)
            {
                if (!value.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matched = true;
                break;
            }

            if (matched && !results.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                results.Add(value);
            }
        }

        return results;
    }

    private void LogWeeklyAtkValues(string addonName, string phase, ReadOnlySpan<AtkValue> values)
    {
        var limit = Math.Min(values.Length, 60);
        for (var index = 0; index < limit; index++)
        {
            var value = values[index];
            var rendered = RenderAtkValue(value);
            var entry = $"{addonName} {phase}[{index}] {rendered}";
            this.AppendWeeklyDiscovery(entry);
            this.pluginLog.Information("Felix weekly addon value: {Entry}", entry);
        }
    }

    private void TryCaptureWeeklyAddonState(string addonName, AddonArgs args, ReadOnlySpan<AtkValue> values)
    {
        if (string.Equals(addonName, "WeeklyBingo", StringComparison.OrdinalIgnoreCase))
        {
            this.CaptureWondrousTails(args, values);
        }
    }

    private unsafe void CaptureWondrousTails(AddonArgs args, ReadOnlySpan<AtkValue> values)
    {
        var tracker = this.configuration.WeeklyTracker ??= new FelixWeeklyTrackerState();
        var state = tracker.WondrousTails ??= new FelixWondrousTailsState();

        var hasJournal = ReadAtkUInt(values, 0) > 0;
        var secondChancePoints = SafeToInt(ReadAtkUInt(values, 35));
        var deadline = ReadAtkString(values, 43);
        var sealCount = 0;
        for (var index = 1; index <= 16 && index < values.Length; index++)
        {
            if (ReadAtkBool(values, index))
            {
                sealCount++;
            }
        }

        sealCount = Math.Min(9, sealCount);

        var dutyIds = new List<uint>();
        for (var index = 44; index <= 59 && index < values.Length; index++)
        {
            var dutyId = ReadAtkUInt(values, index);
            if (dutyId == 0 || dutyIds.Contains(dutyId))
            {
                continue;
            }

            dutyIds.Add(dutyId);
        }

        var dutyNames = ExtractWeeklyBingoDutyTexts(args.Addon.Address)
            .Where(static (name) => LooksLikeUsableWeeklyBingoDuty(name))
            .Distinct(StringComparer.Ordinal)
            .Take(16)
            .ToList();
        if (dutyNames.Count == 0 || dutyNames.Count < Math.Min(dutyIds.Count, 8))
        {
            dutyNames = dutyIds
                .Select(this.snapshotBuilder.ResolveDutyName)
                .Where((name) => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .Take(16)
                .ToList();
        }

        var changed = state.Synced != true
            || state.HasJournal != hasJournal
            || state.SealCount != sealCount
            || state.SecondChancePoints != secondChancePoints
            || !string.Equals(state.Deadline, deadline, StringComparison.Ordinal)
            || !state.DutyIds.SequenceEqual(dutyIds)
            || !state.DutyNames.SequenceEqual(dutyNames, StringComparer.Ordinal);

        state.Synced = true;
        state.SyncedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        state.HasJournal = hasJournal;
        state.SealCount = sealCount;
        state.SecondChancePoints = secondChancePoints;
        state.Deadline = deadline;
        state.DutyIds = dutyIds;
        state.DutyNames = dutyNames;

        var summary = $"WT parsed => Journal={hasJournal} | Seals={sealCount}/9 | SecondChance={secondChancePoints} | Duties={dutyIds.Count} | Deadline={deadline}";
        this.AppendWeeklyDiscovery(summary);
        this.pluginLog.Information("Felix weekly tracker: {Summary}", summary);

        if (changed)
        {
            this.configuration.LastSyncStatus = $"Wondrous Tails synced: {sealCount}/9 seals";
            this.SaveConfiguration();
        }
    }

    private static unsafe List<string> ExtractWeeklyBingoDutyTexts(nint addonAddress)
    {
        var results = new List<string>();
        if (addonAddress == 0)
        {
            return results;
        }

        var addon = (AtkUnitBase*)addonAddress;

        for (uint nodeId = 1; nodeId <= 2000; nodeId++)
        {
            var textNode = addon->GetTextNodeById(nodeId);
            if (textNode == null)
            {
                continue;
            }

            string text;
            try
            {
                text = textNode->NodeText.ToString()?.Trim() ?? string.Empty;
            }
            catch
            {
                continue;
            }
            text = NormalizeWeeklyText(text);
            if (!LooksLikeWeeklyBingoDuty(text))
            {
                continue;
            }

            if (results.Contains(text, StringComparer.Ordinal))
            {
                continue;
            }

            results.Add(text);
            if (results.Count >= 16)
            {
                break;
            }
        }

        return results;
    }

    private static string RenderAtkValue(AtkValue value)
    {
        try
        {
            var stringValue = value.GetValueAsString();
            if (!string.IsNullOrWhiteSpace(stringValue))
            {
                return $"Type={value.Type} Value={stringValue}";
            }
        }
        catch
        {
        }

        return $"Type={value.Type} Int={value.Int} UInt={value.UInt} Bool={value.Bool} Float={value.Float}";
    }

    private static uint ReadAtkUInt(ReadOnlySpan<AtkValue> values, int index)
    {
        if (index < 0 || index >= values.Length)
        {
            return 0;
        }

        return values[index].UInt;
    }

    private static bool ReadAtkBool(ReadOnlySpan<AtkValue> values, int index)
    {
        if (index < 0 || index >= values.Length)
        {
            return false;
        }

        return values[index].Bool;
    }

    private static string ReadAtkString(ReadOnlySpan<AtkValue> values, int index)
    {
        if (index < 0 || index >= values.Length)
        {
            return string.Empty;
        }

        try
        {
            return values[index].GetValueAsString()?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static int SafeToInt(uint value)
    {
        return value > int.MaxValue ? int.MaxValue : (int)value;
    }

    private static string NormalizeWeeklyText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static bool LooksLikeWeeklyBingoDuty(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Length < 4 || value.Length > 120)
        {
            return false;
        }

        if (value.Any(char.IsDigit) && value.StartsWith("Duty ID ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var lowered = value.ToLowerInvariant();
        if (lowered.Contains("deadline")
            || lowered.Contains("second chance")
            || lowered.Contains("complete a task")
            || lowered.Contains("complete a duty in the following category")
            || lowered.Contains("select a completed duty")
            || lowered.Contains("reward list")
            || lowered.Contains("select one")
            || lowered.Contains("guaranteed")
            || lowered.Contains("seal")
            || lowered.Contains("journal")
            || lowered.Contains("khloe")
            || lowered.Contains("use second chance")
            || lowered.Contains("receive your reward"))
        {
            return false;
        }

        if (lowered is "wondrous tails" or "book" or "reward" or "full seals" or "one line" or "two lines" or "three lines")
        {
            return false;
        }

        return true;
    }

    private static bool LooksLikeUsableWeeklyBingoDuty(string value)
    {
        if (!LooksLikeWeeklyBingoDuty(value))
        {
            return false;
        }

        var lowered = value.Trim().ToLowerInvariant();
        return lowered is not "1 line"
            && lowered is not "2 lines"
            && lowered is not "3 lines"
            && lowered is not "full seals";
    }

    private static bool LooksLikeNativeBubbleMessageText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Length < 6)
        {
            return false;
        }

        var lowered = value.ToLowerInvariant();
        if (lowered.Contains("say", StringComparison.Ordinal)
            || lowered.Contains("shout", StringComparison.Ordinal)
            || lowered.Contains("party", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private void TrimExpiredRoleplayBubbleMessagesLocked()
    {
        var lifetime = TimeSpan.FromSeconds(Math.Clamp(this.configuration.RoleplayBubbleLifetimeSeconds, 10, 300));
        var cutoff = DateTimeOffset.UtcNow - lifetime;
        this.recentRoleplayBubbleMessages.RemoveAll((entry) => entry.CreatedAt < cutoff);
    }

    private IGameObject? ResolveRoleplayBubbleAnchor(RoleplayBubbleMessage bubble)
    {
        if (bubble.SpeakerGameObjectId is ulong gameObjectId)
        {
            var directMatch = this.objectTable
                .FirstOrDefault((entry) => entry is not null && entry.IsValid() && entry.GameObjectId == gameObjectId);
            if (directMatch is not null)
            {
                return directMatch;
            }
        }

        return this.ResolveRoleplayBubbleAnchor(bubble.SpeakerName, bubble.IsOwnMessage);
    }

    private unsafe bool TryGetMapAddonTrackedTargets(out IGameObject localPlayer, out List<MapAddonTrackedTarget> trackedTargets)
    {
        trackedTargets = [];
        localPlayer = null!;

        if (!this.configuration.MapAddonFocusTargetColorEnabled
            && !this.configuration.MapAddonFriendColorEnabled
            && !this.configuration.MapAddonFreeCompanyColorEnabled
            && !this.configuration.MapAddonLinkshellColorEnabled)
        {
            return false;
        }

        var player = this.objectTable.LocalPlayer;
        if (player is null || !player.IsValid())
        {
            return false;
        }

        localPlayer = player;
        var trackedByObjectId = new Dictionary<ulong, MapAddonTrackedTarget>();

        if (this.configuration.MapAddonFocusTargetColorEnabled)
        {
            var focusTarget = this.targetManager.FocusTarget;
            if (focusTarget is not null && focusTarget.IsValid() && focusTarget.GameObjectId != 0 && focusTarget.GameObjectId != localPlayer.GameObjectId)
            {
                UpsertMapAddonTrackedTarget(
                    trackedByObjectId,
                    new MapAddonTrackedTarget(
                        focusTarget.GameObjectId,
                        focusTarget.Position,
                        focusTarget.HitboxRadius,
                        this.configuration.MapAddonFocusTargetColor,
                        30,
                        "Focus"));
            }
        }

        var localPlayerFcTag = this.ReadMapAddonFreeCompanyTag(localPlayer);

        HashSet<long>? linkshellMemberContentIds = null;
        if (this.configuration.MapAddonLinkshellColorEnabled)
        {
            linkshellMemberContentIds = [];
            this.TryFillLinkshellMemberContentIds(linkshellMemberContentIds);
        }

        foreach (var entry in this.objectTable)
        {
            if (entry is null || !entry.IsValid() || entry.GameObjectId == 0 || entry.GameObjectId == localPlayer.GameObjectId)
            {
                continue;
            }

            if (!this.TryGetMapAddonCharacter(entry, out var character))
            {
                continue;
            }

            if (this.configuration.MapAddonFriendColorEnabled && character->IsFriend)
            {
                UpsertMapAddonTrackedTarget(
                    trackedByObjectId,
                    new MapAddonTrackedTarget(
                        entry.GameObjectId,
                        entry.Position,
                        entry.HitboxRadius,
                        this.configuration.MapAddonFriendColor,
                        20,
                        "Friend"));
            }

            if (this.configuration.MapAddonFreeCompanyColorEnabled
                && localPlayerFcTag.Length > 0
                && localPlayerFcTag.SequenceEqual(this.ReadMapAddonFreeCompanyTag(entry)))
            {
                UpsertMapAddonTrackedTarget(
                    trackedByObjectId,
                    new MapAddonTrackedTarget(
                        entry.GameObjectId,
                        entry.Position,
                        entry.HitboxRadius,
                        this.configuration.MapAddonFreeCompanyColor,
                        10,
                        "FreeCompany"));
            }

            if (this.configuration.MapAddonLinkshellColorEnabled
                && linkshellMemberContentIds is { Count: > 0 })
            {
                var contentIdLong = unchecked((long)character->ContentId);
                if (contentIdLong != 0 && linkshellMemberContentIds.Contains(contentIdLong))
                {
                    UpsertMapAddonTrackedTarget(
                        trackedByObjectId,
                        new MapAddonTrackedTarget(
                            entry.GameObjectId,
                            entry.Position,
                            entry.HitboxRadius,
                            this.configuration.MapAddonLinkshellColor,
                            18,
                            "Linkshell"));
                }
            }
        }

        trackedTargets = trackedByObjectId.Values
            .OrderBy((entry) => entry.Priority)
            .ThenBy((entry) => entry.SourceLabel, StringComparer.Ordinal)
            .ToList();

        if (this.configuration.DailyDiscoveryMode)
        {
            var signature = trackedTargets.Count == 0
                ? "none"
                : string.Join(
                    " | ",
                    trackedTargets.Select((entry) => $"{entry.SourceLabel}:{entry.ObjectId}:{entry.Position.X:F2},{entry.Position.Z:F2}"));
            if (!string.Equals(signature, this.lastMapFocusMarkerDiscoverySignature, StringComparison.Ordinal))
            {
                this.lastMapFocusMarkerDiscoverySignature = signature;
                this.AppendDailyDiscovery($"Map addon tracked markers => {signature}");
            }
        }

        return trackedTargets.Count > 0;
    }

    private unsafe void TryFillLinkshellMemberContentIds(HashSet<long> sink)
    {
        try
        {
            var module = InfoModule.Instance();
            if (module == null)
            {
                return;
            }

            this.AddLinkshellContentIdsFromProxy(module, InfoProxyId.LinkshellMember, sink);
            this.AddLinkshellContentIdsFromProxy(module, InfoProxyId.CrossWorldLinkshellMember, sink);
        }
        catch
        {
        }
    }

    private unsafe void AddLinkshellContentIdsFromProxy(InfoModule* module, InfoProxyId proxyId, HashSet<long> sink)
    {
        var iface = module->GetInfoProxyById(proxyId);
        if (iface == null)
        {
            return;
        }

        var list = (InfoProxyCommonList*)iface;
        if (list->CharData == null)
        {
            return;
        }

        var count = (int)iface->GetEntryCount();
        if (count <= 0)
        {
            return;
        }

        count = Math.Min(count, 200);
        for (var i = 0u; i < (uint)count; i++)
        {
            var entry = list->GetEntry(i);
            if (entry == null)
            {
                continue;
            }

            var cid = entry->ContentId;
            if (cid == 0)
            {
                continue;
            }

            sink.Add(unchecked((long)cid));
        }
    }

    private unsafe bool TryProjectActorToAreaMap(
        Vector3 localWorldPosition,
        Vector3 actorWorldPosition,
        float actorHitboxRadius,
        out FocusTargetMapMarker marker)
    {
        marker = default;

        var addon = this.gameGui.GetAddonByName("AreaMap", 1);
        if (addon.IsNull)
        {
            addon = this.gameGui.GetAddonByName("_AreaMap", 1);
        }

        if (addon.IsNull || !addon.IsVisible || addon.Address == nint.Zero)
        {
            return false;
        }

        var areaMapAddon = (AddonAreaMap*)addon.Address;
        if (areaMapAddon == null || areaMapAddon->ComponentMap == null)
        {
            return false;
        }

        if (this.TryProjectActorToAreaMapJacobianPin(
                localWorldPosition,
                actorWorldPosition,
                actorHitboxRadius,
                areaMapAddon,
                out marker))
        {
            return true;
        }

        if (!this.TryGetPreciseMapCoordinates(actorWorldPosition, out var actorMapCoords))
        {
            return false;
        }

        var zoomScale = areaMapAddon->AreaMap.MapScale;
        if (zoomScale <= 0.001f)
        {
            zoomScale = areaMapAddon->ComponentMap->MapScale > 0.001f ? areaMapAddon->ComponentMap->MapScale : 1f;
        }

        if (TryResolveAreaMapScreenRect(areaMapAddon, out var mapMin, out var mapMax)
            && TryProjectAreaMapCoordinatesAbsolute(actorMapCoords, mapMin, mapMax, out var absolutePosition))
        {
            var markerRadius = Math.Clamp(3.0f * MathF.Max(0.55f, zoomScale), 2.0f, 5.5f);
            marker = new FocusTargetMapMarker(absolutePosition, markerRadius);
            return true;
        }

        return false;
    }

    // Linearize WorldToMap at the player so world deltas match map motion (raw map coord delta sticks to your pin).
    private unsafe bool TryProjectActorToAreaMapJacobianPin(
        Vector3 localWorldPosition,
        Vector3 actorWorldPosition,
        float actorHitboxRadius,
        AddonAreaMap* areaMapAddon,
        out FocusTargetMapMarker marker)
    {
        marker = default;

        if (areaMapAddon == null || areaMapAddon->ComponentMap == null || areaMapAddon->AreaMap.PlayerPin == null)
        {
            return false;
        }

        if (!this.TryGetPreciseMapCoordinates(localWorldPosition, out var mapLocal))
        {
            return false;
        }

        Vector2 mapDelta;
        const float eps = 1.0f;
        if (this.TryGetPreciseMapCoordinates(localWorldPosition + new Vector3(eps, 0f, 0f), out var mapPlusX)
            && this.TryGetPreciseMapCoordinates(localWorldPosition + new Vector3(0f, 0f, eps), out var mapPlusZ))
        {
            var j00 = (mapPlusX.X - mapLocal.X) / eps;
            var j01 = (mapPlusZ.X - mapLocal.X) / eps;
            var j10 = (mapPlusX.Y - mapLocal.Y) / eps;
            var j11 = (mapPlusZ.Y - mapLocal.Y) / eps;
            var dwX = actorWorldPosition.X - localWorldPosition.X;
            var dwZ = actorWorldPosition.Z - localWorldPosition.Z;
            mapDelta = new Vector2(
                (j00 * dwX) + (j01 * dwZ),
                (j10 * dwX) + (j11 * dwZ));
        }
        else if (this.TryGetPreciseMapCoordinates(actorWorldPosition, out var mapActor))
        {
            mapDelta = new Vector2(mapActor.X - mapLocal.X, mapActor.Y - mapLocal.Y);
        }
        else
        {
            return false;
        }

        var playerPinNode = &areaMapAddon->AreaMap.PlayerPin->AtkResNode;
        if (!TryGetNodeCenter(playerPinNode, out var playerPinCenter))
        {
            return false;
        }

        var markerScale = areaMapAddon->AreaMap.MarkerPositionScaling;
        var zoomScale = areaMapAddon->AreaMap.MapScale;
        if (markerScale <= 0.001f)
        {
            markerScale = areaMapAddon->ComponentMap->MapWidth > 0f ? areaMapAddon->ComponentMap->MapWidth / 41f : 1f;
        }

        if (zoomScale <= 0.001f)
        {
            zoomScale = areaMapAddon->ComponentMap->MapScale > 0.001f ? areaMapAddon->ComponentMap->MapScale : 1f;
        }

        var pixelsPerMapUnit = markerScale * zoomScale;
        var screenPosition = playerPinCenter + (mapDelta * pixelsPerMapUnit);

        var markerRadius = Math.Clamp(
            (3.2f + MathF.Min(actorHitboxRadius, 2.5f)) * MathF.Max(0.5f, zoomScale * 0.75f),
            2.2f,
            8.0f);
        marker = new FocusTargetMapMarker(screenPosition, markerRadius);

        if (TryResolveAreaMapScreenRect(areaMapAddon, out var mapMin, out var mapMax))
        {
            return screenPosition.X >= mapMin.X - 12f
                   && screenPosition.X <= mapMax.X + 12f
                   && screenPosition.Y >= mapMin.Y - 12f
                   && screenPosition.Y <= mapMax.Y + 12f;
        }

        return true;
    }

    private bool TryProjectAreaMapMarkerCoordinates(Vector3 localWorldPosition, MapMarkerBase nativeMapMarker, out FocusTargetMapMarker marker)
    {
        marker = default;
        var nativeMapCoords = new Vector2(nativeMapMarker.X / 100f, nativeMapMarker.Y / 100f);
        if (!IsFiniteMapCoordinate(nativeMapCoords)
            || !this.TryGetPreciseMapCoordinates(localWorldPosition, out var localMapCoords))
        {
            return false;
        }

        return this.TryProjectAreaMapDelta(localMapCoords, nativeMapCoords, out marker);
    }

    private unsafe bool TryProjectAreaMapDelta(Vector2 localMapCoords, Vector2 targetMapCoords, out FocusTargetMapMarker marker)
    {
        marker = default;
        var addon = this.gameGui.GetAddonByName("AreaMap", 1);
        if (addon.IsNull)
        {
            addon = this.gameGui.GetAddonByName("_AreaMap", 1);
        }

        if (addon.IsNull || !addon.IsVisible || addon.Address == nint.Zero)
        {
            return false;
        }

        var areaMapAddon = (AddonAreaMap*)addon.Address;
        if (areaMapAddon == null || areaMapAddon->ComponentMap == null)
        {
            return false;
        }

        if (!TryResolveAreaMapScreenRect(areaMapAddon, out var mapMin, out var mapMax))
        {
            return false;
        }

        var zoomScale = areaMapAddon->AreaMap.MapScale;
        if (zoomScale <= 0.001f)
        {
            zoomScale = areaMapAddon->ComponentMap->MapScale > 0.001f ? areaMapAddon->ComponentMap->MapScale : 1f;
        }

        if (!TryProjectAreaMapCoordinatesAbsolute(targetMapCoords, mapMin, mapMax, out var screenPosition))
        {
            return false;
        }

        var markerRadius = Math.Clamp(3.0f * MathF.Max(0.55f, zoomScale), 2.0f, 5.5f);
        marker = new FocusTargetMapMarker(screenPosition, markerRadius);
        return true;
    }

    private static bool TryProjectAreaMapCoordinatesAbsolute(Vector2 targetMapCoords, Vector2 mapMin, Vector2 mapMax, out Vector2 screenPosition)
    {
        screenPosition = default;

        if (!IsFiniteMapCoordinate(targetMapCoords))
        {
            return false;
        }

        var width = mapMax.X - mapMin.X;
        var height = mapMax.Y - mapMin.Y;
        if (width <= 1f || height <= 1f)
        {
            return false;
        }

        // FFXIV displayed map coordinates are effectively the 1..41 grid shown in the map UI.
        // Project directly into the visible map bounds instead of using the player pin as the origin.
        var normalizedX = Math.Clamp((targetMapCoords.X - 1f) / 40f, 0f, 1f);
        var normalizedY = Math.Clamp((targetMapCoords.Y - 1f) / 40f, 0f, 1f);

        screenPosition = new Vector2(
            mapMin.X + (normalizedX * width),
            mapMin.Y + (normalizedY * height));
        return !float.IsNaN(screenPosition.X) && !float.IsNaN(screenPosition.Y);
    }

    private unsafe bool TryGetFocusedPartyAreaMapOverlayMarker(IGameObject localPlayer, out MapAddonOverlayMarker marker)
    {
        marker = default;
        if (!this.configuration.MapAddonFocusTargetColorEnabled)
        {
            return false;
        }

        var focusTarget = this.targetManager.FocusTarget;
        if (focusTarget is null || !focusTarget.IsValid())
        {
            return false;
        }

        if (!this.TryGetFocusedPartyMember(focusTarget, out var focusedPartyMember, out var focusedPartySlot))
        {
            return false;
        }

        var agentMap = AgentMap.Instance();
        if (agentMap == null)
        {
            return false;
        }

        if (!this.TryFindFocusedPartyFullMapMarker(agentMap->MapMarkers, focusedPartyMember, focusedPartySlot, out var nativeMapMarker))
        {
            return false;
        }

        if (!this.TryProjectAreaMapMarkerCoordinates(localPlayer.Position, nativeMapMarker, out var projectedMarker))
        {
            return false;
        }

        marker = new MapAddonOverlayMarker(projectedMarker.ScreenPosition, Math.Clamp(projectedMarker.Radius * 0.7f, 2.2f, 5.4f), this.configuration.MapAddonFocusTargetColor, 30);
        return true;
    }

    private unsafe bool TryProjectActorToMiniMap(IGameObject localPlayer, Vector3 actorWorldPosition, float actorHitboxRadius, out FocusTargetMapMarker marker)
    {
        marker = default;
        var addon = this.gameGui.GetAddonByName("_NaviMap", 1);
        if (addon.IsNull)
        {
            addon = this.gameGui.GetAddonByName("NaviMap", 1);
        }

        if (addon.IsNull || !addon.IsVisible || addon.Address == nint.Zero)
        {
            return false;
        }

        var naviMapAddon = (AddonNaviMap*)addon.Address;
        var naviUnitBase = (AtkUnitBase*)addon.Address;
        if (naviMapAddon == null || naviUnitBase == null)
        {
            return false;
        }

        var zoneScale = 1f;
        if (this.TryGetCurrentMapRow(out var mapRow) && mapRow.SizeFactor > 0)
        {
            zoneScale = mapRow.SizeFactor / 100f;
        }

        var minimapScale = Math.Max(0.6f, naviUnitBase->Scale);
        var zoomScale = 1f;
        try
        {
            var zoomNode = naviUnitBase->GetNodeById(18);
            if (zoomNode != null)
            {
                zoomScale = Math.Max(0.4f, zoomNode->GetComponent()->GetImageNodeById(6)->ScaleX);
            }
        }
        catch
        {
            zoomScale = 1f;
        }

        var rotation = 0f;
        try
        {
            var rotationNode = naviUnitBase->GetNodeById(8);
            if (rotationNode != null)
            {
                rotation = rotationNode->Rotation;
            }
        }
        catch
        {
            rotation = 0f;
        }

        var mainViewport = ImGui.GetMainViewport();
        var windowPos = mainViewport.Pos;
        var mapSize = 218f * minimapScale;
        var playerPinCenter = new Vector2(
            naviUnitBase->X + (mapSize * 0.5f),
            naviUnitBase->Y + (mapSize * 0.5f)) + windowPos;

        // MiniMappingway nudges the pivot slightly upward to line up with the real minimap center.
        playerPinCenter.Y -= 5f;

        var playerWorldPosition = localPlayer.Position;
        var relativeActorPosition = new Vector2(
            playerWorldPosition.X - actorWorldPosition.X,
            playerWorldPosition.Z - actorWorldPosition.Z);
        relativeActorPosition *= zoneScale;
        relativeActorPosition *= minimapScale;
        relativeActorPosition *= zoomScale;

        var screenPosition = playerPinCenter - relativeActorPosition;
        if (!naviMapAddon->NaviMap.NorthLockedUp)
        {
            screenPosition = RotateAround(playerPinCenter, screenPosition, rotation);
        }

        var minimapRadius = mapSize * 0.315f;
        var distanceFromCenter = Vector2.Distance(playerPinCenter, screenPosition);
        if (distanceFromCenter > minimapRadius && distanceFromCenter > 0.01f)
        {
            var originToMarker = screenPosition - playerPinCenter;
            originToMarker *= minimapRadius / distanceFromCenter;
            screenPosition = playerPinCenter + originToMarker;
        }

        var markerRadius = Math.Clamp((3.2f + MathF.Min(actorHitboxRadius, 2.5f)) * MathF.Max(0.5f, zoomScale * 0.75f), 2.2f, 8.0f);
        marker = new FocusTargetMapMarker(screenPosition, markerRadius);
        return true;
    }

    private bool TryGetPreciseMapCoordinates(Vector3 worldPosition, out Vector2 mapCoords)
    {
        mapCoords = default;

        if (!this.TryGetCurrentMapRow(out var mapRow))
        {
            return false;
        }

        mapCoords = MapUtil.WorldToMap(new Vector2(worldPosition.X, worldPosition.Z), mapRow);
        return IsFiniteMapCoordinate(mapCoords);
    }

    private static void UpsertMapAddonTrackedTarget(
        IDictionary<ulong, MapAddonTrackedTarget> trackedTargets,
        MapAddonTrackedTarget candidate)
    {
        if (trackedTargets.TryGetValue(candidate.ObjectId, out var existing)
            && existing.Priority >= candidate.Priority)
        {
            return;
        }

        trackedTargets[candidate.ObjectId] = candidate;
    }

    private static MapAddonOverlayMarker? SelectBestFocusOverlayMarker(
        MapAddonOverlayMarker? nativeMarker,
        MapAddonOverlayMarker? fallbackMarker)
    {
        // If we found a focused party-member marker, trust it and draw on that path.
        // Falling back to the player-relative projection is what causes the highlight
        // to cling to the local player when the two spaces disagree.
        if (nativeMarker.HasValue)
        {
            return nativeMarker;
        }

        return fallbackMarker;
    }

    private unsafe bool TryGetMapAddonCharacter(IGameObject gameObject, out Character* character)
    {
        character = null;
        if (gameObject is null || !gameObject.IsValid() || gameObject.Address == nint.Zero)
        {
            return false;
        }

        character = (Character*)gameObject.Address;
        if (character == null
            || (Dalamud.Game.ClientState.Objects.Enums.ObjectKind)character->GameObject.ObjectKind
                != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
        {
            character = null;
            return false;
        }

        return true;
    }

    private unsafe byte[] ReadMapAddonFreeCompanyTag(IGameObject gameObject)
    {
        if (!this.TryGetMapAddonCharacter(gameObject, out var character))
        {
            return [];
        }

        try
        {
            if (character->FreeCompanyTagString.IsNullOrEmpty())
            {
                return [];
            }

            return character->FreeCompanyTag.ToArray();
        }
        catch
        {
            return [];
        }
    }

    private bool TryGetCurrentMapRow(out Map mapRow)
    {
        mapRow = default;

        try
        {
            var mapSheet = this.dataManager.GetExcelSheet<Map>();
            if (mapSheet is null || !mapSheet.TryGetRow(this.clientState.MapId, out mapRow))
            {
                return false;
            }

            return mapRow.RowId != 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsFiniteMapCoordinate(Vector2 mapCoords)
    {
        return !float.IsNaN(mapCoords.X)
               && !float.IsNaN(mapCoords.Y)
               && !float.IsInfinity(mapCoords.X)
               && !float.IsInfinity(mapCoords.Y);
    }

    private static Vector2 RotateVector(Vector2 value, float radians)
    {
        var cosine = MathF.Cos(radians);
        var sine = MathF.Sin(radians);
        return new Vector2(
            (value.X * cosine) - (value.Y * sine),
            (value.X * sine) + (value.Y * cosine));
    }

    private static Vector2 RotateAround(Vector2 center, Vector2 value, float radians)
    {
        var rotatedDelta = RotateVector(value - center, radians);
        return center + rotatedDelta;
    }

    private unsafe static float GetGameObjectRotation(IGameObject gameObject)
    {
        if (gameObject.Address == nint.Zero)
        {
            return 0f;
        }

        var nativeGameObject = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)gameObject.Address;
        return nativeGameObject == null ? 0f : nativeGameObject->Rotation;
    }

    private unsafe static bool TryGetNodeCenter(AtkResNode* node, out Vector2 center)
    {
        center = default;
        if (node == null)
        {
            return false;
        }

        var width = Math.Max(1f, node->Width * Math.Max(0.01f, node->ScaleX));
        var height = Math.Max(1f, node->Height * Math.Max(0.01f, node->ScaleY));
        center = new Vector2(node->ScreenX + (width * 0.5f), node->ScreenY + (height * 0.5f));
        return !float.IsNaN(center.X) && !float.IsNaN(center.Y);
    }

    private unsafe static bool TryGetMapBounds(AtkResNode* node, out Vector2 min, out Vector2 max)
    {
        min = default;
        max = default;
        if (node == null)
        {
            return false;
        }

        FFXIVClientStructs.FFXIV.Common.Math.Bounds bounds = default;
        node->GetBounds(&bounds);

        min = new Vector2(bounds.Pos1.X, bounds.Pos1.Y);
        max = new Vector2(bounds.Pos2.X, bounds.Pos2.Y);
        return max.X > min.X && max.Y > min.Y;
    }

    // GetBounds on the map surface can be empty; use ScreenX/Y + size as fallback so absolute projection still runs.
    private unsafe static bool TryResolveAreaMapScreenRect(AddonAreaMap* areaMapAddon, out Vector2 mapMin, out Vector2 mapMax)
    {
        mapMin = default;
        mapMax = default;
        if (areaMapAddon == null || areaMapAddon->ComponentMap == null)
        {
            return false;
        }

        var node = areaMapAddon->ComponentMap->AtkResNode;
        if (node is null)
        {
            return false;
        }

        if (TryGetMapBounds(node, out mapMin, out mapMax))
        {
            var bw = mapMax.X - mapMin.X;
            var bh = mapMax.Y - mapMin.Y;
            if (bw > 4f && bh > 4f)
            {
                return true;
            }
        }

        var width = Math.Max(8f, node->Width * Math.Max(0.01f, node->ScaleX));
        var height = Math.Max(8f, node->Height * Math.Max(0.01f, node->ScaleY));
        mapMin = new Vector2(node->ScreenX, node->ScreenY);
        mapMax = new Vector2(node->ScreenX + width, node->ScreenY + height);
        return width > 4f && height > 4f && mapMax.X > mapMin.X && mapMax.Y > mapMin.Y;
    }

    private IGameObject? ResolveRoleplayBubbleAnchor(string speakerName, bool isOwnMessage)
    {
        if (isOwnMessage)
        {
            return this.objectTable.LocalPlayer;
        }

        var normalizedSpeaker = ExtractRoleplayBubbleCharacterName(speakerName);
        if (string.IsNullOrWhiteSpace(normalizedSpeaker))
        {
            return null;
        }

        return this.objectTable
            .Where((entry) => entry is not null && entry.IsValid())
            .Select((entry) => new
            {
                Entry = entry,
                Name = ExtractRoleplayBubbleCharacterName(entry.Name.TextValue),
            })
            .Where((entry) => !string.IsNullOrWhiteSpace(entry.Name))
            .OrderByDescending((entry) => string.Equals(entry.Name, normalizedSpeaker, StringComparison.OrdinalIgnoreCase))
            .ThenBy((entry) => Vector3.DistanceSquared(entry.Entry.Position, this.objectTable.LocalPlayer?.Position ?? Vector3.Zero))
            .Select((entry) => entry.Entry)
            .FirstOrDefault((entry) => string.Equals(
                ExtractRoleplayBubbleCharacterName(entry.Name.TextValue),
                normalizedSpeaker,
                StringComparison.OrdinalIgnoreCase));
    }

    private unsafe bool TryProjectRoleplayBubblePosition(IGameObject actor, bool isOwnMessage, float yOffset, out Vector2 screenPosition)
    {
        var actorPosition = actor.Position;
        var hitboxRadius = actor.HitboxRadius;
        var modelHeight = this.GetRoleplayBubbleActorHeight(actor);
        if (this.TryGetRoleplayBubbleModelAnchorPosition(actor, isOwnMessage, yOffset, modelHeight, out var modelAnchorWorldPosition)
            && this.gameGui.WorldToScreen(modelAnchorWorldPosition, out screenPosition))
        {
            return true;
        }

        return this.TryProjectRoleplayBubblePosition(actorPosition, hitboxRadius, modelHeight, isOwnMessage, yOffset, out screenPosition);
    }

    private unsafe float GetRoleplayBubbleActorHeight(IGameObject actor)
    {
        if (actor.Address == nint.Zero)
        {
            return MathF.Max(1.2f, actor.HitboxRadius * 2f);
        }

        var nativeActor = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)actor.Address;
        if (nativeActor == null)
        {
            return MathF.Max(1.2f, actor.HitboxRadius * 2f);
        }

        return nativeActor->Height > 0.3f ? nativeActor->Height : MathF.Max(1.2f, actor.HitboxRadius * 2f);
    }

    private unsafe bool TryGetRoleplayBubbleModelAnchorPosition(
        IGameObject actor,
        bool isOwnMessage,
        float yOffset,
        float modelHeight,
        out Vector3 worldPosition)
    {
        worldPosition = default;
        if (actor.Address == nint.Zero)
        {
            return false;
        }

        var nativeActor = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)actor.Address;
        if (nativeActor == null)
        {
            return false;
        }

        var nameplateWorldCs = default(FFXIVClientStructs.FFXIV.Common.Math.Vector3);
        _ = nativeActor->GetNamePlateWorldPosition(&nameplateWorldCs);
        var nameplateWorld = new Vector3(nameplateWorldCs.X, nameplateWorldCs.Y, nameplateWorldCs.Z);
        var relNp = nameplateWorld - actor.Position;
        if (relNp.LengthSquared() > 1e-4f && relNp.Y > -0.4f)
        {
            var h = modelHeight > 0.3f ? modelHeight : MathF.Max(1f, actor.HitboxRadius * 2f);
            var lift = Math.Clamp(h * 0.055f, 0.028f, 0.26f);
            var userNudge = Math.Clamp(yOffset, 0.05f, 0.95f) * h * 0.035f;
            worldPosition = nameplateWorld + new Vector3(0f, lift + userNudge, 0f);
            return true;
        }

        var nameplateOffset = new Vector3(
            nativeActor->NameplateOffset.X,
            nativeActor->NameplateOffset.Y,
            nativeActor->NameplateOffset.Z);
        var nameplateTarget = new Vector3(
            nativeActor->NameplateOffsetTarget.X,
            nativeActor->NameplateOffsetTarget.Y,
            nativeActor->NameplateOffsetTarget.Z);
        var cameraOffset = new Vector3(
            nativeActor->CameraOffset.X,
            nativeActor->CameraOffset.Y,
            nativeActor->CameraOffset.Z);
        var cameraTarget = new Vector3(
            nativeActor->CameraOffsetTarget.X,
            nativeActor->CameraOffsetTarget.Y,
            nativeActor->CameraOffsetTarget.Z);
        var scaleMultiplier = nativeActor->NameplateOffsetScaleMultiplier;
        var nameplateAnchor = nameplateTarget;
        if (nameplateOffset.LengthSquared() > 0.0001f)
        {
            nameplateAnchor += nameplateOffset * Math.Max(0.25f, Math.Min(1f, scaleMultiplier));
        }

        var cameraAnchor = cameraTarget;
        if (cameraOffset.LengthSquared() > 0.0001f)
        {
            cameraAnchor += cameraOffset * 0.25f;
        }

        var hasNameplateAnchor = nameplateAnchor.Y > 0f;
        var hasCameraAnchor = cameraAnchor.Y > 0f;
        Vector3 anchorOffset;

        if (hasCameraAnchor && hasNameplateAnchor)
        {
            anchorOffset = nameplateAnchor.Y >= cameraAnchor.Y ? nameplateAnchor : cameraAnchor;
        }
        else if (hasNameplateAnchor)
        {
            anchorOffset = nameplateAnchor;
        }
        else if (hasCameraAnchor)
        {
            anchorOffset = cameraAnchor;
        }
        else
        {
            return false;
        }

        var hFallback = modelHeight > 0.3f ? modelHeight : MathF.Max(1f, actor.HitboxRadius * 2f);
        var extraYOffset = Math.Clamp(yOffset, 0.05f, 0.95f) * hFallback * (isOwnMessage ? 0.028f : 0.022f);
        worldPosition = actor.Position + anchorOffset + new Vector3(0f, extraYOffset, 0f);
        return true;
    }

    private bool TryProjectRoleplayBubblePosition(
        Vector3 actorPosition,
        float hitboxRadius,
        float modelHeight,
        bool isOwnMessage,
        float yOffset,
        out Vector2 screenPosition)
    {
        var effectiveHeight = MathF.Max(modelHeight, hitboxRadius * 2f);
        var headHeight = isOwnMessage
            ? Math.Clamp((effectiveHeight * 0.9f) + (yOffset * 0.08f), 1.05f, 2.5f)
            : Math.Clamp((effectiveHeight * 0.86f) + (yOffset * 0.07f), 0.95f, 2.4f);
        var headWorldPosition = actorPosition + new Vector3(0f, headHeight, 0f);
        if (this.gameGui.WorldToScreen(headWorldPosition, out screenPosition))
        {
            return true;
        }

        if (!this.gameGui.WorldToScreen(actorPosition, out var bodyScreenPosition))
        {
            screenPosition = default;
            return false;
        }

        var verticalPixels = isOwnMessage
            ? 72f + (Math.Max(0.45f, hitboxRadius) * 26f)
            : 62f + (Math.Max(0.45f, hitboxRadius) * 20f);
        screenPosition = bodyScreenPosition - new Vector2(0f, verticalPixels);
        return true;
    }

    private static bool ShouldTrackRoleplayBubbleChatType(XivChatType type, bool captureAllChat)
    {
        return type switch
        {
            XivChatType.Say => true,
            XivChatType.Shout => true,
            XivChatType.Yell => true,
            XivChatType.CustomEmote => true,
            XivChatType.StandardEmote => true,
            XivChatType.TellIncoming => true,
            XivChatType.TellOutgoing => true,
            XivChatType.Party => true,
            XivChatType.Alliance => captureAllChat,
            XivChatType.CrossParty => captureAllChat,
            XivChatType.FreeCompany => true,
            XivChatType.NoviceNetwork => captureAllChat,
            XivChatType.Ls1 => captureAllChat,
            XivChatType.Ls2 => captureAllChat,
            XivChatType.Ls3 => captureAllChat,
            XivChatType.Ls4 => captureAllChat,
            XivChatType.Ls5 => captureAllChat,
            XivChatType.Ls6 => captureAllChat,
            XivChatType.Ls7 => captureAllChat,
            XivChatType.Ls8 => captureAllChat,
            XivChatType.CrossLinkShell1 => captureAllChat,
            XivChatType.CrossLinkShell2 => captureAllChat,
            XivChatType.CrossLinkShell3 => captureAllChat,
            XivChatType.CrossLinkShell4 => captureAllChat,
            XivChatType.CrossLinkShell5 => captureAllChat,
            XivChatType.CrossLinkShell6 => captureAllChat,
            XivChatType.CrossLinkShell7 => captureAllChat,
            XivChatType.CrossLinkShell8 => captureAllChat,
            XivChatType.Echo => captureAllChat,
            _ => false,
        };
    }

    private static bool ShouldTrackNativeSpeechBubbleChatType(XivChatType type)
    {
        return type switch
        {
            XivChatType.Say => true,
            XivChatType.Shout => true,
            XivChatType.Yell => true,
            XivChatType.CustomEmote => true,
            XivChatType.StandardEmote => true,
            _ => false,
        };
    }

    private static string GetRoleplayBubbleChannelLabel(XivChatType type)
    {
        return type switch
        {
            XivChatType.Say => "Say",
            XivChatType.Shout => "Shout",
            XivChatType.Yell => "Yell",
            XivChatType.CustomEmote => "Custom Emote",
            XivChatType.StandardEmote => "Emote",
            XivChatType.TellIncoming or XivChatType.TellOutgoing => "Tell",
            XivChatType.Party => "Party",
            XivChatType.Alliance => "Alliance",
            XivChatType.CrossParty => "Cross-party",
            XivChatType.FreeCompany => "Free Company",
            XivChatType.NoviceNetwork => "Novice Network",
            XivChatType.Ls1 or XivChatType.Ls2 or XivChatType.Ls3 or XivChatType.Ls4 or XivChatType.Ls5 or XivChatType.Ls6 or XivChatType.Ls7 or XivChatType.Ls8 => "Linkshell",
            XivChatType.CrossLinkShell1 or XivChatType.CrossLinkShell2 or XivChatType.CrossLinkShell3 or XivChatType.CrossLinkShell4 or XivChatType.CrossLinkShell5 or XivChatType.CrossLinkShell6 or XivChatType.CrossLinkShell7 or XivChatType.CrossLinkShell8 => "Cross-world Linkshell",
            XivChatType.Echo => "Echo",
            _ => type.ToString(),
        };
    }

    private static string NormalizeRoleplayBubbleSpeaker(string value)
    {
        var normalized = NormalizeRoleplayBubbleText(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return NormalizeRoleplayBubbleIdentity(normalized);
    }

    private static string ExtractRoleplayBubbleSpeaker(SeString sender)
    {
        foreach (var payload in sender.Payloads)
        {
            if (payload is not PlayerPayload playerPayload)
            {
                continue;
            }

            var playerName = ExtractRoleplayBubbleCharacterName(playerPayload.PlayerName?.ToString() ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(playerName))
            {
                return playerName;
            }

            var displayedName = ExtractRoleplayBubbleCharacterName(playerPayload.DisplayedName?.ToString() ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(displayedName))
            {
                return displayedName;
            }
        }

        return ExtractRoleplayBubbleCharacterName(sender.TextValue);
    }

    private static string ExtractRoleplayBubbleCharacterName(string value)
    {
        var normalized = StripRoleplayBubbleCompanyTagsSafe(NormalizeRoleplayBubbleIdentity(value));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(normalized.Length);
        var sawNonSpace = false;
        var lastWasSpace = false;

        foreach (var ch in normalized)
        {
            if (char.IsLetter(ch) || ch == '\'' || ch == '-')
            {
                builder.Append(ch);
                sawNonSpace = true;
                lastWasSpace = false;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (sawNonSpace && !lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            if (sawNonSpace)
            {
                break;
            }
        }

        var collapsed = builder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(collapsed))
        {
            return string.Empty;
        }

        var parts = collapsed
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length >= 2
            ? $"{parts[0]} {parts[1]}"
            : collapsed;
    }

    private static string StripRoleplayBubbleCompanyTags(string value)
    {
        var normalized = NormalizeRoleplayBubbleText(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var freeCompanyTagIndex = normalized.IndexOf('«');
        if (freeCompanyTagIndex <= 0)
        {
            freeCompanyTagIndex = normalized.IndexOf("Â«", StringComparison.Ordinal);
        }

        if (freeCompanyTagIndex > 0)
        {
            normalized = normalized[..freeCompanyTagIndex];
        }

        return normalized.Trim();
    }

    private static string StripRoleplayBubbleCompanyTagsSafe(string value)
    {
        var normalized = NormalizeRoleplayBubbleText(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var freeCompanyTagIndex = normalized.IndexOf("\u00AB", StringComparison.Ordinal);
        if (freeCompanyTagIndex <= 0)
        {
            freeCompanyTagIndex = normalized.IndexOf("\u00C2\u00AB", StringComparison.Ordinal);
        }

        if (freeCompanyTagIndex > 0)
        {
            normalized = normalized[..freeCompanyTagIndex];
        }

        return normalized.Trim();
    }

    private static string NormalizeRoleplayBubbleIdentity(string value)
    {
        var normalized = NormalizeRoleplayBubbleText(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var worldSeparatorIndex = normalized.IndexOf('@');
        if (worldSeparatorIndex > 0)
        {
            normalized = normalized[..worldSeparatorIndex];
        }

        var freeCompanyTagIndex = normalized.IndexOf('«');
        if (freeCompanyTagIndex > 0)
        {
            normalized = normalized[..freeCompanyTagIndex];
        }

        normalized = normalized.TrimEnd(':').Trim();
        return normalized;
    }

    private static string NormalizeRoleplayBubbleText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Replace("\u0085", " ", StringComparison.Ordinal)
            .Replace("\u2028", " ", StringComparison.Ordinal)
            .Replace("\u2029", " ", StringComparison.Ordinal)
            .Replace("\v", " ", StringComparison.Ordinal)
            .Replace("\f", " ", StringComparison.Ordinal);

        while (normalized.Contains("  ", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        }

        return normalized.Trim();
    }

}
