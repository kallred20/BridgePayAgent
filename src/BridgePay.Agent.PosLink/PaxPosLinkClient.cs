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

    public Task<TerminalTransactionResult> GiftAsync(
        TerminalGiftRequest request,
        TerminalEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = ExecuteGift(request, endpoint, cancellationToken);
        return Task.FromResult(result);
    }

    public Task<TerminalTransactionResult> VoidAsync(
        TerminalVoidRequest request,
        TerminalEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = ExecuteVoid(request, endpoint, cancellationToken);
        return Task.FromResult(result);
    }

    public Task<TerminalTransactionResult> ReturnAsync(
        TerminalReturnRequest request,
        TerminalEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = ExecuteReturn(request, endpoint, cancellationToken);
        return Task.FromResult(result);
    }

    private TerminalTransactionResult ExecuteGift(
        TerminalGiftRequest request,
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

        if (string.IsNullOrWhiteSpace(request.EcrReferenceNumber))
        {
            return Failure("INVALID_ECR_REF", "EcrReferenceNumber is required.");
        }

        if (request.EcrReferenceNumber.Length > MaxEcrReferenceLength)
        {
            return Failure(
                "INVALID_ECR_REF",
                $"EcrReferenceNumber exceeds {MaxEcrReferenceLength} characters.");
        }

        if (RequiresAmount(request.Type) && (!request.Amount.HasValue || request.Amount.Value <= 0))
        {
            return Failure("INVALID_AMOUNT", "Amount must be greater than zero.");
        }

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
                "Starting PAX gift {GiftType} for payment {PaymentId} terminal {TerminalId}{AmountSuffix} to {IpAddress}:{Port}",
                request.Type,
                request.PaymentId,
                request.TerminalId,
                request.Amount.HasValue ? $" amount {request.Amount.Value}" : string.Empty,
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

            var giftRequest = new DoGiftRequest
            {
                TransactionType = MapGiftTransactionType(request.Type),
                TraceInformation = new TraceRequest
                {
                    EcrReferenceNumber = request.EcrReferenceNumber,
                    InvoiceNumber = invoiceNumber
                },
                // Gift inquiry should omit amount entirely, while activate/redeem send the amount in cents.
                AmountInformation = request.Amount.HasValue && RequiresAmount(request.Type)
                    ? new AmountRequest
                    {
                        TransactionAmount = request.Amount.Value.ToString()
                    }
                    : null,
                CashierInformation = string.IsNullOrWhiteSpace(clerkId)
                    ? null
                    : new CashierRequest
                    {
                        ClerkId = clerkId
                    }
            };

            terminal.Transaction.DoGift(giftRequest, out DoGiftResponse? response);

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
                "PAX gift {GiftType} finished for payment {PaymentId} terminal {TerminalId}: success={Success}, code={ResponseCode}, message={ResponseMessage}",
                request.Type,
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
                ReferenceNumber = response.TraceInformation?.ReferenceNumber ?? response.TraceInformation?.EcrReferenceNumber ?? string.Empty,
                TerminalReferenceNumber = response.TraceInformation?.ReferenceNumber ?? string.Empty,
                EcrReferenceNumber = response.TraceInformation?.EcrReferenceNumber ?? string.Empty,
                HostReferenceNumber = response.HostInformation?.HostReferenceNumber ?? string.Empty,
                CardType = response.AccountInformation?.CardType.ToString() ?? string.Empty,
                MaskedPan = response.AccountInformation?.CurrentAccountNumber ?? string.Empty
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "PAX gift {GiftType} cancelled for payment {PaymentId} terminal {TerminalId}",
                request.Type,
                request.PaymentId,
                request.TerminalId);
            return Failure("CANCELLED", "Gift operation was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "PAX gift {GiftType} threw an exception for payment {PaymentId} terminal {TerminalId}",
                request.Type,
                request.PaymentId,
                request.TerminalId);
            return Failure("EXCEPTION", ex.Message);
        }
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

        var ecrReferenceNumber = request.EcrReferenceNumber?.Trim();
        if (string.IsNullOrWhiteSpace(ecrReferenceNumber))
        {
            return Failure("INVALID_ECR_REF", "EcrReferenceNumber is required.");
        }

        if (ecrReferenceNumber.Length != MaxEcrReferenceLength)
        {
            return Failure(
                "INVALID_ECR_REF",
                $"EcrReferenceNumber must be exactly {MaxEcrReferenceLength} characters.");
        }

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
                    EcrReferenceNumber = ecrReferenceNumber,
                    InvoiceNumber = invoiceNumber
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
                ReferenceNumber = response.TraceInformation?.ReferenceNumber ?? string.Empty,
                TerminalReferenceNumber = response.TraceInformation?.ReferenceNumber ?? string.Empty,
                EcrReferenceNumber = response.TraceInformation?.EcrReferenceNumber ?? string.Empty,
                HostReferenceNumber = response.HostInformation?.HostReferenceNumber ?? string.Empty,
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

    private TerminalTransactionResult ExecuteVoid(
        TerminalVoidRequest request,
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

        var ecrReferenceNumber = BuildEcrReferenceNumber(request.EcrReferenceNumber);
        if (ecrReferenceNumber.Length > MaxEcrReferenceLength)
        {
            return Failure(
                "INVALID_ECR_REF",
                $"EcrReferenceNumber exceeds {MaxEcrReferenceLength} characters.");
        }

        var originalReferenceNumber = request.OriginalReferenceNumber?.Trim();
        var originalEcrReferenceNumber = request.OriginalEcrReferenceNumber?.Trim();
        var hostReferenceNumber = request.HostReferenceNumber?.Trim();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Starting PAX void for payment {PaymentId} terminal {TerminalId} to {IpAddress}:{Port}",
                request.PaymentId,
                request.TerminalId,
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

            var voidRequest = new DoCreditRequest
            {
                TransactionType = TransactionType.VoidSale,
                TraceInformation = new TraceRequest
                {
                    EcrReferenceNumber = ecrReferenceNumber,
                    OriginalReferenceNumber = originalReferenceNumber,
                    OriginalEcrReferenceNumber = originalEcrReferenceNumber
                },
                HostTraceInformation = string.IsNullOrWhiteSpace(hostReferenceNumber)
                    ? null
                    : new HostTraceRequest
                    {
                        HostReferenceNumber = hostReferenceNumber
                    }
            };

            terminal.Transaction.DoCredit(voidRequest, out DoCreditResponse? response);

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
                "PAX void finished for payment {PaymentId} terminal {TerminalId}: success={Success}, code={ResponseCode}, message={ResponseMessage}",
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
                ReferenceNumber = response.TraceInformation?.ReferenceNumber ?? response.TraceInformation?.EcrReferenceNumber ?? string.Empty,
                TerminalReferenceNumber = response.TraceInformation?.ReferenceNumber ?? string.Empty,
                EcrReferenceNumber = response.TraceInformation?.EcrReferenceNumber ?? string.Empty,
                HostReferenceNumber = response.HostInformation?.HostReferenceNumber ?? string.Empty,
                CardType = response.AccountInformation?.CardType.ToString() ?? string.Empty,
                MaskedPan = response.AccountInformation?.CurrentAccountNumber ?? string.Empty
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "PAX void cancelled for payment {PaymentId} terminal {TerminalId}",
                request.PaymentId,
                request.TerminalId);
            return Failure("CANCELLED", "Void operation was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "PAX void threw an exception for payment {PaymentId} terminal {TerminalId}",
                request.PaymentId,
                request.TerminalId);
            return Failure("EXCEPTION", ex.Message);
        }
    }

    private TerminalTransactionResult ExecuteReturn(
        TerminalReturnRequest request,
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

        var ecrReferenceNumber = BuildEcrReferenceNumber(request.EcrReferenceNumber);
        if (ecrReferenceNumber.Length > MaxEcrReferenceLength)
        {
            return Failure(
                "INVALID_ECR_REF",
                $"EcrReferenceNumber exceeds {MaxEcrReferenceLength} characters.");
        }

        var originalReferenceNumber = request.OriginalReferenceNumber?.Trim();
        var originalEcrReferenceNumber = request.OriginalEcrReferenceNumber?.Trim();
        var hostReferenceNumber = request.HostReferenceNumber?.Trim();

        if (string.IsNullOrWhiteSpace(originalReferenceNumber) &&
            string.IsNullOrWhiteSpace(originalEcrReferenceNumber) &&
            string.IsNullOrWhiteSpace(hostReferenceNumber))
        {
            return Failure(
                "INVALID_REQUEST",
                "A return requires at least one original reference value.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Starting PAX return for payment {PaymentId} terminal {TerminalId} to {IpAddress}:{Port}",
                request.PaymentId,
                request.TerminalId,
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

            var returnRequest = new DoCreditRequest
            {
                TransactionType = TransactionType.Return,
                TraceInformation = new TraceRequest
                {
                    EcrReferenceNumber = ecrReferenceNumber,
                    OriginalReferenceNumber = originalReferenceNumber,
                    OriginalEcrReferenceNumber = originalEcrReferenceNumber
                },
                HostTraceInformation = string.IsNullOrWhiteSpace(hostReferenceNumber)
                    ? null
                    : new HostTraceRequest
                    {
                        HostReferenceNumber = hostReferenceNumber
                    }
            };

            terminal.Transaction.DoCredit(returnRequest, out DoCreditResponse? response);

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
                "PAX return finished for payment {PaymentId} terminal {TerminalId}: success={Success}, code={ResponseCode}, message={ResponseMessage}",
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
                ReferenceNumber = response.TraceInformation?.ReferenceNumber ?? response.TraceInformation?.EcrReferenceNumber ?? string.Empty,
                TerminalReferenceNumber = response.TraceInformation?.ReferenceNumber ?? string.Empty,
                EcrReferenceNumber = response.TraceInformation?.EcrReferenceNumber ?? string.Empty,
                HostReferenceNumber = response.HostInformation?.HostReferenceNumber ?? string.Empty,
                CardType = response.AccountInformation?.CardType.ToString() ?? string.Empty,
                MaskedPan = response.AccountInformation?.CurrentAccountNumber ?? string.Empty
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "PAX return cancelled for payment {PaymentId} terminal {TerminalId}",
                request.PaymentId,
                request.TerminalId);
            return Failure("CANCELLED", "Return operation was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "PAX return threw an exception for payment {PaymentId} terminal {TerminalId}",
                request.PaymentId,
                request.TerminalId);
            return Failure("EXCEPTION", ex.Message);
        }
    }

    private static string BuildEcrReferenceNumber(string? ecrReferenceNumber)
    {
        if (!string.IsNullOrWhiteSpace(ecrReferenceNumber))
        {
            return ecrReferenceNumber.Trim();
        }

        // Use a 32-char terminal-safe fallback.
        return Guid.NewGuid().ToString("N");
    }

    private static bool RequiresAmount(TerminalGiftTransactionType type)
    {
        return type is TerminalGiftTransactionType.Redeem or TerminalGiftTransactionType.Activate;
    }

    private static TransactionType MapGiftTransactionType(TerminalGiftTransactionType type)
    {
        // "Redeem" is the business label, but POSLink models gift redemption as Sale.
        return type switch
        {
            TerminalGiftTransactionType.Inquiry => TransactionType.Inquiry,
            TerminalGiftTransactionType.Redeem => TransactionType.Sale,
            TerminalGiftTransactionType.Activate => TransactionType.Activate,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported gift transaction type.")
        };
    }

    private static TerminalTransactionResult Failure(string code, string message) =>
        new()
        {
            Success = false,
            ResponseCode = code,
            ResponseMessage = message,
            ApprovalCode = string.Empty,
            ReferenceNumber = string.Empty,
            TerminalReferenceNumber = string.Empty,
            EcrReferenceNumber = string.Empty,
            HostReferenceNumber = string.Empty,
            CardType = string.Empty,
            MaskedPan = string.Empty
        };
}
