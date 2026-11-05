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

### Architecture Tests
- **Scope**: Dependency and layering rules (NetArchTest), trait `Category=Architecture`
- **Location**: `tests/HeroMessaging.Architecture.Tests/`

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
dotnet test --filter-trait "Category=Unit"
dotnet test --filter-trait "Category=Integration"

# Run with coverage
dotnet test --project tests/HeroMessaging.Tests --filter-not-trait Category=Integration --coverlet --coverlet-output-format cobertura

# Run benchmarks
dotnet run --project tests/HeroMessaging.Benchmarks -c Release
```

### Framework-Specific Testing
```bash
# Test on specific .NET version
dotnet test --framework net9.0
dotnet test --framework net8.0
dotnet test --framework net10.0
```

### Parallel Execution
Tests are designed to run in parallel for faster feedback:
```bash
# Run with maximum parallelism
dotnet test --max-parallel-test-modules 8
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
    var message = TestMessageBuilder.CreateValidMessage();

    // Act
    var result = await processor.ProcessAsync(message);

    // Assert
    Assert.True(result.Success);
    Assert.NotNull(result.ProcessedMessage);
}
```

### 2. Run Test (Should Fail)
```bash
dotnet test --filter-method "*ProcessMessage_WithValidInput*"
```

### 3. Implement Feature
Write minimal code to make the test pass.

### 4. Run Test (Should Pass)
```bash
dotnet test --filter-method "*ProcessMessage_WithValidInput*"
```

### 5. Refactor
Improve code quality while keeping tests green.

## Coverage Analysis

### Generate Coverage Report
```bash
# Collect coverage data
dotnet test --project tests/HeroMessaging.Tests --filter-not-trait Category=Integration --coverlet --coverlet-output-format cobertura --results-directory ./TestResults

# Generate HTML report
reportgenerator -reports:"TestResults/**/coverage.cobertura*.xml" \
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
- **ci.yml** (Build and Test): unit-test matrix (Ubuntu/Windows/macOS × net8.0/net9.0/net10.0), NativeAOT package consumer, in-process performance comparison, coverage and quality gates, release package build
- **run-integration-tests.yml**: PostgreSQL, SQL Server and RabbitMQ integration tests (called from ci.yml)
- **codeql.yml**, **security-scan.yml**: CodeQL and dependency/secret scanning
- **profile-inprocess.yml**: native in-process profiling (manual)
- **create-release.yml**, **publish-nuget.yml**: release and publishing (maintainers only)

### Running CI Locally
```bash
# Simulate CI test run
act -j unit-tests
```

## Troubleshooting

### Common Issues

#### Tests Timing Out
```bash
# Increase timeout for slow tests
dotnet test --timeout 5m
```

#### Flaky Tests
```bash
# Run test multiple times to identify flakiness
for i in {1..10}; do
    dotnet test --filter-method "*FlakyTest*"
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
dotnet test --output Detailed

# Capture diagnostic data
dotnet test --diagnostic --diagnostic-output-directory ./TestLogs

# For faster iterations when debugging specific tests
dotnet build  # Build once
dotnet test --no-build --filter-method "*YourTestName*" --output Detailed
```

## Test Utilities

### TestMessageBuilder
```csharp
// tests/HeroMessaging.Tests.Shared/TestMessageBuilder.cs
var message = TestMessageBuilder.CreateValidMessage(content: "test data", correlationId: "corr-1");
```

### PluginTestBase
```csharp
// tests/HeroMessaging.Tests/Infrastructure/PluginTestBase.cs
public class MyPluginTests(ITestOutputHelper output) : PluginTestBase<IMyPlugin>(output)
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task Plugin_Starts()
    {
        var plugin = await CreatePluginAsync<MyPlugin>();
        Assert.NotNull(plugin);
    }
}
```

### Database services
Integration tests run against real PostgreSQL, SQL Server and RabbitMQ. The Saga and RabbitMQ fixtures start Testcontainers unless you set `PostgreSql__ConnectionString`, `SqlServer__ConnectionString` or `RabbitMq__Host` to point them at services you provide (as CI does). See [CONTRIBUTING.md](../CONTRIBUTING.md#integration-tests) for the database rules.

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