# HeroMessaging competitive foundation

## User goal

HeroMessaging should replace MassTransit for supported workloads while delivering better performance, lower resource consumption, and a friendly public API. These are acceptance requirements, not achieved performance claims.

## Approach

Build on the current public API and prove one supported path at a time. First repair existing configuration entry points and establish a reproducible comparison protocol. Keep production reliability and performance evidence together: removing persistence, publisher confirmation, consumer work, retries, or diagnostics from only one implementation invalidates a comparison.

The alternatives considered were a broad API rewrite, immediate hot-path optimization, and incremental fixes with equivalent-work benchmarks. The incremental approach is the implementation choice: it preserves compatibility and produces reviewable changes before wider API redesign. The user authorized progression and the three competitive goals; numeric superiority has not been measured or promised.

## First implementation slice

1. Fix every existing SQL Server builder registration overload to use `IHeroMessagingBuilder.Services` rather than casting the builder to `IServiceCollection`.
2. Exercise the real `AddHeroMessaging` builder and resolve the configured storage/saga services without opening a database connection. Preserve signatures, lifetimes, and option propagation. Reject null required arguments at the public boundary.
3. Commit the competitive acceptance matrix and document the first runnable benchmark. Initial in-process comparisons must be labeled as in-process command dispatch, separate from broker and transactional comparisons.

This slice does not implement SQL Server external Outbox leases, Saga concurrency, remote request/response, durable scheduling, or RabbitMQ consumer retry policy. They remain explicit replacement requirements in the acceptance matrix.

## Measurement requirements

- Pin and record both implementations' package/source versions, runtime, OS, CPU limits, payload, concurrency, serialization, durability, acknowledgements, retries, and diagnostic configuration.
- Count completed handler work. Submission throughput alone is not completion throughput.
- Report throughput and p50/p95/p99 together with CPU time per completed message, allocated bytes per completed message, GC activity, and process memory. Report broker/database resources separately for distributed scenarios.
- Compare resources at the same achieved throughput and latency objective as well as maximum throughput at the same resource budget.
- Run independent paired measurements with warmup and retained raw results. A smoke run validates the harness only. Shared-host timing cannot establish superiority.
- Require zero lost work in the tested contract, bounded queues/memory, and explicit duplicate handling before accepting a speed or resource improvement.
- Use the supported MassTransit version relevant to a migration; record older baselines explicitly. Respect package licensing and never label a result for one version as a result for all versions.

## API requirements

- Existing public signatures remain compatible in this slice.
- Configuration through the documented builder must succeed and return the same builder for fluent chaining.
- Required null arguments fail immediately with the relevant parameter name.
- Registration does not establish a database connection.
- Quick-start snippets must be backed by a compile/run consumer or behavior test.
- Later API changes must make send, publish, request, durable delivery, cancellation, and completion semantics explicit with useful defaults and actionable errors.

## Verification and release boundary

Use existing xUnit packages and Microsoft.Testing.Platform. Prove the registration defect with failing tests before the fix, then run the affected unit suite on net8.0/net9.0/net10.0 and a strict build. Keep database integration results separate from configuration-only results. Do not describe unmerged work as released or a comparison harness as a performance win.
