using System.Collections.Immutable;

namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Provides context and control for message processing in the transport layer.
/// Optimized as a readonly record struct for zero allocations in message processing loops.
/// </summary>
/// <remarks>
/// MessageContext is provided to message handlers during consumption and contains:
/// - Transport metadata (which transport received the message, from where)
/// - Message acknowledgment callbacks (ack, nack, defer, dead letter)
/// - Transport-specific properties for advanced scenarios
/// - Receive timestamp for latency tracking
///
/// The context enables message handlers to control message lifecycle:
/// - <see cref="AcknowledgeAsync"/>: Mark message as successfully processed
/// - <see cref="RejectAsync"/>: Reject message and optionally requeue
/// - <see cref="DeferAsync"/>: Defer processing to a later time
/// - <see cref="DeadLetterAsync"/>: Move message to dead letter queue
///
/// Acknowledgment is critical for at-least-once delivery guarantees.
/// Messages that are not acknowledged may be redelivered.
///
/// Example usage:
/// <code>
/// var consumer = await transport.SubscribeAsync(
///     TransportAddress.Queue("orders"),
///     async (envelope, context, ct) =>
///     {
///         try
///         {
///             // Process the message
///             await ProcessOrderAsync(envelope, ct);
///
///             // Acknowledge successful processing
///             await context.AcknowledgeAsync(ct);
///         }
///         catch (ValidationException ex)
///         {
///             // Invalid message - move to dead letter
///             await context.DeadLetterAsync(ex.Message, ct);
///         }
///         catch (TemporaryException ex)
///         {
///             // Temporary failure - defer for retry
///             await context.DeferAsync(TimeSpan.FromSeconds(30), ct);
///         }
///         catch (Exception ex)
///         {
///             // Unknown error - reject and requeue
///             logger.LogError(ex, "Failed to process order");
///             await context.RejectAsync(requeue: true, ct);
///         }
///     });
/// </code>
///
/// Transport-specific properties:
/// <code>
/// // Access native message properties
/// var deliveryTag = context.GetProperty&lt;ulong&gt;("DeliveryTag"); // RabbitMQ
/// var lockToken = context.GetProperty&lt;string&gt;("LockToken");    // Azure Service Bus
/// var partition = context.GetProperty&lt;int&gt;("Partition");        // Kafka
/// </code>
/// </remarks>
public readonly record struct MessageContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MessageContext"/> struct with default values.
    /// </summary>
    /// <remarks>
    /// Creates a context with:
    /// - Empty transport name
    /// - Default source address
    /// - Current UTC timestamp
    /// - No properties
    /// - No acknowledgment callbacks
    ///
    /// Typically used for testing scenarios.
    /// </remarks>
    public MessageContext()
    {
        TransportName = string.Empty;
        SourceAddress = default;
        ReceiveTimestamp = TimeProvider.System.GetUtcNow().DateTime;
        Properties = ImmutableDictionary<string, object>.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageContext"/> struct with transport information.
    /// </summary>
    /// <param name="transportName">The name of the transport that received the message</param>
    /// <param name="sourceAddress">The address from which the message was received</param>
    /// <param name="timeProvider">Optional time provider for timestamp (defaults to system time)</param>
    /// <remarks>
    /// Creates a message context for a received message.
    ///
    /// The transport implementation creates this context when delivering messages to handlers.
    /// Applications typically receive this as a parameter and don't create it directly.
    ///
    /// Acknowledgment callbacks are typically set by the transport implementation
    /// after construction using the init-only properties.
    ///
    /// Example (transport implementation):
    /// <code>
    /// var context = new MessageContext(
    ///     transportName: "RabbitMQ",
    ///     sourceAddress: TransportAddress.Queue("orders"))
    /// {
    ///     Acknowledge = async ct => await channel.BasicAckAsync(deliveryTag, ct),
    ///     Reject = async (requeue, ct) => await channel.BasicNackAsync(deliveryTag, requeue, ct)
    /// };
    ///
    /// await messageHandler(envelope, context, cancellationToken);
    /// </code>
    /// </remarks>
    public MessageContext(string transportName, TransportAddress sourceAddress, TimeProvider? timeProvider = null)
    {
        TransportName = transportName;
        SourceAddress = sourceAddress;
        ReceiveTimestamp = (timeProvider ?? TimeProvider.System).GetUtcNow().DateTime;
        Properties = ImmutableDictionary<string, object>.Empty;
    }

    /// <summary>
    /// Name of the transport that received the message
    /// </summary>
    public string TransportName { get; init; }

    /// <summary>
    /// Source address where the message was received from
    /// </summary>
    public TransportAddress SourceAddress { get; init; }

    /// <summary>
    /// When the message was received by the transport
    /// </summary>
    public DateTime ReceiveTimestamp { get; init; }

    /// <summary>
    /// Transport-specific properties
    /// </summary>
    public ImmutableDictionary<string, object> Properties { get; init; }

    /// <summary>
    /// Acknowledgment callback
    /// </summary>
    public Func<CancellationToken, Task>? Acknowledge { get; init; }

    /// <summary>
    /// Negative acknowledgment callback (reject/nack)
    /// </summary>
    public Func<bool, CancellationToken, Task>? Reject { get; init; }

    /// <summary>
    /// Defer/defer message processing callback
    /// </summary>
    public Func<TimeSpan?, CancellationToken, Task>? Defer { get; init; }

    /// <summary>
    /// Dead letter callback
    /// </summary>
    public Func<string?, CancellationToken, Task>? DeadLetter { get; init; }

    /// <summary>
    /// Add or update a property
    /// </summary>
    public MessageContext WithProperty(string key, object value)
    {
        return this with { Properties = Properties.SetItem(key, value) };
    }

    /// <summary>
    /// Get a property value
    /// </summary>
    public T? GetProperty<T>(string key)
    {
        return Properties.TryGetValue(key, out var value) && value is T typedValue
            ? typedValue
            : default;
    }

    /// <summary>
    /// Acknowledges successful processing of the message.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort the acknowledgment</param>
    /// <returns>A task representing the asynchronous acknowledgment operation</returns>
    /// <remarks>
    /// Calling this method:
    /// - Removes the message from the queue/topic
    /// - Confirms successful processing to the broker
    /// - Prevents redelivery of the message
    /// - Advances consumer offset (Kafka)
    ///
    /// For at-least-once delivery, always acknowledge after successful processing.
    ///
    /// If auto-acknowledgment is enabled (<see cref="ConsumerOptions.AutoAcknowledge"/>),
    /// the transport automatically acknowledges after handler returns successfully.
    /// Manual acknowledgment gives finer control over when messages are acknowledged.
    ///
    /// Example:
    /// <code>
    /// async (envelope, context, ct) =>
    /// {
    ///     await ProcessMessageAsync(envelope, ct);
    ///
    ///     // Explicitly acknowledge after processing
    ///     await context.AcknowledgeAsync(ct);
    /// }
    /// </code>
    /// </remarks>
    public Task AcknowledgeAsync(CancellationToken cancellationToken = default)
    {
        return Acknowledge?.Invoke(cancellationToken) ?? Task.CompletedTask;
    }

    /// <summary>
    /// Rejects the message and optionally requeues it for retry.
    /// </summary>
    /// <param name="requeue">If true, message is requeued; if false, message is discarded or dead-lettered</param>
    /// <param name="cancellationToken">Cancellation token to abort the rejection</param>
    /// <returns>A task representing the asynchronous rejection operation</returns>
    /// <remarks>
    /// Use this method when:
    /// - Message processing fails
    /// - Message should be retried (requeue = true)
    /// - Message should be discarded (requeue = false)
    ///
    /// Behavior by transport:
    /// - RabbitMQ: BasicNack with requeue flag
    /// - Azure Service Bus: Abandon (requeue) or DeadLetter (no requeue)
    /// - Kafka: No action (requeue), commit offset (no requeue)
    ///
    /// Requeue considerations:
    /// - Be careful with infinite retry loops
    /// - Consider using <see cref="DeferAsync"/> for temporary failures
    /// - Use dead lettering for poison messages
    ///
    /// Example:
    /// <code>
    /// try
    /// {
    ///     await ProcessMessageAsync(envelope, ct);
    ///     await context.AcknowledgeAsync(ct);
    /// }
    /// catch (TemporaryException)
    /// {
    ///     // Requeue for retry
    ///     await context.RejectAsync(requeue: true, ct);
    /// }
    /// catch (ValidationException)
    /// {
    ///     // Don't requeue invalid messages
    ///     await context.RejectAsync(requeue: false, ct);
    /// }
    /// </code>
    /// </remarks>
    public Task RejectAsync(bool requeue = false, CancellationToken cancellationToken = default)
    {
        return Reject?.Invoke(requeue, cancellationToken) ?? Task.CompletedTask;
    }

    /// <summary>
    /// Defers message processing to a later time.
    /// </summary>
    /// <param name="delay">Time to wait before making the message visible again (null uses transport default)</param>
    /// <param name="cancellationToken">Cancellation token to abort the defer operation</param>
    /// <returns>A task representing the asynchronous defer operation</returns>
    /// <remarks>
    /// Use defer for temporary failures that may resolve:
    /// - Downstream service temporarily unavailable
    /// - Rate limiting
    /// - Scheduled retry after backoff
    ///
    /// Behavior by transport:
    /// - Azure Service Bus: Native defer support with ScheduledEnqueueTimeUtc
    /// - Amazon SQS: ChangeMessageVisibility
    /// - RabbitMQ: Nack with requeue and TTL
    /// - Kafka: Seek back and commit later offset
    ///
    /// The message will become available for processing again after the delay.
    ///
    /// Example:
    /// <code>
    /// try
    /// {
    ///     await CallExternalServiceAsync(envelope, ct);
    ///     await context.AcknowledgeAsync(ct);
    /// }
    /// catch (ServiceUnavailableException)
    /// {
    ///     // Defer for 30 seconds and retry
    ///     await context.DeferAsync(TimeSpan.FromSeconds(30), ct);
    /// }
    /// </code>
    /// </remarks>
    public Task DeferAsync(TimeSpan? delay = null, CancellationToken cancellationToken = default)
    {
        return Defer?.Invoke(delay, cancellationToken) ?? Task.CompletedTask;
    }

    /// <summary>
    /// Moves the message to the dead letter queue.
    /// </summary>
    /// <param name="reason">Optional reason for dead lettering (for diagnostics and troubleshooting)</param>
    /// <param name="cancellationToken">Cancellation token to abort the operation</param>
    /// <returns>A task representing the asynchronous dead letter operation</returns>
    /// <remarks>
    /// Use dead lettering for:
    /// - Poison messages (invalid format, corrupt data)
    /// - Messages that exceed max retry count
    /// - Messages that cannot be processed due to business rules
    /// - Messages for manual intervention and investigation
    ///
    /// Dead letter queues:
    /// - Store problematic messages for analysis
    /// - Prevent blocking of healthy message processing
    /// - Enable monitoring and alerting on failures
    /// - Allow manual replay after fixing issues
    ///
    /// Behavior by transport:
    /// - Azure Service Bus: Native dead letter queue per queue/subscription
    /// - RabbitMQ: Configurable dead letter exchange
    /// - Amazon SQS: Redrive policy to DLQ
    /// - Kafka: Produce to separate topic
    ///
    /// Always include a descriptive reason for troubleshooting:
    /// <code>
    /// catch (ValidationException ex)
    /// {
    ///     await context.DeadLetterAsync(
    ///         $"Validation failed: {ex.Message}",
    ///         ct);
    /// }
    ///
    /// if (envelope.DeliveryCount > 5)
    /// {
    ///     await context.DeadLetterAsync(
    ///         "Exceeded max retry count of 5",
    ///         ct);
    /// }
    /// </code>
    /// </remarks>
    public Task DeadLetterAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        return DeadLetter?.Invoke(reason, cancellationToken) ?? Task.CompletedTask;
    }
}
