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
        cancellationToken.ThrowIfCancellationRequested();

        var result = ExecuteSale(request, endpoint, cancellationToken);
        return Task.FromResult(result);
    }

    private TerminalTransactionResult ExecuteSale(
        TerminalSaleRequest request,
        TerminalEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        if (request.Amount <= 0)
        {
            return Failure("INVALID_AMOUNT", "Amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(endpoint.IpAddress))
        {
            return Failure("INVALID_ENDPOINT", "Terminal IP address is required.");
        }

        if (endpoint.Port <= 0)
        {
            return Failure("INVALID_ENDPOINT", "Terminal port must be greater than zero.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tcp = new TcpSetting
            {
                Ip = endpoint.IpAddress,
                Port = endpoint.Port,
                Timeout = 60000
            };

            var sdk = POSLinkSemi.GetPOSLinkSemi();
            var terminal = sdk.GetTerminal(tcp);

            var ecrRef = string.IsNullOrWhiteSpace(request.EcrReferenceNumber)
                ? request.PaymentId
                : request.EcrReferenceNumber;

            var saleRequest = new DoCreditRequest
            {
                TransactionType = TransactionType.Sale,
                AmountInformation = new AmountRequest
                {
                    TransactionAmount = request.Amount.ToString()
                },
                TraceInformation = new TraceRequest
                {
                    EcrReferenceNumber = ecrRef,
                    InvoiceNumber = request.InvoiceNumber ?? string.Empty
                },
                CashierInformation = string.IsNullOrWhiteSpace(request.ClerkId)
                    ? null
                    : new CashierRequest
                    {
                        ClerkId = request.ClerkId
                    }
            };

            terminal.Transaction.DoCredit(saleRequest, out DoCreditResponse? response);

            cancellationToken.ThrowIfCancellationRequested();

            if (response is null)
            {
                return Failure("NO_RESPONSE", "Terminal returned no response.");
            }

            var responseCode = response.ResponseCode ?? string.Empty;
            var success =
                string.Equals(responseCode, "000000", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(responseCode, "0", StringComparison.OrdinalIgnoreCase);

            return new TerminalTransactionResult
            {
                Success = success,
                ResponseCode = responseCode,
                ResponseMessage = response.ResponseMessage ?? string.Empty,
                ApprovalCode = response.HostInformation?.AuthorizationCode,
                ReferenceNumber = response.TraceInformation?.EcrReferenceNumber,
                CardType = response.AccountInformation?.CardType.ToString(),
                MaskedPan = response.AccountInformation?.CurrentAccountNumber,

                // Add these to your result type if available:
                // OriginalReferenceNumber = response.HostInformation?.ReferenceNumber,
                // HostReferenceNumber = response.HostInformation?.HostReferenceNumber,
                // TransactionNumber = response.TraceInformation?.ReferenceNumber,
                // GlobalUid = response.TraceInformation?.GlobalUid
            };
        }
        catch (OperationCanceledException)
        {
            return Failure("CANCELLED", "Sale operation was cancelled.");
        }
        catch (Exception ex)
        {
            return Failure("EXCEPTION", ex.Message);
        }
    }

    private static TerminalTransactionResult Failure(string code, string message) =>
        new()
        {
            Success = false,
            ResponseCode = code,
            ResponseMessage = message
        };
}
