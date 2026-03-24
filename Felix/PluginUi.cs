using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Felix;

public sealed class PluginUi : IDisposable
{
    private readonly FelixPlugin plugin;
    private bool rpSceneLeavePopupOpen;
    public bool IsOpen { get; set; }

    public PluginUi(FelixPlugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var configuration = this.plugin.Configuration;
        var hidUnusedBubbleControls = false;
        if (configuration.RoleplayBubbleChatEnabled)
        {
            configuration.RoleplayBubbleChatEnabled = false;
            hidUnusedBubbleControls = true;
        }

        if (configuration.NativeSpeechBubbleResizeEnabled)
        {
            configuration.NativeSpeechBubbleResizeEnabled = false;
            hidUnusedBubbleControls = true;
        }

        if (configuration.NativeSpeechBubbleDiscoveryMode)
        {
            configuration.NativeSpeechBubbleDiscoveryMode = false;
            hidUnusedBubbleControls = true;
        }

        if (hidUnusedBubbleControls)
        {
            this.plugin.SaveConfiguration();
        }

        this.DrawRoleplayOverheadBubbleOverlay(configuration);
        this.DrawRoleplayBubbleOverlay(configuration);
        this.DrawFocusTargetMapOverlay(configuration);
        this.DrawRpSceneRecordingHud(configuration);

        if (!this.IsOpen)
        {
            return;
        }

        const int settingsWrapChars = 45;
        var settingsWindowWidth = ImGui.CalcTextSize(new string('W', settingsWrapChars)).X
            + (ImGui.GetStyle().WindowPadding.X * 2f)
            + ImGui.GetStyle().ScrollbarSize
            + 6f;
        ImGui.SetNextWindowSizeConstraints(new Vector2(settingsWindowWidth, 280f), new Vector2(settingsWindowWidth, 8000f));
        ImGui.SetNextWindowSize(new Vector2(settingsWindowWidth, 440f), ImGuiCond.FirstUseEver);

        var isOpen = this.IsOpen;
        if (!ImGui.Begin("Felix", ref isOpen))
        {
            this.IsOpen = isOpen;
            ImGui.End();
            return;
        }

        if (ImGui.BeginTabBar("##FelixMainTabs"))
        {
            if (ImGui.BeginTabItem("Settings"))
            {
                this.DrawFelixSettingsTab(configuration);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Map Addon"))
            {
                this.DrawFelixMapTab(configuration);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Roleplay"))
            {
                this.DrawFelixRoleplayTab(configuration);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        if (ImGui.BeginPopupModal("FelixLeaveRpScene##fx", ref this.rpSceneLeavePopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            var q = this.plugin.GetRpScenePendingLineCount();
            ImGui.TextWrapped(
                $"You have {q} line(s) not uploaded yet. Upload them first, leave and discard them, or cancel.");
            if (ImGui.Button("Upload queued lines, then leave"))
            {
                ImGui.CloseCurrentPopup();
                _ = this.plugin.UploadQueuedLinesThenLeaveRpSceneAsync();
            }

            ImGui.SameLine();
            if (ImGui.Button("Discard queue & leave"))
            {
                ImGui.CloseCurrentPopup();
                this.plugin.LeaveRpScene();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        this.IsOpen = isOpen;
        ImGui.End();
    }

    public void Dispose()
    {
    }

    private void DrawFelixSettingsTab(Configuration configuration)
    {
        ImGui.TextWrapped("Felix pairs your FFXIV client with the Felix dashboard so you can build a live command center for currencies, collections, progression, and future combat uploads.");

        var dashboardBaseUrl = configuration.DashboardBaseUrl;
        if (ImGui.InputText("Dashboard Base URL", ref dashboardBaseUrl, 256))
        {
            configuration.DashboardBaseUrl = dashboardBaseUrl;
        }

        var deviceLabel = configuration.DeviceLabel;
        if (ImGui.InputText("Device Label", ref deviceLabel, 128))
        {
            configuration.DeviceLabel = deviceLabel;
        }

        var pairingToken = configuration.PairingToken;
        if (ImGui.InputText("Pairing Token", ref pairingToken, 256))
        {
            configuration.PairingToken = pairingToken;
        }

        ImGui.TextWrapped($"Installation ID: {configuration.InstallationId}");
        var syncScope = string.IsNullOrWhiteSpace(configuration.SelectedGuildId)
            ? "Global Felix Profile"
            : configuration.SelectedGuildId;
        ImGui.TextWrapped($"Sync Scope: {syncScope}");
        ImGui.TextWrapped($"Last Paired: {FormatUtc(configuration.LastPairedAtUtc)}");
        ImGui.TextWrapped($"Last Sync: {FormatUtc(configuration.LastSyncAtUtc)}");
        ImGui.TextWrapped($"Status: {configuration.LastSyncStatus}");

        var autoSyncEnabled = configuration.AutoSyncEnabled;
        if (ImGui.Checkbox("Auto Sync", ref autoSyncEnabled))
        {
            configuration.AutoSyncEnabled = autoSyncEnabled;
        }

        var includeCurrencies = configuration.IncludeCurrencies;
        if (ImGui.Checkbox("Include Currencies", ref includeCurrencies))
        {
            configuration.IncludeCurrencies = includeCurrencies;
        }

        var includeCollections = configuration.IncludeCollections;
        if (ImGui.Checkbox("Include Collections", ref includeCollections))
        {
            configuration.IncludeCollections = includeCollections;
        }

        var includeCombat = configuration.IncludeCombat;
        if (ImGui.Checkbox("Include Combat (future parser feed)", ref includeCombat))
        {
            configuration.IncludeCombat = includeCombat;
        }

        var includeSocialRadar = configuration.IncludeSocialRadar;
        if (ImGui.Checkbox("Include Social radar (friends, FC & party in range on sync)", ref includeSocialRadar))
        {
            configuration.IncludeSocialRadar = includeSocialRadar;
        }

        var fcDiscoveryMode = configuration.FcDiscoveryMode;
        if (ImGui.Checkbox("FC Discovery Mode", ref fcDiscoveryMode))
        {
            configuration.FcDiscoveryMode = fcDiscoveryMode;
        }

        var dailyDiscoveryMode = configuration.DailyDiscoveryMode;
        if (ImGui.Checkbox("Daily Discovery Mode", ref dailyDiscoveryMode))
        {
            configuration.DailyDiscoveryMode = dailyDiscoveryMode;
        }

        var weeklyDiscoveryMode = configuration.WeeklyDiscoveryMode;
        if (ImGui.Checkbox("Weekly Discovery Mode", ref weeklyDiscoveryMode))
        {
            configuration.WeeklyDiscoveryMode = weeklyDiscoveryMode;
        }

        var syncInterval = configuration.SyncIntervalSeconds;
        if (ImGui.InputInt("Sync Interval (seconds)", ref syncInterval))
        {
            configuration.SyncIntervalSeconds = Math.Clamp(syncInterval, 10, 600);
        }

        if (ImGui.Button("Save Settings"))
        {
            this.plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        if (ImGui.Button("Pair Device"))
        {
            _ = this.plugin.PairAsync();
        }

        ImGui.SameLine();
        if (ImGui.Button("Sync Now"))
        {
            _ = this.plugin.SyncNowAsync();
        }

        ImGui.SameLine();
        if (ImGui.Button("Forget Device"))
        {
            this.plugin.ClearDeviceToken();
        }

        ImGui.Separator();
        ImGui.TextWrapped("Free Company discovery helps Felix find the exact in-game data sources for missing FC fields like leader, member count, and estate details.");
        if (ImGui.Button("Inspect Local FC Fields"))
        {
            this.plugin.InspectFreeCompanyFields();
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear FC Discovery Log"))
        {
            this.plugin.ClearFreeCompanyDiscovery();
        }

        ImGui.Separator();
        ImGui.TextWrapped("Daily discovery helps Felix identify the native in-game addon/state sources for Mini Cactpot, Tribal Quests, Hunt Bills, GC turn-ins, and other daily-limited systems. Turn it on, then open those windows in game.");
        if (ImGui.Button("Clear Daily Discovery Log"))
        {
            this.plugin.ClearDailyDiscovery();
        }

        ImGui.Separator();
        ImGui.TextWrapped("Native speech bubble discovery logs the live active MiniTalk bubble entry, including text flags, node sizes, and measured draw size. Turn it on only while testing the native bubble path.");
        if (ImGui.Button("Clear Native Bubble Discovery Log"))
        {
            this.plugin.ClearNativeBubbleDiscovery();
        }

        ImGui.Separator();
        ImGui.TextWrapped("Weekly discovery helps Felix identify the in-game addon/state sources for Wondrous Tails and Custom Deliveries. Turn it on, then open those windows in game.");
        if (ImGui.Button("Clear Weekly Discovery Log"))
        {
            this.plugin.ClearWeeklyDiscovery();
        }
    }

    private void DrawFelixMapTab(Configuration configuration)
    {
        ImGui.TextWrapped("Map Addon draws MiniMappingway-style custom colored markers on the minimap only. Focus targets, friends, FC members, and linkshell mates can each have their own color.");

        var mapAddonFocusTargetColorEnabled = configuration.MapAddonFocusTargetColorEnabled;
        if (ImGui.Checkbox("Enable Focus Target Color", ref mapAddonFocusTargetColorEnabled))
        {
            configuration.MapAddonFocusTargetColorEnabled = mapAddonFocusTargetColorEnabled;
        }

        var mapAddonFocusTargetColor = configuration.MapAddonFocusTargetColor;
        if (ImGui.ColorEdit4("Focus Target Color", ref mapAddonFocusTargetColor))
        {
            configuration.MapAddonFocusTargetColor = mapAddonFocusTargetColor;
        }

        var mapAddonFriendColorEnabled = configuration.MapAddonFriendColorEnabled;
        if (ImGui.Checkbox("Enable Friend Color", ref mapAddonFriendColorEnabled))
        {
            configuration.MapAddonFriendColorEnabled = mapAddonFriendColorEnabled;
        }

        var mapAddonFriendColor = configuration.MapAddonFriendColor;
        if (ImGui.ColorEdit4("Friend Color", ref mapAddonFriendColor))
        {
            configuration.MapAddonFriendColor = mapAddonFriendColor;
        }

        var mapAddonFreeCompanyColorEnabled = configuration.MapAddonFreeCompanyColorEnabled;
        if (ImGui.Checkbox("Enable FC Member Color", ref mapAddonFreeCompanyColorEnabled))
        {
            configuration.MapAddonFreeCompanyColorEnabled = mapAddonFreeCompanyColorEnabled;
        }

        var mapAddonFreeCompanyColor = configuration.MapAddonFreeCompanyColor;
        if (ImGui.ColorEdit4("FC Member Color", ref mapAddonFreeCompanyColor))
        {
            configuration.MapAddonFreeCompanyColor = mapAddonFreeCompanyColor;
        }

        var mapAddonLinkshellColorEnabled = configuration.MapAddonLinkshellColorEnabled;
        if (ImGui.Checkbox("Enable Linkshell Member Color", ref mapAddonLinkshellColorEnabled))
        {
            configuration.MapAddonLinkshellColorEnabled = mapAddonLinkshellColorEnabled;
        }

        var mapAddonLinkshellColor = configuration.MapAddonLinkshellColor;
        if (ImGui.ColorEdit4("Linkshell Member Color", ref mapAddonLinkshellColor))
        {
            configuration.MapAddonLinkshellColor = mapAddonLinkshellColor;
        }
    }

    private void DrawFelixRoleplayTab(Configuration configuration)
    {
        ImGui.TextWrapped("Over-head RP Bubble shows tracked chat as a compact bubble above the speaker while hiding the native speech bubble.");

        var roleplayOverheadBubbleEnabled = configuration.RoleplayOverheadBubbleEnabled;
        if (ImGui.Checkbox("Enable Over-head RP Bubble", ref roleplayOverheadBubbleEnabled))
        {
            configuration.RoleplayOverheadBubbleEnabled = roleplayOverheadBubbleEnabled;
        }

        var roleplayBubbleCaptureAllChat = configuration.RoleplayBubbleCaptureAllChat;
        if (ImGui.Checkbox("Capture all chat channels", ref roleplayBubbleCaptureAllChat))
        {
            configuration.RoleplayBubbleCaptureAllChat = roleplayBubbleCaptureAllChat;
        }

        ImGui.TextWrapped("Say, shout, yell, emotes, tells, party, and free company are always captured. Enable this for alliance, cross-party, novice network, linkshells, cross-world linkshells, and echo.");

        var suppressNativeSpeechBubble = configuration.SuppressNativeSpeechBubbleWhenOverheadEnabled;
        if (ImGui.Checkbox("Hide native speech bubble while RP bubble is on", ref suppressNativeSpeechBubble))
        {
            configuration.SuppressNativeSpeechBubbleWhenOverheadEnabled = suppressNativeSpeechBubble;
        }

        if (ImGui.Button("Clear RP Bubble Chat"))
        {
            this.plugin.ClearRoleplayBubbleMessages();
        }

        var includeRoleplayFeedInSync = configuration.IncludeRoleplayFeedInSync;
        if (ImGui.Checkbox("Send RP chat feed to dashboard (Social tab)", ref includeRoleplayFeedInSync))
        {
            configuration.IncludeRoleplayFeedInSync = includeRoleplayFeedInSync;
        }

        ImGui.TextWrapped(
            "When enabled, each sync uploads recent lines already captured by the RP bubble options on this tab. Turn off if you do not want chat text stored on the Felix server.");

        ImGui.Separator();
        ImGui.TextWrapped(
            "RP Scenes: on the web Command Center, create a scene and share the join code. Paste the code here (spaces are fine), click Join, then enable recording below. Chat lines go to the scene transcript when Over-head RP Bubble or RP Bubble Chat capture is on. Emotes are always captured for the scene while recording (even if those bubble options are off), as Emote: \"Character action\" in the transcript.");

        var rpSceneJoinDraft = configuration.RpSceneJoinDraft ?? string.Empty;
        if (ImGui.InputText("Scene join code (paste OK)", ref rpSceneJoinDraft, 32))
        {
            configuration.RpSceneJoinDraft = rpSceneJoinDraft;
        }

        var normalizedPreview = this.plugin.GetRpSceneNormalizedJoinDraftPreview();
        if (!string.IsNullOrEmpty(normalizedPreview))
        {
            ImGui.TextColored(
                new Vector4(0.75f, 0.88f, 1f, 1f),
                normalizedPreview.Length >= 8
                    ? $"Uses code: {normalizedPreview}"
                    : $"Normalized so far: {normalizedPreview} ({normalizedPreview.Length}/8)");
        }

        if (ImGui.Button("Join RP scene"))
        {
            _ = this.plugin.JoinRpSceneAsync();
        }

        ImGui.SameLine();
        if (ImGui.Button("Leave scene"))
        {
            if (this.plugin.GetRpScenePendingLineCount() > 0)
            {
                this.rpSceneLeavePopupOpen = true;
                ImGui.OpenPopup("FelixLeaveRpScene##fx");
            }
            else
            {
                this.plugin.LeaveRpScene();
            }
        }

        var rpSceneRecordingEnabled = configuration.RpSceneRecordingEnabled;
        if (ImGui.Checkbox("Record lines to RP scene (requires joined scene)", ref rpSceneRecordingEnabled))
        {
            configuration.RpSceneRecordingEnabled = rpSceneRecordingEnabled;
        }

        var rpScenePaused = configuration.RpSceneRecordingPaused;
        if (ImGui.Checkbox("Pause recording (break — stay in scene, no new lines queued)", ref rpScenePaused))
        {
            configuration.RpSceneRecordingPaused = rpScenePaused;
        }

        var autoFlush = configuration.RpSceneAutoFlushIntervalSeconds;
        if (ImGui.SliderInt("Auto-upload queued lines (seconds, 0 = off)", ref autoFlush, 0, 120))
        {
            configuration.RpSceneAutoFlushIntervalSeconds = autoFlush;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.72f, 0.78f, 1f));
        ImGui.TextWrapped("After changing this slider, click Save Settings on the Settings tab.");
        ImGui.PopStyleColor();

        ImGui.TextWrapped("Session note (optional, sent with snapshots while in a scene — shown on the web):");
        var sessionNote = configuration.RpSceneSessionNote ?? string.Empty;
        if (ImGui.InputTextMultiline("##rpSceneNote", ref sessionNote, 512, new Vector2(-1, 56)))
        {
            configuration.RpSceneSessionNote = sessionNote;
        }

        var firstLinePing = configuration.RpSceneNotifyFirstCapturedLine;
        if (ImGui.Checkbox("Chat ping when the first line is captured in a session", ref firstLinePing))
        {
            configuration.RpSceneNotifyFirstCapturedLine = firstLinePing;
        }

        var rpSceneShowHud = configuration.RpSceneShowRecordingHud;
        if (ImGui.Checkbox("Show small REC / PAUSED banner in-game while in a scene", ref rpSceneShowHud))
        {
            configuration.RpSceneShowRecordingHud = rpSceneShowHud;
        }

        if (configuration.RpSceneJoined && !string.IsNullOrWhiteSpace(configuration.RpSceneJoinCode))
        {
            ImGui.TextWrapped($"Joined: {configuration.RpSceneDisplayTitle} — code {configuration.RpSceneJoinCode}");
            if (ImGui.SmallButton("Copy join code"))
            {
                ImGui.SetClipboardText(configuration.RpSceneJoinCode);
            }

            var pending = this.plugin.GetRpScenePendingLineCount();
            if (pending > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.82f, 0.35f, 1f));
                ImGui.TextWrapped($"{pending} line(s) queued for upload (next sync or Upload now).");
                ImGui.PopStyleColor();
            }

            if (pending >= 180)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.45f, 0.35f, 1f));
                ImGui.TextWrapped("Queue is near the ~200 line cap — upload or pause recording so lines are not dropped.");
                ImGui.PopStyleColor();
            }

            var failStreak = this.plugin.RpSceneConsecutiveUploadFailures;
            if (failStreak > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.55f, 0.35f, 1f));
                ImGui.TextWrapped(
                    $"Last uploads failed {failStreak} time(s) in a row. Check the message below, or use Upload scene lines now (counter resets after a successful upload).");
                ImGui.PopStyleColor();
            }

            if (!string.IsNullOrWhiteSpace(this.plugin.RpSceneFlushStatus))
            {
                ImGui.TextWrapped(this.plugin.RpSceneFlushStatus);
            }

            if (configuration.RpSceneRecordingEnabled
                && !configuration.RoleplayOverheadBubbleEnabled
                && !configuration.RoleplayBubbleChatEnabled)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.55f, 0.25f, 1f));
                ImGui.TextWrapped(
                    "Enable Over-head RP Bubble (or RP Bubble Chat) on this tab — chat lines need one of them for the scene transcript (emotes still record).");
                ImGui.PopStyleColor();
            }

            if (ImGui.Button("Upload scene lines now"))
            {
                _ = this.plugin.UploadRpSceneLinesNowAsync();
            }

            ImGui.SameLine();
            if (ImGui.Button("Sync now (full)"))
            {
                _ = this.plugin.SyncNowAsync();
            }
        }
        else
        {
            ImGui.TextWrapped("Not joined to a scene.");
        }

        ImGui.TextWrapped("Quick commands: /felix record · /felix pause · /felix sync · /felix help");

        if (!string.IsNullOrWhiteSpace(configuration.RpSceneLastJoinError))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.35f, 0.35f, 1f));
            ImGui.TextWrapped(configuration.RpSceneLastJoinError);
            ImGui.PopStyleColor();
        }
    }

    private void DrawRoleplayOverheadBubbleOverlay(Configuration configuration)
    {
        if (!configuration.RoleplayOverheadBubbleEnabled)
        {
            return;
        }

        var bubbles = this.plugin.GetRoleplayOverheadBubbleMessages();
        if (bubbles.Count == 0)
        {
            return;
        }

        foreach (var bubble in bubbles.OrderBy((entry) => entry.CreatedAt))
        {
            this.DrawSingleRoleplayOverheadBubble(configuration, bubble);
        }
    }

    private void DrawSingleRoleplayOverheadBubble(Configuration configuration, RoleplayBubbleMessage bubble)
    {
        var hasAnchor = this.plugin.TryGetRoleplayBubbleScreenPosition(bubble, out var screenPosition);
        var isOpen = configuration.RoleplayOverheadBubbleEnabled;
        var scale = Math.Clamp(configuration.RoleplayOverheadBubbleScale, 0.55f, 1.15f);
        var displaySize = ImGui.GetIO().DisplaySize;
        if (!hasAnchor)
        {
            screenPosition = new Vector2(displaySize.X * 0.5f, displaySize.Y * 0.58f);
        }

        var paddingX = 10f * scale;
        var paddingTop = 8f * scale;
        var paddingBottom = 8f * scale;
        var tailHeight = 9f * scale;
        var tailWidth = 16f * scale;
        var bubbleRounding = 11f * scale;
        var shadowOffset = new Vector2(2f * scale, 2f * scale);
        var maxBubbleWidth = Math.Max(320f * scale, displaySize.X * 0.34f);
        var minBubbleWidth = 74f * scale;

        var rawMessageSize = ScaleTextSize(ImGui.CalcTextSize(bubble.Message), scale);
        var messageLengthBoost = Math.Min(bubble.Message.Length * (3.2f * scale), 150f * scale);
        var preferredBubbleWidth = rawMessageSize.X + (paddingX * 2f) + messageLengthBoost;
        var bubbleWidth = Math.Clamp(preferredBubbleWidth, minBubbleWidth, maxBubbleWidth);
        var wrapWidthScaled = Math.Max(bubbleWidth - (paddingX * 2f), 72f * scale);
        var wrapWidthUnscaled = wrapWidthScaled / scale;
        var messageSize = preferredBubbleWidth <= maxBubbleWidth
            ? rawMessageSize
            : ScaleTextSize(ImGui.CalcTextSize(bubble.Message, false, wrapWidthUnscaled), scale);
        var bubbleHeight = paddingTop + messageSize.Y + paddingBottom + Math.Max(0f, (messageSize.Y - (18f * scale)) * 0.16f);
        var windowSize = new Vector2(bubbleWidth, bubbleHeight + tailHeight);

        ImGui.SetNextWindowPos(screenPosition - new Vector2(0f, 6f * scale), ImGuiCond.Always, new Vector2(0.5f, 1f));
        ImGui.SetNextWindowSize(windowSize, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0f);

        var windowId = $"Felix RP Overhead Bubble##{bubble.Id}";
        if (!ImGui.Begin(
                windowId,
                ref isOpen,
                ImGuiWindowFlags.NoDecoration
                | ImGuiWindowFlags.NoBackground
                | ImGuiWindowFlags.NoInputs
                | ImGuiWindowFlags.NoSavedSettings
                | ImGuiWindowFlags.NoNav
                | ImGuiWindowFlags.NoFocusOnAppearing))
        {
            ImGui.End();
            return;
        }

        ImGui.SetWindowFontScale(scale);
        var drawList = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var bubbleMin = windowPos;
        var bubbleMax = new Vector2(windowPos.X + bubbleWidth, windowPos.Y + bubbleHeight);
        var tailBaseY = bubbleMax.Y - 1f * scale;
        var tailTip = new Vector2(windowPos.X + (bubbleWidth * 0.5f), windowPos.Y + windowSize.Y);
        var tailLeft = new Vector2(tailTip.X - (tailWidth * 0.5f), tailBaseY);
        var tailRight = new Vector2(tailTip.X + (tailWidth * 0.5f), tailBaseY);

        var shadowColor = ImGui.GetColorU32(new Vector4(0.14f, 0f, 0f, 0.22f));
        var fillColor = bubble.IsOwnMessage
            ? ImGui.GetColorU32(new Vector4(1.00f, 0.82f, 0.84f, 0.98f))
            : ImGui.GetColorU32(new Vector4(0.99f, 0.86f, 0.88f, 0.98f));
        var borderColor = bubble.IsOwnMessage
            ? ImGui.GetColorU32(new Vector4(0.78f, 0.20f, 0.28f, 1f))
            : ImGui.GetColorU32(new Vector4(0.66f, 0.28f, 0.34f, 1f));
        var textColor = new Vector4(0.22f, 0.05f, 0.07f, 1f);
        var highlightColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.18f));

        drawList.AddRectFilled(bubbleMin + shadowOffset, bubbleMax + shadowOffset, shadowColor, bubbleRounding);
        drawList.AddTriangleFilled(tailLeft + shadowOffset, tailRight + shadowOffset, tailTip + shadowOffset, shadowColor);
        drawList.AddRectFilled(bubbleMin, bubbleMax, fillColor, bubbleRounding);
        drawList.AddTriangleFilled(tailLeft, tailRight, tailTip, fillColor);
        drawList.AddRect(bubbleMin, bubbleMax, borderColor, bubbleRounding, ImDrawFlags.None, Math.Max(1.5f, 2f * scale));
        drawList.AddTriangle(tailLeft, tailRight, tailTip, borderColor, Math.Max(1.5f, 2f * scale));
        drawList.AddLine(
            new Vector2(bubbleMin.X + (8f * scale), bubbleMin.Y + (6f * scale)),
            new Vector2(bubbleMax.X - (8f * scale), bubbleMin.Y + (6f * scale)),
            highlightColor,
            Math.Max(1f, 1.0f * scale));

        if (!hasAnchor && bubble.IsOwnMessage)
        {
            drawList.AddText(
                new Vector2(bubbleMin.X + (paddingX * 0.6f), bubbleMin.Y - (18f * scale)),
                ImGui.GetColorU32(new Vector4(1f, 0.78f, 0.82f, 1f)),
                "Felix RP Bubble Fallback");
        }

        ImGui.SetCursorPos(new Vector2(paddingX, paddingTop));
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.PushTextWrapPos(paddingX + wrapWidthScaled);
        ImGui.TextUnformatted(bubble.Message);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
        ImGui.End();
    }

    private void DrawFocusTargetMapOverlay(Configuration configuration)
    {
        if (!configuration.MapAddonFocusTargetColorEnabled
            && !configuration.MapAddonFriendColorEnabled
            && !configuration.MapAddonFreeCompanyColorEnabled
            && !configuration.MapAddonLinkshellColorEnabled)
        {
            return;
        }

        var drawList = ImGui.GetForegroundDrawList();
        foreach (var marker in this.plugin.GetMiniMapOverlayMarkers())
        {
            DrawFocusTargetMarker(drawList, marker);
        }
    }

    private void DrawRpSceneRecordingHud(Configuration configuration)
    {
        if (!configuration.RpSceneShowRecordingHud
            || !configuration.RpSceneRecordingEnabled
            || !configuration.RpSceneJoined
            || string.IsNullOrWhiteSpace(configuration.RpSceneJoinCode))
        {
            return;
        }

        var vp = ImGui.GetMainViewport();
        var flags = ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoSavedSettings;
        ImGui.SetNextWindowPos(vp.WorkPos + new Vector2(14f, vp.WorkSize.Y * 0.32f), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.78f);
        if (!ImGui.Begin("FelixRpSceneRecHud##fx", flags))
        {
            ImGui.End();
            return;
        }

        if (configuration.RpSceneRecordingPaused)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.75f, 0.35f, 1f));
            ImGui.Text("⏸ PAUSED");
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.98f, 0.82f, 0.42f, 1f));
            ImGui.Text("● REC");
            ImGui.PopStyleColor();
        }

        ImGui.SameLine();
        var title = string.IsNullOrWhiteSpace(configuration.RpSceneDisplayTitle) ? "RP scene" : configuration.RpSceneDisplayTitle;
        ImGui.TextUnformatted(title);
        var pending = this.plugin.GetRpScenePendingLineCount();
        if (pending > 0)
        {
            ImGui.TextColored(new Vector4(0.85f, 0.85f, 0.92f, 1f), $"{pending} queued → server");
        }

        ImGui.End();
    }

    private static void DrawFocusTargetMarker(ImDrawListPtr drawList, FelixPlugin.MapAddonOverlayMarker marker)
    {
        var fillColor = ImGui.GetColorU32(marker.Color);
        var outlineColor = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.88f));
        var glowColor = ImGui.GetColorU32(new Vector4(marker.Color.X, marker.Color.Y, marker.Color.Z, 0.22f));
        var markerPosition = marker.ScreenPosition;
        var markerSize = marker.Radius;
        var outlineSize = markerSize + 2.5f;
        drawList.AddCircleFilled(markerPosition, outlineSize + 3f, glowColor, 24);
        drawList.AddCircleFilled(markerPosition, outlineSize, outlineColor, 24);
        drawList.AddCircleFilled(markerPosition, markerSize, fillColor, 24);
    }

    private void DrawRoleplayBubbleOverlay(Configuration configuration)
    {
        if (!configuration.RoleplayBubbleChatEnabled)
        {
            return;
        }

        var bubbles = this.plugin.GetRoleplayBubbleMessages();
        var isOpen = configuration.RoleplayBubbleChatEnabled;

        ImGui.SetNextWindowSize(new Vector2(620f, 360f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.9f);

        if (!ImGui.Begin("Felix RP Bubble Chat", ref isOpen, ImGuiWindowFlags.NoCollapse))
        {
            if (!isOpen)
            {
                configuration.RoleplayBubbleChatEnabled = false;
                this.plugin.SaveConfiguration();
            }

            ImGui.End();
            return;
        }

        ImGui.SetWindowFontScale(Math.Clamp(configuration.RoleplayBubbleScale, 1.0f, 2.5f));
        ImGui.TextWrapped("Large RP-friendly chat bubbles pulled from your live chat log.");
        ImGui.Separator();

        if (bubbles.Count == 0)
        {
            ImGui.TextWrapped("No tracked chat has landed yet. Send a Say, Emote, Tell, or Party message, or enable all-channel capture in /felix.");
        }
        else
        {
            foreach (var bubble in bubbles)
            {
                DrawBubbleCard(bubble);
            }
        }

        if (!isOpen)
        {
            configuration.RoleplayBubbleChatEnabled = false;
            this.plugin.SaveConfiguration();
        }

        ImGui.End();
    }

    private static void DrawBubbleCard(RoleplayBubbleMessage bubble)
    {
        var accentColor = bubble.IsOwnMessage
            ? new Vector4(0.95f, 0.77f, 0.34f, 1.0f)
            : new Vector4(0.78f, 0.86f, 1.0f, 1.0f);
        var bubbleColor = bubble.IsOwnMessage
            ? new Vector4(0.29f, 0.22f, 0.08f, 0.95f)
            : new Vector4(0.13f, 0.16f, 0.20f, 0.95f);

        var availableWidth = ImGui.GetContentRegionAvail().X;
        var textSize = ImGui.CalcTextSize(bubble.Message, false, Math.Max(availableWidth - 28f, 120f));
        var bubbleHeight = Math.Max(86f, textSize.Y + 56f);

        ImGui.PushID((int)(bubble.Id & int.MaxValue));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 18f);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, bubbleColor);
        ImGui.PushStyleColor(ImGuiCol.Border, accentColor);

        if (ImGui.BeginChild("##rpBubble", new Vector2(-1, bubbleHeight), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.TextColored(accentColor, $"{bubble.SpeakerName}  {NormalizeChannelLabel(bubble.ChannelLabel)}");
            ImGui.Spacing();
            ImGui.TextWrapped(bubble.Message);
        }

        ImGui.EndChild();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar();
        ImGui.PopID();
        ImGui.Spacing();
    }

    private static string NormalizeChannelLabel(string channelLabel)
    {
        return string.IsNullOrWhiteSpace(channelLabel) ? string.Empty : $"[{channelLabel}]";
    }

    private static Vector2 ScaleTextSize(Vector2 textSize, float scale)
    {
        return new Vector2(textSize.X * scale, textSize.Y * scale);
    }

    private static string FormatUtc(string value)
    {
        if (!DateTimeOffset.TryParse(value, out var timestamp))
        {
            return "Never";
        }

        return timestamp.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
    }
}
