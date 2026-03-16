namespace BridgePay.Agent.Messaging;

public interface IPubSubConsumer
{
    Task<string?> PullAsync(CancellationToken cancellationToken);
}
