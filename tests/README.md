# HeroMessaging tests

Tests use xUnit with Microsoft.Testing.Platform. The runner is selected in the root `global.json`; use the .NET 10 SDK (Roslyn 5.9 or later). Test projects still target .NET 8, 9 and 10 as declared in each project.

## Projects

- `HeroMessaging.Tests`: core processing, storage, decorators, policies, idempotency and orchestration.
- `HeroMessaging.Abstractions.Tests`: public contracts and abstractions.
- `HeroMessaging.Architecture.Tests`: dependency and architecture rules.
- `HeroMessaging.Security.Tests`: security implementations.
- `HeroMessaging.RingBuffer.Tests`: ring buffer behavior.
- `HeroMessaging.Serialization.*.Tests`: JSON, MessagePack and protobuf serializers.
- `HeroMessaging.Storage.*.Tests`: PostgreSQL and SQL Server providers.
- `HeroMessaging.Transport.RabbitMQ.Tests`: RabbitMQ transport.
- `HeroMessaging.ReliabilityHarness`: not a test project; an Outbox publisher that RabbitMQ integration tests start as a separate OS process and kill at declared crash points.
- `HeroMessaging.Observability.*.Tests`: health checks and OpenTelemetry.
- `HeroMessaging.SourceGenerators.Tests`: generated code and diagnostics.
- `HeroMessaging.Tests.Shared`: shared helpers; this is not an executable test project.
- `HeroMessaging.Benchmarks`: BenchmarkDotNet microbenchmarks and in-process comparison; not a test project ([README](HeroMessaging.Benchmarks/README.md)).
- `NativeAotConsumer`: packaged NativeAOT consumer check run by CI ([README](NativeAotConsumer/README.md)).

## Running tests

From the repository root:

```bash
# Run unit tests without requiring external services
dotnet test --filter-not-trait Category=Integration

# Run one project and framework
dotnet test --project tests/HeroMessaging.Tests --framework net10.0 --filter-not-trait Category=Integration

# Integration projects require Docker and configured database/RabbitMQ services.
# Exit code 8 means a project has no matching tests; actual test failures still fail.
dotnet test --filter-trait Category=Integration --minimum-expected-tests 0 --ignore-exit-code 8

# Produce TRX and Cobertura reports
dotnet test --project tests/HeroMessaging.Tests --filter-not-trait Category=Integration --report-trx --coverlet --coverlet-output-format cobertura

reportgenerator '-reports:**/coverage.cobertura*.xml' -targetdir:coverage-report
```

Architecture tests run without coverage instrumentation because they inspect assembly metadata. CI collects coverage from the other test projects.

Shared test properties register the TRX and Coverlet MTP extensions only for executable `.Tests` projects. CI runs each supported framework separately and retains the 80% coverage requirement. See [the testing guide](../docs/testing-guide.md) for test conventions and [CONTRIBUTING.md](../CONTRIBUTING.md#integration-tests) for integration service configuration.

## Adding tests

Place tests in the matching project and use `[Trait("Category", "Unit")]`, `[Trait("Category", "Integration")]` or `[Trait("Category", "Architecture")]`. Reuse `HeroMessaging.Tests.Shared` helpers where appropriate. Keep tests independent and preserve the existing coverage requirements.
