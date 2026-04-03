namespace BridgePay.Agent.Contracts;

public sealed class TerminalTransactionResult
{
    public required bool Success { get; init; }
    public required string ResponseCode { get; init; }
    public required string ResponseMessage { get; init; }

    public string? ApprovalCode { get; init; }
    public string? ReferenceNumber { get; init; }
    public string? TerminalReferenceNumber { get; init; }
    public string? EcrReferenceNumber { get; init; }
    public string? HostReferenceNumber { get; init; }
    public string? CardType { get; init; }
    public string? MaskedPan { get; init; }
}
