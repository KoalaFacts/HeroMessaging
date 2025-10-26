using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Scheduling;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Scheduling;

/// <summary>
/// Default implementation of message delivery handler that publishes scheduled messages
/// through the HeroMessaging system.
/// </summary>
internal sealed class DefaultMessageDeliveryHandler : IMessageDeliveryHandler
{
    private readonly IHeroMessaging _messaging;
    private readonly ILogger<DefaultMessageDeliveryHandler> _logger;

    public DefaultMessageDeliveryHandler(
        IHeroMessaging messaging,
        ILogger<DefaultMessageDeliveryHandler> logger)
    {
        _messaging = messaging ?? throw new ArgumentNullException(nameof(messaging));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task DeliverAsync(ScheduledMessage scheduledMessage, CancellationToken cancellationToken = default)
    {
        if (scheduledMessage == null) throw new ArgumentNullException(nameof(scheduledMessage));

        _logger.LogDebug(
            "Delivering scheduled message {ScheduleId} (MessageId: {MessageId}, Type: {MessageType})",
            scheduledMessage.ScheduleId,
            scheduledMessage.Message.MessageId,
            scheduledMessage.Message.GetType().Name);

        try
        {
            // Determine delivery method based on message type
            var message = scheduledMessage.Message;
            var messageType = message.GetType();

            // Check if it's a command
            if (IsCommand(messageType))
            {
                await DeliverCommandAsync(message, cancellationToken);
            }
            // Check if it's a query
            else if (IsQuery(messageType))
            {
                _logger.LogWarning(
                    "Cannot deliver scheduled query {MessageId} - queries require synchronous responses",
                    message.MessageId);
            }
            // Default to publishing as event
            else
            {
                await DeliverEventAsync(message, scheduledMessage.Options.Destination, cancellationToken);
            }

            _logger.LogInformation(
                "Successfully delivered scheduled message {ScheduleId}",
                scheduledMessage.ScheduleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to deliver scheduled message {ScheduleId}",
                scheduledMessage.ScheduleId);
            throw;
        }
    }

    public Task HandleDeliveryFailureAsync(Guid scheduleId, Exception exception, CancellationToken cancellationToken = default)
    {
        _logger.LogError(
            exception,
            "Scheduled message {ScheduleId} delivery failed: {ErrorMessage}",
            scheduleId,
            exception.Message);

        // Future enhancement: Could implement retry logic, dead letter queue, etc.
        return Task.CompletedTask;
    }

    private async Task DeliverCommandAsync(Abstractions.Messages.IMessage message, CancellationToken cancellationToken)
    {
        var commandType = message.GetType();

        // Check if command has a response type
        var commandInterface = commandType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(Abstractions.Commands.ICommand<>));

        if (commandInterface != null)
        {
            _logger.LogWarning(
                "Cannot deliver scheduled command {MessageId} with response type - commands with responses require synchronous handling",
                message.MessageId);
            return;
        }

        // Send command without response
        if (message is Abstractions.Commands.ICommand command)
        {
            await _messaging.Send(command, cancellationToken);
        }
    }

    private async Task DeliverEventAsync(Abstractions.Messages.IMessage message, string? destination, CancellationToken cancellationToken)
    {
        // If destination is specified, enqueue to that queue
        if (!string.IsNullOrEmpty(destination))
        {
            await _messaging.Enqueue(message, destination, cancellationToken: cancellationToken);
        }
        // Otherwise, check if it's an event and publish
        else if (message is Abstractions.Events.IEvent @event)
        {
            await _messaging.Publish(@event, cancellationToken);
        }
        else
        {
            // Default: enqueue to a default scheduled messages queue
            await _messaging.Enqueue(message, "scheduled-messages", cancellationToken: cancellationToken);
        }
    }

    private static bool IsCommand(Type messageType)
    {
        return messageType.GetInterfaces().Any(i =>
            i == typeof(Abstractions.Commands.ICommand) ||
            (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(Abstractions.Commands.ICommand<>)));
    }

    private static bool IsQuery(Type messageType)
    {
        return messageType.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(Abstractions.Queries.IQuery<>));
    }
}
