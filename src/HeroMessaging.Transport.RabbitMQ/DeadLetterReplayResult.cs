namespace HeroMessaging.Transport.RabbitMQ;

/// <summary>
/// Outcome of <see cref="RabbitMqTransport.ReplayDeadLettersAsync"/>.
/// </summary>
/// <param name="Replayed">Messages confirmed by the broker on the target queue and then removed from the dead-letter queue.</param>
/// <param name="Unresolved">
/// Messages left in the dead-letter queue because they carry no <c>x-death</c> history naming their source queue.
/// Replay them with <see cref="DeadLetterReplayOptions.TargetQueue"/>.
/// </param>
public readonly record struct DeadLetterReplayResult(int Replayed, int Unresolved);
