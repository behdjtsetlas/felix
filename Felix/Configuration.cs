using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace Felix;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public string DashboardBaseUrl { get; set; } = "https://felixthebot.com";
    public string DeviceLabel { get; set; } = "Felixthebot Desktop";
    public string PairingToken { get; set; } = string.Empty;
    public string DeviceToken { get; set; } = string.Empty;
    public string SelectedGuildId { get; set; } = string.Empty;
    public string InstallationId { get; set; } = Guid.NewGuid().ToString("N");

    public bool AutoSyncEnabled { get; set; } = true;
    public int SyncIntervalSeconds { get; set; } = 30;
    public bool IncludeCurrencies { get; set; } = true;
    public bool IncludeCollections { get; set; } = true;
    public bool IncludeCombat { get; set; } = false;
    public bool IncludeSocialRadar { get; set; } = true;
    public bool IncludeRoleplayFeedInSync { get; set; } = false;
    public bool RpSceneRecordingEnabled { get; set; } = false;
    public bool RpSceneRecordingPaused { get; set; } = false;
    public int RpSceneAutoFlushIntervalSeconds { get; set; } = 30;
    public string RpSceneSessionNote { get; set; } = string.Empty;
    public bool RpSceneNotifyFirstCapturedLine { get; set; } = true;
    public bool RpSceneJoined { get; set; } = false;
    public string RpSceneJoinCode { get; set; } = string.Empty;
    public string RpSceneJoinDraft { get; set; } = string.Empty;
    public string RpSceneId { get; set; } = string.Empty;
    public string RpSceneDisplayTitle { get; set; } = string.Empty;
    public string RpSceneLastJoinError { get; set; } = string.Empty;
    public bool RpSceneShowRecordingHud { get; set; } = true;
    public bool FcDiscoveryMode { get; set; } = false;
    public bool DailyDiscoveryMode { get; set; } = false;
    public bool WeeklyDiscoveryMode { get; set; } = false;
    public bool RoleplayBubbleChatEnabled { get; set; } = false;
    public bool RoleplayOverheadBubbleEnabled { get; set; } = false;
    public bool NativeSpeechBubbleResizeEnabled { get; set; } = false;
    public bool NativeSpeechBubbleDiscoveryMode { get; set; } = false;
    public bool RoleplayBubbleCaptureAllChat { get; set; } = false;
    public int RoleplayBubbleLifetimeSeconds { get; set; } = 45;
    public int RoleplayBubbleMaxVisible { get; set; } = 4;
    public float RoleplayBubbleScale { get; set; } = 1.2f;
    public int RoleplayOverheadBubbleLifetimeSeconds { get; set; } = 8;
    public float RoleplayOverheadBubbleScale { get; set; } = 0.82f;
    public float RoleplayOverheadBubbleYOffset { get; set; } = 0.35f;
    public bool SuppressNativeSpeechBubbleWhenOverheadEnabled { get; set; } = true;
    public bool MapAddonFocusTargetColorEnabled { get; set; } = false;
    public Vector4 MapAddonFocusTargetColor { get; set; } = new(1.0f, 0.25f, 0.35f, 1.0f);
    public bool MapAddonFriendColorEnabled { get; set; } = false;
    public Vector4 MapAddonFriendColor { get; set; } = new(0.957f, 0.533f, 0.051f, 1.0f);
    public bool MapAddonFreeCompanyColorEnabled { get; set; } = false;
    public Vector4 MapAddonFreeCompanyColor { get; set; } = new(1.0f, 0.15f, 0.15f, 1.0f);
    public bool MapAddonLinkshellColorEnabled { get; set; } = false;
    public Vector4 MapAddonLinkshellColor { get; set; } = new(0.35f, 0.82f, 0.95f, 1.0f);
    public FelixDailyTrackerState DailyTracker { get; set; } = new();
    public FelixDailyNativeState DailyNative { get; set; } = new();
    public FelixWeeklyTrackerState WeeklyTracker { get; set; } = new();

    public string LastSyncAtUtc { get; set; } = string.Empty;
    public string LastSyncStatus { get; set; } = "Not synced yet";
    public string LastPairedAtUtc { get; set; } = string.Empty;

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
    }

    public void Save()
    {
        this.pluginInterface?.SavePluginConfig(this);
    }

    public void EnsureCurrentDailyTrackerDay()
    {
        var todayKey = DateTime.Now.ToString("yyyy-MM-dd");
        if (this.DailyTracker is null)
        {
            this.DailyTracker = new FelixDailyTrackerState { DayKey = todayKey };
            return;
        }

        if (!string.Equals(this.DailyTracker.DayKey, todayKey, StringComparison.Ordinal))
        {
            this.DailyTracker.DayKey = todayKey;
            this.DailyTracker.CompletedDuties.Clear();
            this.DailyTracker.LastDutyCompletedAtUtc = string.Empty;
        }
    }
}

[Serializable]
public sealed class FelixDailyTrackerState
{
    public string DayKey { get; set; } = string.Empty;
    public string LastDutyCompletedAtUtc { get; set; } = string.Empty;
    public List<FelixCompletedDutyState> CompletedDuties { get; set; } = [];
}

[Serializable]
public sealed class FelixDailyNativeState
{
    public FelixNativeDailySystemState MiniCactpot { get; set; } = new();
    public FelixNativeDailySystemState TribalQuests { get; set; } = new();
    public FelixNativeDailySystemState HuntBills { get; set; } = new();
    public FelixNativeDailySystemState GcTurnIns { get; set; } = new();
    public FelixNativeDailySystemState OtherDailySystems { get; set; } = new();
}

[Serializable]
public sealed class FelixNativeDailySystemState
{
    public bool Synced { get; set; }
    public bool Completed { get; set; }
    public string SyncedAtUtc { get; set; } = string.Empty;
    public string NativeSource { get; set; } = string.Empty;
    public string ProgressLabel { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public List<string> Items { get; set; } = [];
}

[Serializable]
public sealed class FelixCompletedDutyState
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CompletedAtUtc { get; set; } = string.Empty;
}

[Serializable]
public sealed class FelixWeeklyTrackerState
{
    public FelixWondrousTailsState WondrousTails { get; set; } = new();
    public FelixCustomDeliveriesState CustomDeliveries { get; set; } = new();
}

[Serializable]
public sealed class FelixWondrousTailsState
{
    public bool Synced { get; set; }
    public string SyncedAtUtc { get; set; } = string.Empty;
    public bool HasJournal { get; set; }
    public int SealCount { get; set; }
    public int SecondChancePoints { get; set; }
    public string Deadline { get; set; } = string.Empty;
    public List<uint> DutyIds { get; set; } = [];
    public List<string> DutyNames { get; set; } = [];
}

[Serializable]
public sealed class FelixCustomDeliveriesState
{
    public bool Synced { get; set; }
    public string SyncedAtUtc { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}
