using BridgePay.Agent.Contracts;
using BridgePay.Agent.Terminals;
using POSLinkAdmin.Const;
using POSLinkAdmin.Util;
using POSLinkCore.CommunicationSetting;
using POSLinkSemiIntegration;
using POSLinkSemiIntegration.Transaction;
using POSLinkSemiIntegration.Util;

namespace BridgePay.Agent.PosLink;

public sealed class PaxPosLinkClient : IPaxPosLinkClient
{
    public Task<TerminalTransactionResult> SaleAsync(
        TerminalSaleRequest request,
        TerminalEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        var result = ExecuteSale(request, endpoint);
        return Task.FromResult(result);
    }

    private TerminalTransactionResult ExecuteSale(
        TerminalSaleRequest request,
        TerminalEndpoint endpoint)
    {
        var tcp = new TcpSetting
        {
            Ip = endpoint.IpAddress,
            Port = endpoint.Port,
            Timeout = 60000
        };

        var sdk = POSLinkSemi.GetPOSLinkSemi();
        var terminal = sdk.GetTerminal(tcp);

        var saleRequest = new DoCreditRequest
        {
            TransactionType = TransactionType.Sale,
            AmountInformation = new AmountRequest
            {
                TransactionAmount = request.Amount.ToString()
            },
            TraceInformation = new TraceRequest
            {
                EcrReferenceNumber = request.EcrReferenceNumber ?? Guid.NewGuid().ToString("N"),
                InvoiceNumber = request.InvoiceNumber ?? string.Empty
            }
        };

        terminal.Transaction.DoCredit(saleRequest, out DoCreditResponse? response);

        if (response is null)
        {
            return new TerminalTransactionResult
            {
                Success = false,
                ResponseCode = "NO_RESPONSE",
                ResponseMessage = "Terminal returned no response."
            };
        }

        return new TerminalTransactionResult
        {
            Success = string.Equals(response.ResponseCode, "000000", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(response.ResponseCode, "0", StringComparison.OrdinalIgnoreCase),
            ResponseCode = response.ResponseCode ?? string.Empty,
            ResponseMessage = response.ResponseMessage ?? string.Empty,
            ApprovalCode = response.HostInformation?.AuthorizationCode,
            ReferenceNumber = response.TraceInformation?.EcrReferenceNumber,
            CardType = response.AccountInformation?.CardType.ToString(),
            MaskedPan = response.AccountInformation?.CurrentAccountNumber
        };
    }
}
