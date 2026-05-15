using System.Net;
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
        string? errorCode,
        string? errorMessage,
        string? ecrReferenceNumber,
        string? hostReferenceNumber,
        string? terminalReferenceNumber,
        string? last4,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            throw new ArgumentException("Payment ID is required.", nameof(paymentId));
        }

        var response = await PostPaymentEventAsync(
            paymentId,
            new PaymentEventWithLast4Request(
                status,
                eventType,
                occurredAt,
                errorCode,
                errorMessage,
                ecrReferenceNumber,
                hostReferenceNumber,
                terminalReferenceNumber,
                last4),
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity && !string.IsNullOrWhiteSpace(last4))
        {
            response.Dispose();
            response = await PostPaymentEventAsync(
                paymentId,
                new PaymentEventRequest(
                    status,
                    eventType,
                    occurredAt,
                    errorCode,
                    errorMessage,
                    ecrReferenceNumber,
                    hostReferenceNumber,
                    terminalReferenceNumber),
                cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Payment event request failed with status {(int)response.StatusCode} ({response.StatusCode}). Response: {responseBody}",
                null,
                response.StatusCode);
        }
    }

    private static string AppendTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
    }

    private Task<HttpResponseMessage> PostPaymentEventAsync(
        string paymentId,
        object request,
        CancellationToken cancellationToken)
    {
        return _httpClient.PostAsJsonAsync(
            $"payments/{Uri.EscapeDataString(paymentId)}/events",
            request,
            cancellationToken);
    }

    private sealed record PaymentEventRequest(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("event_type")] string EventType,
        [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
        [property: JsonPropertyName("error_code")] string? ErrorCode,
        [property: JsonPropertyName("error_message")] string? ErrorMessage,
        [property: JsonPropertyName("ecr_reference_number")] string? EcrReferenceNumber,
        [property: JsonPropertyName("host_reference_number")] string? HostReferenceNumber,
        [property: JsonPropertyName("terminal_reference_number")] string? TerminalReferenceNumber);

    private sealed record PaymentEventWithLast4Request(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("event_type")] string EventType,
        [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
        [property: JsonPropertyName("error_code")] string? ErrorCode,
        [property: JsonPropertyName("error_message")] string? ErrorMessage,
        [property: JsonPropertyName("ecr_reference_number")] string? EcrReferenceNumber,
        [property: JsonPropertyName("host_reference_number")] string? HostReferenceNumber,
        [property: JsonPropertyName("terminal_reference_number")] string? TerminalReferenceNumber,
        [property: JsonPropertyName("last4")] string? Last4);
}
