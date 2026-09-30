namespace HeroMessaging.Abstractions.Events;

/// <summary>
/// Terminal outcome of one in-process handler delivery.
/// </summary>
public enum EventHandlerStatus
{
    /// <summary>The handler pipeline completed successfully.</summary>
    Succeeded,

    /// <summary>The handler pipeline completed with a failure or threw an exception.</summary>
    Failed,

    /// <summary>The event bus did not accept this handler delivery.</summary>
    Rejected,

    /// <summary>The delivery was accepted, but the event bus terminated before its pipeline completed.</summary>
    Aborted
}
