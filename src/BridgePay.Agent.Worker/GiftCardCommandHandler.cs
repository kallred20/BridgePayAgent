using System.Text.Json;
using BridgePay.Agent.Api;
using BridgePay.Agent.Contracts;
using BridgePay.Agent.PosLink;
using BridgePay.Agent.Storage;
using BridgePay.Agent.Terminals;

namespace BridgePay.Agent.Worker;

public sealed class GiftCardCommandHandler : IGiftCardCommandHandler
{
    private const int MaxEcrReferenceLength = 32;
    private readonly ILogger<GiftCardCommandHandler> _logger;
    private readonly ITerminalRegistry _terminalRegistry;
    private readonly IPaxPosLinkClient _paxClient;
    private readonly IPaymentApiClient _paymentApiClient;
    private readonly IExecutionStore _executionStore;

    public GiftCardCommandHandler(
        ILogger<GiftCardCommandHandler> logger,
        ITerminalRegistry terminalRegistry,
        IPaxPosLinkClient paxPosLinkClient,
        IPaymentApiClient paymentApiClient,
        IExecutionStore executionStore)
    {
        _logger = logger;
        _terminalRegistry = terminalRegistry;
        _paxClient = paxPosLinkClient;
        _paymentApiClient = paymentApiClient;
        _executionStore = executionStore;
    }

    public async Task<bool> HandleAsync(
        PublisherPubSubMessage message,
        CancellationToken cancellationToken)
    {
        if (!TryBuildGiftRequest(message, out var request, out var validationError))
        {
            _logger.LogError(
                "Invalid gift payload for payment {PaymentId}: {ValidationError}",
                message.PaymentId,
                validationError);
            await RecordFailureAsync(message, validationError, cancellationToken);
            return true;
        }

        _logger.LogInformation(
            "Processing {Operation} {GiftType} for payment {PaymentId} store {StoreId} terminal {TerminalId}{AmountSuffix}",
            message.Operation,
            request.Type,
            request.PaymentId,
            message.StoreId,
            request.TerminalId,
            request.Amount.HasValue ? $" amount {request.Amount.Value}" : string.Empty);

        var terminal = _terminalRegistry.GetByTerminalId(request.TerminalId);
        if (terminal is null)
        {
            _logger.LogError(
                "Terminal {TerminalId} was not found in configuration.",
                request.TerminalId);
            await RecordFailureAsync(message, "terminal_not_found", cancellationToken);
            return true;
        }

        var result = await _paxClient.GiftAsync(request, terminal, cancellationToken);

        _logger.LogInformation(
            "Gift response for payment {PaymentId}: success={Success}, code={ResponseCode}, message={ResponseMessage}",
            request.PaymentId,
            result.Success,
            result.ResponseCode,
            result.ResponseMessage);

        await _executionStore.SaveAsync(
            request.PaymentId,
            JsonSerializer.Serialize(result),
            cancellationToken);

        // Gift responses flow back through the same callback contract the worker already uses for payments.
        await PostPaymentEventAsync(
            request.PaymentId,
            result.Success,
            result.Success ? null : result.ResponseCode,
            result.Success ? null : result.ResponseMessage,
            result.Success ? result.EcrReferenceNumber : null,
            result.Success ? result.HostReferenceNumber : null,
            result.Success ? result.TerminalReferenceNumber ?? result.ReferenceNumber : null,
            cancellationToken);

        return true;
    }

    private async Task RecordFailureAsync(
        PublisherPubSubMessage message,
        string status,
        CancellationToken cancellationToken)
    {
        var failureResult = new TerminalTransactionResult
        {
            Success = false,
            ResponseCode = status,
            ResponseMessage = status,
            ApprovalCode = string.Empty,
            ReferenceNumber = string.Empty,
            TerminalReferenceNumber = string.Empty,
            EcrReferenceNumber = string.Empty,
            HostReferenceNumber = string.Empty,
            CardType = string.Empty,
            MaskedPan = string.Empty
        };

        await _executionStore.SaveAsync(
            message.PaymentId,
            JsonSerializer.Serialize(failureResult),
            cancellationToken);

        await PostPaymentEventAsync(
            message.PaymentId,
            false,
            status,
            status,
            null,
            null,
            null,
            cancellationToken);
    }

    private Task PostPaymentEventAsync(
        string paymentId,
        bool succeeded,
        string? errorCode,
        string? errorMessage,
        string? ecrReferenceNumber,
        string? hostReferenceNumber,
        string? terminalReferenceNumber,
        CancellationToken cancellationToken)
    {
        return _paymentApiClient.PostPaymentEventAsync(
            paymentId,
            succeeded ? "APPROVED" : "FAILED",
            succeeded ? "PAYMENT_COMPLETED" : "PAYMENT_FAILED",
            DateTimeOffset.UtcNow,
            errorCode,
            errorMessage,
            ecrReferenceNumber,
            hostReferenceNumber,
            terminalReferenceNumber,
            cancellationToken);
    }

    private static bool TryBuildGiftRequest(
        PublisherPubSubMessage message,
        out TerminalGiftRequest request,
        out string validationError)
    {
        request = default!;
        validationError = string.Empty;

        if (string.IsNullOrWhiteSpace(message.PaymentId))
        {
            validationError = "payment_id_missing";
            return false;
        }

        if (string.IsNullOrWhiteSpace(message.StoreId))
        {
            validationError = "store_id_missing";
            return false;
        }

        if (string.IsNullOrWhiteSpace(message.TerminalId))
        {
            validationError = "terminal_id_missing";
            return false;
        }

        var tenderType = TryGetString(message.ExtraFields, "tender_type", "tenderType");
        if (string.IsNullOrWhiteSpace(tenderType))
        {
            validationError = "tender_type_missing";
            return false;
        }

        if (!string.Equals(tenderType, "gift", StringComparison.OrdinalIgnoreCase))
        {
            validationError = "tender_type_invalid";
            return false;
        }

        var giftTypeValue = TryGetString(message.ExtraFields, "type");
        if (string.IsNullOrWhiteSpace(giftTypeValue))
        {
            validationError = "gift_type_missing";
            return false;
        }

        if (!TryParseGiftType(giftTypeValue, out var giftType))
        {
            validationError = "gift_type_invalid";
            return false;
        }

        var amountProvided = TryGetInt64(message.ExtraFields, out var amount, "amount", "amount_cents");
        if (RequiresAmount(giftType) && !amountProvided)
        {
            validationError = "amount_missing";
            return false;
        }

        if (amountProvided && amount <= 0)
        {
            validationError = "amount_invalid";
            return false;
        }

        var ecrReferenceNumber = TryGetString(message.ExtraFields, "ecr_reference_number", "ecrReferenceNumber");
        if (string.IsNullOrWhiteSpace(ecrReferenceNumber))
        {
            validationError = "ecr_reference_number_missing";
            return false;
        }

        if (ecrReferenceNumber.Length > MaxEcrReferenceLength)
        {
            validationError = "ecr_reference_number_invalid_length";
            return false;
        }

        request = new TerminalGiftRequest
        {
            PaymentId = message.PaymentId,
            TerminalId = message.TerminalId,
            Type = giftType,
            Amount = amountProvided ? amount : null,
            InvoiceNumber = TryGetString(message.ExtraFields, "invoice_id", "invoice_number", "invoiceNumber"),
            EcrReferenceNumber = ecrReferenceNumber,
            ClerkId = TryGetString(message.ExtraFields, "clerk_id", "clerkId")
        };

        return true;
    }

    private static bool RequiresAmount(TerminalGiftTransactionType type)
    {
        return type is TerminalGiftTransactionType.Redeem or TerminalGiftTransactionType.Activate;
    }

    private static bool TryParseGiftType(string value, out TerminalGiftTransactionType type)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "inquiry":
                type = TerminalGiftTransactionType.Inquiry;
                return true;
            case "redeem":
                type = TerminalGiftTransactionType.Redeem;
                return true;
            case "activate":
                type = TerminalGiftTransactionType.Activate;
                return true;
            default:
                type = default;
                return false;
        }
    }

    private static string? TryGetString(
        IDictionary<string, JsonElement> extraFields,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!extraFields.TryGetValue(key, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var result = value.GetString();
                if (!string.IsNullOrWhiteSpace(result))
                {
                    return result;
                }
            }

            if (value.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.Undefined)
            {
                var raw = value.ToString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    return raw;
                }
            }
        }

        return null;
    }

    private static bool TryGetInt64(
        IDictionary<string, JsonElement> extraFields,
        out long value,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!extraFields.TryGetValue(key, out var element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value))
            {
                return true;
            }

            if (element.ValueKind == JsonValueKind.String &&
                long.TryParse(element.GetString(), out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }
}
