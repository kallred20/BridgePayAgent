using System.Text.Json;
using BridgePay.Agent.Contracts;
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
        // start test 
        var endpoint = new TerminalEndpoint
        {
            TerminalId = "test-terminal",
            IpAddress = "192.168.68.65",
            Port = 10009
        };

        var request = new TerminalSaleRequest
        {
            PaymentId = Guid.NewGuid().ToString("N"),
            TerminalId = endpoint.TerminalId,
            Amount = 100,
            InvoiceNumber = "INV-1001",
            EcrReferenceNumber = Guid.NewGuid().ToString("N"),
            ClerkId = "123"
        };

        var result = await _paxClient.SaleAsync(request, endpoint, CancellationToken.None);
        Console.WriteLine(request.PaymentId);
        Console.WriteLine(JsonSerializer.Serialize(result));
        // end test
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
