namespace BridgePay.Agent.Api;

public sealed class PaymentApiClient : IPaymentApiClient
{
    public Task PostResultAsync(string paymentId, string status, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}