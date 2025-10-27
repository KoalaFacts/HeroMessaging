# ADR-0003: State Machine Patterns - Choreography vs Orchestration Research

**Status:** Research
**Date:** 2025-10-27
**Decision:** Pending

## Context

HeroMessaging currently supports basic message processing patterns (commands, events, queries) with inbox/outbox patterns for reliability, but lacks support for complex multi-step workflows and long-running business processes. This research explores two fundamental approaches to implementing state machines for managing distributed workflows: **Choreography (Cohesive)** and **Orchestration**.

### Current HeroMessaging Capabilities

**Existing Infrastructure:**
- CQRS pattern with commands, events, and queries
- Inbox/outbox patterns for transactional messaging
- Decorator-based processing pipeline
- Message scheduling (in-memory and storage-backed)
- Queue-based asynchronous processing
- Correlation/causation support in TransportEnvelope
- Retry policies and error handling

**Current Gaps:**
- No saga/process manager pattern implementation
- No state machine abstraction
- No long-running workflow coordination
- Limited multi-step process support
- No compensating transaction framework

## Research Overview

This document provides comprehensive research on two architectural patterns for implementing state machines in distributed messaging systems, with specific recommendations for HeroMessaging.

---

## Pattern 1: Choreography (Cohesive/Event-Driven)

### Definition

**Choreography** is a decentralized approach where each service works independently and communicates with other services through event-based messages, with no central controller. Services react to events, perform their local transactions, and publish new events to trigger the next steps.

### Architecture Characteristics

**Key Properties:**
- **Decentralized control** - No single coordinator
- **Event-driven** - Services communicate via domain events
- **Autonomous services** - Each service makes independent decisions
- **Loose coupling** - Services only know about events, not other services
- **Reactive flow** - Services react to events and publish new events

**Typical Flow:**
```
Service A: Execute → Publish Event A
    ↓ (Event Bus)
Service B: Listen Event A → Execute → Publish Event B
    ↓ (Event Bus)
Service C: Listen Event B → Execute → Publish Event C
    ↓ (Event Bus)
Service D: Listen Event C → Execute → Complete
```

### Implementation Pattern

**Core Components:**

1. **Event Handlers** - Services subscribe to specific events
```csharp
public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    public async Task HandleAsync(OrderCreatedEvent @event)
    {
        // Perform local transaction
        await _inventory.ReserveItems(@event.OrderId, @event.Items);

        // Publish next event
        await _bus.Publish(new ItemsReservedEvent
        {
            OrderId = @event.OrderId,
            CorrelationId = @event.CorrelationId
        });
    }
}
```

2. **Event-Driven Workflow**
```csharp
// Workflow: Order → Reserve Inventory → Process Payment → Ship Order

OrderCreatedEvent → ItemsReservedEvent → PaymentProcessedEvent → OrderShippedEvent
                ↓                    ↓                       ↓
        InventoryService      PaymentService      ShippingService
```

3. **Correlation** - Events linked by CorrelationId
```csharp
public class OrderWorkflowEvent : IEvent
{
    public Guid OrderId { get; set; }
    public Guid CorrelationId { get; set; } // Links all events in workflow
    public string CausationId { get; set; } // Previous event that caused this
}
```

### Advantages

1. **High Decoupling** - Services don't know about each other, only events
2. **Resilience** - No single point of failure (no central orchestrator)
3. **Scalability** - Services can scale independently
4. **Autonomy** - Each service owns its domain logic completely
5. **Natural Fit for Event-Driven Domains** - Aligns with DDD bounded contexts
6. **Performance** - No orchestrator bottleneck, parallel processing possible
7. **Flexibility** - Easy to add new event listeners without changing existing services

### Disadvantages

1. **Complexity in Understanding Flow** - Workflow logic distributed across services
2. **Difficult Debugging** - Hard to trace end-to-end flow
3. **Cyclic Dependencies Risk** - Services can inadvertently create event loops
4. **Limited Visibility** - No single place to see workflow state
5. **Challenging Error Handling** - Compensation logic distributed
6. **Testing Difficulty** - Integration tests require multiple services
7. **Deadlock Potential** - Circular event dependencies can cause deadlocks
8. **Lack of Central Monitoring** - Hard to track saga progress

### When to Use Choreography

**Best Suited For:**
- Simple to moderate workflows (3-5 steps)
- Domains with natural event flows (e.g., e-commerce: order → payment → shipping)
- Cross-bounded-context communication
- Services owned by different teams/organizations
- Systems prioritizing loose coupling and service autonomy
- Event-driven architectures with existing event infrastructure
- Workflows with high concurrency and independent steps

**Example Use Cases:**
- Order processing with independent inventory, payment, shipping steps
- User registration flow with email, profile creation, welcome message
- Content publishing with moderation, indexing, notification steps
- IoT event processing pipelines

---

## Pattern 2: Orchestration (State Machine/Process Manager)

### Definition

**Orchestration** uses a central coordinator (orchestrator/process manager) that controls the sequence of operations, maintains workflow state, and explicitly commands services to execute their local transactions. The orchestrator implements a state machine that tracks workflow progress.

### Architecture Characteristics

**Key Properties:**
- **Centralized control** - Single orchestrator manages workflow
- **Command-driven** - Orchestrator sends commands to services
- **Stateful** - Orchestrator maintains workflow state
- **Explicit flow** - Workflow logic clearly defined in one place
- **Sequential coordination** - Orchestrator determines order of operations

**Typical Flow:**
```
Orchestrator (State Machine):
    State: Created → Send Command A to Service A
    State: Step1Complete → Send Command B to Service B
    State: Step2Complete → Send Command C to Service C
    State: Step3Complete → Send Command D to Service D
    State: Completed → Workflow Done
```

### Implementation Pattern

**Core Components:**

1. **State Machine Definition**
```csharp
public class OrderSaga : StateMachine<OrderSagaState>
{
    public State Created { get; set; }
    public State InventoryReserved { get; set; }
    public State PaymentProcessed { get; set; }
    public State Completed { get; set; }
    public State Failed { get; set; }

    public Event<OrderCreated> OrderCreatedEvent { get; set; }
    public Event<InventoryReserved> InventoryReservedEvent { get; set; }
    public Event<PaymentProcessed> PaymentProcessedEvent { get; set; }
    public Event<InventoryReservationFailed> ReservationFailedEvent { get; set; }

    public OrderSaga()
    {
        Initially(
            When(OrderCreatedEvent)
                .TransitionTo(Created)
                .ThenAsync(async context =>
                {
                    await context.Send(new ReserveInventoryCommand
                    {
                        OrderId = context.Data.OrderId,
                        Items = context.Data.Items
                    });
                })
                .TransitionTo(InventoryReserving)
        );

        During(InventoryReserving,
            When(InventoryReservedEvent)
                .ThenAsync(async context =>
                {
                    await context.Send(new ProcessPaymentCommand
                    {
                        OrderId = context.Instance.OrderId,
                        Amount = context.Instance.TotalAmount
                    });
                })
                .TransitionTo(ProcessingPayment),
            When(ReservationFailedEvent)
                .TransitionTo(Failed)
        );

        // Continue defining state transitions...
    }
}
```

2. **Saga State (Process Manager State)**
```csharp
public class OrderSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; }

    // Business data
    public Guid OrderId { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public int RetryCount { get; set; }

    // Saga-specific
    public Guid? InventoryReservationId { get; set; }
    public Guid? PaymentTransactionId { get; set; }
}
```

3. **Saga Repository**
```csharp
public interface ISagaRepository<TSaga> where TSaga : class
{
    Task<TSaga?> GetAsync(Guid correlationId);
    Task SaveAsync(TSaga saga);
    Task DeleteAsync(Guid correlationId);
}
```

4. **Orchestrator**
```csharp
public class SagaOrchestrator<TSaga, TState>
{
    private readonly ISagaRepository<TState> _repository;
    private readonly StateMachine<TState> _stateMachine;
    private readonly IMessageBus _bus;

    public async Task HandleEventAsync(IEvent @event)
    {
        var correlationId = GetCorrelationId(@event);
        var state = await _repository.GetAsync(correlationId);

        // Apply event to state machine
        await _stateMachine.RaiseAsync(@event, state);

        // Persist updated state
        await _repository.SaveAsync(state);
    }
}
```

### Advantages

1. **Clear Workflow Visibility** - Entire flow defined in one place
2. **Easy Debugging** - Central point to inspect workflow state
3. **Simplified Error Handling** - Compensation logic centralized
4. **Better Monitoring** - Easy to track progress and metrics
5. **Explicit State Management** - Clear state transitions
6. **Easier Testing** - Test orchestrator in isolation with mocked services
7. **Timeouts and Deadlines** - Orchestrator can enforce workflow timeouts
8. **Complex Flow Support** - Handles intricate workflows with branching, loops
9. **Transactional Consistency** - Easier to implement compensating transactions

### Disadvantages

1. **Single Point of Failure** - Orchestrator must be highly available
2. **Potential Bottleneck** - All coordination goes through orchestrator
3. **Tight Coupling** - Services couple to orchestrator's commands
4. **Orchestrator Complexity** - Can become a "God Object" with too much logic
5. **Less Service Autonomy** - Services become command executors
6. **Scaling Challenges** - Orchestrator must handle all workflow instances
7. **Deployment Coupling** - Orchestrator changes may require coordination

### When to Use Orchestration

**Best Suited For:**
- Complex workflows (5+ steps with branching, loops, conditionals)
- Business processes requiring strict ordering
- Workflows with compensating transactions/rollbacks
- Systems needing centralized monitoring and observability
- Time-sensitive workflows with timeouts/SLAs
- Workflows requiring human intervention or approval steps
- Services within same bounded context
- Teams requiring clear workflow documentation

**Example Use Cases:**
- Multi-step order fulfillment with inventory checks, payments, multiple shipping options
- Loan approval process with credit checks, verification, approvals
- Insurance claim processing with investigation, adjudication, payment
- Travel booking with flight, hotel, car rental coordination and cancellation policies
- Healthcare patient onboarding with registration, insurance verification, scheduling

---

## Detailed Comparison

| Aspect | Choreography | Orchestration |
|--------|-------------|---------------|
| **Control** | Decentralized | Centralized |
| **Communication** | Event-driven | Command-driven |
| **State Management** | Implicit (distributed) | Explicit (orchestrator) |
| **Coupling** | Loose | Tighter |
| **Complexity** | Distributed complexity | Centralized complexity |
| **Debugging** | Difficult (trace events) | Easier (single entry point) |
| **Testing** | Harder (integration tests) | Easier (unit test orchestrator) |
| **Scalability** | High (no bottleneck) | Limited (orchestrator scaling) |
| **Resilience** | High (no SPOF) | Moderate (orchestrator SPOF) |
| **Visibility** | Low (distributed flow) | High (central view) |
| **Error Handling** | Distributed compensation | Centralized compensation |
| **Workflow Changes** | Multiple service changes | Orchestrator change only |
| **Best For** | Simple flows, bounded contexts | Complex flows, same context |
| **Team Structure** | Independent teams | Single team |

---

## Industry Examples

### Choreography Implementations
- **Uber** - Ride matching and driver dispatch
- **Netflix** - Content encoding pipeline
- **Amazon** - Order processing events
- **Shopify** - E-commerce event streams

### Orchestration Implementations
- **Airbnb** - Booking workflow orchestration
- **Stripe** - Payment processing sagas
- **Temporal/Uber Cadence** - General-purpose orchestration
- **Azure Durable Functions** - Serverless orchestration
- **AWS Step Functions** - State machine orchestration

### .NET Messaging Frameworks

**MassTransit (Orchestration Support):**
- State machine sagas using Automatonymous
- Explicit state machine DSL
- Saga repositories (EF Core, MongoDB, Redis, etc.)
- Event correlation
- Saga state persistence

**NServiceBus (Both Patterns):**
- Saga support (process manager pattern)
- Choreography via publish/subscribe
- Saga timeout support
- Saga persistence

**Rebus (Both Patterns):**
- Lightweight saga support
- Event-driven workflows
- Saga data persistence

---

## Recommendations for HeroMessaging

### Short-Term: Support Both Patterns

HeroMessaging should support **both** choreography and orchestration patterns, as they solve different problems and many real-world systems use a hybrid approach.

### Implementation Strategy

#### Phase 1: Choreography Enhancement (Already 80% Complete)
**What Exists:**
- Event publishing via `IHeroMessaging.Publish(IEvent)`
- Multiple event handlers per event type
- Correlation ID support in TransportEnvelope
- Inbox/outbox for reliability

**What's Needed:**
1. **Formalize Correlation/Causation Tracking**
   - Add `CorrelationId` and `CausationId` to `IMessage` interface
   - Auto-populate in pipeline decorators
   - Propagate through handlers

2. **Event Choreography Documentation**
   - Provide samples for event-driven workflows
   - Best practices for avoiding cyclic dependencies
   - Distributed tracing integration

#### Phase 2: Orchestration Support (New Feature)
**Components to Build:**

1. **State Machine Abstraction**
```csharp
public interface IStateMachine<TState> where TState : class
{
    string Name { get; }
    IEnumerable<StateTransition<TState>> GetTransitions(TState current);
    Task<TState> TransitionAsync(TState current, IEvent @event);
}
```

2. **Saga/Process Manager Interface**
```csharp
public interface ISaga
{
    Guid CorrelationId { get; }
    string CurrentState { get; }
}

public interface ISagaHandler<TSaga, TEvent>
    where TSaga : ISaga
    where TEvent : IEvent
{
    Task HandleAsync(TSaga saga, TEvent @event);
}
```

3. **Saga Repository**
```csharp
public interface ISagaRepository<TSaga> where TSaga : class, ISaga
{
    Task<TSaga?> FindAsync(Guid correlationId);
    Task SaveAsync(TSaga saga);
    Task DeleteAsync(Guid correlationId);
    Task<IEnumerable<TSaga>> FindByStateAsync(string state);
}
```

4. **Saga Orchestrator**
```csharp
public class SagaOrchestrator<TSaga> where TSaga : class, ISaga
{
    public async Task ProcessEventAsync(IEvent @event)
    {
        var correlationId = ExtractCorrelationId(@event);
        var saga = await _repository.FindAsync(correlationId);

        if (saga == null && IsStartingEvent(@event))
        {
            saga = CreateNewSaga(@event);
        }

        if (saga != null)
        {
            await _handler.HandleAsync(saga, @event);
            await _repository.SaveAsync(saga);
        }
    }
}
```

5. **Fluent State Machine DSL** (Inspired by MassTransit)
```csharp
public class OrderSaga : StateMachineBuilder<OrderSagaData>
{
    public OrderSaga()
    {
        Initially(
            When(OrderCreated)
                .Then(ctx => ctx.Instance.OrderId = ctx.Data.OrderId)
                .TransitionTo(AwaitingPayment)
                .Send(new ProcessPaymentCommand())
        );

        During(AwaitingPayment,
            When(PaymentSucceeded)
                .TransitionTo(AwaitingFulfillment)
                .Send(new FulfillOrderCommand()),
            When(PaymentFailed)
                .TransitionTo(Cancelled)
                .Send(new CancelOrderCommand())
        );
    }
}
```

6. **Compensation Transaction Support**
```csharp
public interface ICompensatingAction
{
    Task CompensateAsync(object context);
}

public class SagaWithCompensation : ISaga
{
    private Stack<ICompensatingAction> _compensations = new();

    public async Task CompensateAsync()
    {
        while (_compensations.Count > 0)
        {
            var compensation = _compensations.Pop();
            await compensation.CompensateAsync(this);
        }
    }
}
```

### Recommended Architecture

```
HeroMessaging.Abstractions
├── Sagas/
│   ├── ISaga.cs
│   ├── ISagaRepository.cs
│   ├── ISagaHandler.cs
│   └── SagaStateMachineInstance.cs

HeroMessaging (Core)
├── Sagas/
│   ├── SagaOrchestrator.cs
│   ├── StateMachineBuilder.cs
│   ├── StateTransition.cs
│   └── CompensationManager.cs

HeroMessaging.Storage.{Provider}
├── {Provider}SagaRepository.cs  // Per storage provider

HeroMessaging.Tests
├── Sagas/
│   ├── SagaOrchestratorTests.cs
│   └── StateMachineBuilderTests.cs
```

### Configuration API

```csharp
// Choreography (existing)
services.AddHeroMessaging()
    .WithEventBus()
    .ScanAssembly(typeof(Program).Assembly);  // Auto-register event handlers

// Orchestration (new)
services.AddHeroMessaging()
    .WithSagas()
    .AddSaga<OrderSaga, OrderSagaData>()
    .UseSagaRepository<PostgreSqlSagaRepository>()
    .WithCompensation();
```

### Hybrid Approach Example

```csharp
// Simple workflow: Choreography
// User Registration Flow
UserCreatedEvent → EmailVerificationEvent → ProfileCreatedEvent

// Complex workflow: Orchestration
// Order Fulfillment Saga
OrderSaga:
    Created → ValidateInventory → ProcessPayment → AllocateInventory →
    CreateShipment → Shipped → Completed

    (With compensation on failure)
```

---

## Decision Guidelines for Teams

When implementing a workflow in HeroMessaging, ask:

1. **How many steps?**
   - 2-4 steps → Consider choreography
   - 5+ steps → Consider orchestration

2. **How complex is the logic?**
   - Linear flow → Choreography
   - Branching, loops, conditions → Orchestration

3. **Do you need compensation/rollback?**
   - Yes → Orchestration (easier to manage)
   - No → Either pattern works

4. **How critical is observability?**
   - Very critical → Orchestration
   - Less critical → Choreography

5. **Are services in same bounded context?**
   - Yes → Orchestration
   - No (cross-context) → Choreography

6. **Single team or multiple teams?**
   - Single team → Orchestration
   - Multiple teams → Choreography

7. **Performance requirements?**
   - High throughput, low latency → Choreography
   - Moderate, need consistency → Orchestration

---

## Next Steps

### Immediate Actions
1. **Formalize correlation tracking** - Add CorrelationId/CausationId to IMessage
2. **Create choreography examples** - Document event-driven workflow patterns
3. **Design saga abstractions** - ISaga, ISagaRepository, ISagaHandler interfaces

### Future Work
1. **Implement state machine DSL** - Fluent API for defining sagas
2. **Build saga persistence** - Repository implementations for SQL, PostgreSQL
3. **Add compensation framework** - Automatic compensation transaction support
4. **Saga timeouts** - Scheduled timeout events for long-running sagas
5. **Saga testing framework** - Test harness for saga state machines
6. **Observability integration** - Saga dashboards, metrics, tracing

### Research Required
1. **Saga versioning** - How to evolve saga definitions over time
2. **Saga migration** - Migrating in-flight sagas to new versions
3. **Saga archival** - Long-term storage and querying of completed sagas

---

## References

- [Saga Pattern - Chris Richardson](https://microservices.io/patterns/data/saga.html)
- [MassTransit State Machines](https://masstransit.io/documentation/patterns/saga/state-machine)
- [AWS Saga Orchestration Pattern](https://docs.aws.amazon.com/prescriptive-guidance/latest/cloud-design-patterns/saga-orchestration.html)
- [Event-driven.io: Saga and Process Manager](https://event-driven.io/en/saga_process_manager_distributed_transactions/)
- [Microsoft Azure: Saga Pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/saga)
- [DZone: Modeling Saga as State Machine](https://dzone.com/articles/modelling-saga-as-a-state-machine)

---

## Conclusion

Both choreography and orchestration patterns have valid use cases. HeroMessaging should support both:

- **Choreography** fits naturally with the existing event-driven architecture for simple, loosely-coupled workflows across bounded contexts
- **Orchestration** provides necessary structure for complex, stateful workflows requiring centralized coordination and compensation

A hybrid approach—using choreography for cross-context communication and orchestration within bounded contexts—provides the best balance of flexibility, maintainability, and performance.

The recommended implementation prioritizes:
1. Minimal breaking changes to existing API
2. Plugin-based saga repositories (following HeroMessaging patterns)
3. Fluent, developer-friendly DSL
4. Constitutional compliance: TDD, 80%+ coverage, <1ms overhead
5. Zero-allocation where possible (readonly structs, ValueTask)
