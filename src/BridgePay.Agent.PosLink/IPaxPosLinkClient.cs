using BridgePay.Agent.Contracts;
using BridgePay.Agent.Terminals;

namespace BridgePay.Agent.PosLink;

public interface IPaxPosLinkClient
{
    Task<TerminalTransactionResult> GiftAsync(
        TerminalGiftRequest request,
        TerminalEndpoint endpoint,
        CancellationToken cancellationToken);

    Task<TerminalTransactionResult> SaleAsync(
        TerminalSaleRequest request,
        TerminalEndpoint endpoint,
        CancellationToken cancellationToken);

    Task<TerminalTransactionResult> ReturnAsync(
        TerminalReturnRequest request,
        TerminalEndpoint endpoint,
        CancellationToken cancellationToken);

    Task<TerminalTransactionResult> VoidAsync(
        TerminalVoidRequest request,
        TerminalEndpoint endpoint,
        CancellationToken cancellationToken);
}
