using BridgePay.Agent.Contracts;
using BridgePay.Agent.Terminals;
using Microsoft.Extensions.Logging;
using POSLinkAdmin.Const;
using POSLinkAdmin.Util;
using POSLinkCore.CommunicationSetting;
using POSLinkSemiIntegration;
using POSLinkSemiIntegration.Transaction;
using POSLinkSemiIntegration.Util;

namespace BridgePay.Agent.PosLink;

public sealed class PaxPosLinkClient : IPaxPosLinkClient
{
    private const int TimeoutMs = 60000;
    private const int MaxEcrReferenceLength = 32;
    private const int MaxClerkIdLength = 8;
    private readonly ILogger<PaxPosLinkClient> _logger;

    public PaxPosLinkClient(ILogger<PaxPosLinkClient> logger)
    {
        _logger = logger;
    }

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
        if (request is null)
        {
            return Failure("INVALID_REQUEST", "Request is required.");
        }

        if (endpoint is null)
        {
            return Failure("INVALID_ENDPOINT", "Terminal endpoint is required.");
        }

        if (string.IsNullOrWhiteSpace(request.TerminalId))
        {
            return Failure("INVALID_REQUEST", "TerminalId is required.");
        }

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

        if (!string.Equals(request.TerminalId, endpoint.TerminalId, StringComparison.OrdinalIgnoreCase))
        {
            return Failure("TERMINAL_MISMATCH", "Request TerminalId does not match endpoint TerminalId.");
        }

        var ecrReferenceNumber = BuildEcrReferenceNumber(request);
        if (ecrReferenceNumber.Length > MaxEcrReferenceLength)
        {
            return Failure(
                "INVALID_ECR_REF",
                $"EcrReferenceNumber exceeds {MaxEcrReferenceLength} characters.");
        }
        _logger.LogInformation(ecrReferenceNumber);

        var invoiceNumber = request.InvoiceNumber?.Trim() ?? string.Empty;

        var clerkId = request.ClerkId?.Trim();
        if (!string.IsNullOrWhiteSpace(clerkId) && clerkId.Length > MaxClerkIdLength)
        {
            return Failure(
                "INVALID_CLERK_ID",
                $"ClerkId exceeds {MaxClerkIdLength} characters.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Starting PAX sale for payment {PaymentId} terminal {TerminalId} amount {Amount} to {IpAddress}:{Port}",
                request.PaymentId,
                request.TerminalId,
                request.Amount,
                endpoint.IpAddress,
                endpoint.Port);

            var tcp = new TcpSetting
            {
                Ip = endpoint.IpAddress,
                Port = endpoint.Port,
                Timeout = TimeoutMs
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
                    EcrReferenceNumber = "PAY110023226",
                    InvoiceNumber = "inv000123"
                }
            };

            // if (!string.IsNullOrWhiteSpace(clerkId))
            // {
            //     saleRequest.CashierInformation = new CashierRequest
            //     {
            //         ClerkId = clerkId
            //     };
            // }

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

            _logger.LogInformation(
                "PAX sale finished for payment {PaymentId} terminal {TerminalId}: success={Success}, code={ResponseCode}, message={ResponseMessage}",
                request.PaymentId,
                request.TerminalId,
                success,
                responseCode,
                response.ResponseMessage ?? string.Empty);

            return new TerminalTransactionResult
            {
                Success = success,
                ResponseCode = responseCode,
                ResponseMessage = response.ResponseMessage ?? string.Empty,
                ApprovalCode = response.HostInformation?.AuthorizationCode ?? string.Empty,
                ReferenceNumber = response.TraceInformation?.EcrReferenceNumber ?? string.Empty,
                CardType = response.AccountInformation?.CardType.ToString() ?? string.Empty,
                MaskedPan = response.AccountInformation?.CurrentAccountNumber ?? string.Empty
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "PAX sale cancelled for payment {PaymentId} terminal {TerminalId}",
                request.PaymentId,
                request.TerminalId);
            return Failure("CANCELLED", "Sale operation was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "PAX sale threw an exception for payment {PaymentId} terminal {TerminalId}",
                request.PaymentId,
                request.TerminalId);
            return Failure("EXCEPTION", ex.Message);
        }
    }

    private static string BuildEcrReferenceNumber(TerminalSaleRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.EcrReferenceNumber))
        {
            return request.EcrReferenceNumber.Trim();
        }

        // Use a 32-char terminal-safe fallback.
        return Guid.NewGuid().ToString("N");
    }

    private static TerminalTransactionResult Failure(string code, string message) =>
        new()
        {
            Success = false,
            ResponseCode = code,
            ResponseMessage = message,
            ApprovalCode = string.Empty,
            ReferenceNumber = string.Empty,
            CardType = string.Empty,
            MaskedPan = string.Empty
        };
}
