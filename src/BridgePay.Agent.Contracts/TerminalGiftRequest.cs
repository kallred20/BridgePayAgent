namespace BridgePay.Agent.Contracts;

public sealed class TerminalGiftRequest
{
    public required string PaymentId { get; init; }
    public required string TerminalId { get; init; }
    public required TerminalGiftTransactionType Type { get; init; }

    public long? Amount { get; init; } // cents
    public string? InvoiceNumber { get; init; }
    public string? EcrReferenceNumber { get; init; }
    public string? ClerkId { get; init; }
}
