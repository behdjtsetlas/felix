using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Felix.Models;

public sealed class FelixRpSceneJoinRequest
{
    [JsonPropertyName("joinCode")]
    public string JoinCode { get; set; } = string.Empty;

    [JsonPropertyName("contentId")]
    public string ContentId { get; set; } = string.Empty;

    [JsonPropertyName("characterName")]
    public string CharacterName { get; set; } = string.Empty;
}

public sealed class FelixRpSceneLinesRequest
{
    [JsonPropertyName("joinCode")]
    public string JoinCode { get; set; } = string.Empty;

    [JsonPropertyName("lines")]
    public List<FelixRpSceneLineUpload> Lines { get; set; } = [];
}

public sealed class FelixRpSceneLineUpload
{
    [JsonPropertyName("lineId")]
    public string LineId { get; set; } = string.Empty;

    [JsonPropertyName("contentId")]
    public string ContentId { get; set; } = string.Empty;

    [JsonPropertyName("speakerName")]
    public string SpeakerName { get; set; } = string.Empty;

    [JsonPropertyName("channelLabel")]
    public string ChannelLabel { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("isOwnMessage")]
    public bool IsOwnMessage { get; set; }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class FelixRpSceneJoinResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    [JsonPropertyName("sceneId")]
    public string SceneId { get; set; } = string.Empty;

    [JsonPropertyName("joinCode")]
    public string JoinCode { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}

public sealed class FelixRpSceneLinesResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("appended")]
    public int Appended { get; set; }

    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;
}
