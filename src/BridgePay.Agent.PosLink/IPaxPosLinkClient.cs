using BridgePay.Agent.Contracts;
using BridgePay.Agent.Terminals;

namespace BridgePay.Agent.PosLink;

public interface IPaxPosLinkClient
{
    Task<TerminalTransactionResult> SaleAsync(
        TerminalSaleRequest request,
        TerminalEndpoint endpoint,
        CancellationToken cancellationToken);
}