using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;

namespace HeroMessaging.Tests.TestUtilities;

/// <summary>
/// Test fixture saga demonstrating order fulfillment workflow with compensation
/// Shows state transitions: Initial -> AwaitingPayment -> AwaitingInventory -> AwaitingShipment -> Completed
/// Each step can be compensated if a later step fails
/// Used by integration tests to verify saga orchestration functionality
/// </summary>
public class OrderSaga : SagaBase
{
    // Order data
    public string? OrderId { get; set; }
    public string? CustomerId { get; set; }
    public decimal TotalAmount { get; set; }
    public List<OrderItem> Items { get; set; } = new();

    // Tracking data
    public string? PaymentTransactionId { get; set; }
    public string? InventoryReservationId { get; set; }
    public string? ShipmentTrackingNumber { get; set; }

    // Failure tracking
    public string? FailureReason { get; set; }
}

public class OrderItem
{
    public string ProductId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

#region Events

public record OrderCreatedEvent(
    string OrderId,
    string CustomerId,
    decimal TotalAmount,
    List<OrderItem> Items) : IEvent, IMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record PaymentProcessedEvent(
    string OrderId,
    string TransactionId,
    decimal Amount) : IEvent, IMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record PaymentFailedEvent(
    string OrderId,
    string Reason) : IEvent, IMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record InventoryReservedEvent(
    string OrderId,
    string ReservationId,
    List<OrderItem> Items) : IEvent, IMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record InventoryReservationFailedEvent(
    string OrderId,
    string Reason) : IEvent, IMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record OrderShippedEvent(
    string OrderId,
    string TrackingNumber) : IEvent, IMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record ShipmentFailedEvent(
    string OrderId,
    string Reason) : IEvent, IMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record OrderCancelledEvent(
    string OrderId,
    string Reason) : IEvent, IMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

#endregion

/// <summary>
/// Defines the state machine for OrderSaga
/// </summary>
public static class OrderSagaStateMachine
{
    // State definitions
    public static readonly State Initial = new("Initial");
    public static readonly State AwaitingPayment = new("AwaitingPayment");
    public static readonly State AwaitingInventory = new("AwaitingInventory");
    public static readonly State AwaitingShipment = new("AwaitingShipment");
    public static readonly State Completed = new("Completed");
    public static readonly State Failed = new("Failed");
    public static readonly State Cancelled = new("Cancelled");

    // Event definitions
    public static readonly Event<OrderCreatedEvent> OrderCreated = new(nameof(OrderCreatedEvent));
    public static readonly Event<PaymentProcessedEvent> PaymentProcessed = new(nameof(PaymentProcessedEvent));
    public static readonly Event<PaymentFailedEvent> PaymentFailed = new(nameof(PaymentFailedEvent));
    public static readonly Event<InventoryReservedEvent> InventoryReserved = new(nameof(InventoryReservedEvent));
    public static readonly Event<InventoryReservationFailedEvent> InventoryReservationFailed = new(nameof(InventoryReservationFailedEvent));
    public static readonly Event<OrderShippedEvent> OrderShipped = new(nameof(OrderShippedEvent));
    public static readonly Event<ShipmentFailedEvent> ShipmentFailed = new(nameof(ShipmentFailedEvent));
    public static readonly Event<OrderCancelledEvent> OrderCancelled = new(nameof(OrderCancelledEvent));

    public static StateMachineDefinition<OrderSaga> Build()
    {
        var builder = new StateMachineBuilder<OrderSaga>();

        // Initial state: Order created
        builder.Initially()
            .When(OrderCreated)
                .Then(async ctx =>
                {
                    ctx.Instance.OrderId = ctx.Data.OrderId;
                    ctx.Instance.CustomerId = ctx.Data.CustomerId;
                    ctx.Instance.TotalAmount = ctx.Data.TotalAmount;
                    ctx.Instance.Items = ctx.Data.Items;

                    // In a real system, this would send a ProcessPaymentCommand
                    await Task.CompletedTask;
                })
                .TransitionTo(AwaitingPayment);

        // Awaiting payment
        builder.During(AwaitingPayment)
            .When(PaymentProcessed)
                .Then(async ctx =>
                {
                    ctx.Instance.PaymentTransactionId = ctx.Data.TransactionId;

                    // Register compensation: refund payment if later steps fail
                    ctx.Compensation.AddCompensation(
                        "RefundPayment",
                        async ct =>
                        {
                            // In real system: call payment service to refund
                            await Task.CompletedTask;
                        });

                    // In real system: send ReserveInventoryCommand
                    await Task.CompletedTask;
                })
                .TransitionTo(AwaitingInventory)
            .When(PaymentFailed)
                .Then(ctx =>
                {
                    ctx.Instance.FailureReason = ctx.Data.Reason;
                    return Task.CompletedTask;
                })
                .TransitionTo(Failed);

        // Awaiting inventory reservation
        builder.During(AwaitingInventory)
            .When(InventoryReserved)
                .Then(async ctx =>
                {
                    ctx.Instance.InventoryReservationId = ctx.Data.ReservationId;

                    // Register compensation: release inventory if shipping fails
                    ctx.Compensation.AddCompensation(
                        "ReleaseInventory",
                        async ct =>
                        {
                            // In real system: call inventory service to release
                            await Task.CompletedTask;
                        });

                    // In real system: send CreateShipmentCommand
                    await Task.CompletedTask;
                })
                .TransitionTo(AwaitingShipment)
            .When(InventoryReservationFailed)
                .Then(async ctx =>
                {
                    ctx.Instance.FailureReason = ctx.Data.Reason;

                    // Compensate previous steps (refund payment)
                    await ctx.Compensation.CompensateAsync();
                })
                .TransitionTo(Failed);

        // Awaiting shipment
        builder.During(AwaitingShipment)
            .When(OrderShipped)
                .Then(ctx =>
                {
                    ctx.Instance.ShipmentTrackingNumber = ctx.Data.TrackingNumber;
                    return Task.CompletedTask;
                })
                .TransitionTo(Completed)
                .Finalize()
            .When(ShipmentFailed)
                .Then(async ctx =>
                {
                    ctx.Instance.FailureReason = ctx.Data.Reason;

                    // Compensate all previous steps (release inventory, refund payment)
                    await ctx.Compensation.CompensateAsync();
                })
                .TransitionTo(Failed);

        // Cancellation can happen from any state
        builder.During(AwaitingPayment)
            .When(OrderCancelled)
                .Then(ctx =>
                {
                    ctx.Instance.FailureReason = ctx.Data.Reason;
                    return Task.CompletedTask;
                })
                .TransitionTo(Cancelled)
                .Finalize();

        builder.During(AwaitingInventory)
            .When(OrderCancelled)
                .Then(async ctx =>
                {
                    ctx.Instance.FailureReason = ctx.Data.Reason;
                    await ctx.Compensation.CompensateAsync(); // Refund payment
                })
                .TransitionTo(Cancelled)
                .Finalize();

        builder.During(AwaitingShipment)
            .When(OrderCancelled)
                .Then(async ctx =>
                {
                    ctx.Instance.FailureReason = ctx.Data.Reason;
                    await ctx.Compensation.CompensateAsync(); // Release inventory, refund payment
                })
                .TransitionTo(Cancelled)
                .Finalize();

        return builder.Build();
    }
}
