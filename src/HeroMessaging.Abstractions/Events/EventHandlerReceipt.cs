using HeroMessaging.Abstractions.Processing;

namespace HeroMessaging.Abstractions.Events;

/// <summary>
/// Immutable outcome of one handler registration for an in-process event.
/// </summary>
public sealed class EventHandlerReceipt
{
    /// <summary>Creates a receipt for one handler delivery.</summary>
    /// <param name="handlerType">Concrete type of the resolved handler instance.</param>
    /// <param name="status">Terminal delivery status.</param>
    /// <param name="result">Final pipeline result, or a failure explaining admission or bus termination.</param>
    public EventHandlerReceipt(Type handlerType, EventHandlerStatus status, ProcessingResult? result = null)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        HandlerType = handlerType;
        Status = status;
        Result = result;
    }

    /// <summary>Gets the concrete handler type. Duplicate registrations may have the same type.</summary>
    public Type HandlerType { get; }

    /// <summary>Gets the terminal delivery status.</summary>
    public EventHandlerStatus Status { get; }

    /// <summary>Gets the final pipeline result or infrastructure failure, when available.</summary>
    public ProcessingResult? Result { get; }
}
