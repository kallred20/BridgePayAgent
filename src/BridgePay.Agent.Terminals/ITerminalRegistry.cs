namespace BridgePay.Agent.Terminals;

public interface ITerminalRegistry
{
    TerminalEndpoint? GetByTerminalId(string terminalId);
}
