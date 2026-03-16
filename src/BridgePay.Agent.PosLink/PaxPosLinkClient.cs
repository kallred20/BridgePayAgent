namespace BridgePay.Agent.PosLink;

public sealed class PaxPosLinkClient : IPaxPosLinkClient
{
    public Task<bool> PingAsync(string ipAddress, int port, CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }
}