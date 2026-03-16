namespace BridgePay.Agent.Terminals;

public sealed class TerminalRegistry : ITerminalRegistry
{
    public TerminalEndpoint? GetByTerminalId(string terminalId)
    {
        return new TerminalEndpoint
        {
            TerminalId = terminalId,
            IpAddress = "192.168.1.50",
            Port = 10009
        };
    }
}