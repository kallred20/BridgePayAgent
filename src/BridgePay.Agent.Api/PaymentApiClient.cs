using BridgePay.Agent.Contracts;

namespace BridgePay.Agent.Api;

public sealed class PaymentApiClient : IPaymentApiClient
{
    public Task PostResultAsync(
        PublisherPubSubMessage message,
        string status,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
