namespace BridgePay.Agent.PosLink;

public interface IPaxPosLinkClient
{
    Task<bool> PingAsync(string ipAddress, int port, CancellationToken cancellationToken);
}