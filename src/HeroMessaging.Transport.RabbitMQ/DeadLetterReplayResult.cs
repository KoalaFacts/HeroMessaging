namespace HeroMessaging.Transport.RabbitMQ;

/// <summary>
/// Outcome of <see cref="RabbitMqTransport.ReplayDeadLettersAsync"/>.
/// </summary>
/// <param name="Replayed">Messages confirmed by the broker on the target queue and then removed from the dead-letter queue.</param>
/// <param name="Unresolved">
/// Messages kept in the dead-letter queue (moved to its back) because their <c>x-death</c> history names no source queue other than the
/// dead-letter queue, or names a queue that no longer routes. Replay them with <see cref="DeadLetterReplayOptions.TargetQueue"/>.
/// </param>
public readonly record struct DeadLetterReplayResult(int Replayed, int Unresolved);
