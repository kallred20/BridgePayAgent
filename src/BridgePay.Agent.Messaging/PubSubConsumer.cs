using Google.Api.Gax;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BridgePay.Agent.Messaging;

public sealed class PubSubConsumer : IPubSubConsumer
{
    private readonly ILogger<PubSubConsumer> _logger;
    private readonly string _projectId;
    private readonly string _subscriptionId;
    private readonly int _maxOutstandingMessages;

    public PubSubConsumer(
        ILogger<PubSubConsumer> logger,
        IConfiguration configuration)
    {
        _logger = logger;

        _projectId = configuration["PubSub:ProjectId"]
            ?? throw new InvalidOperationException("PubSub:ProjectId is missing.");

        _subscriptionId = configuration["PubSub:SubscriptionId"]
            ?? throw new InvalidOperationException("PubSub:SubscriptionId is missing.");

        _maxOutstandingMessages =
            int.TryParse(configuration["PubSub:MaxOutstandingMessages"], out var value)
                ? value
                : 1;
    }

    public async Task RunAsync(
        Func<string, CancellationToken, Task<bool>> handler,
        CancellationToken cancellationToken)
    {
        var subscriptionName = SubscriptionName.FromProjectSubscription(_projectId, _subscriptionId);

        var settings = new SubscriberClient.Settings
        {
            AckExtensionWindow = TimeSpan.FromSeconds(30),
            AckDeadline = TimeSpan.FromMinutes(2),
            FlowControlSettings = new FlowControlSettings(
                maxOutstandingElementCount: _maxOutstandingMessages,
                maxOutstandingByteCount: 10 * 1024 * 1024)
        };

        var subscriber = await SubscriberClient.CreateAsync(subscriptionName, null, settings);

        _logger.LogInformation(
            "Starting Pub/Sub subscriber for {ProjectId}/{SubscriptionId}",
            _projectId,
            _subscriptionId);

        var startTask = subscriber.StartAsync(async (message, ct) =>
        {
            var payload = message.Data.ToStringUtf8();

            try
            {
                _logger.LogInformation("Received Pub/Sub message {MessageId}", message.MessageId);

                var success = await handler(payload, ct);

                if (success)
                {
                    _logger.LogInformation("Acking Pub/Sub message {MessageId}", message.MessageId);
                    return SubscriberClient.Reply.Ack;
                }

                _logger.LogWarning("Nacking Pub/Sub message {MessageId}", message.MessageId);
                return SubscriberClient.Reply.Nack;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Pub/Sub message {MessageId}", message.MessageId);
                return SubscriberClient.Reply.Nack;
            }
        });

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }

        _logger.LogInformation("Stopping Pub/Sub subscriber...");
        await subscriber.StopAsync(CancellationToken.None);
        await startTask;
    }
}
