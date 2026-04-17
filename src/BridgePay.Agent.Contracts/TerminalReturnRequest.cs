namespace BridgePay.Agent.Contracts;

public sealed class TerminalReturnRequest
{
    public required string PaymentId { get; init; }
    public required string TerminalId { get; init; }

    public string? EcrReferenceNumber { get; init; }
    public string? OriginalReferenceNumber { get; init; }
    public string? OriginalEcrReferenceNumber { get; init; }
    public string? HostReferenceNumber { get; init; }
}
