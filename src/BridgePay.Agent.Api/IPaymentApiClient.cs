using BridgePay.Agent.Contracts;

namespace BridgePay.Agent.Api;

public interface IPaymentApiClient
{
    Task PostResultAsync(
        PublisherPubSubMessage message,
        string status,
        CancellationToken cancellationToken);
}
