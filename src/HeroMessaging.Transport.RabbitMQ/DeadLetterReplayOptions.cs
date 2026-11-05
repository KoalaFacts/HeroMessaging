namespace HeroMessaging.Transport.RabbitMQ;

/// <summary>
/// Options for <see cref="RabbitMqTransport.ReplayDeadLettersAsync"/>.
/// </summary>
public sealed record DeadLetterReplayOptions
{
    /// <summary>
    /// Maximum number of messages to replay in one call. Messages left in place (unresolved) do not count against
    /// it. Must be positive. Default: 100.
    /// </summary>
    public int MaxMessages { get; init; } = 100;

    /// <summary>
    /// Queue to replay into. <see langword="null"/> (default) sends each message back to the queue that
    /// dead-lettered it, read from the broker's <c>x-death</c> history.
    /// </summary>
    public string? TargetQueue { get; init; }
}
