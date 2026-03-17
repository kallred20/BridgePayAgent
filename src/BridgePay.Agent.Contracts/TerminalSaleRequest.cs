namespace BridgePay.Agent.Contracts;

public sealed class TerminalSaleRequest
{
    public required string TerminalId { get; init; }
    public required string IpAddress { get; init; }
    public required int Port { get; init; }

    // store in cents
    public required long Amount { get; init; }

    public string? InvoiceNumber { get; init; }
    public string? EcrReferenceNumber { get; init; }
    public string? ClerkId { get; init; }
}