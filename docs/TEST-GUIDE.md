# HeroMessaging Test Suite Guide

## Overview

The HeroMessaging library includes a comprehensive test suite designed to ensure reliability, performance, and cross-platform compatibility. This guide covers test execution, development practices, and troubleshooting.

## Test Categories

### Unit Tests
- **Scope**: Individual components in isolation
- **Target Time**: < 30 seconds
- **Coverage Goal**: 80% minimum
- **Location**: `tests/HeroMessaging.Tests/Unit/`

### Integration Tests
- **Scope**: Component interactions and plugin systems
- **Target Time**: < 2 minutes
- **Coverage Goal**: Complete workflow validation
- **Location**: `tests/HeroMessaging.Tests/Integration/`

### Contract Tests
- **Scope**: Public API validation
- **Target Time**: < 30 seconds
- **Coverage Goal**: 100% of public APIs
- **Location**: `tests/HeroMessaging.Contract.Tests/`

### Performance Benchmarks
- **Scope**: Latency and throughput validation
- **Targets**: <1ms p99 latency, >100K msg/s
- **Location**: `tests/HeroMessaging.Benchmarks/`

## Running Tests

### Quick Start
```bash
# Run all tests
dotnet test

# Run specific category
dotnet test --filter Category=Unit
dotnet test --filter Category=Integration
dotnet test --filter Category=Contract

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run benchmarks
dotnet run --project tests/HeroMessaging.Benchmarks -c Release
```

### Framework-Specific Testing
```bash
# Test on specific .NET version
dotnet test --framework net6.0
dotnet test --framework net8.0
dotnet test --framework net10.0

# Cross-platform validation
pwsh tests/validate-cross-platform.ps1
```

### Parallel Execution
Tests are designed to run in parallel for faster feedback:
```bash
# Run with maximum parallelism
dotnet test --parallel --max-parallel-threads 8
```

## Test Development (TDD Workflow)

### 1. Write Test First
```csharp
[Fact]
[Trait("Category", "Unit")]
public async Task ProcessMessage_WithValidInput_ShouldSucceed()
{
    // Arrange
    var processor = new MessageProcessor();
    var message = TestMessageBuilder.Create().Build();

    // Act
    var result = await processor.ProcessAsync(message);

    // Assert
    Assert.True(result.Success);
    Assert.NotNull(result.ProcessedMessage);
}
```

### 2. Run Test (Should Fail)
```bash
dotnet test --filter "FullyQualifiedName~ProcessMessage_WithValidInput"
```

### 3. Implement Feature
Write minimal code to make the test pass.

### 4. Run Test (Should Pass)
```bash
dotnet test --filter "FullyQualifiedName~ProcessMessage_WithValidInput"
```

### 5. Refactor
Improve code quality while keeping tests green.

## Coverage Analysis

### Generate Coverage Report
```bash
# Collect coverage data
dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults

# Generate HTML report
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" \
                -targetdir:"coveragereport" \
                -reporttypes:Html
```

### View Coverage Report
Open `coveragereport/index.html` in your browser.

### Coverage Requirements
- **Minimum**: 80% overall coverage
- **Public APIs**: 100% coverage required
- **Exclusions**:
  - Code marked with `[ExcludeFromCodeCoverage]`
  - Auto-generated code
  - Debug-only utilities

## Performance Testing

### Running Benchmarks
```bash
# Build first, then run benchmarks
dotnet build -c Release
dotnet run -c Release --project tests/HeroMessaging.Benchmarks --no-build

# Run specific benchmark
dotnet run -c Release --project tests/HeroMessaging.Benchmarks --no-build -- --filter "*MessageProcessing*"

# Generate detailed results
dotnet run -c Release --project tests/HeroMessaging.Benchmarks --no-build -- --exporters html json
```

### Performance Targets
- **Latency**: < 1ms p99 (99th percentile)
- **Throughput**: > 100,000 messages/second
- **Memory**: < 1KB allocation per message
- **Regression Tolerance**: < 10% performance drop

## CI/CD Integration

### GitHub Actions Workflows
- **test.yml**: Cross-platform test execution
- **coverage.yml**: Coverage analysis and reporting
- **performance.yml**: Performance benchmark validation
- **test-matrix.yml**: Multi-framework testing

### Running CI Locally
```bash
# Simulate CI test run
act -j test

# Run with specific .NET version
act -j test -e '{"matrix":{"dotnet":["8.0"]}}'
```

## Troubleshooting

### Common Issues

#### Tests Timing Out
```bash
# Increase timeout for slow tests
dotnet test --blame-hang-timeout 5m
```

#### Flaky Tests
```bash
# Run test multiple times to identify flakiness
for i in {1..10}; do
    dotnet test --filter "FullyQualifiedName~FlakyTest"
done
```

#### Database Container Issues
```bash
# Check Docker status
docker ps -a

# Clean up test containers
docker container prune -f
```

### Debug Output
```bash
# Enable detailed logging (will build automatically)
dotnet test --logger:"console;verbosity=detailed"

# Capture diagnostic data
dotnet test --diag:testlog.txt

# For faster iterations when debugging specific tests
dotnet build  # Build once
dotnet test --no-build --filter "FullyQualifiedName~YourTestName" --logger:"console;verbosity=detailed"
```

## Test Utilities

### TestMessageBuilder
```csharp
var message = TestMessageBuilder.Create()
    .WithId(Guid.NewGuid())
    .WithPayload("test data")
    .WithMetadata("key", "value")
    .Build();
```

### PluginTestBase
```csharp
public class MyPluginTests : PluginTestBase<IMyPlugin>
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<IMyPlugin, MyPlugin>();
    }
}
```

### Database Test Containers
```csharp
await using var container = new PostgreSqlTestContainer();
await container.StartAsync();
var connectionString = container.ConnectionString;
// Run tests with database
```

## Best Practices

### Test Naming
- Use descriptive names: `MethodName_Scenario_ExpectedOutcome`
- Group related tests in nested classes
- Use traits for categorization

### Test Isolation
- Each test should be independent
- Clean up resources in `Dispose()` or `finally` blocks
- Use fresh test data for each test

### Assertion Best Practices
- One logical assertion per test
- Use specific assertions (`Assert.Equal` vs `Assert.True`)
- Include meaningful failure messages

### Performance Test Guidelines
- Warm up before measurements
- Run multiple iterations
- Test with realistic data sizes
- Compare against baselines

## Contributing

When adding new features:

1. **Write tests first** (TDD requirement)
2. **Ensure 80%+ coverage** for new code
3. **Add performance benchmarks** for critical paths
4. **Update this documentation** as needed
5. **Run cross-platform validation** before submitting PR

## Resources

- [Xunit Documentation](https://xunit.net/)
- [BenchmarkDotNet Guide](https://benchmarkdotnet.org/)
- [Coverlet Documentation](https://github.com/coverlet-coverage/coverlet)
- [Test Containers](https://testcontainers.org/)