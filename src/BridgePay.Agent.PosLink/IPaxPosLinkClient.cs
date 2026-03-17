using BridgePay.Agent.Contracts;

namespace BridgePay.Agent.PosLink;

public interface IPaxPosLinkClient
{
    Task<TerminalTransactionResult> SaleAsync(
        TerminalSaleRequest request,
        CancellationToken cancellationToken);
}