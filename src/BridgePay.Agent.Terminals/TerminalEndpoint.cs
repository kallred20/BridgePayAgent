namespace BridgePay.Agent.Terminals;

public sealed class TerminalEndpoint
{
    public required string TerminalId { get; init; }
    public required string IpAddress { get; init; }
    public required int Port { get; init; }
}