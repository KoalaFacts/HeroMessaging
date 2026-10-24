# Competitive foundation implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish the performance/resource/API acceptance contract and repair the existing SQL Server configuration API.

**Architecture:** Preserve the existing builder and provider architecture. Exercise registrations using the real builder and real service provider, with no database I/O. Keep comparison infrastructure separate from production dependencies.

**Tech Stack:** C#, net8.0/net9.0/net10.0, existing xunit.v3 package version 4.0.2, Microsoft.Testing.Platform.

**Spec:** `docs/superpowers/specs/2026-10-24-competitive-foundation-design.md`

## Global Constraints

- Preserve existing public signatures and storage lifetimes.
- Use `IHeroMessagingBuilder.Services`; registration must not open a database connection.
- Existing test packages and Microsoft.Testing.Platform remain unchanged.
- No warning suppression, weakened correctness/performance gates, or numeric performance claims without measurements.
- Only root creates commits and pushes; independent workers own disjoint files.
- Unimplemented replacement capabilities remain marked open.

## Review Focus

- Real `HeroMessagingBuilder` works for every existing overload rather than only a mock implementing an extra interface.
- Explicit options and default options reach the corresponding registrations correctly.
- Null builder/options/delegate fail before service-provider resolution.
- Provider creation and disposal do not establish a database connection.
- Repeated registration and fluent chaining preserve existing DI behavior and scope/lifetime contracts.

## Task 1: Repair SQL Server builder registration

**Files:** Modify `src/HeroMessaging.Storage.SqlServer/ServiceCollectionExtensions.cs`; add `tests/HeroMessaging.Storage.SqlServer.Tests/Unit/SqlServerBuilderRegistrationTests.cs`.

**Interfaces:** Consume `IHeroMessagingBuilder.Services` and all existing `UseSqlServer*` overloads. Produce the same public signatures with valid real-builder behavior.

- [x] Write tests through `new ServiceCollection().AddHeroMessaging()` covering all existing registration overloads, returned builder identity, resolved provider types, explicit/default options, and null argument boundaries.
- [x] Run the focused tests against unchanged production code; retain the failure showing `Builder must implement IServiceCollection`.
- [x] Replace the invalid casts with `builder.Services` and add consistent required argument guards without changing lifetimes or adding unrelated features.
- [x] Run focused tests and all non-integration tests in the SQL Server test project on all three target frameworks, with a strict build.
- [x] Independently review the patch and correct any findings.

## Task 2: Record competitive acceptance requirements

**Files:** Create `docs/masstransit-replacement-acceptance.md`; update relevant README SQL Server registration and benchmark guidance after verification.

**Interfaces:** Human-readable matrix links each supported behavior and competitive claim to its evidence or remaining work.

- [x] Record complete in-process, broker, durable inbox/outbox, saga, scheduling, operational, compatibility, and API usability categories.
- [x] Define equivalent work, completed-work measurements, resource normalization, reproducibility, and honest claim boundaries.
- [x] Separate observed baseline, present implementation, open requirement, and acceptance result.
- [x] Update SQL Server examples to the verified API while retaining remaining provider limitations.

## Task 3: Add the first actual competitor benchmark

**Files:** Create `benchmarks/HeroMessaging.CompetitiveBenchmarks/` containing a standalone executable, shared workload, BenchmarkDotNet comparison, and README. Root may add it to the solution after it builds.

**Interfaces:** Pin `MassTransit` 8.5.12 and `BenchmarkDotNet` 0.15.8. Compare HeroMessaging's public command dispatch with MassTransit's `IScopedMediator.Send<T>` in an explicit scope. Explicit singleton handler/consumer registrations preserve the same retained lifetime on both sides. Use the same concrete public `ICommand`, payload, synchronous CPU work, and observable completion probe. Await handler completion.

- [x] Add a `--verify` mode that checks both implementations actually complete the expected work and propagates failure as a nonzero exit. Demonstrate that a missing handler/completion causes this verification to fail.
- [x] Add a BenchmarkDotNet steady-state comparison with managed allocation/GC metrics; label it in-process command dispatch, without broker, serialization, or durability. Use an out-of-process toolchain so cleanup verification failures invalidate the result.
- [x] Add a separate single-implementation resource measurement mode, running only the selected provider in a fresh process. Report completed count, elapsed time, CPU time per completed message, allocated bytes per completed message, GC collection deltas, p50/p95/p99, and working-set observations, with exact runtime/package metadata. Bound input counts and fail on incomplete work. Do not emit a superiority verdict.
- [x] Compile with analyzers and warnings as errors; run verification and short resource smoke runs for each provider. Smoke timings are not acceptance evidence.
- [x] Document the explicit 8.x baseline; v9 comparison requires the applicable license and a separately recorded version. Do not change existing performance workflows or introduce MassTransit into production packages.

## Task 4: Verify and deliver

- [x] Run whitespace checks and strict affected-project builds; inspect test output and summarize exact coverage boundaries.
- [x] Obtain independent review of the full change and resolve material findings.
- [ ] Commit to the task branch, push, and create a focused PR with changes, evidence, and remaining gates.
- [ ] Check CI and resolve failures introduced by this change; report any pre-existing performance-calibration failure separately.

Local implementation and review status is recorded before publication. The resulting PR and its check runs track delivery and CI status.
