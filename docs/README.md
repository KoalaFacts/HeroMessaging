# HeroMessaging documentation index

Every Markdown document in the repository is listed here once. Start with the [main README](../README.md) for the overview and quick start. `node .github/scripts/check-docs.ts` fails when a document is linked from neither this index nor the main README, a relative link or anchor is broken, or a repository path named in inline code does not exist (dated records are exempt from the path check only).

## Users (current)

- [MassTransit replacement acceptance](masstransit-replacement-acceptance.md) — replacement requirements, evidence status and the work roadmap.
- [Batch processing guide](batch-processing-guide.md) — configuring and using batch processing.
- [Saga orchestration pattern](orchestration-pattern.md) — sagas with state machines.
- [Choreography pattern](choreography-pattern.md) — event-driven coordination without an orchestrator.
- [State machine builder API](builder-api-guide.md) — saga state-machine builder extensions.
- [OpenTelemetry integration](opentelemetry-integration.md) — tracing and metrics setup.
- [Idempotency](../src/HeroMessaging/Idempotency/README.md) — idempotency framework usage and storage.
- [Source generators](../src/HeroMessaging.SourceGenerators/README.md) — generator package overview and compatibility limits; extended [usage](../src/HeroMessaging.SourceGenerators/USAGE.md) and [examples](../src/HeroMessaging.SourceGenerators/Examples/README.md) (2025 material; see the package README for what the runtime supports).
- [Competitive benchmarks](../benchmarks/HeroMessaging.CompetitiveBenchmarks/README.md) — in-process comparison with MassTransit.
- Security policy — supported versions, vulnerability reporting, secure-use guidance and repository security: [.github/SECURITY.md](../.github/SECURITY.md).

## Contributors and maintainers (current)

- [Contributing guide](../CONTRIBUTING.md) — workflow, standards and integration-test services.
- [Development guidelines](../CLAUDE.md) — binding rules for contributors and AI assistants.
- [Verification-first skill](../.claude/skills/verification-first/SKILL.md) and [HeroMessaging verify skill](../.claude/skills/verify-heromessaging/SKILL.md) — for AI agents: how to prove a change works at the real boundary, per acceptance row; current. The [advance skill](../.claude/skills/advance-heromessaging/SKILL.md) is the scheduled task's goal, gate and work order.
- [Testing guide](testing-guide.md) — test layout, patterns and CI.
- [Test projects](../tests/README.md) — test project list and commands.
- [Architecture tests](../tests/HeroMessaging.Architecture.Tests/README.md) — enforced architecture rules.
- [Benchmarks](../tests/HeroMessaging.Benchmarks/README.md) — microbenchmarks and same-harness in-process comparison.
- [NativeAOT package consumer](../tests/NativeAotConsumer/README.md) — packaged NativeAOT check run in CI.
- [Release package preparation](release-package-preparation.md) — how release packages are built and verified.
- [Environment protection](../.github/ENVIRONMENT_PROTECTION.md), [package signing](../.github/PACKAGE_SIGNING.md), [GitHub Advanced Security](../.github/GITHUB_ADVANCED_SECURITY.md), [security scan review](../.github/SECURITY_SCAN_REVIEW.md) — repository security and publishing setup.
- [Changelog](../CHANGELOG.md) — release history and unreleased changes.

## Architecture decision records

- [0001 Message scheduling](adr/0001-message-scheduling.md)
- [0002 Transport abstraction layer](adr/0002-transport-abstraction-layer.md)
- [0003 RabbitMQ transport](adr/0003-rabbitmq-transport.md)
- [0004 Saga patterns](adr/0004-saga-patterns.md)
- [0005 Idempotency framework](adr/0005-idempotency-framework.md)
- [0006 Rate limiting](adr/0006-rate-limiting.md)
- [0007 Batch processing](adr/0007-batch-processing.md)

## Historical records and evidence (kept unchanged)

- [Native evidence audit, 2026-10-17](native-evidence-audit-2026-10-17.md) — native profiling evidence decision.
- Code-quality audit, 2025-11-28: [summary](audit/quality-summary.md), [plan](audit/code-quality-audit-plan.md), [generators](audit/generators-audit.md), [PostgreSQL](audit/postgresql-audit.md), [RabbitMQ](audit/rabbitmq-audit.md), [security](audit/security-audit.md), [SQL Server](audit/sqlserver-audit.md).
- Benchmark results: [in-process allocations](../tests/HeroMessaging.Benchmarks/results/2026-09-30-inprocess-allocations.md), [in-process concurrent](../tests/HeroMessaging.Benchmarks/results/2026-09-30-inprocess-concurrent.md), [CPU scheduling](../tests/HeroMessaging.Benchmarks/results/2026-09-30-inprocess-cpu-scheduling.md), [publish receipt fast path](../tests/HeroMessaging.Benchmarks/results/2026-09-30-publish-receipt-fast-path.md), [channel dispatch experiment](../tests/HeroMessaging.Benchmarks/results/2026-10-01-channel-dispatch-experiment.md).
