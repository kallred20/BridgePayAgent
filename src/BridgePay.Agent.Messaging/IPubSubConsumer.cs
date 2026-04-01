namespace BridgePay.Agent.Messaging;

public interface IPubSubConsumer
{
    Task RunAsync(Func<string, CancellationToken, Task<bool>> handler, CancellationToken cancellationToken);
}
