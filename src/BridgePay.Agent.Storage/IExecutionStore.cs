namespace BridgePay.Agent.Storage;

public interface IExecutionStore
{
    Task SaveAsync(string key, string value, CancellationToken cancellationToken);
}