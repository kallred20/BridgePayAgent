using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace BridgePay.Agent.Api;

public sealed class PaymentApiClient : IPaymentApiClient
{
    private readonly HttpClient _httpClient;

    public PaymentApiClient(HttpClient httpClient, IOptions<PaymentApiOptions> options)
    {
        _httpClient = httpClient;

        var baseUrl = options.Value.BaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("PaymentApi:BaseUrl is required.");
        }

        _httpClient.BaseAddress = new Uri(AppendTrailingSlash(baseUrl), UriKind.Absolute);
    }

    public async Task PostPaymentEventAsync(
        string paymentId,
        string status,
        string eventType,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            throw new ArgumentException("Payment ID is required.", nameof(paymentId));
        }

        var response = await _httpClient.PostAsJsonAsync(
            $"payments/{Uri.EscapeDataString(paymentId)}/events",
            new PaymentEventRequest(status, eventType, occurredAt),
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static string AppendTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
    }

    private sealed record PaymentEventRequest(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("event_type")] string EventType,
        [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);
}
