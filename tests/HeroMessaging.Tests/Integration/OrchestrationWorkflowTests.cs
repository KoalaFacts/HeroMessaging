using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using HeroMessaging.Tests.Examples;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroMessaging.Tests.Integration;

[Trait("Category", "Integration")]
public class OrchestrationWorkflowTests
{
    [Fact]
    public async Task OrderSaga_HappyPath_CompletesSuccessfully()
    {
        // Arrange
        var repository = new InMemorySagaRepository<OrderSaga>();
        var stateMachine = OrderSagaStateMachine.Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<OrderSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<OrderSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var orderId = $"ORDER-{Guid.NewGuid()}";
        var customerId = $"CUST-{Guid.NewGuid()}";

        // Act - Complete order workflow
        var orderCreated = new OrderCreatedEvent(
            orderId,
            customerId,
            TotalAmount: 99.99m,
            Items: new List<OrderItem>
            {
                new() { ProductId = "PROD-1", Quantity = 2, Price = 49.99m }
            })
        {
            CorrelationId = correlationId.ToString()
        };
        await orchestrator.ProcessAsync(orderCreated);

        var paymentProcessed = new PaymentProcessedEvent(
            orderId,
            TransactionId: "TXN-123",
            Amount: 99.99m)
        {
            CorrelationId = correlationId.ToString()
        };
        await orchestrator.ProcessAsync(paymentProcessed);

        var inventoryReserved = new InventoryReservedEvent(
            orderId,
            ReservationId: "RES-456",
            Items: orderCreated.Items)
        {
            CorrelationId = correlationId.ToString()
        };
        await orchestrator.ProcessAsync(inventoryReserved);

        var orderShipped = new OrderShippedEvent(
            orderId,
            TrackingNumber: "TRACK-789")
        {
            CorrelationId = correlationId.ToString()
        };
        await orchestrator.ProcessAsync(orderShipped);

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.NotNull(saga);
        Assert.Equal("Completed", saga!.CurrentState);
        Assert.True(saga.IsCompleted);
        Assert.Equal(orderId, saga.OrderId);
        Assert.Equal(customerId, saga.CustomerId);
        Assert.Equal(99.99m, saga.TotalAmount);
        Assert.Equal("TXN-123", saga.PaymentTransactionId);
        Assert.Equal("RES-456", saga.InventoryReservationId);
        Assert.Equal("TRACK-789", saga.ShipmentTrackingNumber);
    }

    [Fact]
    public async Task OrderSaga_PaymentFailed_TransitionsToFailed()
    {
        // Arrange
        var repository = new InMemorySagaRepository<OrderSaga>();
        var stateMachine = OrderSagaStateMachine.Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<OrderSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<OrderSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var orderId = $"ORDER-{Guid.NewGuid()}";

        // Act - Order created, payment fails
        var orderCreated = new OrderCreatedEvent(
            orderId,
            CustomerId: "CUST-123",
            TotalAmount: 99.99m,
            Items: new List<OrderItem>())
        {
            CorrelationId = correlationId.ToString()
        };
        await orchestrator.ProcessAsync(orderCreated);

        var paymentFailed = new PaymentFailedEvent(
            orderId,
            Reason: "Insufficient funds")
        {
            CorrelationId = correlationId.ToString()
        };
        await orchestrator.ProcessAsync(paymentFailed);

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.NotNull(saga);
        Assert.Equal("Failed", saga!.CurrentState);
        Assert.Equal("Insufficient funds", saga.FailureReason);
        Assert.False(saga.IsCompleted); // Failed, not completed
    }

    [Fact]
    public async Task OrderSaga_InventoryFailed_TransitionsToFailedWithCompensation()
    {
        // Arrange
        var repository = new InMemorySagaRepository<OrderSaga>();
        var stateMachine = OrderSagaStateMachine.Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<OrderSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<OrderSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var orderId = $"ORDER-{Guid.NewGuid()}";

        // Act - Order created, payment processed, inventory fails
        await orchestrator.ProcessAsync(new OrderCreatedEvent(
            orderId, "CUST-123", 99.99m, new List<OrderItem>())
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new PaymentProcessedEvent(
            orderId, "TXN-123", 99.99m)
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new InventoryReservationFailedEvent(
            orderId, "Out of stock")
        { CorrelationId = correlationId.ToString() });

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.NotNull(saga);
        Assert.Equal("Failed", saga!.CurrentState);
        Assert.Equal("Out of stock", saga.FailureReason);
        Assert.Equal("TXN-123", saga.PaymentTransactionId); // Payment was processed
        Assert.Null(saga.InventoryReservationId); // Inventory never reserved
    }

    [Fact]
    public async Task OrderSaga_ShipmentFailed_TransitionsToFailedWithFullCompensation()
    {
        // Arrange
        var repository = new InMemorySagaRepository<OrderSaga>();
        var stateMachine = OrderSagaStateMachine.Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<OrderSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<OrderSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var orderId = $"ORDER-{Guid.NewGuid()}";

        // Act - Complete workflow until shipment fails
        await orchestrator.ProcessAsync(new OrderCreatedEvent(
            orderId, "CUST-123", 99.99m, new List<OrderItem>())
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new PaymentProcessedEvent(
            orderId, "TXN-123", 99.99m)
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new InventoryReservedEvent(
            orderId, "RES-456", new List<OrderItem>())
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new ShipmentFailedEvent(
            orderId, "Carrier unavailable")
        { CorrelationId = correlationId.ToString() });

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.NotNull(saga);
        Assert.Equal("Failed", saga!.CurrentState);
        Assert.Equal("Carrier unavailable", saga.FailureReason);
        Assert.Equal("TXN-123", saga.PaymentTransactionId);
        Assert.Equal("RES-456", saga.InventoryReservationId);
        Assert.Null(saga.ShipmentTrackingNumber);
    }

    [Fact]
    public async Task OrderSaga_CancelledDuringPayment_TransitionsToCancelled()
    {
        // Arrange
        var repository = new InMemorySagaRepository<OrderSaga>();
        var stateMachine = OrderSagaStateMachine.Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<OrderSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<OrderSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var orderId = $"ORDER-{Guid.NewGuid()}";

        // Act
        await orchestrator.ProcessAsync(new OrderCreatedEvent(
            orderId, "CUST-123", 99.99m, new List<OrderItem>())
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new OrderCancelledEvent(
            orderId, "Customer requested cancellation")
        { CorrelationId = correlationId.ToString() });

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.NotNull(saga);
        Assert.Equal("Cancelled", saga!.CurrentState);
        Assert.True(saga.IsCompleted); // Cancelled is a final state
        Assert.Equal("Customer requested cancellation", saga.FailureReason);
    }

    [Fact]
    public async Task OrderSaga_CancelledAfterPayment_CompensatesPayment()
    {
        // Arrange
        var repository = new InMemorySagaRepository<OrderSaga>();
        var stateMachine = OrderSagaStateMachine.Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<OrderSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<OrderSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var orderId = $"ORDER-{Guid.NewGuid()}";

        // Act
        await orchestrator.ProcessAsync(new OrderCreatedEvent(
            orderId, "CUST-123", 99.99m, new List<OrderItem>())
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new PaymentProcessedEvent(
            orderId, "TXN-123", 99.99m)
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new OrderCancelledEvent(
            orderId, "Customer changed mind")
        { CorrelationId = correlationId.ToString() });

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.NotNull(saga);
        Assert.Equal("Cancelled", saga!.CurrentState);
        Assert.True(saga.IsCompleted);
        Assert.Equal("TXN-123", saga.PaymentTransactionId);
    }

    [Fact]
    public async Task OrderSaga_MultipleInstancesConcurrently_ProcessIndependently()
    {
        // Arrange
        var repository = new InMemorySagaRepository<OrderSaga>();
        var stateMachine = OrderSagaStateMachine.Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<OrderSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<OrderSaga>(repository, stateMachine, services, logger);

        var correlation1 = Guid.NewGuid();
        var correlation2 = Guid.NewGuid();
        var correlation3 = Guid.NewGuid();

        // Act - Process three orders concurrently
        var tasks = new List<Task>
        {
            ProcessFullOrder(orchestrator, correlation1, "ORDER-1"),
            ProcessFullOrder(orchestrator, correlation2, "ORDER-2"),
            ProcessFullOrder(orchestrator, correlation3, "ORDER-3")
        };

        await Task.WhenAll(tasks);

        // Assert - All three orders completed independently
        var saga1 = await repository.FindAsync(correlation1);
        var saga2 = await repository.FindAsync(correlation2);
        var saga3 = await repository.FindAsync(correlation3);

        Assert.NotNull(saga1);
        Assert.NotNull(saga2);
        Assert.NotNull(saga3);

        Assert.Equal("Completed", saga1!.CurrentState);
        Assert.Equal("Completed", saga2!.CurrentState);
        Assert.Equal("Completed", saga3!.CurrentState);

        Assert.Equal("ORDER-1", saga1.OrderId);
        Assert.Equal("ORDER-2", saga2.OrderId);
        Assert.Equal("ORDER-3", saga3.OrderId);
    }

    [Fact]
    public async Task OrderSaga_StateTransitions_UpdateTimestamps()
    {
        // Arrange
        var repository = new InMemorySagaRepository<OrderSaga>();
        var stateMachine = OrderSagaStateMachine.Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<OrderSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<OrderSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var orderId = "ORDER-123";

        // Act
        await orchestrator.ProcessAsync(new OrderCreatedEvent(
            orderId, "CUST-123", 99.99m, new List<OrderItem>())
        { CorrelationId = correlationId.ToString() });

        var sagaAfterCreation = await repository.FindAsync(correlationId);
        var createdTime = sagaAfterCreation!.UpdatedAt;

        await Task.Delay(10); // Small delay to ensure timestamp difference

        await orchestrator.ProcessAsync(new PaymentProcessedEvent(
            orderId, "TXN-123", 99.99m)
        { CorrelationId = correlationId.ToString() });

        var sagaAfterPayment = await repository.FindAsync(correlationId);

        // Assert
        Assert.True(sagaAfterPayment!.UpdatedAt > createdTime);
        Assert.Equal(sagaAfterCreation.CreatedAt, sagaAfterPayment.CreatedAt); // Created time unchanged
    }

    private static async Task ProcessFullOrder(
        SagaOrchestrator<OrderSaga> orchestrator,
        Guid correlationId,
        string orderId)
    {
        await orchestrator.ProcessAsync(new OrderCreatedEvent(
            orderId, "CUST-123", 99.99m, new List<OrderItem>())
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new PaymentProcessedEvent(
            orderId, $"TXN-{orderId}", 99.99m)
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new InventoryReservedEvent(
            orderId, $"RES-{orderId}", new List<OrderItem>())
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new OrderShippedEvent(
            orderId, $"TRACK-{orderId}")
        { CorrelationId = correlationId.ToString() });
    }

    [Fact]
    public async Task OrderSaga_SingleEventCompensation_ExecutesActionsInLIFOOrder()
    {
        // Arrange - Test compensation within a single complex event
        var compensationLog = new System.Collections.Concurrent.ConcurrentBag<(DateTime Time, string Action)>();

        var repository = new InMemorySagaRepository<CompensationTrackingSaga>();
        var stateMachine = BuildCompensationTrackingStateMachine(compensationLog);
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<CompensationTrackingSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<CompensationTrackingSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();

        // Act - Send an event that performs multiple operations, then fails and compensates
        await orchestrator.ProcessAsync(new ComplexOperationEvent("ComplexOp", shouldFail: true)
        { CorrelationId = correlationId.ToString() });

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.NotNull(saga);
        Assert.Equal("Failed", saga!.CurrentState);

        // Verify all three compensations executed in LIFO order
        Assert.Equal(3, compensationLog.Count);
        var orderedLog = compensationLog.OrderBy(x => x.Time).Select(x => x.Action).ToList();
        Assert.Equal("CompensateStep3", orderedLog[0]);
        Assert.Equal("CompensateStep2", orderedLog[1]);
        Assert.Equal("CompensateStep1", orderedLog[2]);
    }

    [Fact]
    public async Task OrderSaga_StateBasedCompensation_ExecutesBasedOnSagaState()
    {
        // Arrange - Test compensation based on saga state (proper pattern for cross-event compensation)
        var compensationLog = new System.Collections.Concurrent.ConcurrentBag<string>();

        var repository = new InMemorySagaRepository<StateBasedCompensationSaga>();
        var stateMachine = BuildStateBasedCompensationStateMachine(compensationLog);
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<StateBasedCompensationSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<StateBasedCompensationSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();

        // Act - Payment succeeds, then inventory fails (should check state and compensate payment)
        await orchestrator.ProcessAsync(new StateBasedPaymentEvent("TXN-123")
        { CorrelationId = correlationId.ToString() });

        await orchestrator.ProcessAsync(new StateBasedInventoryFailedEvent("Out of stock")
        { CorrelationId = correlationId.ToString() });

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.NotNull(saga);
        Assert.Equal("Failed", saga!.CurrentState);

        // Verify payment was compensated based on saga state
        Assert.Contains("RefundPayment-TXN-123", compensationLog);
        Assert.Single(compensationLog);
    }

    #region Compensation Testing Sagas

    // Test 1: Single-event compensation (proper use of CompensationContext)
    private class CompensationTrackingSaga : SagaBase
    {
        public string? OperationData { get; set; }
        public string? FailureReason { get; set; }
    }

    private record ComplexOperationEvent(string Data, bool shouldFail) : IEvent, IMessage
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    private static StateMachineDefinition<CompensationTrackingSaga> BuildCompensationTrackingStateMachine(
        System.Collections.Concurrent.ConcurrentBag<(DateTime Time, string Action)> compensationLog)
    {
        var builder = new StateMachineBuilder<CompensationTrackingSaga>();

        var initial = new State("Initial");
        var processing = new State("Processing");
        var completed = new State("Completed");
        var failed = new State("Failed");

        var complexOp = new Event<ComplexOperationEvent>(nameof(ComplexOperationEvent));

        builder.Initially()
            .When(complexOp)
                .Then(async ctx =>
                {
                    // Simulate a complex operation with multiple steps and potential failure
                    ctx.Instance.OperationData = ctx.Data.Data;

                    // Step 1: Allocate resource
                    ctx.Compensation.AddCompensation("CompensateStep1",
                        async ct => compensationLog.Add((DateTime.UtcNow, "CompensateStep1")));

                    // Step 2: Reserve capacity
                    ctx.Compensation.AddCompensation("CompensateStep2",
                        async ct => compensationLog.Add((DateTime.UtcNow, "CompensateStep2")));

                    // Step 3: Lock records
                    ctx.Compensation.AddCompensation("CompensateStep3",
                        async ct => compensationLog.Add((DateTime.UtcNow, "CompensateStep3")));

                    // Simulate failure after registering compensations
                    if (ctx.Data.shouldFail)
                    {
                        ctx.Instance.FailureReason = "Simulated failure after registering compensations";
                        // Execute all compensations in LIFO order (Step3, Step2, Step1)
                        await ctx.Compensation.CompensateAsync();
                        ctx.Instance.CurrentState = failed.Name;
                    }
                    else
                    {
                        ctx.Instance.CurrentState = completed.Name;
                    }
                })
                .TransitionTo(processing);

        return builder.Build();
    }

    // Test 2: State-based compensation (cross-event compensation pattern)
    private class StateBasedCompensationSaga : SagaBase
    {
        public string? PaymentTransactionId { get; set; }
        public string? FailureReason { get; set; }
    }

    private record StateBasedPaymentEvent(string TransactionId) : IEvent, IMessage
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    private record StateBasedInventoryFailedEvent(string Reason) : IEvent, IMessage
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    private static StateMachineDefinition<StateBasedCompensationSaga> BuildStateBasedCompensationStateMachine(
        System.Collections.Concurrent.ConcurrentBag<string> compensationLog)
    {
        var builder = new StateMachineBuilder<StateBasedCompensationSaga>();

        var initial = new State("Initial");
        var awaitingInventory = new State("AwaitingInventory");
        var failed = new State("Failed");

        var paymentEvent = new Event<StateBasedPaymentEvent>(nameof(StateBasedPaymentEvent));
        var inventoryFailedEvent = new Event<StateBasedInventoryFailedEvent>(nameof(StateBasedInventoryFailedEvent));

        // Event 1: Payment succeeds - store transaction ID
        builder.Initially()
            .When(paymentEvent)
                .Then(ctx =>
                {
                    ctx.Instance.PaymentTransactionId = ctx.Data.TransactionId;
                    return Task.CompletedTask;
                })
                .TransitionTo(awaitingInventory);

        // Event 2: Inventory fails - compensate based on saga state
        builder.During(awaitingInventory)
            .When(inventoryFailedEvent)
                .Then(async ctx =>
                {
                    ctx.Instance.FailureReason = ctx.Data.Reason;

                    // Proper pattern: Check saga state and manually compensate
                    if (!string.IsNullOrEmpty(ctx.Instance.PaymentTransactionId))
                    {
                        // Simulate refunding payment
                        compensationLog.Add($"RefundPayment-{ctx.Instance.PaymentTransactionId}");
                        await Task.CompletedTask;
                    }
                })
                .TransitionTo(failed);

        return builder.Build();
    }

    #endregion
}
