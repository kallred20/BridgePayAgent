using Microsoft.Extensions.Options;

namespace BridgePay.Agent.Terminals;

public sealed class TerminalRegistry : ITerminalRegistry
{
    private readonly TerminalOptions _options;

    public TerminalRegistry(IOptions<TerminalOptions> options)
    {
        _options = options.Value;
    }

    public TerminalEndpoint? GetByTerminalId(string terminalId)
    {
        return _options.Terminals.FirstOrDefault(t =>
            string.Equals(t.TerminalId, terminalId, StringComparison.OrdinalIgnoreCase));
    }
}