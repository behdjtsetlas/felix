#pragma warning disable Dalamud001

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Felix.Models;
using FFXIVClientStructs.FFXIV.Client.Game;
using UIInstanceContent = FFXIVClientStructs.FFXIV.Client.Game.UI.InstanceContent;
using Lumina.Excel.Sheets;

namespace Felix.Services;

public sealed unsafe partial class SnapshotBuilder
{
    private static readonly (byte Index, uint Id, string Name)[] DailyRouletteDefinitions =
    [
        (0, 1, "Expert"),
        (1, 2, "High-level Dungeons"),
        (2, 3, "Leveling"),
        (3, 4, "Trials"),
        (4, 5, "Main Scenario"),
        (5, 6, "Guildhests"),
        (6, 7, "Alliance Raids"),
        (7, 8, "Normal Raids"),
        (8, 9, "Mentor"),
        (9, 10, "Frontline"),
    ];

    private static readonly Dictionary<int, (string Key, string Label, int Order)> FixedEquipmentSlots = new()
    {
        [0] = ("mainHand", "Main Hand", 0),
        [1] = ("offHand", "Off Hand", 2),
        [2] = ("head", "Head", 1),
        [3] = ("body", "Body", 3),
        [4] = ("hands", "Hands", 4),
        [5] = ("waist", "Waist", 13),
        [6] = ("legs", "Legs", 8),
        [7] = ("feet", "Feet", 10),
        [8] = ("earrings", "Earrings", 5),
        [9] = ("necklace", "Necklace", 6),
        [10] = ("bracelets", "Bracelets", 7),
        [11] = ("rightRing", "Right Ring", 11),
        [12] = ("leftRing", "Left Ring", 12),
        [13] = ("soulCrystal", "Soul Crystal", 9),
    };

    private readonly IDataManager dataManager;
    private readonly IUnlockState unlockState;
    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly IObjectTable objectTable;
    private readonly IPartyList partyList;

    public SnapshotBuilder(
        IDataManager dataManager,
        IUnlockState unlockState,
        IClientState clientState,
        IPlayerState playerState,
        IObjectTable objectTable,
        IPartyList partyList)
    {
        this.dataManager = dataManager;
        this.unlockState = unlockState;
        this.clientState = clientState;
        this.playerState = playerState;
        this.objectTable = objectTable;
        this.partyList = partyList;
    }

    public FelixSnapshotPayload Build(Configuration configuration, string pluginVersion)
    {
        var payload = new FelixSnapshotPayload
        {
            Status = new FelixStatusPayload
            {
                IsLoggedIn = this.clientState.IsLoggedIn,
                IsInDuty = false,
                InCombat = false,
                LastCombatSource = configuration.IncludeCombat ? "felix-plugin-stub" : string.Empty,
                PluginVersion = pluginVersion,
            },
        };

        var player = this.objectTable.LocalPlayer;
        if (player is null || !this.playerState.IsLoaded)
        {
            payload.Character.ZoneName = "Not logged in";
            return payload;
        }

        var zoneName = this.ResolveZoneName(this.clientState.TerritoryType);
        payload.Character = new FelixCharacterPayload
        {
            ContentId = this.playerState.ContentId.ToString(),
            EntityId = player.EntityId.ToString(),
            Name = player.Name.TextValue,
            HomeWorld = GetRowName(player.HomeWorld),
            CurrentWorld = GetRowName(player.CurrentWorld),
            JobId = (int)player.ClassJob.RowId,
            JobName = GetRowName(player.ClassJob),
            Level = player.Level,
            TerritoryId = this.clientState.TerritoryType,
            ZoneName = zoneName,
            MapId = this.clientState.MapId,
            ClassJobs = this.BuildClassJobs(),
        };
        payload.FreeCompany = BuildFreeCompanyPayload(player, payload.Character);

        if (configuration.IncludeCurrencies)
        {
            PopulateCurrencies(payload);
        }

        payload.Collections = this.BuildCollections(configuration);
        payload.Progression ??= new FelixProgressionPayload();
        payload.Tracker = this.BuildTracker(configuration);
        payload.Equipment = this.BuildEquipment();
        payload.Social = this.BuildSocialPayload(configuration, payload.Character.ZoneName);
        return payload;
    }

    public List<string> InspectFreeCompanyFields()
    {
        var lines = new List<string>();
        var player = this.objectTable.LocalPlayer;
        if (player is null)
        {
            lines.Add("LocalPlayer is null.");
            return lines;
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        InspectObject(lines, visited, "LocalPlayer", player, 0);
        return lines;
    }

    private FelixCollectionPayload BuildCollections(Configuration configuration)
    {
        var collections = new FelixCollectionPayload();
        if (!configuration.IncludeCollections)
        {
            return collections;
        }

        try
        {
            var mounts = this.BuildMountEntries();
            collections.Mounts = mounts;
            collections.MountsOwned = mounts.Count((entry) => entry.Unlocked);
            collections.MountsTotal = mounts.Count;

            var minions = this.BuildMinionEntries();
            collections.Minions = minions;
            collections.MinionsOwned = minions.Count((entry) => entry.Unlocked);
            collections.MinionsTotal = minions.Count;

            var orchestrions = this.BuildOrchestrionEntries();
            collections.Orchestrions = orchestrions;
            collections.OrchestrionsOwned = orchestrions.Count((entry) => entry.Unlocked);
            collections.OrchestrionsTotal = orchestrions.Count;

            var cards = this.BuildCardEntries();
            collections.Cards = cards;
            collections.CardsOwned = cards.Count((entry) => entry.Unlocked);
            collections.CardsTotal = cards.Count;

            var emotes = this.BuildEmoteEntries();
            collections.Emotes = emotes;
            collections.EmotesOwned = emotes.Count((entry) => entry.Unlocked);
            collections.EmotesTotal = emotes.Count;

            var hairstyles = this.BuildHairstyleEntries();
            collections.Hairstyles = hairstyles;
            collections.HairstylesOwned = hairstyles.Count((entry) => entry.Unlocked);
            collections.HairstylesTotal = hairstyles.Count;

            var ornaments = this.BuildOrnamentEntries();
            collections.Ornaments = ornaments;
            collections.OrnamentsOwned = ornaments.Count((entry) => entry.Unlocked);
            collections.OrnamentsTotal = ornaments.Count;

            var glasses = this.BuildGlassesEntries();
            collections.Glasses = glasses;
            collections.GlassesOwned = glasses.Count((entry) => entry.Unlocked);
            collections.GlassesTotal = glasses.Count;

            var blueMageSpells = this.BuildBlueMageSpellEntries();
            collections.BlueMageSpells = blueMageSpells;
            collections.BlueMageSpellsOwned = blueMageSpells.Count((entry) => entry.Unlocked);
            collections.BlueMageSpellsTotal = blueMageSpells.Count;

            var fieldRecords = this.BuildOccultRecordEntries();
            collections.FieldRecords = fieldRecords;
            collections.FieldRecordsOwned = fieldRecords.Count((entry) => entry.Unlocked);
            collections.FieldRecordsTotal = fieldRecords.Count;
        }
        catch
        {
        }

        return collections;
    }

    private List<FelixClassJobPayload> BuildClassJobs()
    {
        var entries = new List<FelixClassJobPayload>();
        var sheet = this.dataManager.GetExcelSheet<ClassJob>();
        if (sheet is null)
        {
            return entries;
        }

        foreach (var classJob in sheet)
        {
            if (classJob.RowId == 0)
            {
                continue;
            }

            var name = GetRowName(classJob);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            short level;
            int experience;
            try
            {
                level = this.playerState.GetClassJobLevel(classJob);
                experience = this.playerState.GetClassJobExperience(classJob);
            }
            catch
            {
                continue;
            }

            if (level <= 0)
            {
                continue;
            }

            entries.Add(new FelixClassJobPayload
            {
                Id = classJob.RowId,
                Name = name,
                ShortName = name,
                Level = level,
                Experience = experience,
            });
        }

        entries.Sort(static (left, right) =>
        {
            var levelCompare = right.Level.CompareTo(left.Level);
            if (levelCompare != 0)
            {
                return levelCompare;
            }

            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });
        return entries;
    }

    private List<FelixCollectionEntryPayload> BuildMountEntries()
    {
        var entries = new List<FelixCollectionEntryPayload>();
        var sheet = this.dataManager.GetExcelSheet<Mount>();
        if (sheet is null)
        {
            return entries;
        }

        foreach (var mount in sheet)
        {
            if (mount.RowId == 0)
            {
                continue;
            }

            var name = mount.Singular.ToString().Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            entries.Add(new FelixCollectionEntryPayload
            {
                Id = mount.RowId,
                Name = name,
                IconId = GetIconId(mount),
                Unlocked = this.unlockState.IsMountUnlocked(mount),
            });
        }

        entries.Sort(static (left, right) =>
        {
            var unlockedCompare = right.Unlocked.CompareTo(left.Unlocked);
            if (unlockedCompare != 0)
            {
                return unlockedCompare;
            }

            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });
        return entries;
    }

    private List<FelixCollectionEntryPayload> BuildMinionEntries()
    {
        var entries = new List<FelixCollectionEntryPayload>();
        var sheet = this.dataManager.GetExcelSheet<Companion>();
        if (sheet is null)
        {
            return entries;
        }

        foreach (var minion in sheet)
        {
            if (minion.RowId == 0)
            {
                continue;
            }

            var name = minion.Singular.ToString().Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            entries.Add(new FelixCollectionEntryPayload
            {
                Id = minion.RowId,
                Name = name,
                IconId = GetIconId(minion),
                Unlocked = this.unlockState.IsCompanionUnlocked(minion),
            });
        }

        entries.Sort(static (left, right) =>
        {
            var unlockedCompare = right.Unlocked.CompareTo(left.Unlocked);
            if (unlockedCompare != 0)
            {
                return unlockedCompare;
            }

            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });
        return entries;
    }

    private List<FelixCollectionEntryPayload> BuildOrchestrionEntries()
    {
        var entries = new List<FelixCollectionEntryPayload>();
        var sheet = this.dataManager.GetExcelSheet<Orchestrion>();
        if (sheet is null)
        {
            return entries;
        }

        foreach (var orchestrion in sheet)
        {
            if (orchestrion.RowId == 0)
            {
                continue;
            }

            var name = orchestrion.Name.ToString().Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            entries.Add(new FelixCollectionEntryPayload
            {
                Id = orchestrion.RowId,
                Name = name,
                IconId = GetIconId(orchestrion),
                Unlocked = this.unlockState.IsOrchestrionUnlocked(orchestrion),
            });
        }

        entries.Sort(static (left, right) =>
        {
            var unlockedCompare = right.Unlocked.CompareTo(left.Unlocked);
            if (unlockedCompare != 0)
            {
                return unlockedCompare;
            }

            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });
        return entries;
    }

    private List<FelixCollectionEntryPayload> BuildCardEntries()
    {
        var entries = new List<FelixCollectionEntryPayload>();
        var sheet = this.dataManager.GetExcelSheet<TripleTriadCard>();
        if (sheet is null)
        {
            return entries;
        }

        foreach (var card in sheet)
        {
            if (card.RowId == 0)
            {
                continue;
            }

            var name = card.Name.ToString().Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            entries.Add(new FelixCollectionEntryPayload
            {
                Id = card.RowId,
                Name = name,
                IconId = GetIconId(card),
                Unlocked = this.unlockState.IsTripleTriadCardUnlocked(card),
            });
        }

        entries.Sort(static (left, right) =>
        {
            var unlockedCompare = right.Unlocked.CompareTo(left.Unlocked);
            if (unlockedCompare != 0)
            {
                return unlockedCompare;
            }

            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });
        return entries;
    }

    private List<FelixCollectionEntryPayload> BuildEmoteEntries()
    {
        var entries = new List<FelixCollectionEntryPayload>();
        var sheet = this.dataManager.GetExcelSheet<Emote>();
        if (sheet is null)
        {
            return entries;
        }

        foreach (var emote in sheet)
        {
            if (emote.RowId == 0)
            {
                continue;
            }

            var name = GetBestText(emote, "Name", "TextCommand.Command");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            entries.Add(new FelixCollectionEntryPayload
            {
                Id = emote.RowId,
                Name = name,
                IconId = GetIconId(emote),
                Unlocked = this.unlockState.IsEmoteUnlocked(emote),
            });
        }

        return SortCollectionEntries(entries);
    }

    private List<FelixCollectionEntryPayload> BuildHairstyleEntries()
    {
        var entries = new List<FelixCollectionEntryPayload>();
        var sheet = this.dataManager.GetExcelSheet<Item>();
        if (sheet is null)
        {
            return entries;
        }

        foreach (var item in sheet)
        {
            if (item.RowId == 0)
            {
                continue;
            }

            var itemName = GetBestText(item, "Name");
            if (string.IsNullOrWhiteSpace(itemName) || !itemName.StartsWith("Modern Aesthetics", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var hairstyleName = itemName.Replace("Modern Aesthetics - ", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            if (string.IsNullOrWhiteSpace(hairstyleName))
            {
                hairstyleName = itemName.Trim();
            }

            entries.Add(new FelixCollectionEntryPayload
            {
                Id = item.RowId,
                Name = hairstyleName,
                IconId = GetIconId(item),
                Unlocked = this.InvokeUnlockState(["IsItemUnlocked", "IsUnlockableItemUnlocked"], item, item.RowId),
            });
        }

        return SortCollectionEntries(entries);
    }

    private List<FelixCollectionEntryPayload> BuildOrnamentEntries()
    {
        var entries = new List<FelixCollectionEntryPayload>();
        var sheet = this.dataManager.GetExcelSheet<Ornament>();
        if (sheet is null)
        {
            return entries;
        }

        foreach (var ornament in sheet)
        {
            if (ornament.RowId == 0)
            {
                continue;
            }

            var name = GetBestText(ornament, "Singular", "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            entries.Add(new FelixCollectionEntryPayload
            {
                Id = ornament.RowId,
                Name = name,
                IconId = GetIconId(ornament),
                Unlocked = this.unlockState.IsOrnamentUnlocked(ornament),
            });
        }

        return SortCollectionEntries(entries);
    }

    private List<FelixCollectionEntryPayload> BuildGlassesEntries()
    {
        var entries = new List<FelixCollectionEntryPayload>();
        var sheet = this.dataManager.GetExcelSheet<Glasses>();
        if (sheet is null)
        {
            return entries;
        }

        foreach (var glasses in sheet)
        {
            if (glasses.RowId == 0)
            {
                continue;
            }

            var name = GetBestText(glasses, "Name", "Singular");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            entries.Add(new FelixCollectionEntryPayload
            {
                Id = glasses.RowId,
                Name = name,
                IconId = GetIconId(glasses),
                Unlocked = this.unlockState.IsGlassesUnlocked(glasses),
            });
        }

        return SortCollectionEntries(entries);
    }

    private List<FelixCollectionEntryPayload> BuildBlueMageSpellEntries()
    {
        var typedEntries = new List<FelixCollectionEntryPayload>();
        var sheet = this.dataManager.GetExcelSheet<AozAction>();
        if (sheet is not null)
        {
            foreach (var spell in sheet)
            {
                if (spell.RowId == 0)
                {
                    continue;
                }

                var name = GetBestText(spell, "Name", "Action.Name", "Action.Value.Name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                typedEntries.Add(new FelixCollectionEntryPayload
                {
                    Id = spell.RowId,
                    Name = name,
                    IconId = GetIconId(spell),
                    Unlocked = this.unlockState.IsAozActionUnlocked(spell),
                });
            }
        }

        var reflectiveEntries = this.BuildCollectionEntries(
            "AozAction",
            ["IsAozActionUnlocked", "IsBlueMageActionUnlocked", "IsAozActionLearned"],
            ["Name", "Action.Name", "Action.Value.Name", "Transient.Value.Name"]);

        return MergeCollectionEntries(typedEntries, reflectiveEntries);
    }

    private List<FelixCollectionEntryPayload> BuildOccultRecordEntries()
    {
        var entries = new List<FelixCollectionEntryPayload>();
        var sheet = this.dataManager.GetExcelSheet<MKDLore>();
        if (sheet is null)
        {
            return entries;
        }

        foreach (var record in sheet)
        {
            if (record.RowId == 0)
            {
                continue;
            }

            var name = GetBestText(record, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            entries.Add(new FelixCollectionEntryPayload
            {
                Id = record.RowId,
                Name = name,
                IconId = GetIconId(record),
                Unlocked = this.unlockState.IsMKDLoreUnlocked(record),
            });
        }

        return SortCollectionEntries(entries);
    }

    private void PopulateCurrencies(FelixSnapshotPayload payload)
    {
        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager is null)
        {
            payload.Currencies = new FelixCurrencyPayload();
            payload.Progression = new FelixProgressionPayload();
            return;
        }

        var grandCompanyId = this.GetGrandCompanyId();
        var weeklyCap = InventoryManager.GetLimitedTomestoneWeeklyLimit();
        var weeklyEarned = SafeToInt(inventoryManager->GetWeeklyAcquiredTomestoneCount());

        payload.Currencies = new FelixCurrencyPayload
        {
            Gil = inventoryManager->GetGil(),
            Mgp = SafeToInt(inventoryManager->GetGoldSaucerCoin()),
            CompanySeals = grandCompanyId > 0 ? SafeToInt(inventoryManager->GetCompanySeals((byte)grandCompanyId)) : 0,
            AlliedSeals = SafeToInt(inventoryManager->GetAlliedSeals()),
            SacksOfNuts = 0,
            WolfMarks = SafeToInt(inventoryManager->GetWolfMarks()),
            Tomestones = [],
        };

        payload.Progression = new FelixProgressionPayload
        {
            WeeklyTomestoneCap = weeklyCap,
            WeeklyTomestonesEarned = weeklyEarned,
            CurrentMoogleTomestones = 0,
            CurrentMoogleGoal = 0,
            CurrentMoogleEvent = string.Empty,
            MissingMoogleRewards = [],
        };
    }

    public string ResolveDutyName(uint contentFinderConditionId)
    {
        if (contentFinderConditionId == 0)
        {
            return string.Empty;
        }

        try
        {
            foreach (var candidateId in ExpandWeeklyBingoTextCandidates(contentFinderConditionId))
            {
                var weeklyBingoOrderLabel = this.ResolveWeeklyBingoOrderLabelByContent(candidateId);
                if (LooksLikeResolvedDutyLabel(weeklyBingoOrderLabel))
                {
                    return weeklyBingoOrderLabel;
                }

                var addonText = ResolveSheetRowText("Addon", candidateId, "Text", "Name", "Singular", "Description");
                if (LooksLikeResolvedDutyLabel(addonText))
                {
                    return addonText;
                }

                var weeklyBingoText = ResolveSheetRowText(
                    "WeeklyBingoMultipleOrder",
                    candidateId,
                    "Name",
                    "Text",
                    "Description",
                    "Content.Name",
                    "Content.Text",
                    "Content");
                if (LooksLikeResolvedDutyLabel(weeklyBingoText))
                {
                    return weeklyBingoText;
                }

            }

            var sheet = this.dataManager.GetExcelSheet<ContentFinderCondition>();
            if (sheet is null)
            {
                return string.Empty;
            }

            foreach (var candidateId in ExpandDutyIdCandidates(contentFinderConditionId))
            {
                var row = sheet.GetRow(candidateId);
                if (row.RowId == 0)
                {
                    continue;
                }

                var name = row.Name.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private string ResolveWeeklyBingoOrderLabelByContent(uint contentId)
    {
        if (contentId == 0)
        {
            return string.Empty;
        }

        var rows = this.GetSheetRows("WeeklyBingoMultipleOrder");
        if (rows is null)
        {
            return string.Empty;
        }

        foreach (var row in rows)
        {
            if (row is null)
            {
                continue;
            }

            var directContent = ConvertToUInt(GetNestedValue(row, "Content"));
            var contentRowId = ConvertToUInt(GetNestedValue(row, "Content.RowId"));
            if (directContent != contentId && contentRowId != contentId)
            {
                continue;
            }

            foreach (var label in ExtractWeeklyBingoRowLabels(row))
            {
                if (LooksLikeResolvedDutyLabel(label))
                {
                    return label;
                }
            }
        }

        return string.Empty;
    }

    private static IEnumerable<string> ExtractWeeklyBingoRowLabels(object row)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(object Value, int Depth)>();
        queue.Enqueue((row, 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (current is null || depth > 2)
            {
                continue;
            }

            foreach (var candidate in new[]
                     {
                         "Name",
                         "Text",
                         "Description",
                         "Duty",
                         "Target",
                         "Content",
                         "Category",
                         "Type",
                         "Order",
                     })
            {
                var candidateValue = GetNestedValue(current, candidate);
                var text = NormalizeResolvedDutyLabel(ReadLuminaText(candidateValue));
                if (!string.IsNullOrWhiteSpace(text) && seen.Add(text))
                {
                    yield return text;
                }
            }

            var type = current.GetType();
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                object? propertyValue = null;
                try
                {
                    propertyValue = property.GetValue(current);
                }
                catch
                {
                    continue;
                }

                if (propertyValue is null)
                {
                    continue;
                }

                var text = NormalizeResolvedDutyLabel(ReadLuminaText(propertyValue));
                if (!string.IsNullOrWhiteSpace(text) && seen.Add(text))
                {
                    yield return text;
                }

                var propertyType = propertyValue.GetType();
                if (depth < 2
                    && !propertyType.IsPrimitive
                    && propertyType != typeof(string)
                    && !typeof(IEnumerable).IsAssignableFrom(propertyType))
                {
                    queue.Enqueue((propertyValue, depth + 1));
                }
            }
        }
    }

    private FelixTrackerPayload BuildTracker(Configuration configuration)
    {
        configuration.EnsureCurrentDailyTrackerDay();
        var state = configuration.DailyTracker ?? new FelixDailyTrackerState();
        var duties = (state.CompletedDuties ?? [])
            .Where((entry) => !string.IsNullOrWhiteSpace(entry.Name))
            .OrderByDescending((entry) => entry.CompletedAtUtc)
            .Select((entry) => new FelixTrackedDailyDutyPayload
            {
                Id = entry.Id,
                Name = entry.Name,
                Completed = true,
                CompletedAt = entry.CompletedAtUtc ?? string.Empty,
            })
            .ToList();
        var roulettes = this.BuildRouletteTrackerEntries();

        return new FelixTrackerPayload
        {
            Dailies = new FelixDailiesPayload
            {
                DayKey = state.DayKey ?? string.Empty,
                SyncedToday = string.Equals(state.DayKey, DateTime.Now.ToString("yyyy-MM-dd"), StringComparison.Ordinal),
                CompletedDutyCount = duties.Count,
                CompletedRouletteCount = roulettes.Count((entry) => entry.Completed),
                AvailableRouletteCount = roulettes.Count,
                LastDutyCompletedAt = state.LastDutyCompletedAtUtc ?? string.Empty,
                Duties = duties,
                Roulettes = roulettes,
                Entries = BuildDailySystemEntries(configuration, duties.Count, roulettes.Count((entry) => entry.Completed), roulettes.Count),
            },
            Weeklies = this.BuildWeeklies(configuration),
        };
    }

    private static List<FelixTrackedDailySystemPayload> BuildDailySystemEntries(Configuration configuration, int completedDutyCount, int completedRouletteCount, int availableRouletteCount)
    {
        var native = configuration.DailyNative ?? new FelixDailyNativeState();

        return
        [
            new FelixTrackedDailySystemPayload
            {
                Id = "duty_roulettes",
                Name = "Duty Roulettes",
                Completed = availableRouletteCount > 0 && completedRouletteCount >= availableRouletteCount,
                Synced = availableRouletteCount > 0,
                ProgressLabel = availableRouletteCount > 0
                    ? $"{completedRouletteCount} / {availableRouletteCount} completed"
                    : "No entries detected",
                Detail = "Tracked directly from the client roulette state. Entries check off when Felix resolves the roulette as completed for today.",
                Items = DailyRouletteDefinitions.Select((definition) => definition.Name).ToList(),
            },
            new FelixTrackedDailySystemPayload
            {
                Id = "completed_duties",
                Name = "Completed Duties",
                Completed = completedDutyCount > 0,
                Synced = true,
                ProgressLabel = completedDutyCount > 0 ? $"{completedDutyCount} captured today" : "No duties captured yet",
                Detail = "Felix records duty completions today while the plugin is running. This is separate from roulette reward detection.",
            },
            new FelixTrackedDailySystemPayload
            {
                Id = "mini_cactpot",
                Name = "Mini Cactpot",
                Completed = native.MiniCactpot?.Completed == true,
                Synced = native.MiniCactpot?.Synced == true,
                ProgressLabel = BuildNativeProgressLabel(native.MiniCactpot, "Not synced yet"),
                Detail = BuildNativeDetail(native.MiniCactpot, "Mini Cactpot will live here once its native client state is mapped from the Gold Saucer UI."),
                Items = BuildReadableNativeItems(native.MiniCactpot),
            },
            new FelixTrackedDailySystemPayload
            {
                Id = "tribal_quests",
                Name = "Tribal Quests",
                Completed = native.TribalQuests?.Completed == true,
                Synced = native.TribalQuests?.Synced == true,
                ProgressLabel = BuildNativeProgressLabel(native.TribalQuests, "Not synced yet"),
                Detail = BuildNativeDetail(native.TribalQuests, "Daily tribal allowance and completed quest tracking still needs a native client discovery pass."),
                Items = BuildReadableNativeItems(native.TribalQuests),
            },
            new FelixTrackedDailySystemPayload
            {
                Id = "hunt_bills",
                Name = "Hunt Bills",
                Completed = native.HuntBills?.Completed == true,
                Synced = native.HuntBills?.Synced == true,
                ProgressLabel = BuildNativeProgressLabel(native.HuntBills, "Not synced yet"),
                Detail = BuildNativeDetail(native.HuntBills, "Daily hunt bill completion will appear here once Felix reads the hunt board state exactly."),
                Items = BuildReadableNativeItems(native.HuntBills),
            },
            new FelixTrackedDailySystemPayload
            {
                Id = "gc_turn_ins",
                Name = "GC Turn-ins",
                Completed = native.GcTurnIns?.Completed == true,
                Synced = native.GcTurnIns?.Synced == true,
                ProgressLabel = BuildNativeProgressLabel(native.GcTurnIns, "Not synced yet"),
                Detail = BuildNativeDetail(native.GcTurnIns, "Grand Company daily provisioning and expert delivery tracking still needs a dedicated client-state mapping."),
                Items = BuildReadableNativeItems(native.GcTurnIns),
            },
            new FelixTrackedDailySystemPayload
            {
                Id = "other_daily_systems",
                Name = "Other Daily / Limited Systems",
                Completed = native.OtherDailySystems?.Completed == true,
                Synced = native.OtherDailySystems?.Synced == true,
                ProgressLabel = BuildNativeProgressLabel(native.OtherDailySystems, "Discovery queue"),
                Detail = BuildNativeDetail(native.OtherDailySystems, "Reserved for additional daily-limited systems after Mini Cactpot, tribes, hunts, and GC turn-ins are wired."),
                Items = BuildReadableNativeItems(native.OtherDailySystems),
            },
        ];
    }

    private static string BuildNativeProgressLabel(FelixNativeDailySystemState? state, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(state?.ProgressLabel))
        {
            return state.ProgressLabel;
        }

        return fallback;
    }

    private static string BuildNativeDetail(FelixNativeDailySystemState? state, string fallback)
    {
        if (state is null)
        {
            return fallback;
        }

        if (!string.IsNullOrWhiteSpace(state.NativeSource))
        {
            var sourceDetail = $"Native source: {state.NativeSource}";
            if (!string.IsNullOrWhiteSpace(state.Detail))
            {
                return $"{sourceDetail}. {state.Detail}";
            }

            return sourceDetail;
        }

        if (!string.IsNullOrWhiteSpace(state.Detail))
        {
            return state.Detail;
        }

        return fallback;
    }

    private static List<string> BuildReadableNativeItems(FelixNativeDailySystemState? state)
    {
        if (state?.Items is null)
        {
            return [];
        }

        return state.Items
            .Where((item) => !string.IsNullOrWhiteSpace(item))
            .Where((item) => !item.Contains("Type=", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private FelixWeekliesPayload BuildWeeklies(Configuration configuration)
    {
        var weekKey = $"{ISOWeek.GetYear(DateTime.Now)}-W{ISOWeek.GetWeekOfYear(DateTime.Now):00}";
        var inventoryManager = InventoryManager.Instance();
        var weeklyCap = 0;
        var weeklyEarned = 0;

        if (inventoryManager is not null)
        {
            try
            {
                weeklyCap = InventoryManager.GetLimitedTomestoneWeeklyLimit();
                weeklyEarned = SafeToInt(inventoryManager->GetWeeklyAcquiredTomestoneCount());
            }
            catch
            {
                weeklyCap = 0;
                weeklyEarned = 0;
            }
        }

        var entries = new List<FelixTrackedWeeklyPayload>
        {
            new()
            {
                Id = "weekly_tomestones",
                Name = "Weekly Tomestones",
                Completed = weeklyCap > 0 && weeklyEarned >= weeklyCap,
                Synced = weeklyCap > 0,
                ProgressLabel = weeklyCap > 0 ? $"{weeklyEarned:N0} / {weeklyCap:N0}" : "Not available",
                Detail = weeklyCap > 0
                    ? "Tracked directly from the weekly capped tomestone counter."
                    : "No weekly tomestone cap was available from the client state.",
            },
            new()
            {
                Id = "custom_deliveries",
                Name = "Custom Deliveries",
                Completed = false,
                Synced = false,
                ProgressLabel = "Not synced yet",
                Detail = "Weekly tab is ready; native Custom Deliveries state still needs to be mapped from the client.",
            },
        };

        var wondrousTails = configuration.WeeklyTracker?.WondrousTails;
        entries.Insert(1, BuildWondrousTailsEntry(wondrousTails));

        return new FelixWeekliesPayload
        {
            WeekKey = weekKey,
            Entries = entries,
        };
    }

    private static FelixTrackedWeeklyPayload BuildWondrousTailsEntry(FelixWondrousTailsState? state)
    {
        if (state is null || !state.Synced)
        {
            return new FelixTrackedWeeklyPayload
            {
                Id = "wondrous_tails",
                Name = "Wondrous Tails",
                Completed = false,
                Synced = false,
                ProgressLabel = "Not synced yet",
                Detail = "Open your Wondrous Tails journal once with Weekly Discovery Mode enabled so Felix can read the current book.",
            };
        }

        var sealCount = Math.Clamp(state.SealCount, 0, 9);
        var dutyPreview = (state.DutyNames ?? [])
            .Where((name) => !string.IsNullOrWhiteSpace(name))
            .Take(3)
            .ToList();

        var detailParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(state.Deadline))
        {
            detailParts.Add(state.Deadline);
        }

        detailParts.Add($"Second Chance: {Math.Max(0, state.SecondChancePoints)}");

        if (dutyPreview.Count > 0)
        {
            detailParts.Add($"Book entries: {string.Join(", ", dutyPreview)}");
        }
        else if ((state.DutyIds?.Count ?? 0) > 0)
        {
            detailParts.Add($"Book duties detected: {state.DutyIds.Count}");
        }

        if (!state.HasJournal)
        {
            detailParts.Add("No active journal detected.");
        }

        return new FelixTrackedWeeklyPayload
        {
            Id = "wondrous_tails",
            Name = "Wondrous Tails",
            Completed = sealCount >= 9,
            Synced = true,
            ProgressLabel = $"{sealCount} / 9 seals",
            Detail = string.Join(" • ", detailParts.Where((part) => !string.IsNullOrWhiteSpace(part))),
            Items = BuildWondrousTailsItems(state),
        };
    }

    private static List<string> BuildWondrousTailsItems(FelixWondrousTailsState state)
    {
        var items = (state.DutyNames ?? [])
            .Where((name) => !string.IsNullOrWhiteSpace(name))
            .Take(16)
            .ToList();

        if (items.Count > 0)
        {
            return items;
        }

        return (state.DutyIds ?? [])
            .Where((id) => id > 0)
            .Take(16)
            .Select(static (id) => $"Duty ID {id}")
            .ToList();
    }

    private List<FelixTrackedRoulettePayload> BuildRouletteTrackerEntries()
    {
        var instanceContent = UIInstanceContent.Instance();
        var entries = new List<FelixTrackedRoulettePayload>();
        foreach (var definition in DailyRouletteDefinitions)
        {
            var (_, completed, _) = ResolveRouletteState(instanceContent, definition.Index);

            entries.Add(new FelixTrackedRoulettePayload
            {
                Id = definition.Id,
                Name = definition.Name,
                Completed = completed,
                Available = true,
            });
        }

        entries.Sort(static (left, right) =>
        {
            var completedCompare = left.Completed.CompareTo(right.Completed);
            if (completedCompare != 0)
            {
                return completedCompare;
            }

            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });

        return entries;
    }

    private static (byte ResolvedIndex, bool Completed, bool Available) ResolveRouletteState(UIInstanceContent* instanceContent, byte rouletteId)
    {
        if (instanceContent == null)
        {
            return (rouletteId, false, false);
        }

        foreach (var candidate in EnumerateRouletteCandidates(rouletteId))
        {
            try
            {
                var completed = instanceContent->IsRouletteComplete(candidate);
                var available = completed || instanceContent->IsRouletteIncomplete(candidate);
                if (available)
                {
                    return (candidate, completed, true);
                }
            }
            catch
            {
            }
        }

        return (rouletteId, false, false);
    }

    private static IEnumerable<uint> ExpandWeeklyBingoTextCandidates(uint rawId)
    {
        var seen = new HashSet<uint>();

        foreach (var candidate in ExpandDutyIdCandidates(rawId).Reverse())
        {
            if (candidate > 0 && seen.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<uint> ExpandDutyIdCandidates(uint rawId)
    {
        yield return rawId;

        if (rawId >= 100000)
        {
            yield return rawId - 100000;
        }

        if (rawId >= 10000)
        {
            yield return rawId - 10000;
        }

        var moduloCandidate = rawId % 100000;
        if (moduloCandidate > 0 && moduloCandidate != rawId)
        {
            yield return moduloCandidate;
        }
    }

    private string ResolveSheetRowText(string sheetTypeName, uint rowId, params string[] candidates)
    {
        var row = this.GetSheetRow(sheetTypeName, rowId);
        if (row is null)
        {
            return string.Empty;
        }

        return NormalizeResolvedDutyLabel(GetBestText(row, candidates));
    }

    private object? GetSheetRow(string sheetTypeName, uint rowId)
    {
        if (rowId == 0)
        {
            return null;
        }

        var sheetType = ResolveSheetType(sheetTypeName);
        if (sheetType is null)
        {
            return null;
        }

        try
        {
            var getExcelSheetMethod = this.dataManager.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault((candidate) => candidate.Name == "GetExcelSheet" && candidate.IsGenericMethodDefinition && candidate.GetParameters().Length == 0);
            if (getExcelSheetMethod is null)
            {
                return null;
            }

            var sheet = getExcelSheetMethod.MakeGenericMethod(sheetType).Invoke(this.dataManager, null);
            if (sheet is null)
            {
                return null;
            }

            var tryGetRowMethod = sheet.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault((candidate) =>
                    candidate.Name == "TryGetRow"
                    && candidate.GetParameters().Length == 2
                    && candidate.GetParameters()[0].ParameterType == typeof(uint));
            if (tryGetRowMethod is not null)
            {
                var parameters = new object?[] { rowId, null };
                if (tryGetRowMethod.Invoke(sheet, parameters) is true)
                {
                    return parameters[1];
                }
            }

            var getRowMethod = sheet.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault((candidate) => candidate.Name == "GetRow" && candidate.GetParameters().Length == 1 && candidate.GetParameters()[0].ParameterType == typeof(uint));
            return getRowMethod?.Invoke(sheet, [rowId]);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeResolvedDutyLabel(string value)
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

    private static bool LooksLikeResolvedDutyLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith("Duty ID ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var lowered = value.ToLowerInvariant();
        if (lowered is "1 line" or "2 lines" or "3 lines" or "full seals")
        {
            return false;
        }

        return !lowered.Contains("deadline")
            && !lowered.Contains("second chance")
            && !lowered.Contains("reward list")
            && !lowered.Contains("select one")
            && !lowered.Contains("receive your reward")
            && !lowered.Contains("khloe");
    }

    private static IEnumerable<byte> EnumerateRouletteCandidates(byte rouletteId)
    {
        yield return rouletteId;
        if (rouletteId < byte.MaxValue)
        {
            yield return (byte)(rouletteId + 1);
        }

        if (rouletteId > 0)
        {
            yield return (byte)(rouletteId - 1);
        }
    }

    private FelixEquipmentPayload BuildEquipment()
    {
        var payload = new FelixEquipmentPayload();
        var inventoryManager = InventoryManager.Instance();
        var itemSheet = this.dataManager.GetExcelSheet<Item>();
        if (inventoryManager is null || itemSheet is null)
        {
            return payload;
        }

        var slots = new List<FelixEquipmentSlotPayload>();
        var totalItemLevel = 0;
        var countedSlots = 0;

        for (var slotIndex = 0; slotIndex <= 13; slotIndex++)
        {
            InventoryItem* inventoryItem = null;
            try
            {
                inventoryItem = inventoryManager->GetInventorySlot(InventoryType.EquippedItems, slotIndex);
            }
            catch
            {
                inventoryItem = null;
            }

            if (inventoryItem is null || inventoryItem->IsEmpty())
            {
                continue;
            }

            var resolvedItem = inventoryItem;
            try
            {
                var linkedItem = inventoryItem->GetLinkedItem();
                if (linkedItem is not null && !linkedItem->IsEmpty())
                {
                    resolvedItem = linkedItem;
                }
            }
            catch
            {
                resolvedItem = inventoryItem;
            }

            var itemId = resolvedItem->GetBaseItemId();
            if (itemId == 0)
            {
                itemId = resolvedItem->GetItemId();
            }
            if (itemId == 0 || !itemSheet.TryGetRow(itemId, out var itemRow))
            {
                continue;
            }

            var meta = ResolveEquipmentSlotMeta(slotIndex, itemRow);
            if (string.IsNullOrWhiteSpace(meta.Key))
            {
                continue;
            }

            var itemLevel = SafeToInt(GetNestedValue(itemRow, "LevelItem.RowId"));
            var requiredLevel = SafeToInt(GetNestedValue(itemRow, "LevelEquip"));
            var stainId = resolvedItem->GetStain(0);
            var slotPayload = new FelixEquipmentSlotPayload
            {
                Key = meta.Key,
                Label = meta.Label,
                SlotIndex = slotIndex,
                ItemId = itemId,
                ItemName = GetBestText(itemRow, "Name"),
                IconId = GetIconId(itemRow),
                GlamourId = resolvedItem->GetGlamourId(),
                ItemLevel = itemLevel,
                RequiredLevel = requiredLevel,
                ConditionPercent = resolvedItem->GetConditionPercentage(),
                SpiritbondPercent = NormalizePercent(resolvedItem->GetSpiritbondOrCollectability()),
                StainId = stainId,
                StainName = this.ResolveStainName(stainId),
                IsHighQuality = resolvedItem->IsHighQuality(),
            };

            if (!string.IsNullOrWhiteSpace(slotPayload.ItemName))
            {
                slots.Add(slotPayload);
                if (!string.Equals(slotPayload.Key, "soulCrystal", StringComparison.OrdinalIgnoreCase) && itemLevel > 0)
                {
                    totalItemLevel += itemLevel;
                    countedSlots += 1;
                }
            }
        }

        payload.Slots = slots
            .OrderBy((entry) => ResolveEquipmentOrder(entry.Key))
            .ThenBy((entry) => entry.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        payload.AverageItemLevel = countedSlots > 0
            ? (int)Math.Round(totalItemLevel / (double)countedSlots, MidpointRounding.AwayFromZero)
            : 0;
        return payload;
    }

    private static FelixFreeCompanyPayload BuildFreeCompanyPayload(object player, FelixCharacterPayload character)
    {
        var tag = GetBestText(
            player,
            "CompanyTag",
            "FreeCompanyTag",
            "FCTag",
            "FreeCompany.Tag",
            "Company.Tag");
        var name = GetBestText(
            player,
            "FreeCompanyName",
            "CompanyName",
            "FreeCompany.Name",
            "Company.Name");
        var rank = GetBestText(
            player,
            "FreeCompanyRank",
            "FreeCompanyMemberRank",
            "CompanyRank",
            "CompanyRank.Name",
            "FreeCompany.Rank",
            "FreeCompany.MemberRank");
        var companyRank = GetBestText(
            player,
            "FreeCompany.Rank",
            "FreeCompanyRank.Name",
            "Company.Rank",
            "Company.Rank.Name");
        var leaderCharacterName = GetBestText(
            player,
            "FreeCompany.Leader.Name",
            "FreeCompany.Master.Name",
            "FreeCompanyLeaderName",
            "Company.Leader.Name",
            "CompanyMaster");
        var slogan = GetBestText(
            player,
            "FreeCompany.Slogan",
            "Company.Slogan",
            "FreeCompany.Message",
            "CompanyMessage");
        var estate = GetBestText(
            player,
            "FreeCompany.Estate.Name",
            "FreeCompany.Estate.Plot",
            "Company.Estate.Name",
            "CompanyEstateName");
        var estateLocation = GetBestText(
            player,
            "FreeCompany.Estate.Address",
            "FreeCompany.Estate.Location",
            "FreeCompany.Estate.Area",
            "Company.Estate.Address",
            "CompanyEstateLocation");
        var housingType = GetBestText(
            player,
            "FreeCompany.Estate.Size",
            "FreeCompany.Estate.HousingType",
            "Company.Estate.Size",
            "CompanyEstateType");
        var memberCount = SafeToInt(
            GetNestedValue(player, "FreeCompany.MemberCount")
            ?? GetNestedValue(player, "FreeCompany.ActiveMembers")
            ?? GetNestedValue(player, "Company.MemberCount"));
        var activeMemberCount = SafeToInt(
            GetNestedValue(player, "FreeCompany.ActiveMemberCount")
            ?? GetNestedValue(player, "FreeCompany.ActiveMembers")
            ?? GetNestedValue(player, "Company.ActiveMemberCount"));

        var isLeader = false;
        var leaderValue = GetNestedValue(player, "IsFreeCompanyLeader")
            ?? GetNestedValue(player, "IsCompanyLeader")
            ?? GetNestedValue(player, "IsFcLeader");
        if (leaderValue is bool boolValue)
        {
            isLeader = boolValue;
        }
        else if (leaderValue is not null && bool.TryParse(leaderValue.ToString(), out var parsedLeader))
        {
            isLeader = parsedLeader;
        }

        return new FelixFreeCompanyPayload
        {
            Name = name,
            Tag = tag,
            World = character.CurrentWorld,
            Rank = rank,
            CompanyRank = companyRank,
            LeaderCharacterName = leaderCharacterName,
            MemberCount = memberCount,
            ActiveMemberCount = activeMemberCount,
            Slogan = slogan,
            Estate = estate,
            EstateLocation = estateLocation,
            HousingType = housingType,
            IsLeader = isLeader,
        };
    }

    private int GetGrandCompanyId()
    {
        return SafeToInt(this.playerState.GrandCompany.RowId);
    }

    private string ResolveStainName(int stainId)
    {
        if (stainId <= 0)
        {
            return string.Empty;
        }

        try
        {
            var stainSheet = this.dataManager.GetExcelSheet<Stain>();
            if (stainSheet is not null && stainSheet.TryGetRow((uint)stainId, out var stain))
            {
                return GetBestText(stain, "Name");
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private string ResolveZoneName(uint territoryId)
    {
        try
        {
            var territorySheet = this.dataManager.GetExcelSheet<TerritoryType>();
            if (territorySheet is not null && territorySheet.TryGetRow(territoryId, out var territory))
            {
                var placeName = territory.PlaceName.ValueNullable;
                var zoneName = ReadLuminaText(placeName);
                if (!string.IsNullOrWhiteSpace(zoneName))
                {
                    return zoneName;
                }

                var fallback = ReadLuminaText(territory.Name);
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    return fallback;
                }
            }
        }
        catch
        {
        }

        return $"Territory {territoryId}";
    }

    private static string ReadLuminaText(object? value)
    {
        object? current = value;
        for (var depth = 0; depth < 4 && current is not null; depth++)
        {
            if (current is string text)
            {
                return text.Trim();
            }

            var currentType = current.GetType();

            var textValue = currentType.GetProperty("TextValue")?.GetValue(current) as string;
            if (!string.IsNullOrWhiteSpace(textValue))
            {
                return textValue.Trim();
            }

            var extractText = currentType.GetMethod("ExtractText", Type.EmptyTypes);
            if (extractText is not null)
            {
                var extracted = extractText.Invoke(current, null) as string;
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    return extracted.Trim();
                }
            }

            foreach (var propertyName in new[] { "Name", "Singular", "Text" })
            {
                var propertyValue = currentType.GetProperty(propertyName)?.GetValue(current);
                if (propertyValue is string propertyText && !string.IsNullOrWhiteSpace(propertyText))
                {
                    return propertyText.Trim();
                }

                var nestedText = propertyValue?.ToString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(nestedText) && !nestedText.StartsWith("Lumina.", StringComparison.Ordinal))
                {
                    return nestedText;
                }
            }

            current = currentType.GetProperty("ValueNullable")?.GetValue(current)
                ?? currentType.GetProperty("Value")?.GetValue(current);
        }

        var fallback = value?.ToString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(fallback) && !fallback.StartsWith("Lumina.", StringComparison.Ordinal)
            ? fallback
            : string.Empty;
    }

    private List<FelixCollectionEntryPayload> BuildCollectionEntries(string sheetTypeName, string[] unlockMethodNames, string[] nameCandidates)
    {
        var rows = this.GetSheetRows(sheetTypeName);
        if (rows is null)
        {
            return [];
        }

        var entries = new List<FelixCollectionEntryPayload>();
        foreach (var row in rows)
        {
            if (row is null)
            {
                continue;
            }

            var rowId = GetUIntProperty(row, "RowId");
            if (rowId == 0)
            {
                continue;
            }

            var name = GetBestText(row, nameCandidates);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            entries.Add(new FelixCollectionEntryPayload
            {
                Id = rowId,
                Name = name,
                IconId = GetIconId(row),
                Unlocked = this.InvokeUnlockState(unlockMethodNames, row, rowId),
            });
        }

        entries.Sort(static (left, right) =>
        {
            var unlockedCompare = right.Unlocked.CompareTo(left.Unlocked);
            if (unlockedCompare != 0)
            {
                return unlockedCompare;
            }

            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });

        return entries;
    }

    private IEnumerable? GetSheetRows(string sheetTypeName)
    {
        var sheetType = ResolveSheetType(sheetTypeName);
        if (sheetType is null)
        {
            return null;
        }

        try
        {
            var method = this.dataManager.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault((candidate) => candidate.Name == "GetExcelSheet" && candidate.IsGenericMethodDefinition && candidate.GetParameters().Length == 0);
            if (method is null)
            {
                return null;
            }

            return method.MakeGenericMethod(sheetType).Invoke(this.dataManager, null) as IEnumerable;
        }
        catch
        {
            return null;
        }
    }

    private bool InvokeUnlockState(string[] methodNames, object row, uint rowId)
    {
        try
        {
            var methods = this.unlockState.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var methodName in methodNames)
            {
                foreach (var method in methods)
                {
                    if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (method.ReturnType != typeof(bool))
                    {
                        continue;
                    }

                    var parameters = method.GetParameters();
                    if (parameters.Length != 1)
                    {
                        continue;
                    }

                    try
                    {
                        var parameterType = parameters[0].ParameterType;
                        if (parameterType.IsInstanceOfType(row))
                        {
                            return method.Invoke(this.unlockState, new[] { row }) is bool unlocked && unlocked;
                        }

                        if (parameterType == typeof(uint))
                        {
                            return method.Invoke(this.unlockState, new object[] { rowId }) is bool unlocked && unlocked;
                        }

                        if (parameterType == typeof(int))
                        {
                            return method.Invoke(this.unlockState, new object[] { SafeToInt(rowId) }) is bool unlocked && unlocked;
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static int SafeToInt(uint value)
    {
        return value > int.MaxValue ? int.MaxValue : (int)value;
    }

    private static int SafeToInt(int value)
    {
        return value;
    }

    private static int SafeToInt(object? value)
    {
        if (value is null)
        {
            return 0;
        }

        return value switch
        {
            int intValue => intValue,
            uint uintValue => SafeToInt(uintValue),
            short shortValue => shortValue,
            ushort ushortValue => ushortValue,
            byte byteValue => byteValue,
            sbyte sbyteValue => sbyteValue,
            long longValue when longValue > int.MaxValue => int.MaxValue,
            long longValue when longValue < int.MinValue => int.MinValue,
            long longValue => (int)longValue,
            ulong ulongValue when ulongValue > (ulong)int.MaxValue => int.MaxValue,
            ulong ulongValue => (int)ulongValue,
            _ => int.TryParse(value.ToString(), out var parsed) ? parsed : 0,
        };
    }

    private static List<FelixCollectionEntryPayload> SortCollectionEntries(List<FelixCollectionEntryPayload> entries)
    {
        entries.Sort(static (left, right) =>
        {
            var unlockedCompare = right.Unlocked.CompareTo(left.Unlocked);
            if (unlockedCompare != 0)
            {
                return unlockedCompare;
            }

            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });

        return entries;
    }

    private static List<FelixCollectionEntryPayload> MergeCollectionEntries(params List<FelixCollectionEntryPayload>[] groups)
    {
        var merged = new List<FelixCollectionEntryPayload>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            foreach (var entry in group ?? [])
            {
                if (entry is null)
                {
                    continue;
                }

                var key = entry.Id > 0
                    ? $"id:{entry.Id}"
                    : $"name:{entry.Name}";
                if (!seen.Add(key))
                {
                    continue;
                }

                merged.Add(entry);
            }
        }

        return SortCollectionEntries(merged);
    }

    private static string GetBestText(object source, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var value = GetNestedValue(source, candidate);
            var text = ReadLuminaText(value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static uint GetIconId(object source)
    {
        var candidates = new[] { "Icon", "IconMain", "IconOff", "Image" };
        foreach (var candidate in candidates)
        {
            var value = GetNestedValue(source, candidate);
            var iconId = ConvertToUInt(value);
            if (iconId > 0)
            {
                return iconId;
            }
        }

        return 0;
    }

    private static uint GetUIntProperty(object source, string propertyName)
    {
        return ConvertToUInt(GetNestedValue(source, propertyName));
    }

    private static string GetRowName<T>(T rowRef)
    {
        object? value = null;
        var rowRefType = rowRef?.GetType();
        if (rowRefType is null)
        {
            return string.Empty;
        }

        value = rowRefType.GetProperty("ValueNullable")?.GetValue(rowRef)
            ?? rowRefType.GetProperty("Value")?.GetValue(rowRef);
        if (value is null)
        {
            return string.Empty;
        }

        var name = value.GetType().GetProperty("Name")?.GetValue(value);
        return ReadLuminaText(name);
    }

    private static object? GetNestedValue(object? source, string path)
    {
        if (source is null || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        object? current = source;
        foreach (var part in path.Split('.'))
        {
            if (current is null)
            {
                return null;
            }

            var type = current.GetType();
            var property = type.GetProperty(part, BindingFlags.Public | BindingFlags.Instance);
            if (property is null)
            {
                return null;
            }

            current = property.GetValue(current);
            if (current is null)
            {
                return null;
            }

            var currentType = current.GetType();
            current = currentType.GetProperty("ValueNullable")?.GetValue(current)
                ?? currentType.GetProperty("Value")?.GetValue(current)
                ?? current;
        }

        return current;
    }

    private static uint ConvertToUInt(object? value)
    {
        if (value is null)
        {
            return 0;
        }

        return value switch
        {
            uint uintValue => uintValue,
            int intValue when intValue > 0 => (uint)intValue,
            ushort ushortValue => ushortValue,
            short shortValue when shortValue > 0 => (uint)shortValue,
            byte byteValue => byteValue,
            long longValue when longValue > 0 && longValue <= uint.MaxValue => (uint)longValue,
            ulong ulongValue when ulongValue <= uint.MaxValue => (uint)ulongValue,
            _ => uint.TryParse(value.ToString(), out var parsed) ? parsed : 0,
        };
    }

    private static Type? ResolveSheetType(string sheetTypeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var resolved = assembly.GetType($"Lumina.Excel.Sheets.{sheetTypeName}");
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static void InspectObject(List<string> lines, HashSet<object> visited, string prefix, object value, int depth)
    {
        if (depth > 2 || value is null)
        {
            return;
        }

        if (!visited.Add(value))
        {
            return;
        }

        var type = value.GetType();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            var name = property.Name;
            if (!ContainsFreeCompanyKeyword(name) && depth == 0)
            {
                continue;
            }

            object? propertyValue = null;
            try
            {
                propertyValue = property.GetValue(value);
            }
            catch
            {
                continue;
            }

            propertyValue = GetUnwrappedValue(propertyValue);
            var path = $"{prefix}.{name}";
            if (propertyValue is null)
            {
                continue;
            }

            if (IsInspectableScalar(propertyValue))
            {
                lines.Add($"{path} = {propertyValue}");
                continue;
            }

            if (depth < 2 && (ContainsFreeCompanyKeyword(name) || ContainsFreeCompanyKeyword(propertyValue.GetType().Name)))
            {
                InspectObject(lines, visited, path, propertyValue, depth + 1);
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
        return lowered.Contains("freecompany")
            || lowered.Contains("company")
            || lowered.Contains("estate")
            || lowered.Contains("housing")
            || lowered.Contains("member")
            || lowered.Contains("leader")
            || lowered.Contains("rank")
            || lowered.Contains("tag")
            || lowered.Contains("slogan");
    }

    private static object? GetUnwrappedValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var type = value.GetType();
        return type.GetProperty("ValueNullable")?.GetValue(value)
            ?? type.GetProperty("Value")?.GetValue(value)
            ?? value;
    }

    private static bool IsInspectableScalar(object value)
    {
        return value is string
            || value is bool
            || value is byte
            || value is sbyte
            || value is short
            || value is ushort
            || value is int
            || value is uint
            || value is long
            || value is ulong
            || value is float
            || value is double
            || value is decimal
            || value.GetType().IsEnum;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public new bool Equals(object? x, object? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }

    private static int NormalizePercent(ushort rawValue)
    {
        if (rawValue == 0)
        {
            return 0;
        }

        if (rawValue <= 100)
        {
            return rawValue;
        }

        return Math.Min(100, rawValue / 100);
    }

    private static int ResolveEquipmentOrder(string key)
    {
        return key switch
        {
            "mainHand" => 0,
            "head" => 1,
            "offHand" => 2,
            "body" => 3,
            "hands" => 4,
            "earrings" => 5,
            "necklace" => 6,
            "bracelets" => 7,
            "legs" => 8,
            "soulCrystal" => 9,
            "feet" => 10,
            "rightRing" => 11,
            "leftRing" => 12,
            "waist" => 13,
            _ => 99,
        };
    }

    private static (string Key, string Label, int Order) ResolveEquipmentSlotMeta(int slotIndex, Item itemRow)
    {
        if (FixedEquipmentSlots.TryGetValue(slotIndex, out var fixedSlot))
        {
            return fixedSlot;
        }

        if (HasEquipFlag(itemRow, "Head"))
        {
            return ("head", "Head", 1);
        }

        if (HasEquipFlag(itemRow, "Body"))
        {
            return ("body", "Body", 3);
        }

        if (HasEquipFlag(itemRow, "Hands", "Gloves"))
        {
            return ("hands", "Hands", 4);
        }

        if (HasEquipFlag(itemRow, "Legs"))
        {
            return ("legs", "Legs", 8);
        }

        if (HasEquipFlag(itemRow, "Feet"))
        {
            return ("feet", "Feet", 10);
        }

        if (HasEquipFlag(itemRow, "Ears", "Earrings"))
        {
            return ("earrings", "Earrings", 5);
        }

        if (HasEquipFlag(itemRow, "Neck"))
        {
            return ("necklace", "Necklace", 6);
        }

        if (HasEquipFlag(itemRow, "Wrists"))
        {
            return ("bracelets", "Bracelets", 7);
        }

        if (HasEquipFlag(itemRow, "Waist"))
        {
            return ("waist", "Waist", 13);
        }

        return ($"slot{slotIndex}", $"Slot {slotIndex}", 99);
    }

    private static bool HasEquipFlag(Item itemRow, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (IsTruthyValue(GetNestedValue(itemRow, $"EquipSlotCategory.{propertyName}")))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTruthyValue(object? value)
    {
        return value switch
        {
            bool boolValue => boolValue,
            byte byteValue => byteValue > 0,
            sbyte sbyteValue => sbyteValue > 0,
            short shortValue => shortValue > 0,
            ushort ushortValue => ushortValue > 0,
            int intValue => intValue > 0,
            uint uintValue => uintValue > 0,
            long longValue => longValue > 0,
            ulong ulongValue => ulongValue > 0,
            _ => bool.TryParse(value?.ToString(), out var parsed) && parsed,
        };
    }
}
