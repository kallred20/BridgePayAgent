using BridgePay.Agent.Contracts;

namespace BridgePay.Agent.Api;

public interface IPaymentApiClient
{
    Task PostPaymentEventAsync(
        string paymentId,
        string status,
        string eventType,
        DateTimeOffset occurredAt,
        string? errorCode,
        string? errorMessage,
        string? ecrReferenceNumber,
        string? hostReferenceNumber,
        string? terminalReferenceNumber,
        CancellationToken cancellationToken);
}
 
