# HeroMessaging Development Guidelines for Claude Code

Development guidelines for Claude Code and AI assistants. Last updated: 2025-11-02

## Constitutional Principles
1. **Code Quality & Maintainability**: SOLID principles, clear naming, low complexity
2. **Testing Excellence**: TDD, 80%+ coverage, Xunit.v3 exclusively
3. **User Experience Consistency**: Intuitive APIs, actionable errors, semantic versioning
4. **Performance & Efficiency**: <1ms overhead, zero-allocation paths, 100K+ msg/s
5. **Architectural Governance**: ADRs for decisions, plugin architecture, multi-framework support
6. **Task Verification Protocol**: Every task must be verified before marking as finished

## Active Technologies
- **Language**: C# 12, .NET multi-target (netstandard2.0, net6.0-net10.0)
- **Testing**: Xunit.v3, BenchmarkDotNet, Coverlet, ReportGenerator
- **Build**: MSBuild, Directory.Build.props for shared configuration
- **CI/CD**: GitHub Actions with cross-platform matrix testing
- **Dependencies**: Microsoft.Extensions.* (core), PolySharp (polyfills)

## Project Structure
```
src/
├── HeroMessaging/                          # Core library
├── HeroMessaging.Abstractions/             # Interfaces and contracts
├── HeroMessaging.Storage.{Provider}/       # Storage plugins
├── HeroMessaging.Serialization.{Format}/   # Serialization plugins
└── HeroMessaging.Observability.{Tool}/     # Observability plugins

tests/
├── HeroMessaging.Tests/                    # Unit and integration tests
├── HeroMessaging.Benchmarks/               # Performance benchmarks
├── HeroMessaging.Contract.Tests/           # API contract tests
└── Plugin.Tests/                           # Plugin-specific tests

.github/workflows/                          # CI/CD automation
```

## Commands
```bash
# Build and test (fast feedback)
dotnet build
dotnet test --filter Category=Unit         # <30s target
dotnet test --filter Category=Integration  # <2min target
dotnet test                                 # <5min full suite

# Coverage analysis
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report

# Performance benchmarks
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release

# Multi-framework testing
dotnet test --framework net6.0
dotnet test --framework netstandard2.0
```

## Code Style & Quality Standards

### Constitutional Requirements
- **Testing Framework**: Xunit.v3 exclusively (no FluentAssertions)
- **TDD Approach**: Tests written before implementation
- **Coverage Target**: 80% minimum (100% for public APIs)
- **Performance**: <1ms p99 latency, >100K msg/s throughput
- **Quality Gates**: Coverage thresholds, performance regression detection (10% tolerance)

### Naming Conventions
- **Extension Classes**: `ExtensionsTo{TargetType}` in target namespace
- **Test Classes**: `{ClassUnderTest}Tests` with descriptive test method names
- **Benchmark Classes**: `{Feature}Benchmarks` with `[Benchmark]` methods
- **Categories**: Use `[Trait("Category", "Unit|Integration|Contract|Performance")]`

### Code Organization
- **Primary Constructors**: Use C# 12 syntax for services and decorators
- **Plugin Architecture**: Separate packages for different concerns
- **Decorator Pattern**: For cross-cutting concerns (logging, retry, metrics)
- **Builder Pattern**: Fluent configuration APIs
- **Error Handling**: Actionable error messages with remediation steps

### Test Patterns
```csharp
// Unit test structure
[Fact]
[Trait("Category", "Unit")]
public void MethodName_Scenario_ExpectedOutcome()
{
    // Arrange
    var dependency = new Mock<IDependency>();
    var sut = new SystemUnderTest(dependency.Object);

    // Act
    var result = sut.MethodName(input);

    // Assert
    Assert.True(result.Success);
    dependency.Verify(x => x.Method(), Times.Once);
}

// Performance benchmark structure
[Benchmark]
[BenchmarkCategory("MessageProcessing")]
public async Task ProcessMessage_Latency()
{
    await _processor.ProcessAsync(_testMessage);
}

// Integration test structure
[Fact]
[Trait("Category", "Integration")]
public async Task EndToEnd_WithRealDependencies_WorksCorrectly()
{
    // Use real services, test component interactions
    // Ensure proper cleanup in Dispose or using statements
}
```

### Performance Standards
- **Latency Targets**: <1ms p99 for message processing overhead
- **Throughput Targets**: >100K messages/second single-threaded capability
- **Memory Targets**: <1KB allocation per message in steady state
- **Regression Detection**: Fail CI if performance drops >10% from baseline
- **Benchmarking**: Use BenchmarkDotNet for all performance claims

### Documentation Requirements
- **XML Documentation**: All public APIs must have comprehensive XML docs
- **Error Messages**: Include error code, message, and resolution steps
- **Test Documentation**: Test names should be self-documenting
- **Architecture Decisions**: Document significant decisions in ADR format

## Development Workflow

### Test-Driven Development
1. **Write Test First**: Constitutional requirement, test must fail initially
2. **Implement Minimum**: Write minimal code to make test pass
3. **Refactor**: Improve code quality while keeping tests green
4. **Coverage Check**: Ensure 80% coverage minimum, 100% for public APIs
5. **Task Verification**: Verify deliverable meets requirements before marking task complete

### Quality Assurance
- **Code Review**: All code requires peer review with constitutional compliance check
- **Automated Gates**: Coverage, performance, and quality thresholds enforced in CI
- **Cross-Platform**: Test on Windows, Linux, macOS with all supported .NET versions
- **Plugin Isolation**: Each plugin must be independently testable

### Performance Monitoring
- **Continuous Benchmarking**: Performance tests run on every PR
- **Baseline Management**: Track performance trends, update baselines carefully
- **Regression Alerts**: Automated alerts for performance degradation >10%
- **Comparison Standards**: Benchmark against gRPC/HTTP2 performance levels

## Recent Changes
1. **Test Suite Implementation**: Comprehensive testing infrastructure with 80%+ coverage
2. **Performance Benchmarking**: BenchmarkDotNet integration with regression detection
3. **Cross-Platform CI**: GitHub Actions matrix testing across all supported platforms
4. **Constitutional Compliance**: Quality gates enforcing testing excellence principles

## Plugin Development Guidelines
- **Isolation**: Each plugin package must be independently buildable and testable
- **Dependencies**: External dependencies isolated to plugin packages only
- **Configuration**: Use builder pattern for plugin configuration
- **Testing**: Plugin-specific test projects with integration test coverage
- **Documentation**: Each plugin needs quickstart guide and API documentation

## Error Handling Patterns
```csharp
// Constitutional requirement: actionable error messages
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

## Constitutional Compliance Checklist
Before implementing any feature, verify:
- [ ] Tests written first (TDD)
- [ ] 80%+ coverage achieved (100% for public APIs)
- [ ] Performance targets met (<1ms latency, >100K msg/s)
- [ ] Cross-platform compatibility verified
- [ ] Error messages are actionable
- [ ] Documentation is comprehensive
- [ ] Plugin architecture preserved
- [ ] Code quality standards met (SOLID, <20 lines/method, <10 complexity)

## Task Verification Protocol
Before marking any task as finished, verify:
- [ ] **Deliverable Exists**: All specified files/components have been created or modified
- [ ] **Requirements Met**: Implementation fulfills all specified task requirements
- [ ] **Build Success**: Code compiles without errors or warnings
- [ ] **Test Coverage**: Related tests pass and coverage meets constitutional standards
- [ ] **Documentation Updated**: Relevant documentation reflects changes made
- [ ] **Integration Verified**: Changes work correctly with existing codebase
- [ ] **Constitutional Compliance**: All constitutional principles are upheld
- [ ] **Quality Standards**: Code meets established quality and performance standards

### Verification Commands
```bash
# Verify build
dotnet build --no-restore --verbosity normal

# Verify tests
dotnet test --no-build --verbosity normal

# Verify coverage (if applicable)
dotnet test --collect:"XPlat Code Coverage" --no-build

# Verify file existence
ls -la [specified-file-path]

# Verify integration (context-dependent)
# Run relevant integration tests or manual validation
```