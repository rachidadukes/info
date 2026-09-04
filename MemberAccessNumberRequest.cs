using System.Text.Json.Serialization;

namespace LegalHoldAdmin.Models;

public sealed class MemberAccessNumberRequest
{
    [JsonPropertyName("NECUREquestHeader")]
    public required NecuRequestHeader NECUREquestHeader { get; init; }

    [JsonPropertyName("accessNumber")]
    public required string AccessNumber { get; init; }
}

public sealed class NecuRequestHeader
{
    [JsonPropertyName("rqUID")]
    public required string RqUID { get; init; }

    [JsonPropertyName("consumerChannel")]
    public required string ConsumerChannel { get; init; }

    [JsonPropertyName("consumingApplicationName")]
    public required string ConsumingApplicationName { get; init; }

    [JsonPropertyName("credential")]
    public required string Credential { get; init; }
}
