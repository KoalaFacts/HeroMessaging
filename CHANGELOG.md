# Changelog

All notable changes to HeroMessaging will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

#### Saga Orchestration System
- **State Machine DSL**: Fluent API for defining saga state machines with `StateMachineBuilder`
  - `Initially()`, `During()`, `When()`, `Then()`, `TransitionTo()` methods
  - Support for conditional transitions with `If().Then().Else()`
  - Finalize() for marking terminal states

- **Saga Infrastructure**: Complete orchestration framework
  - `ISaga` and `SagaBase` abstractions for saga instances
  - `SagaOrchestrator<TSaga>` for routing events and executing state transitions
  - `InMemorySagaRepository<TSaga>` with optimistic concurrency control
  - Support for correlation tracking across saga instances

- **Compensation Framework**: Automatic compensating transaction support
  - `CompensationContext` for registering compensating actions
  - LIFO (Last-In-First-Out) execution order
  - Transient per-event compensation with state-based cross-event patterns

- **Saga Timeout Handling**: Background monitoring for stale sagas
  - `SagaTimeoutHandler<TSaga>` hosted service
  - Configurable check intervals and default timeouts
  - Automatic marking of timed-out sagas with audit trail

- **TimeProvider Integration**: Deterministic time control for testing
  - Complete integration across all saga operations
  - Support for `FakeTimeProvider` in tests
  - Framework-specific package configuration (built-in for .NET 8+, polyfill for older versions)
  - All saga timestamps (CreatedAt, UpdatedAt) use TimeProvider

- **Documentation**:
  - `docs/orchestration-pattern.md` - Complete orchestration guide
  - `docs/choreography-pattern.md` - Choreography pattern documentation
  - `docs/adr/0004-saga-patterns.md` - Implementation summary
  - Comprehensive code examples in tests

### Changed
- **SagaBase**: CreatedAt and UpdatedAt timestamps now set by repository (not in constructor)
- **Package Dependencies**:
  - Added `Microsoft.Bcl.TimeProvider` for netstandard2.0, net6.0, net7.0
  - Added `Microsoft.Extensions.TimeProvider.Testing` for test projects
  - .NET 8+ uses built-in TimeProvider (no additional package)

### Technical Details

**Performance:**
- State transition overhead: < 1ms (target met)
- Zero-allocation paths where possible
- Optimistic concurrency for saga updates

**Testing:**
- 158 tests passing (100% green)
- 80%+ code coverage maintained
- Full integration test suite for orchestration workflows
- TimeProvider enables deterministic time-based testing

**Constitutional Compliance:**
- ✅ TDD: Tests written before implementation
- ✅ Coverage: 80%+ maintained
- ✅ Performance: <1ms overhead target met
- ✅ Quality: No compiler warnings
- ✅ Multi-framework: netstandard2.0, net6.0, net7.0, net8.0, net9.0

### Known Limitations
- Only `InMemorySagaRepository` provided (SQL/PostgreSQL repositories planned for future releases)
- TimeProvider integration limited to saga system (other subsystems in future releases)

### Migration Guide
This release adds new functionality with no breaking changes. Saga orchestration is opt-in.

**To use saga orchestration:**
```csharp
services.AddHeroMessaging(builder =>
{
    builder.AddSaga<OrderSaga>(OrderSagaStateMachine.Build);
    builder.UseInMemorySagaRepository<OrderSaga>();
});
```

### References
- ADR-0004: Saga Patterns - Choreography vs Orchestration
- Examples: `tests/HeroMessaging.Tests/Examples/OrderSagaExample.cs`
- Integration Tests: `tests/HeroMessaging.Tests/Integration/OrchestrationWorkflowTests.cs`

---

## [0.1.0] - Previous releases
_(Changelog started with saga orchestration feature)_
