# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Development Commands

```bash
# Build all projects
dotnet build

# Build specific configuration
dotnet build --configuration Release

# Clean build artifacts
dotnet clean

# Restore NuGet packages
dotnet restore

# Run tests (when test projects are added)
dotnet test

# Pack NuGet packages
dotnet pack --configuration Release

# Build for specific framework
dotnet build -f net8.0
```

## Architecture Overview

HeroMessaging is a modular, plugin-based messaging library that unifies multiple messaging patterns (MediatR, Event Bus, Queuing, Outbox/Inbox) into one lightweight framework built on `System.Threading.Tasks.Dataflow`. The library includes comprehensive error handling with dead letter queue support and retry mechanisms.

### Core Design Principles

1. **Sequential vs Parallel Processing**
   - Commands/Queries: Sequential processing (one-by-one) for data consistency - implemented via `ActionBlock` with `MaxDegreeOfParallelism = 1`
   - Events: Parallel processing for performance - implemented via `ActionBlock` with `MaxDegreeOfParallelism = Environment.ProcessorCount`
   - Queues: Sequential per queue, multiple queues run in parallel

2. **Pattern Separation**
   - Each pattern (Mediator, EventBus, Queues, Outbox, Inbox) is independently opt-in via the builder pattern
   - Processors are only instantiated when their corresponding `With*()` method is called during configuration
   - Each processor checks for null dependencies and throws clear exceptions if features aren't enabled

3. **Multi-Framework Support**
   - Targets: netstandard2.0, net6.0, net7.0, net8.0, net9.0
   - Directory.Build.props centralizes framework configuration
   - Conditional compilation for framework-specific features (e.g., `Random.Shared` for net6.0+)
   - Different package versions for netstandard2.0 compatibility

### Project Structure

- **HeroMessaging.Abstractions**: Interfaces and contracts with zero implementation
  - Commands, Queries, Events, Messages
  - Handler interfaces (ICommandHandler, IQueryHandler, IEventHandler)
  - Storage abstractions (IMessageStorage, IOutboxStorage, IInboxStorage, IQueueStorage)
  - Plugin system (IMessagingPlugin)
  - Configuration (IHeroMessagingBuilder)

- **HeroMessaging.Core**: Concrete implementations
  - Processing: CommandProcessor, QueryProcessor, EventBus, QueueProcessor, OutboxProcessor, InboxProcessor
  - Storage: In-memory implementations for all storage interfaces
  - Configuration: HeroMessagingBuilder with fluent API
  - HeroMessagingService: Main facade that delegates to appropriate processors

### Processing Flow

1. **Command/Query Processing**: 
   - Messages queued in ActionBlock with bounded capacity (backpressure)
   - Handlers resolved from DI container at runtime
   - Results returned via TaskCompletionSource

2. **Event Processing**:
   - Multiple handlers per event type allowed
   - Each handler execution wrapped in separate ActionBlock task
   - Failures in one handler don't affect others

3. **Queue Processing**:
   - Each queue has dedicated QueueWorker with polling task
   - Priority-based dequeuing with visibility timeout
   - Automatic retry with exponential backoff

4. **Outbox Pattern**:
   - Persistent storage of messages for guaranteed delivery
   - Polling-based processor with configurable retry policies
   - Support for external system destinations

5. **Inbox Pattern**:
   - Idempotency through message deduplication
   - Configurable deduplication windows
   - Automatic cleanup of old processed entries

### Dependency Injection Integration

The library uses constructor injection throughout and registers all components as singletons for performance. Handler registration is done through assembly scanning with automatic interface detection.

### Storage Abstraction

All storage implementations follow the same pattern:
- Async-first API
- CancellationToken support throughout
- Query support with filtering, pagination, and ordering
- Thread-safe implementations using ConcurrentDictionary

### Error Handling Strategy

- **Dead Letter Queue**: Failed messages are automatically sent to DLQ after max retries
- **Retry Logic**: Exponential backoff with jitter for transient errors
- **Error Classification**: Automatic detection of transient vs permanent failures
- **Error Handler Interface**: Pluggable error handling with `IErrorHandler` and `IDeadLetterQueue`
- **Parallel Event Processing**: Events maintain parallel processing even with error handling
- **Contextual Information**: All errors include message IDs, component names, retry counts

## Key Implementation Details

### Dataflow Configuration
- BoundedCapacity prevents memory issues under load
- EnsureOrdered maintains FIFO for sequential processors
- Graceful shutdown via Complete() and Completion awaiting

### Handler Resolution
- Runtime type resolution using MakeGenericType
- Reflection-based method invocation with proper async handling
- Support for both void and result-returning handlers

### Multi-targeting Considerations
- GlobalUsings.cs provides using statements for netstandard2.0
- Nullable reference types enabled but with pragmas for compatibility
- Conditional package references based on target framework