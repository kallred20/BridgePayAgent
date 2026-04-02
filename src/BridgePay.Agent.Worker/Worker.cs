using System.Text.Json;
using BridgePay.Agent.Contracts;
using BridgePay.Agent.Api;
using BridgePay.Agent.Messaging;
using BridgePay.Agent.PosLink;
using BridgePay.Agent.Storage;
using BridgePay.Agent.Terminals;

namespace BridgePay.Agent.Worker;

public sealed class Worker : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<Worker> _logger;
    private readonly IPubSubConsumer _consumer;
    private readonly ITerminalRegistry _terminalRegistry;
    private readonly IPaxPosLinkClient _paxClient;
    private readonly IPaymentApiClient _paymentApiClient;
    private readonly IExecutionStore _executionStore;

    public Worker(
        ILogger<Worker> logger,
        IPubSubConsumer consumer,
        ITerminalRegistry terminalRegistry,
        IPaxPosLinkClient paxPosLinkClient,
        IPaymentApiClient paymentApiClient,
        IExecutionStore executionStore)
    {
        _logger = logger;
        _consumer = consumer;
        _terminalRegistry = terminalRegistry;
        _paxClient = paxPosLinkClient;
        _paymentApiClient = paymentApiClient;
        _executionStore = executionStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BridgePay Agent started.");
        await _consumer.RunAsync(HandleMessageAsync, stoppingToken);
    }

    private async Task<bool> HandleMessageAsync(string payload, CancellationToken cancellationToken)
    {
        PublisherPubSubMessage? message;

        try
        {
            message = JsonSerializer.Deserialize<PublisherPubSubMessage>(payload, SerializerOptions);
            _logger.LogInformation(payload);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid Pub/Sub payload. Message will be acknowledged to avoid a retry loop.");
            return true;
        }

        if (message is null)
        {
            _logger.LogError("Pub/Sub payload deserialized to null. Message will be acknowledged.");
            return true;
        }

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Operation"] = message.Operation,
            ["PaymentId"] = message.PaymentId,
            ["StoreId"] = message.StoreId,
            ["TerminalId"] = message.TerminalId,
            ["IdempotencyKey"] = message.IdempotencyKey
        });

        if (!string.Equals(message.Operation, "PAY", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "Unsupported operation {Operation} for payment {PaymentId}.",
                message.Operation,
                message.PaymentId);
            await RecordFailureAsync(message, "unsupported_operation", cancellationToken);
            return true;
        }

        if (!TryBuildSaleRequest(message, out var request, out var validationError))
        {
            _logger.LogError(
                "Invalid sale payload for payment {PaymentId}: {ValidationError}",
                message.PaymentId,
                validationError);
            await RecordFailureAsync(message, validationError, cancellationToken);
            return true;
        }

        _logger.LogInformation(
            "Processing {Operation} for payment {PaymentId} store {StoreId} terminal {TerminalId} amount {Amount}",
            message.Operation,
            request.PaymentId,
            message.StoreId,
            request.TerminalId,
            request.Amount);

        var terminal = _terminalRegistry.GetByTerminalId(request.TerminalId);
        if (terminal is null)
        {
            _logger.LogError("Terminal {TerminalId} was not found in configuration.", request.TerminalId);
            await RecordFailureAsync(message, "terminal_not_found", cancellationToken);
            return true;
        }

        var result = await _paxClient.SaleAsync(request, terminal, cancellationToken);

        _logger.LogInformation(
            "Terminal response for payment {PaymentId}: success={Success}, code={ResponseCode}, message={ResponseMessage}",
            request.PaymentId,
            result.Success,
            result.ResponseCode,
            result.ResponseMessage);

        var serializedResult = JsonSerializer.Serialize(result);
        await _executionStore.SaveAsync(request.PaymentId, serializedResult, cancellationToken);
        await PostPaymentEventAsync(
            message.PaymentId,
            result.Success,
            result.Success ? null : result.ResponseCode,
            result.Success ? null : result.ResponseMessage,
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
            CardType = string.Empty,
            MaskedPan = string.Empty
        };

        await _executionStore.SaveAsync(
            message.PaymentId,
            JsonSerializer.Serialize(failureResult),
            cancellationToken);

        await PostPaymentEventAsync(message.PaymentId, false, status, status, cancellationToken);
    }

    private Task PostPaymentEventAsync(
        string paymentId,
        bool succeeded,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        return _paymentApiClient.PostPaymentEventAsync(
            paymentId,
            succeeded ? "APPROVED" : "FAILED",
            succeeded ? "PAYMENT_COMPLETED" : "PAYMENT_FAILED",
            DateTimeOffset.UtcNow,
            errorCode,
            errorMessage,
            cancellationToken);
    }

    private static bool TryBuildSaleRequest(
        PublisherPubSubMessage message,
        out TerminalSaleRequest request,
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

        if (!TryGetInt64(message.ExtraFields, out var amount, "amount", "amount_cents"))
        {
            validationError = "amount_missing";
            return false;
        }

        request = new TerminalSaleRequest
        {
            PaymentId = message.PaymentId,
            TerminalId = message.TerminalId,
            Amount = amount,
            InvoiceNumber = TryGetString(message.ExtraFields, "invoice_id", "invoice_number", "invoiceNumber"),
            EcrReferenceNumber = TryGetString(message.ExtraFields, "ecr_reference_number", "ecrReferenceNumber"),
            ClerkId = TryGetString(message.ExtraFields, "clerk_id", "clerkId")
        };

        return true;
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

        value = 0;
        return false;
    }
}
