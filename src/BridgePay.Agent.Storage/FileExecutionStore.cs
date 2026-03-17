namespace BridgePay.Agent.Storage;

public sealed class FileExecutionStore : IExecutionStore
{
    public Task SaveAsync(string key, string value, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}