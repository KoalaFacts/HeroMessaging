// Example: Order Processing Saga using the DSL Generator
// This file demonstrates how to use [GenerateSaga] to create a complex saga

using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.SourceGeneration;

namespace HeroMessaging.Examples;

// Define your saga data
public class OrderSagaData
{
    public string OrderId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentId { get; set; } = string.Empty;
    public string ShipmentId { get; set; } = string.Empty;
}

// Define your events
public record OrderCreatedEvent(string OrderId, string CustomerId, decimal Amount);
public record PaymentProcessedEvent(string OrderId, string PaymentId);
public record PaymentFailedEvent(string OrderId, string Reason);
public record ShipmentScheduledEvent(string OrderId, string ShipmentId);
public record OrderCompletedEvent(string OrderId);

// Define the saga using the DSL
[GenerateSaga]
public partial class OrderSaga : SagaBase<OrderSagaData>
{
    // Initial state - where the saga starts
    [InitialState]
    [SagaState("Created")]
    public class Created
    {
        [On<OrderCreatedEvent>]
        public async Task OnOrderCreated(OrderCreatedEvent evt)
        {
            // Save data
            Data.OrderId = evt.OrderId;
            Data.CustomerId = evt.CustomerId;
            Data.Amount = evt.Amount;

            // Transition to next state
            TransitionTo("PaymentPending");

            // Could publish a command here
            // await PublishAsync(new ProcessPaymentCommand(evt.OrderId, evt.Amount));
        }
    }

    // Payment processing state
    [SagaState("PaymentPending")]
    public class PaymentPending
    {
        [On<PaymentProcessedEvent>]
        public async Task OnPaymentProcessed(PaymentProcessedEvent evt)
        {
            Data.PaymentId = evt.PaymentId;

            // Move to shipping
            TransitionTo("ShipmentPending");

            // await PublishAsync(new ScheduleShipmentCommand(Data.OrderId));
        }

        [On<PaymentFailedEvent>]
        public async Task OnPaymentFailed(PaymentFailedEvent evt)
        {
            // Payment failed - mark saga as failed
            Fail($"Payment failed: {evt.Reason}");
        }

        // If payment doesn't complete in 5 minutes, timeout
        [OnTimeout(300)] // 5 minutes
        public async Task OnPaymentTimeout()
        {
            Fail("Payment timeout - no response in 5 minutes");
        }

        // If we need to roll back, cancel the payment
        [Compensate]
        public async Task CancelPayment()
        {
            // Refund the payment
            // await PublishAsync(new RefundPaymentCommand(Data.PaymentId));
        }
    }

    // Shipment scheduling state
    [SagaState("ShipmentPending")]
    public class ShipmentPending
    {
        [On<ShipmentScheduledEvent>]
        public async Task OnShipmentScheduled(ShipmentScheduledEvent evt)
        {
            Data.ShipmentId = evt.ShipmentId;

            // Move to completed
            TransitionTo("Completed");

            // Publish completion event
            // await PublishAsync(new OrderCompletedEvent(Data.OrderId));

            // Mark saga as complete
            Complete();
        }

        [OnTimeout(600)] // 10 minutes
        public async Task OnShipmentTimeout()
        {
            Fail("Shipment timeout - could not schedule shipment");
        }

        [Compensate]
        public async Task CancelShipment()
        {
            // Cancel the shipment
            // await PublishAsync(new CancelShipmentCommand(Data.ShipmentId));
        }
    }

    // Final state
    [SagaState("Completed")]
    public class Completed
    {
        // No transitions from completed state
        // Saga is done
    }

    // The generator will create:
    // - States enum with all state names
    // - ConfigureStateMachine() method with all transitions
    // - Helper methods: TransitionTo(), Complete(), Fail()
}

// Generated code will look like:
/*
public partial class OrderSaga
{
    public enum States
    {
        Created,
        PaymentPending,
        ShipmentPending,
        Completed
    }

    protected override void ConfigureStateMachine()
    {
        Initially("Created");

        During("Created", state =>
        {
            state.When<OrderCreatedEvent>()
                 .Then(async (evt, ct) => await new Created().OnOrderCreated(evt))
                 .Execute();
        });

        During("PaymentPending", state =>
        {
            state.When<PaymentProcessedEvent>()
                 .Then(async (evt, ct) => await new PaymentPending().OnPaymentProcessed(evt))
                 .Execute();

            state.When<PaymentFailedEvent>()
                 .Then(async (evt, ct) => await new PaymentPending().OnPaymentFailed(evt))
                 .Execute();

            state.WithTimeout(TimeSpan.FromSeconds(300),
                async () => await new PaymentPending().OnPaymentTimeout());

            state.OnCompensate(async () => await new PaymentPending().CancelPayment());
        });

        During("ShipmentPending", state =>
        {
            state.When<ShipmentScheduledEvent>()
                 .Then(async (evt, ct) => await new ShipmentPending().OnShipmentScheduled(evt))
                 .Execute();

            state.WithTimeout(TimeSpan.FromSeconds(600),
                async () => await new ShipmentPending().OnShipmentTimeout());

            state.OnCompensate(async () => await new ShipmentPending().CancelShipment());
        });

        During("Completed", state =>
        {
            // No transitions
        });
    }

    protected void TransitionTo(string stateName)
    {
        State = stateName;
    }

    protected void Complete()
    {
        IsCompleted = true;
    }

    protected void Fail(string reason)
    {
        IsFailed = true;
        FailureReason = reason;
    }
}
*/
