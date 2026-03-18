using BridgePay.Agent.Messaging;
using BridgePay.Agent.PosLink;
using BridgePay.Agent.Terminals;

namespace BridgePay.Agent.Worker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IPubSubConsumer _consumer;
    private readonly ITerminalRegistry _terminalRegistry;
    private readonly IPaxPosLinkClient _paxClient;

    public Worker(
        ILogger<Worker> logger,
        IPubSubConsumer consumer,
        ITerminalRegistry terminalRegistry,
        IPaxPosLinkClient paxPosLinkClient)
    {
        _logger = logger;
        _consumer = consumer;
        _terminalRegistry = terminalRegistry;
        _paxClient = paxPosLinkClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BridgePay Agent started.");

        var terminal = _terminalRegistry.GetByTerminalId("test-terminal");

        if (terminal is null)
        {
            _logger.LogError("Terminal not found.");
        }
        else
        {
            _logger.LogInformation(
                "Resolved terminal {TerminalId} to {IpAddress}:{Port}",
                terminal.TerminalId,
                terminal.IpAddress,
                terminal.Port);
        }

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}