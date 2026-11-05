namespace HeroMessaging.Transport.RabbitMQ;

/// <summary>
/// Options for <see cref="RabbitMqTransport.ReplayDeadLettersAsync"/>.
/// </summary>
public sealed record DeadLetterReplayOptions
{
    /// <summary>
    /// Maximum number of dead-lettered messages to take from the queue in one call, including messages left in
    /// place because their source queue is unknown. Must be positive. Default: 100.
    /// </summary>
    public int MaxMessages { get; init; } = 100;

    /// <summary>
    /// Queue to replay into. <see langword="null"/> (default) sends each message back to the queue that
    /// dead-lettered it, read from the broker's <c>x-death</c> history.
    /// </summary>
    public string? TargetQueue { get; init; }
}
