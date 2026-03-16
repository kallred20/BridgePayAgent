namespace BridgePay.Agent.Messaging;

public sealed class PubSubConsumer : IPubSubConsumer
{
    public Task<string?> PullAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(null);
    }
}