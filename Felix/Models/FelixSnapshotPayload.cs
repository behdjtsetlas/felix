using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Felix.Models;

public sealed class FelixSnapshotPayload
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = "felix-plugin";

    [JsonPropertyName("character")]
    public FelixCharacterPayload Character { get; set; } = new();

    [JsonPropertyName("freeCompany")]
    public FelixFreeCompanyPayload FreeCompany { get; set; } = new();

    [JsonPropertyName("currencies")]
    public FelixCurrencyPayload Currencies { get; set; } = new();

    [JsonPropertyName("collections")]
    public FelixCollectionPayload Collections { get; set; } = new();

    [JsonPropertyName("progression")]
    public FelixProgressionPayload Progression { get; set; } = new();

    [JsonPropertyName("tracker")]
    public FelixTrackerPayload Tracker { get; set; } = new();

    [JsonPropertyName("equipment")]
    public FelixEquipmentPayload Equipment { get; set; } = new();

    [JsonPropertyName("inventory")]
    public List<FelixInventoryEntryPayload> Inventory { get; set; } = [];

    [JsonPropertyName("status")]
    public FelixStatusPayload Status { get; set; } = new();

    [JsonPropertyName("social")]
    public FelixSocialPayload Social { get; set; } = new();

    [JsonPropertyName("roleplay")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FelixRoleplayPayload? Roleplay { get; set; }
}

public sealed class FelixCharacterPayload
{
    [JsonPropertyName("contentId")]
    public string ContentId { get; set; } = string.Empty;

    [JsonPropertyName("entityId")]
    public string EntityId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("homeWorld")]
    public string HomeWorld { get; set; } = string.Empty;

    [JsonPropertyName("currentWorld")]
    public string CurrentWorld { get; set; } = string.Empty;

    [JsonPropertyName("jobId")]
    public int JobId { get; set; }

    [JsonPropertyName("jobName")]
    public string JobName { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("territoryId")]
    public uint TerritoryId { get; set; }

    [JsonPropertyName("zoneName")]
    public string ZoneName { get; set; } = string.Empty;

    [JsonPropertyName("mapId")]
    public uint MapId { get; set; }

    [JsonPropertyName("classJobs")]
    public List<FelixClassJobPayload> ClassJobs { get; set; } = [];
}

public sealed class FelixClassJobPayload
{
    [JsonPropertyName("id")]
    public uint Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("shortName")]
    public string ShortName { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("experience")]
    public int Experience { get; set; }
}

public sealed class FelixFreeCompanyPayload
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;

    [JsonPropertyName("world")]
    public string World { get; set; } = string.Empty;

    [JsonPropertyName("rank")]
    public string Rank { get; set; } = string.Empty;

    [JsonPropertyName("companyRank")]
    public string CompanyRank { get; set; } = string.Empty;

    [JsonPropertyName("leaderCharacterName")]
    public string LeaderCharacterName { get; set; } = string.Empty;

    [JsonPropertyName("memberCount")]
    public int MemberCount { get; set; }

    [JsonPropertyName("activeMemberCount")]
    public int ActiveMemberCount { get; set; }

    [JsonPropertyName("slogan")]
    public string Slogan { get; set; } = string.Empty;

    [JsonPropertyName("estate")]
    public string Estate { get; set; } = string.Empty;

    [JsonPropertyName("estateLocation")]
    public string EstateLocation { get; set; } = string.Empty;

    [JsonPropertyName("housingType")]
    public string HousingType { get; set; } = string.Empty;

    [JsonPropertyName("isLeader")]
    public bool IsLeader { get; set; }
        = false;
}

public sealed class FelixCurrencyPayload
{
    [JsonPropertyName("gil")]
    public long Gil { get; set; }

    [JsonPropertyName("mgp")]
    public int Mgp { get; set; }

    [JsonPropertyName("companySeals")]
    public int CompanySeals { get; set; }

    [JsonPropertyName("alliedSeals")]
    public int AlliedSeals { get; set; }

    [JsonPropertyName("sacksOfNuts")]
    public int SacksOfNuts { get; set; }

    [JsonPropertyName("wolfMarks")]
    public int WolfMarks { get; set; }

    [JsonPropertyName("tomestones")]
    public List<FelixTomestonePayload> Tomestones { get; set; } = [];
}

public sealed class FelixTomestonePayload
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("cap")]
    public int Cap { get; set; }

    [JsonPropertyName("weeklyCap")]
    public int WeeklyCap { get; set; }
}

public sealed class FelixCollectionPayload
{
    [JsonPropertyName("mountsOwned")]
    public int MountsOwned { get; set; }

    [JsonPropertyName("mountsTotal")]
    public int MountsTotal { get; set; }

    [JsonPropertyName("minionsOwned")]
    public int MinionsOwned { get; set; }

    [JsonPropertyName("minionsTotal")]
    public int MinionsTotal { get; set; }

    [JsonPropertyName("orchestrionsOwned")]
    public int OrchestrionsOwned { get; set; }

    [JsonPropertyName("orchestrionsTotal")]
    public int OrchestrionsTotal { get; set; }

    [JsonPropertyName("cardsOwned")]
    public int CardsOwned { get; set; }

    [JsonPropertyName("cardsTotal")]
    public int CardsTotal { get; set; }

    [JsonPropertyName("emotesOwned")]
    public int EmotesOwned { get; set; }

    [JsonPropertyName("emotesTotal")]
    public int EmotesTotal { get; set; }

    [JsonPropertyName("hairstylesOwned")]
    public int HairstylesOwned { get; set; }

    [JsonPropertyName("hairstylesTotal")]
    public int HairstylesTotal { get; set; }

    [JsonPropertyName("ornamentsOwned")]
    public int OrnamentsOwned { get; set; }

    [JsonPropertyName("ornamentsTotal")]
    public int OrnamentsTotal { get; set; }

    [JsonPropertyName("glassesOwned")]
    public int GlassesOwned { get; set; }

    [JsonPropertyName("glassesTotal")]
    public int GlassesTotal { get; set; }

    [JsonPropertyName("blueMageSpellsOwned")]
    public int BlueMageSpellsOwned { get; set; }

    [JsonPropertyName("blueMageSpellsTotal")]
    public int BlueMageSpellsTotal { get; set; }

    [JsonPropertyName("fieldRecordsOwned")]
    public int FieldRecordsOwned { get; set; }

    [JsonPropertyName("fieldRecordsTotal")]
    public int FieldRecordsTotal { get; set; }

    [JsonPropertyName("mounts")]
    public List<FelixCollectionEntryPayload> Mounts { get; set; } = [];

    [JsonPropertyName("minions")]
    public List<FelixCollectionEntryPayload> Minions { get; set; } = [];

    [JsonPropertyName("orchestrions")]
    public List<FelixCollectionEntryPayload> Orchestrions { get; set; } = [];

    [JsonPropertyName("cards")]
    public List<FelixCollectionEntryPayload> Cards { get; set; } = [];

    [JsonPropertyName("emotes")]
    public List<FelixCollectionEntryPayload> Emotes { get; set; } = [];

    [JsonPropertyName("hairstyles")]
    public List<FelixCollectionEntryPayload> Hairstyles { get; set; } = [];

    [JsonPropertyName("ornaments")]
    public List<FelixCollectionEntryPayload> Ornaments { get; set; } = [];

    [JsonPropertyName("glasses")]
    public List<FelixCollectionEntryPayload> Glasses { get; set; } = [];

    [JsonPropertyName("blueMageSpells")]
    public List<FelixCollectionEntryPayload> BlueMageSpells { get; set; } = [];

    [JsonPropertyName("fieldRecords")]
    public List<FelixCollectionEntryPayload> FieldRecords { get; set; } = [];
}

public sealed class FelixCollectionEntryPayload
{
    [JsonPropertyName("id")]
    public uint Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("iconId")]
    public uint IconId { get; set; }

    [JsonPropertyName("unlocked")]
    public bool Unlocked { get; set; }
}

public sealed class FelixProgressionPayload
{
    [JsonPropertyName("weeklyTomestoneCap")]
    public int WeeklyTomestoneCap { get; set; }

    [JsonPropertyName("weeklyTomestonesEarned")]
    public int WeeklyTomestonesEarned { get; set; }

    [JsonPropertyName("currentMoogleTomestones")]
    public int CurrentMoogleTomestones { get; set; }

    [JsonPropertyName("currentMoogleGoal")]
    public int CurrentMoogleGoal { get; set; }

    [JsonPropertyName("currentMoogleEvent")]
    public string CurrentMoogleEvent { get; set; } = string.Empty;

    [JsonPropertyName("missingMoogleRewards")]
    public List<string> MissingMoogleRewards { get; set; } = [];
}

public sealed class FelixTrackerPayload
{
    [JsonPropertyName("dailies")]
    public FelixDailiesPayload Dailies { get; set; } = new();

    [JsonPropertyName("weeklies")]
    public FelixWeekliesPayload Weeklies { get; set; } = new();
}

public sealed class FelixDailiesPayload
{
    [JsonPropertyName("dayKey")]
    public string DayKey { get; set; } = string.Empty;

    [JsonPropertyName("syncedToday")]
    public bool SyncedToday { get; set; }

    [JsonPropertyName("completedDutyCount")]
    public int CompletedDutyCount { get; set; }

    [JsonPropertyName("completedRouletteCount")]
    public int CompletedRouletteCount { get; set; }

    [JsonPropertyName("availableRouletteCount")]
    public int AvailableRouletteCount { get; set; }

    [JsonPropertyName("lastDutyCompletedAt")]
    public string LastDutyCompletedAt { get; set; } = string.Empty;

    [JsonPropertyName("duties")]
    public List<FelixTrackedDailyDutyPayload> Duties { get; set; } = [];

    [JsonPropertyName("roulettes")]
    public List<FelixTrackedRoulettePayload> Roulettes { get; set; } = [];

    [JsonPropertyName("entries")]
    public List<FelixTrackedDailySystemPayload> Entries { get; set; } = [];
}

public sealed class FelixTrackedDailyDutyPayload
{
    [JsonPropertyName("id")]
    public uint Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("completedAt")]
    public string CompletedAt { get; set; } = string.Empty;
}

public sealed class FelixTrackedRoulettePayload
{
    [JsonPropertyName("id")]
    public uint Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("available")]
    public bool Available { get; set; }
}

public sealed class FelixTrackedDailySystemPayload
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("synced")]
    public bool Synced { get; set; }

    [JsonPropertyName("progressLabel")]
    public string ProgressLabel { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    public string Detail { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<string> Items { get; set; } = [];
}

public sealed class FelixWeekliesPayload
{
    [JsonPropertyName("weekKey")]
    public string WeekKey { get; set; } = string.Empty;

    [JsonPropertyName("entries")]
    public List<FelixTrackedWeeklyPayload> Entries { get; set; } = [];
}

public sealed class FelixTrackedWeeklyPayload
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("synced")]
    public bool Synced { get; set; }

    [JsonPropertyName("progressLabel")]
    public string ProgressLabel { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    public string Detail { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<string> Items { get; set; } = [];
}

public sealed class FelixEquipmentPayload
{
    [JsonPropertyName("averageItemLevel")]
    public int AverageItemLevel { get; set; }

    [JsonPropertyName("slots")]
    public List<FelixEquipmentSlotPayload> Slots { get; set; } = [];
}

public sealed class FelixEquipmentSlotPayload
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("slotIndex")]
    public int SlotIndex { get; set; }

    [JsonPropertyName("itemId")]
    public uint ItemId { get; set; }

    [JsonPropertyName("itemName")]
    public string ItemName { get; set; } = string.Empty;

    [JsonPropertyName("iconId")]
    public uint IconId { get; set; }

    [JsonPropertyName("glamourId")]
    public uint GlamourId { get; set; }

    [JsonPropertyName("itemLevel")]
    public int ItemLevel { get; set; }

    [JsonPropertyName("requiredLevel")]
    public int RequiredLevel { get; set; }

    [JsonPropertyName("conditionPercent")]
    public int ConditionPercent { get; set; }

    [JsonPropertyName("spiritbondPercent")]
    public int SpiritbondPercent { get; set; }

    [JsonPropertyName("stainId")]
    public int StainId { get; set; }

    [JsonPropertyName("stainName")]
    public string StainName { get; set; } = string.Empty;

    [JsonPropertyName("isHighQuality")]
    public bool IsHighQuality { get; set; }
}

public sealed class FelixInventoryEntryPayload
{
    [JsonPropertyName("itemId")]
    public int ItemId { get; set; }

    [JsonPropertyName("itemName")]
    public string ItemName { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
}

public sealed class FelixStatusPayload
{
    [JsonPropertyName("isLoggedIn")]
    public bool IsLoggedIn { get; set; }

    [JsonPropertyName("isInDuty")]
    public bool IsInDuty { get; set; }

    [JsonPropertyName("inCombat")]
    public bool InCombat { get; set; }

    [JsonPropertyName("lastCombatSource")]
    public string LastCombatSource { get; set; } = string.Empty;

    [JsonPropertyName("pluginVersion")]
    public string PluginVersion { get; set; } = string.Empty;
}

public sealed class FelixRoleplayPayload
{
    [JsonPropertyName("syncEnabled")]
    public bool SyncEnabled { get; set; } = true;

    [JsonPropertyName("captureEnabled")]
    public bool CaptureEnabled { get; set; }

    [JsonPropertyName("messageCount")]
    public int MessageCount { get; set; }

    [JsonPropertyName("messages")]
    public List<FelixRoleplayMessagePayload> Messages { get; set; } = [];

    [JsonPropertyName("sceneRecording")]
    public bool SceneRecording { get; set; }

    [JsonPropertyName("sceneId")]
    public string SceneId { get; set; } = string.Empty;

    [JsonPropertyName("sceneJoinCode")]
    public string SceneJoinCode { get; set; } = string.Empty;

    [JsonPropertyName("scenePaused")]
    public bool ScenePaused { get; set; }

    [JsonPropertyName("sceneSessionNote")]
    public string SceneSessionNote { get; set; } = string.Empty;
}

public sealed class FelixRoleplayMessagePayload
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("channelLabel")]
    public string ChannelLabel { get; set; } = string.Empty;

    [JsonPropertyName("speakerName")]
    public string SpeakerName { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("isOwnMessage")]
    public bool IsOwnMessage { get; set; }
}

public sealed class FelixSocialPayload
{
    [JsonPropertyName("nearbyCount")]
    public int NearbyCount { get; set; }

    [JsonPropertyName("friendCount")]
    public int FriendCount { get; set; }

    [JsonPropertyName("freeCompanyCount")]
    public int FreeCompanyCount { get; set; }

    [JsonPropertyName("partyCount")]
    public int PartyCount { get; set; }

    [JsonPropertyName("linkshellCount")]
    public int LinkshellCount { get; set; }

    [JsonPropertyName("crossWorldLinkshellCount")]
    public int CrossWorldLinkshellCount { get; set; }

    [JsonPropertyName("nearby")]
    public List<FelixSocialContactPayload> Nearby { get; set; } = [];

    [JsonPropertyName("recentContacts")]
    public List<FelixSocialContactPayload> RecentContacts { get; set; } = [];
}

public sealed class FelixSocialContactPayload
{
    [JsonPropertyName("objectId")]
    public string ObjectId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("world")]
    public string World { get; set; } = string.Empty;

    [JsonPropertyName("distance")]
    public float Distance { get; set; }

    [JsonPropertyName("isNearby")]
    public bool IsNearby { get; set; }

    [JsonPropertyName("isFriend")]
    public bool IsFriend { get; set; }

    [JsonPropertyName("isPartyMember")]
    public bool IsPartyMember { get; set; }

    [JsonPropertyName("isFreeCompanyMember")]
    public bool IsFreeCompanyMember { get; set; }

    [JsonPropertyName("isLinkshellContact")]
    public bool IsLinkshellContact { get; set; }

    [JsonPropertyName("isCrossWorldLinkshellContact")]
    public bool IsCrossWorldLinkshellContact { get; set; }

    [JsonPropertyName("lastSeenAt")]
    public string LastSeenAt { get; set; } = string.Empty;

    [JsonPropertyName("lastSpokeAt")]
    public string LastSpokeAt { get; set; } = string.Empty;

    [JsonPropertyName("lastZoneName")]
    public string LastZoneName { get; set; } = string.Empty;

    [JsonPropertyName("leftAt")]
    public string LeftAt { get; set; } = string.Empty;

    [JsonPropertyName("badges")]
    public List<string> Badges { get; set; } = [];
}

public sealed class FelixPairRequest
{
    [JsonPropertyName("pairingToken")]
    public string PairingToken { get; set; } = string.Empty;

    [JsonPropertyName("installationId")]
    public string InstallationId { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("pluginVersion")]
    public string PluginVersion { get; set; } = string.Empty;

    [JsonPropertyName("apiBaseUrl")]
    public string ApiBaseUrl { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "windows";

    [JsonPropertyName("syncConfig")]
    public FelixSyncConfigPayload SyncConfig { get; set; } = new();
}

public sealed class FelixSyncConfigPayload
{
    [JsonPropertyName("autoSyncEnabled")]
    public bool AutoSyncEnabled { get; set; } = true;

    [JsonPropertyName("syncIntervalSeconds")]
    public int SyncIntervalSeconds { get; set; } = 30;

    [JsonPropertyName("includeCollections")]
    public bool IncludeCollections { get; set; } = true;

    [JsonPropertyName("includeCurrencies")]
    public bool IncludeCurrencies { get; set; } = true;

    [JsonPropertyName("includeCombat")]
    public bool IncludeCombat { get; set; }
}

public sealed class FelixPairResponseEnvelope
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("device")]
    public FelixPairedDevicePayload Device { get; set; } = new();
}

public sealed class FelixPairedDevicePayload
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("guildId")]
    public string GuildId { get; set; } = string.Empty;

    [JsonPropertyName("guildName")]
    public string GuildName { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("deviceToken")]
    public string DeviceToken { get; set; } = string.Empty;
}

public sealed class FelixMirrorRenderJobEnvelope
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("job")]
    public FelixMirrorRenderJobPayload? Job { get; set; }
}

public sealed class FelixMirrorRenderJobPayload
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("requestedAt")]
    public string RequestedAt { get; set; } = string.Empty;

    [JsonPropertyName("sourceCharacterName")]
    public string SourceCharacterName { get; set; } = string.Empty;

    [JsonPropertyName("sourceSnapshotAt")]
    public string SourceSnapshotAt { get; set; } = string.Empty;

    [JsonPropertyName("overrides")]
    public Dictionary<string, FelixEquipmentSlotPayload> Overrides { get; set; } = [];
}

public sealed class FelixMirrorRenderResultRequest
{
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "failed";

    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    [JsonPropertyName("imageDataUrl")]
    public string ImageDataUrl { get; set; } = string.Empty;
}
