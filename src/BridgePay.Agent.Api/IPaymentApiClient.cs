namespace BridgePay.Agent.Api;

public interface IPaymentApiClient
{
    Task PostResultAsync(string paymentId, string status, CancellationToken cancellationToken);
}