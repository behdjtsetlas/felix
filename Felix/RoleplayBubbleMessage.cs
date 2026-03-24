using System;
using System.Numerics;

namespace Felix;

public sealed class RoleplayBubbleMessage
{
    public long Id { get; init; } = DateTimeOffset.UtcNow.Ticks;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string ChannelLabel { get; init; } = string.Empty;
    public string SpeakerName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool IsOwnMessage { get; init; }
    public ulong? SpeakerGameObjectId { get; init; }
    public Vector3? SpeakerWorldPosition { get; init; }
    public float SpeakerHitboxRadius { get; init; }
}
