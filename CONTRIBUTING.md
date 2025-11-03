# Contributing to HeroMessaging

Thank you for your interest in contributing to HeroMessaging! We welcome contributions of all kinds: bug reports, feature requests, documentation improvements, and code contributions.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Workflow](#development-workflow)
- [Coding Standards](#coding-standards)
- [Testing Requirements](#testing-requirements)
- [Performance Standards](#performance-standards)
- [Documentation](#documentation)
- [Pull Request Process](#pull-request-process)
- [Architecture Decisions](#architecture-decisions)

## Code of Conduct

This project adheres to a code of conduct that we expect all contributors to follow:

- Be respectful and inclusive
- Focus on constructive feedback
- Assume good intentions
- Help create a welcoming environment for all

## Getting Started

### Prerequisites

- .NET 6.0 SDK or higher
- Git
- Docker (for integration tests)
- Your favorite IDE (Visual Studio, VS Code, Rider)

### Setting Up Your Development Environment

1. **Fork and clone the repository:**
   ```bash
   git clone https://github.com/YOUR-USERNAME/HeroMessaging.git
   cd HeroMessaging
   ```

2. **Restore dependencies:**
   ```bash
   dotnet restore
   ```

3. **Build the project:**
   ```bash
   dotnet build
   ```

4. **Run tests to verify setup:**
   ```bash
   dotnet test
   ```

## Development Workflow

HeroMessaging follows a **Test-Driven Development (TDD)** approach:

### 1. Write Tests First

Before implementing any feature, write tests that define the expected behavior:

```csharp
[Fact]
[Trait("Category", "Unit")]
public void NewFeature_WhenConditionMet_ShouldBehaveCorrectly()
{
    // Arrange
    var sut = new SystemUnderTest();

    // Act
    var result = sut.NewFeature();

    // Assert
    Assert.True(result.Success);
}
```

### 2. Implement Minimal Code

Write the simplest code that makes the test pass.

### 3. Refactor

Improve code quality while keeping all tests green:
- Follow SOLID principles
- Keep methods under 20 lines
- Keep cyclomatic complexity under 10
- Use meaningful names

### 4. Verify Coverage

Ensure your changes maintain coverage standards:
```bash
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report
```

**Coverage Requirements:**
- **Public APIs**: 100% coverage (constitutional requirement)
- **Overall Project**: 80% minimum coverage

### 5. Run Performance Benchmarks

If your change affects performance-critical paths:
```bash
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release
```

Performance regressions >10% from baseline will fail CI.

## Coding Standards

### Code Organization

- **Primary Constructors**: Use C# 12 primary constructor syntax
- **File Organization**: One type per file
- **Namespaces**: Match folder structure
- **Extension Classes**: Named `ExtensionsTo{TargetType}` in target namespace

### Naming Conventions

- **Classes/Interfaces**: PascalCase
- **Methods/Properties**: PascalCase
- **Local Variables**: camelCase
- **Private Fields**: _camelCase with underscore
- **Test Methods**: `MethodName_Scenario_ExpectedOutcome`

### Code Quality Standards

```csharp
// GOOD: Clear, focused method
public async Task<Result> ProcessOrderAsync(Order order)
{
    if (!order.IsValid())
        return Result.Invalid("Order validation failed");

    await _repository.SaveAsync(order);
    return Result.Success();
}

// BAD: Method too long, unclear responsibility
public async Task<Result> ProcessOrderAndDoEverythingElse(Order order)
{
    // 50+ lines of mixed concerns...
}
```

**Quality Metrics:**
- Methods ≤20 lines
- Cyclomatic complexity ≤10
- No compiler warnings
- All public APIs have XML documentation

### Error Handling

All exceptions should provide actionable error messages:

```csharp
public class ProcessingException : Exception
{
    public string ErrorCode { get; }
    public string[] RemediationSteps { get; }

    public ProcessingException(string errorCode, string message, string[] remediation)
        : base($"[{errorCode}] {message}")
    {
        ErrorCode = errorCode;
        RemediationSteps = remediation;
    }
}
```

## Testing Requirements

### Test Categories

Use traits to categorize tests:

```csharp
[Trait("Category", "Unit")]        // Fast, isolated tests (<30s total)
[Trait("Category", "Integration")] // Database/service integration (<2min total)
[Trait("Category", "Contract")]    // API contract validation
[Trait("Category", "Performance")] // Performance benchmarks
```

### Test Structure

Follow the **Arrange-Act-Assert** pattern:

```csharp
[Fact]
[Trait("Category", "Unit")]
public void SagaOrchestrator_WhenEventReceived_ShouldTransitionState()
{
    // Arrange
    var saga = new TestSaga { State = "Initial" };
    var evt = new TestEvent();
    var orchestrator = new SagaOrchestrator<TestSaga>(/* deps */);

    // Act
    await orchestrator.HandleAsync(saga, evt);

    // Assert
    Assert.Equal("NextState", saga.State);
}
```

### Testing Framework

- **Required**: Xunit.v3 exclusively
- **Mocking**: Moq or NSubstitute
- **Assertions**: Xunit assertions only (no FluentAssertions)

### Integration Tests

Integration tests should:
- Use real dependencies where possible
- Clean up resources (implement `IDisposable` or use `using` statements)
- Be isolated (can run in parallel without conflicts)
- Complete within 2 minutes total

## Performance Standards

HeroMessaging has strict performance requirements:

### Latency Targets
- **Message Processing**: <1ms p99 overhead
- **Saga State Transitions**: <1ms p99

### Throughput Targets
- **Message Bus**: >100K messages/second (single-threaded)

### Memory Targets
- **Per Message**: <1KB allocation in steady state
- **Zero-allocation paths** where possible

### Benchmark Changes

If your PR affects performance:
1. Run benchmarks before and after your changes
2. Document results in PR description
3. Performance regressions >10% require justification

## Documentation

### XML Documentation

All public APIs **must** have comprehensive XML documentation:

```csharp
/// <summary>
/// Processes a message asynchronously through the messaging pipeline.
/// </summary>
/// <param name="message">The message to process.</param>
/// <param name="cancellationToken">Cancellation token for the operation.</param>
/// <returns>A task representing the asynchronous operation.</returns>
/// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
public async Task ProcessAsync(IMessage message, CancellationToken cancellationToken)
{
    // Implementation...
}
```

### Architecture Decisions

Significant design decisions should be documented as Architecture Decision Records (ADRs):

1. Create a new file in `docs/adr/`
2. Use the format: `NNNN-decision-title.md`
3. Include: Context, Decision, Consequences, Alternatives Considered

See existing ADRs for examples:
- [docs/adr/0002-transport-abstraction-layer.md](docs/adr/0002-transport-abstraction-layer.md)
- [docs/adr/0004-saga-patterns.md](docs/adr/0004-saga-patterns.md)

## Pull Request Process

### Before Submitting

1. **Run all tests locally:**
   ```bash
   dotnet test
   ```

2. **Verify code quality:**
   ```bash
   dotnet build /p:TreatWarningsAsErrors=true
   ```

3. **Check coverage:**
   ```bash
   dotnet test --collect:"XPlat Code Coverage"
   ```

4. **Run benchmarks (if applicable):**
   ```bash
   dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release
   ```

### PR Guidelines

1. **Title Format:**
   - `feat: Add saga timeout monitoring`
   - `fix: Correct race condition in message processing`
   - `docs: Update README with new examples`
   - `perf: Optimize message serialization`
   - `refactor: Extract duplicate compression logic`

2. **Description Requirements:**
   - Clear description of the problem being solved
   - Explanation of the solution approach
   - Breaking changes (if any) with migration guide
   - Performance impact (if applicable)
   - Screenshots/examples (if relevant)

3. **Checklist:**
   - [ ] Tests written first (TDD)
   - [ ] All tests passing
   - [ ] Coverage ≥80% (100% for public APIs)
   - [ ] No compiler warnings
   - [ ] XML documentation on public APIs
   - [ ] Performance benchmarks run (if applicable)
   - [ ] CHANGELOG.md updated
   - [ ] ADR created (if architectural change)

### CI Requirements

All PRs must pass:
- **Build**: No errors or warnings
- **Tests**: All categories (Unit, Integration, Contract)
- **Coverage**: Meets 80% threshold
- **Performance**: No regression >10%
- **Cross-Platform**: Windows, Linux, macOS
- **Multi-Framework**: netstandard2.0, net6.0, net7.0, net8.0, net9.0

## Architecture Decisions

### Plugin Architecture

HeroMessaging uses a plugin architecture to isolate concerns:

```
HeroMessaging (Core)
├── HeroMessaging.Abstractions (Contracts)
├── HeroMessaging.Storage.* (Storage Plugins)
├── HeroMessaging.Serialization.* (Serialization Plugins)
├── HeroMessaging.Transport.* (Transport Plugins)
└── HeroMessaging.Observability.* (Observability Plugins)
```

**Key Principles:**
- Core library has minimal dependencies
- Plugins encapsulate external dependencies
- Each plugin is independently testable
- Use builder pattern for configuration

### Multi-Framework Support

HeroMessaging supports multiple .NET versions:
- netstandard2.0 (broad compatibility)
- net6.0 (LTS)
- net7.0 (STS)
- net8.0 (LTS)
- net9.0 (STS)

Use conditional compilation and polyfills (PolySharp) for API differences.

## Getting Help

- **Questions?** [Open a discussion](https://github.com/KoalaFacts/HeroMessaging/discussions)
- **Bug reports?** [Create an issue](https://github.com/KoalaFacts/HeroMessaging/issues)
- **Need guidance?** Review [CLAUDE.md](CLAUDE.md) for detailed development guidelines

## Recognition

Contributors will be recognized in:
- GitHub contributors list
- Release notes for significant contributions
- Project documentation

Thank you for contributing to HeroMessaging! 🎉
