namespace BridgePay.Agent.Terminals;

public sealed class TerminalOptions
{
    public List<TerminalEndpoint> Terminals { get; init; } = new();
}