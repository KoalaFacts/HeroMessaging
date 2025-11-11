# HeroMessaging Test Projects

This directory contains feature-based test projects organized by domain concern.

## Project Structure

### HeroMessaging.Tests.Shared
**Purpose**: Shared test infrastructure and utilities used across all test projects
**Dependencies**: None (references core libraries only)
**Contents**:
- Test message builders
- Test fixtures and helpers
- Common test patterns
- Shared test data and mocks

### HeroMessaging.Core.Tests (~5,000 LOC)
**Purpose**: Tests for core messaging processing infrastructure
**Test Categories**: Unit, Integration
**Key Areas**:
- Message processing (Command/Query processors, Event bus)
- Message validation (Composite validators)
- Plugin system
- Utilities (serialization, buffer pooling)
- Decorator patterns
- Pipeline integration tests

**Run tests**:
```bash
dotnet test tests/HeroMessaging.Core.Tests
```

### HeroMessaging.Storage.Tests (~3,000 LOC)
**Purpose**: Tests for storage implementations
**Test Categories**: Unit
**Key Areas**:
- InMemory message storage
- InMemory inbox storage
- InMemory outbox storage
- InMemory queue storage

**Run tests**:
```bash
dotnet test tests/HeroMessaging.Storage.Tests
```

### HeroMessaging.Policies.Tests (~1,500 LOC)
**Purpose**: Tests for retry and circuit breaker policies
**Test Categories**: Unit
**Key Areas**:
- Exponential backoff retry policy
- Linear retry policy
- No-retry policy
- Circuit breaker retry policy

**Run tests**:
```bash
dotnet test tests/HeroMessaging.Policies.Tests
```

### HeroMessaging.ErrorHandling.Tests (~1,200 LOC)
**Purpose**: Tests for error handling infrastructure
**Test Categories**: Unit
**Key Areas**:
- Default error handler
- Dead letter queue (InMemory)
- Error classification and handling

**Run tests**:
```bash
dotnet test tests/HeroMessaging.ErrorHandling.Tests
```

### HeroMessaging.Configuration.Tests (~3,000 LOC)
**Purpose**: Tests for configuration and dependency injection
**Test Categories**: Unit
**Key Areas**:
- HeroMessaging builder (fluent API)
- Service registration and configuration
- Builder patterns
- HeroMessaging service lifecycle

**Run tests**:
```bash
dotnet test tests/HeroMessaging.Configuration.Tests
```

### HeroMessaging.Idempotency.Tests (~2,500 LOC)
**Purpose**: Tests for idempotency framework (exactly-once semantics)
**Test Categories**: Unit, Integration
**Key Areas**:
- Idempotency decorators
- Idempotency stores (InMemory)
- Key generation strategies
- Policy configuration
- Response caching
- Exception classification

**Run tests**:
```bash
dotnet test tests/HeroMessaging.Idempotency.Tests
```

### HeroMessaging.Orchestration.Tests (~7,500 LOC)
**Purpose**: Tests for saga orchestration and state machines
**Test Categories**: Unit, Integration
**Key Areas**:
- Saga orchestrators
- State machines
- State transitions
- Compensation logic
- Saga repositories (InMemory)
- Timeout handling
- Event triggers

**Run tests**:
```bash
dotnet test tests/HeroMessaging.Orchestration.Tests
```

### HeroMessaging.Choreography.Tests (~500 LOC)
**Purpose**: Tests for event-driven choreography workflows
**Test Categories**: Unit, Integration
**Key Areas**:
- Correlation context
- Message correlation
- Event-driven workflows

**Run tests**:
```bash
dotnet test tests/HeroMessaging.Choreography.Tests
```

### HeroMessaging.Features.Tests (~5,000 LOC)
**Purpose**: Tests for additional features (batch, rate limiting, scheduling, transport)
**Test Categories**: Unit
**Key Areas**:
- **Batch Processing**: Batch decorators, batch processing logic
- **Rate Limiting**: Token bucket rate limiter, rate limiting decorators
- **Scheduling**: Message scheduling (InMemory)
- **Transport**: InMemory transport, instrumentation

**Run tests**:
```bash
dotnet test tests/HeroMessaging.Features.Tests
```

### HeroMessaging.Processing.Decorators.Tests (~7,500 LOC)
**Purpose**: Tests for processing pipeline decorators (cross-cutting concerns)
**Test Categories**: Unit
**Key Areas**:
- Circuit breaker
- Retry logic
- Validation
- Logging
- Metrics collection
- Error handling
- Correlation context

**Run tests**:
```bash
dotnet test tests/HeroMessaging.Processing.Decorators.Tests
```

## Running All Tests

```bash
# Run all tests across all projects
dotnet test

# Run tests by category
dotnet test --filter Category=Unit
dotnet test --filter Category=Integration

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Generate coverage report
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report
```

## Legacy Project

### HeroMessaging.Tests (DEPRECATED)
**Status**: ⚠️ This project has been split into multiple feature-based projects (above)
**Action Required**: Please use the new feature-based test projects instead

## Test Organization Principles

1. **Feature-Based Structure**: Tests are organized by feature domain (idempotency, orchestration, etc.) rather than technical layer
2. **Self-Contained**: Each test project is independently buildable and executable
3. **Shared Utilities**: Common test helpers are in HeroMessaging.TestUtilities
4. **Consistent Naming**: All test projects follow `HeroMessaging.{Feature}.Tests` pattern
5. **Constitutional Compliance**: All projects follow TDD principles with 80%+ coverage target

## Adding New Tests

When adding new tests:

1. **Determine the domain**: Identify which feature domain your test belongs to
2. **Add to appropriate project**: Place tests in the correct feature-based project
3. **Use shared utilities**: Leverage HeroMessaging.Tests.Shared for common patterns
4. **Follow conventions**: Use `[Trait("Category", "Unit|Integration")]` attributes
5. **Maintain coverage**: Ensure 80%+ coverage for all public APIs

## Benefits of Feature-Based Structure

- **Faster CI/CD**: Parallel test execution per domain
- **Clearer boundaries**: Domain isolation and reduced coupling
- **Better navigation**: Easy to find tests for specific features
- **Selective testing**: Run only relevant test suites during development
- **Reduced cognitive load**: Each project focuses on one domain
