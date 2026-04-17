using BridgePay.Agent.Contracts;

namespace BridgePay.Agent.Worker;

public interface IGiftCardCommandHandler
{
    Task<bool> HandleAsync(PublisherPubSubMessage message, CancellationToken cancellationToken);
}
