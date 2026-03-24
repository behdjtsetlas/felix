using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Felix.Models;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Character = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace Felix.Services;

public sealed unsafe partial class SnapshotBuilder
{
    private const int SocialNearbyCap = 64;
    private const int SocialRecentCap = 24;

    private Dictionary<string, FelixSocialContactPayload>? socialLastNearbyByKey;
    private readonly List<SocialRecentDeparted> socialRecentDeparted = [];

    private sealed class SocialRecentDeparted
    {
        public required string Key { get; init; }
        public required FelixSocialContactPayload Contact { get; set; }
        public DateTimeOffset LeftAt { get; set; }
    }

    private FelixSocialPayload BuildSocialPayload(Configuration configuration, string zoneName)
    {
        var social = new FelixSocialPayload();
        if (!configuration.IncludeSocialRadar)
        {
            return social;
        }

        var local = this.objectTable.LocalPlayer;
        if (local is null || !local.IsValid() || local.ObjectKind != ObjectKind.Player)
        {
            return social;
        }

        var now = DateTimeOffset.UtcNow;
        var nowIso = now.ToString("O", CultureInfo.InvariantCulture);
        var partyContentIdsLong = BuildPartyContentIdSet();
        var localFcTag = ReadFreeCompanyTag(local);
        var currentByKey = new Dictionary<string, FelixSocialContactPayload>(StringComparer.Ordinal);
        var nearbyWorking = new List<FelixSocialContactPayload>();

        foreach (var entry in this.objectTable)
        {
            if (entry is null || !entry.IsValid() || entry.GameObjectId == 0 || entry.GameObjectId == local.GameObjectId)
            {
                continue;
            }

            if (entry.ObjectKind != ObjectKind.Player)
            {
                continue;
            }

            if (!TryGetCharacter(entry, out var character))
            {
                continue;
            }

            var contentId = character->ContentId;
            var contentIdLong = unchecked((long)contentId);
            var key = contentId != 0 ? $"c:{contentIdLong}" : $"o:{entry.GameObjectId}";
            var world = ResolveWorldName(character);
            var name = entry.Name.TextValue;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var distance = Vector3.Distance(local.Position, entry.Position);
            var isFriend = character->IsFriend;
            var otherFcTag = ReadFreeCompanyTag(entry);
            var isFc = localFcTag.Length > 0 && localFcTag.AsSpan().SequenceEqual(otherFcTag);
            var isParty = contentId != 0 && partyContentIdsLong.Contains(contentIdLong);
            if (!isFriend && !isFc && !isParty)
            {
                continue;
            }

            var badges = BuildSocialBadges(isFriend, isFc, isParty);

            var contact = new FelixSocialContactPayload
            {
                ObjectId = key,
                Name = name,
                World = world,
                Distance = distance,
                IsNearby = true,
                IsFriend = isFriend,
                IsPartyMember = isParty,
                IsFreeCompanyMember = isFc,
                IsLinkshellContact = false,
                IsCrossWorldLinkshellContact = false,
                LastSeenAt = nowIso,
                LastSpokeAt = string.Empty,
                LastZoneName = zoneName,
                LeftAt = string.Empty,
                Badges = badges,
            };

            currentByKey[key] = contact;
            nearbyWorking.Add(contact);
        }

        nearbyWorking.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        var nearby = nearbyWorking.Count <= SocialNearbyCap
            ? nearbyWorking
            : nearbyWorking.GetRange(0, SocialNearbyCap);

        if (this.socialLastNearbyByKey is not null)
        {
            foreach (var (prevKey, prevContact) in this.socialLastNearbyByKey)
            {
                if (currentByKey.ContainsKey(prevKey))
                {
                    continue;
                }

                var departed = CloneContactDeparted(prevContact, zoneName, now);
                UpsertDeparted(prevKey, departed, now);
            }
        }

        foreach (var key in currentByKey.Keys)
        {
            RemoveDepartedIfPresent(key);
        }

        this.socialLastNearbyByKey = currentByKey;

        var recentContacts = this.socialRecentDeparted
            .OrderByDescending((entry) => entry.LeftAt)
            .Take(SocialRecentCap)
            .Select((entry) => entry.Contact)
            .ToList();

        social.Nearby = nearby;
        social.NearbyCount = nearby.Count;
        social.RecentContacts = recentContacts;
        social.FriendCount = nearby.Count((contact) => contact.IsFriend);
        social.FreeCompanyCount = nearby.Count((contact) => contact.IsFreeCompanyMember);
        social.PartyCount = nearby.Count((contact) => contact.IsPartyMember);
        social.LinkshellCount = 0;
        social.CrossWorldLinkshellCount = 0;
        return social;
    }

    private HashSet<long> BuildPartyContentIdSet()
    {
        var set = new HashSet<long>();
        try
        {
            for (var i = 0; i < this.partyList.Length; i++)
            {
                var member = this.partyList[i];
                if (member is null)
                {
                    continue;
                }

                var id = member.ContentId;
                if (id != 0)
                {
                    set.Add(id);
                }
            }
        }
        catch
        {
        }

        return set;
    }

    private static List<string> BuildSocialBadges(bool isFriend, bool isFreeCompany, bool isParty)
    {
        var badges = new List<string>();
        if (isFriend)
        {
            badges.Add("friend");
        }

        if (isFreeCompany)
        {
            badges.Add("freeCompany");
        }

        if (isParty)
        {
            badges.Add("party");
        }

        return badges;
    }

    private void UpsertDeparted(string key, FelixSocialContactPayload contact, DateTimeOffset leftAt)
    {
        var existing = this.socialRecentDeparted.Find((entry) => entry.Key == key);
        if (existing is not null)
        {
            existing.Contact = contact;
            existing.LeftAt = leftAt;
            return;
        }

        this.socialRecentDeparted.Add(new SocialRecentDeparted
        {
            Key = key,
            Contact = contact,
            LeftAt = leftAt,
        });

        if (this.socialRecentDeparted.Count > SocialRecentCap * 2)
        {
            this.socialRecentDeparted.Sort((a, b) => b.LeftAt.CompareTo(a.LeftAt));
            this.socialRecentDeparted.RemoveRange(SocialRecentCap, this.socialRecentDeparted.Count - SocialRecentCap);
        }
    }

    private void RemoveDepartedIfPresent(string key)
    {
        for (var i = this.socialRecentDeparted.Count - 1; i >= 0; i--)
        {
            if (this.socialRecentDeparted[i].Key == key)
            {
                this.socialRecentDeparted.RemoveAt(i);
            }
        }
    }

    private static FelixSocialContactPayload CloneContactDeparted(FelixSocialContactPayload source, string zoneName, DateTimeOffset leftAt)
    {
        var leftIso = leftAt.ToString("O", CultureInfo.InvariantCulture);
        return new FelixSocialContactPayload
        {
            ObjectId = source.ObjectId,
            Name = source.Name,
            World = source.World,
            Distance = source.Distance,
            IsNearby = false,
            IsFriend = source.IsFriend,
            IsPartyMember = source.IsPartyMember,
            IsFreeCompanyMember = source.IsFreeCompanyMember,
            IsLinkshellContact = false,
            IsCrossWorldLinkshellContact = false,
            LastSeenAt = source.LastSeenAt,
            LastSpokeAt = source.LastSpokeAt,
            LastZoneName = string.IsNullOrWhiteSpace(source.LastZoneName) ? zoneName : source.LastZoneName,
            LeftAt = leftIso,
            Badges = source.Badges is { Count: > 0 } ? [.. source.Badges] : [],
        };
    }

    private unsafe string ResolveWorldName(Character* character)
    {
        try
        {
            var world = character->HomeWorld;
            return GetRowName(world);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static unsafe bool TryGetCharacter(IGameObject gameObject, out Character* character)
    {
        character = null;
        if (gameObject.Address == nint.Zero)
        {
            return false;
        }

        character = (Character*)gameObject.Address;
        if (character is null || (ObjectKind)character->GameObject.ObjectKind != ObjectKind.Player)
        {
            character = null;
            return false;
        }

        return true;
    }

    private static unsafe byte[] ReadFreeCompanyTag(IGameObject gameObject)
    {
        if (!TryGetCharacter(gameObject, out var character))
        {
            return [];
        }

        try
        {
            return character->FreeCompanyTag.ToArray();
        }
        catch
        {
            return [];
        }
    }
}
