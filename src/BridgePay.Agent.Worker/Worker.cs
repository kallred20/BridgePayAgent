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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var terminal = _terminalRegistry.GetByTerminalId("lane-1-terminal");

                if (terminal is not null)
                {
                    var ok = await _paxClient.PingAsync(
                        terminal.IpAddress,
                        terminal.Port,
                        stoppingToken);

                    _logger.LogInformation(
                        "Terminal lookup succeeded. TerminalId={TerminalId}, Ip={Ip}, Port={Port}, Ping={Ping}",
                        terminal.TerminalId,
                        terminal.IpAddress,
                        terminal.Port,
                        ok);
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled worker error.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("BridgePay Agent stopping.");
    }
}