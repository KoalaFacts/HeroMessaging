namespace HeroMessaging.SourceGeneration;

/// <summary>
/// Generates a saga state machine from a declarative DSL.
/// The class should define states, events, and transitions using nested classes and methods.
/// </summary>
/// <example>
/// <code>
/// [GenerateSaga]
/// public partial class OrderSaga : SagaBase&lt;OrderSagaData&gt;
/// {
///     [SagaState("Created")]
///     public class Created
///     {
///         [On&lt;OrderCreatedEvent&gt;]
///         public async Task OnOrderCreated(OrderCreatedEvent evt)
///         {
///             Data.OrderId = evt.OrderId;
///             TransitionTo("PaymentPending");
///         }
///     }
///
///     [SagaState("PaymentPending")]
///     public class PaymentPending
///     {
///         [On&lt;PaymentProcessedEvent&gt;]
///         public async Task OnPaymentProcessed(PaymentProcessedEvent evt)
///         {
///             TransitionTo("Completed");
///             Complete();
///         }
///
///         [Compensate]
///         public async Task CompensatePayment()
///         {
///             // Refund payment
///         }
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GenerateSagaAttribute : Attribute
{
}

/// <summary>
/// Marks a nested class as representing a saga state.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SagaStateAttribute : Attribute
{
    /// <summary>
    /// The name of the state.
    /// </summary>
    public string StateName { get; }

    public SagaStateAttribute(string stateName)
    {
        StateName = stateName ?? throw new ArgumentNullException(nameof(stateName));
    }
}

/// <summary>
/// Marks a method as an event handler for a specific event type.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class OnAttribute<TEvent> : Attribute where TEvent : class
{
}

/// <summary>
/// Marks a method as a compensation action for the current state.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class CompensateAttribute : Attribute
{
}

/// <summary>
/// Marks a method as a timeout handler for the current state.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class OnTimeoutAttribute : Attribute
{
    /// <summary>
    /// The timeout duration.
    /// </summary>
    public TimeSpan Timeout { get; }

    public OnTimeoutAttribute(int seconds)
    {
        Timeout = TimeSpan.FromSeconds(seconds);
    }

    public OnTimeoutAttribute(int hours, int minutes, int seconds)
    {
        Timeout = new TimeSpan(hours, minutes, seconds);
    }
}

/// <summary>
/// Marks the initial state of the saga.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class InitialStateAttribute : Attribute
{
}
