using System.Text.Json;
using System.Text.Json.Serialization;

namespace BridgePay.Agent.Contracts;

public sealed class PublisherPubSubMessage
{
    [JsonPropertyName("operation")]
    public required string Operation { get; init; }

    [JsonPropertyName("payment_id")]
    public required string PaymentId { get; init; }

    [JsonPropertyName("store_id")]
    public required string StoreId { get; init; }

    [JsonPropertyName("terminal_id")]
    public required string TerminalId { get; init; }

    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement> ExtraFields { get; init; } = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
}
