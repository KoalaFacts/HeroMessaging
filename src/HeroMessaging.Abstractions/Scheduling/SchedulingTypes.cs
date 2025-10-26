using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Scheduling;

/// <summary>
/// Represents a message scheduled for future delivery.
/// </summary>
public class ScheduledMessage
{
    /// <summary>
    /// Gets or initializes the unique schedule identifier.
    /// </summary>
    public Guid ScheduleId { get; init; }

    /// <summary>
    /// Gets or initializes the message to be delivered.
    /// </summary>
    public IMessage Message { get; init; } = null!;

    /// <summary>
    /// Gets or initializes the time when the message should be delivered.
    /// </summary>
    public DateTimeOffset DeliverAt { get; init; }

    /// <summary>
    /// Gets or initializes the scheduling options.
    /// </summary>
    public SchedulingOptions Options { get; init; } = new();

    /// <summary>
    /// Gets or initializes the time when the message was scheduled.
    /// </summary>
    public DateTimeOffset ScheduledAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Options for message scheduling behavior.
/// </summary>
public class SchedulingOptions
{
    /// <summary>
    /// Gets or sets the destination for the scheduled message.
    /// If not specified, uses the message's default routing.
    /// </summary>
    public string? Destination { get; set; }

    /// <summary>
    /// Gets or sets the priority of the scheduled message (higher values = higher priority).
    /// Default is 0.
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Gets or sets additional metadata for the scheduled message.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Gets or sets whether to skip delivery if the scheduled time has already passed.
    /// Default is false (deliver immediately if past due).
    /// </summary>
    public bool SkipIfPastDue { get; set; } = false;
}

/// <summary>
/// Result of a message scheduling operation.
/// </summary>
public class ScheduleResult
{
    /// <summary>
    /// Gets or initializes the unique schedule identifier.
    /// </summary>
    public Guid ScheduleId { get; init; }

    /// <summary>
    /// Gets or initializes the time the message is scheduled for delivery.
    /// </summary>
    public DateTimeOffset ScheduledFor { get; init; }

    /// <summary>
    /// Gets or initializes whether the scheduling operation was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets or initializes an error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful schedule result.
    /// </summary>
    public static ScheduleResult Successful(Guid scheduleId, DateTimeOffset scheduledFor) =>
        new()
        {
            ScheduleId = scheduleId,
            ScheduledFor = scheduledFor,
            Success = true
        };

    /// <summary>
    /// Creates a failed schedule result.
    /// </summary>
    public static ScheduleResult Failed(string errorMessage) =>
        new()
        {
            Success = false,
            ErrorMessage = errorMessage
        };
}

/// <summary>
/// Information about a scheduled message.
/// </summary>
public class ScheduledMessageInfo
{
    /// <summary>
    /// Gets or initializes the unique schedule identifier.
    /// </summary>
    public Guid ScheduleId { get; init; }

    /// <summary>
    /// Gets or initializes the message ID.
    /// </summary>
    public Guid MessageId { get; init; }

    /// <summary>
    /// Gets or initializes the message type name.
    /// </summary>
    public string MessageType { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the time when the message should be delivered.
    /// </summary>
    public DateTimeOffset DeliverAt { get; init; }

    /// <summary>
    /// Gets or initializes the time when the message was scheduled.
    /// </summary>
    public DateTimeOffset ScheduledAt { get; init; }

    /// <summary>
    /// Gets or initializes the current status of the scheduled message.
    /// </summary>
    public ScheduledMessageStatus Status { get; init; }

    /// <summary>
    /// Gets or initializes the destination for the message.
    /// </summary>
    public string? Destination { get; init; }

    /// <summary>
    /// Gets or initializes the priority of the scheduled message.
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// Gets or initializes any error message if delivery failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Status of a scheduled message.
/// </summary>
public enum ScheduledMessageStatus
{
    /// <summary>
    /// The message is pending delivery.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The message has been delivered.
    /// </summary>
    Delivered = 1,

    /// <summary>
    /// The message was cancelled before delivery.
    /// </summary>
    Cancelled = 2,

    /// <summary>
    /// The message delivery failed.
    /// </summary>
    Failed = 3
}

/// <summary>
/// Storage entry for a scheduled message.
/// </summary>
public class ScheduledMessageEntry
{
    /// <summary>
    /// Gets or sets the unique schedule identifier.
    /// </summary>
    public Guid ScheduleId { get; set; }

    /// <summary>
    /// Gets or sets the scheduled message.
    /// </summary>
    public ScheduledMessage Message { get; set; } = null!;

    /// <summary>
    /// Gets or sets the current status.
    /// </summary>
    public ScheduledMessageStatus Status { get; set; } = ScheduledMessageStatus.Pending;

    /// <summary>
    /// Gets or sets the time when the message was actually delivered.
    /// </summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>
    /// Gets or sets any error message if delivery failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the time this entry was last updated.
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Query parameters for scheduled messages.
/// </summary>
public class ScheduledMessageQuery
{
    /// <summary>
    /// Gets or sets the status filter.
    /// </summary>
    public ScheduledMessageStatus? Status { get; set; }

    /// <summary>
    /// Gets or sets the destination filter.
    /// </summary>
    public string? Destination { get; set; }

    /// <summary>
    /// Gets or sets the message type filter.
    /// </summary>
    public string? MessageType { get; set; }

    /// <summary>
    /// Gets or sets the start of the delivery time range.
    /// </summary>
    public DateTimeOffset? DeliverAfter { get; set; }

    /// <summary>
    /// Gets or sets the end of the delivery time range.
    /// </summary>
    public DateTimeOffset? DeliverBefore { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of results to return.
    /// </summary>
    public int Limit { get; set; } = 100;

    /// <summary>
    /// Gets or sets the number of results to skip (for paging).
    /// </summary>
    public int Offset { get; set; } = 0;
}
