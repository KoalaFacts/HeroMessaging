using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;

namespace HeroMessaging.Tests.TestUtilities;

/// <summary>
/// Example demonstrating the enhanced state machine builder API
/// Shows cleaner, more intuitive syntax for common patterns
/// </summary>
public class ShoppingCartSaga : SagaBase
{
    public string? UserId { get; set; }
    public decimal TotalAmount { get; set; }
    public bool IsPremiumCustomer { get; set; }
    public string? DiscountCode { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? PaymentId { get; set; }
    public string? FailureReason { get; set; }
}

#region Events

public record CartCreatedEvent(string UserId, decimal TotalAmount, bool IsPremiumCustomer) : IEvent, IMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record DiscountAppliedEvent(string UserId, string DiscountCode, decimal DiscountAmount) : IEvent, IMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record PaymentAuthorizedEvent(string UserId, string PaymentId, decimal Amount) : IEvent, IMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record CartPaymentFailedEvent(string UserId, string Reason) : IEvent, IMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record CartCompletedEvent(string UserId, string PaymentId) : IEvent, IMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

#endregion

/// <summary>
/// Enhanced builder pattern example - much cleaner and more intuitive
/// </summary>
public static class ShoppingCartStateMachine
{
    public static StateMachineDefinition<ShoppingCartSaga> BuildEnhanced()
    {
        var builder = new StateMachineBuilder<ShoppingCartSaga>();

        // Example 1: Inline state definition + CopyFrom for easy data mapping
        builder.Initially()
            .When(new Event<CartCreatedEvent>("CartCreated"))
                .CopyFrom((saga, evt) =>
                {
                    saga.UserId = evt.UserId;
                    saga.TotalAmount = evt.TotalAmount;
                    saga.IsPremiumCustomer = evt.IsPremiumCustomer;
                })
                .TransitionTo(new State("AwaitingDiscount"));

        // Example 2: Conditional transitions with If/Else
        builder.InState("AwaitingDiscount")
            .When(new Event<DiscountAppliedEvent>("DiscountApplied"))
                .If(ctx => ctx.Instance.IsPremiumCustomer && ctx.Data.DiscountAmount > 0)
                    .Then(ctx =>
                    {
                        // Premium customer gets extra bonus
                        ctx.Instance.DiscountAmount = ctx.Data.DiscountAmount * 1.1m;
                        ctx.Instance.DiscountCode = ctx.Data.DiscountCode;
                    })
                    .TransitionTo("AwaitingPayment")
                .Else()
                    .Then(ctx =>
                    {
                        // Regular customer gets standard discount
                        ctx.Instance.DiscountAmount = ctx.Data.DiscountAmount;
                        ctx.Instance.DiscountCode = ctx.Data.DiscountCode;
                    })
                    .TransitionTo("AwaitingPayment")
                .EndIf();

        // Example 3: SetProperty for cleaner property assignment
        builder.InState("AwaitingPayment")
            .When(new Event<PaymentAuthorizedEvent>("PaymentAuthorized"))
                .SetProperty(
                    (saga, value) => saga.PaymentId = value,
                    ctx => ctx.Data.PaymentId)
                .CompensateWith(
                    "RefundPayment",
                    async ct => { /* Refund logic here */ await Task.CompletedTask; })
                .TransitionTo(new State("Completed"))
                .MarkAsCompleted();

        // Example 4: Multiple failure paths with clear error handling
        builder.InState("AwaitingPayment")
            .When(new Event<CartPaymentFailedEvent>("CartPaymentFailed"))
                .SetProperty(
                    (saga, reason) => saga.FailureReason = reason,
                    ctx => ctx.Data.Reason)
                .TransitionTo(new State("Failed"))
                .MarkAsCompleted();

        return builder.Build();
    }

    /// <summary>
    /// Comparison: OLD builder pattern (more verbose)
    /// </summary>
    public static StateMachineDefinition<ShoppingCartSaga> BuildTraditional()
    {
        var builder = new StateMachineBuilder<ShoppingCartSaga>();

        // Requires explicit State and Event declarations
        var awaitingDiscount = new State("AwaitingDiscount");
        var awaitingPayment = new State("AwaitingPayment");
        var completed = new State("Completed");
        var failed = new State("Failed");

        var cartCreated = new Event<CartCreatedEvent>("CartCreated");
        var discountApplied = new Event<DiscountAppliedEvent>("DiscountApplied");
        var paymentAuthorized = new Event<PaymentAuthorizedEvent>("PaymentAuthorized");
        var paymentFailed = new Event<CartPaymentFailedEvent>("CartPaymentFailed");

        builder.Initially()
            .When(cartCreated)
                .Then(ctx =>
                {
                    ctx.Instance.UserId = ctx.Data.UserId;
                    ctx.Instance.TotalAmount = ctx.Data.TotalAmount;
                    ctx.Instance.IsPremiumCustomer = ctx.Data.IsPremiumCustomer;
                    return Task.CompletedTask;
                })
                .TransitionTo(awaitingDiscount);

        builder.During(awaitingDiscount)
            .When(discountApplied)
                .Then(ctx =>
                {
                    // No If/Else support - have to do it manually
                    if (ctx.Instance.IsPremiumCustomer && ctx.Data.DiscountAmount > 0)
                    {
                        ctx.Instance.DiscountAmount = ctx.Data.DiscountAmount * 1.1m;
                    }
                    else
                    {
                        ctx.Instance.DiscountAmount = ctx.Data.DiscountAmount;
                    }
                    ctx.Instance.DiscountCode = ctx.Data.DiscountCode;
                    return Task.CompletedTask;
                })
                .TransitionTo(awaitingPayment);

        builder.During(awaitingPayment)
            .When(paymentAuthorized)
                .Then(ctx =>
                {
                    ctx.Instance.PaymentId = ctx.Data.PaymentId;

                    // Compensation inline
                    ctx.Compensation.AddCompensation(
                        "RefundPayment",
                        async ct => { await Task.CompletedTask; });

                    return Task.CompletedTask;
                })
                .TransitionTo(completed)
                .Finalize();

        builder.During(awaitingPayment)
            .When(paymentFailed)
                .Then(ctx =>
                {
                    ctx.Instance.FailureReason = ctx.Data.Reason;
                    return Task.CompletedTask;
                })
                .TransitionTo(failed)
                .Finalize();

        return builder.Build();
    }
}

/// <summary>
/// Complex example showing advanced patterns
/// </summary>
public static class AdvancedBuilderExample
{
    public static StateMachineDefinition<ShoppingCartSaga> BuildWithAdvancedFeatures()
    {
        var builder = new StateMachineBuilder<ShoppingCartSaga>();

        builder.Initially()
            .When(new Event<CartCreatedEvent>("CartCreated"))
                // Example: ThenAll for executing multiple actions
                .ThenAll(
                    ctx =>
                    {
                        ctx.Instance.UserId = ctx.Data.UserId;
                        return Task.CompletedTask;
                    },
                    ctx =>
                    {
                        ctx.Instance.TotalAmount = ctx.Data.TotalAmount;
                        return Task.CompletedTask;
                    },
                    async ctx =>
                    {
                        // Could log or notify
                        await Task.CompletedTask;
                    })
                .TransitionTo(new State("AwaitingPayment"));

        // Example: Conditional with inline logic for complex scenarios
        builder.InState("AwaitingPayment")
            .When(new Event<PaymentAuthorizedEvent>("PaymentAuthorized"))
                .Then(ctx =>
                {
                    // For complex nested conditions, use inline logic
                    if (ctx.Instance.TotalAmount > 1000)
                    {
                        ctx.Instance.CurrentState = "RequiresApproval";
                    }
                    else if (ctx.Instance.IsPremiumCustomer)
                    {
                        ctx.Instance.CurrentState = "FastTrackCompleted";
                    }
                    else
                    {
                        ctx.Instance.CurrentState = "Completed";
                    }
                    ctx.Instance.UpdatedAt = DateTime.UtcNow;
                });

        return builder.Build();
    }
}
