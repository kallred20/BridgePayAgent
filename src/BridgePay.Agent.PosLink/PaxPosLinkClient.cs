using BridgePay.Agent.Contracts;
using POSLinkAdmin;
using POSLinkAdmin.Util;
using POSLinkCore.CommSetting;
using POSLinkSemiIntegration;
using POSLinkSemiIntegration.Transaction;
using POSLinkSemiIntegration.Util;

namespace BridgePay.Agent.PosLink;

public sealed class PaxPosLinkClient : IPaxPosLinkClient
{
    public Task<TerminalTransactionResult> SaleAsync(
        TerminalSaleRequest request,
        CancellationToken cancellationToken)
    {
        // POSLink is sync-style, so keep it simple for now
        var result = ExecuteSale(request);
        return Task.FromResult(result);
    }

    private TerminalTransactionResult ExecuteSale(TerminalSaleRequest request)
    {
        var tcp = new TcpSetting
        {
            Ip = request.IpAddress,
            Port = request.Port,
            Timeout = 60000
        };

        var sdk = POSLinkSemi.GetPOSLinkSemi();
        var terminal = sdk.GetTerminal(tcp);

        var saleRequest = new DoCreditRequest
        {
            TransactionType = TransactionType.Sale,
            AmountInformation = new AmountRequest
            {
                TransactionAmount = ToPosLinkAmount(request.Amount)
            },
            TraceInformation = new TraceRequest
            {
                EcrReferenceNumber = request.EcrReferenceNumber ?? Guid.NewGuid().ToString("N"),
                InvoiceNumber = request.InvoiceNumber ?? string.Empty
            },
            CashierInformation = new CashierRequest
            {
                ClerkId = request.ClerkId ?? string.Empty
            }

            // We will set TransactionType next once you confirm the enum value in IntelliSense
        };

        DoCreditResponse? response = null;
        var txResult = terminal.Transaction.DoCredit(saleRequest, ref response);

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
            Success = IsApproved(response.ResponseCode),
            ResponseCode = response.ResponseCode ?? string.Empty,
            ResponseMessage = response.ResponseMessage ?? string.Empty,
            ApprovalCode = response.HostInformation?.AuthorizationCode,
            ReferenceNumber = response.TraceInformation?.EcrReferenceNumber,
            CardType = response.AccountInformation?.CardType?.ToString(),
            MaskedPan = response.AccountInformation?.MaskedPan
        };
    }

    private static string ToPosLinkAmount(long amountInCents)
    {
        // POSLink amount is numeric cents as string, e.g. 12.34 => "1234"
        return amountInCents.ToString();
    }

    private static bool IsApproved(string? responseCode)
    {
        // Start simple. You can refine after you see real terminal values.
        return string.Equals(responseCode, "000000", StringComparison.OrdinalIgnoreCase)
            || string.Equals(responseCode, "0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(responseCode, "APPROVED", StringComparison.OrdinalIgnoreCase);
    }
}