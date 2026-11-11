---
name: advance-heromessaging
description: How the scheduled task advances HeroMessaging — goal, headline metric, gate, work order, scope and project rules. Read with agent-harness/skills/autonomous-dev at the start of every run.
---

# Advance HeroMessaging

Repo-specific half of the scheduled development task ("HeroMessaging 推进计划"); the run loop is `agent-harness/skills/autonomous-dev`, verification is `agent-harness/skills/verification-first` plus `.claude/skills/verify-heromessaging`. Owner seed: `agent-harness/projects/heromessaging.md` — where it and this file differ, the stricter wins. This file may make rules stricter, never looser, and never relaxes the hard limits in the scheduled task's prompt. `CLAUDE.md` is binding.

## Goal and metric
- A .NET messaging library (net8.0/net9.0/net10.0): in-process commands/queries/publish, PostgreSQL and SQL Server storage (Outbox, Inbox, Saga), a RabbitMQ transport, serialization plugins, source generators, observability and a NativeAOT consumer.
- Goal: a credible MassTransit replacement for the workloads it claims — better performance, lower resource use, a friendly public API — proven scenario by scenario as `docs/masstransit-replacement-acceptance.md` requires.
- Headline metric: acceptance rows F01–F25 / P01–P05 by status in that document ("Passed" needs a named scenario, exact configuration, executable verification and retained result), plus the Roadmap rows done.
- Real progress: a merged PR that changes working code and tests (or acceptance evidence backed by an executable test) and moves a Roadmap row.

## Setup
- Read: `CLAUDE.md`, `CONTRIBUTING.md`, `docs/README.md`, `docs/masstransit-replacement-acceptance.md` (inventory and Roadmap), `docs/adr/`, `docs/audit/`, `docs/testing-guide.md`, the package README of the area, and the last few PR bodies and handoff comments of this task.
- SDKs (scratch install dir, e.g. `~/.dotnet`): `dotnet-install.sh --channel 10.0` (SDK pinned by `global.json`, `rollForward: latestPatch`) plus `--runtime dotnet --channel 8.0` and `--runtime dotnet --channel 9.0`.
- Docker images: pull through `mirror.gcr.io` (e.g. `mirror.gcr.io/library/postgres:17`, `mirror.gcr.io/library/rabbitmq:3.13-management`) when Docker Hub is rate-limited.
- Tests use Microsoft.Testing.Platform (`global.json` `test.runner`): filter with `--filter-trait`, `--filter-not-trait`, `--filter-class`; pass `--project` and `--framework`.

## Gate
In order, on the final source (after main is merged in):
1. `dotnet build HeroMessaging.slnx -c Release -warnaserror` — 0 warnings, 0 errors (also NativeAotConsumer and any project CI builds outside the solution when touched).
2. Unit tests (`--filter-not-trait Category=Integration`) for every touched test project, and core `tests/HeroMessaging.Tests` on net8.0, net9.0 and net10.0.
3. Integration tests (PostgreSQL, SQL Server, RabbitMQ) for touched areas locally when Docker is available; otherwise the hosted `integration-tests` jobs are the evidence.
4. Publish and run NativeAotConsumer when dispatch, generators or trimming-relevant code change.
5. Benchmarks only for performance claims, via the existing workflows or CLAUDE.md's same-harness A/B.
6. `node .github/scripts/check-docs.ts` → `check-docs: OK`.

## Ledger
The "Roadmap" section of `docs/masstransit-replacement-acceptance.md` (there is no separate roadmap file under docs/plans; do not create one). Every row names the acceptance rows it moves; every PR updates its row and, with evidence, the acceptance row's status.

## Work order
First route: RabbitMQ + PostgreSQL, two independent processes. Take the first row not done or blocked.
- R0 green main; D0 first docs reorganisation (done).
- R1 F09 consumer retries/redelivery; R2 F10 dead letters and replay (done).
- R3 F06 PostgreSQL Outbox; R4 F07 transactional external publish; R5 F08 durable Inbox (persist-before-ACK, duplicates, multi-host ownership, crash recovery, retention/poison policy).
- R6 two-process reliability harness (separate OS processes killed at declared points, business ledger) running R3–R5 evidence in hosted CI.
- R7 F24 consumer testing API; R8 F16 remote request/response; R9 F17 persistent scheduling; R10 F14 Saga state plus outgoing work; R11 F12 SQL Server external Outbox.
- R12 P01–P05 equivalent-work benchmarks against MassTransit, same machine and workflow, with resource use; negative results recorded.
- R13 F18 idempotency in the pipeline; C-rows for CI capacity / red main as found.

## Authority
- Branch prefixes `fix/`, `feat/`, `test/`, `perf/`, `docs/`, `ci/`; squash merge pinned to the head SHA.
- First line of every PR body: "Opened by the HeroMessaging scheduled task."
- Never touch `codex/` branches (e.g. #118), Dependabot PRs or other sessions' work.
- Never dispatch or re-run `create-release.yml`, `publish-nuget.yml` or any publish workflow. CHANGELOG entries go under `[Unreleased]` only.
- Merging deploys nothing.

## Project rules
- Do not edit LICENSE. Preserve existing public signatures unless the item is an API change stated in a new section of the closest existing ADR in `docs/adr/` (never a new ADR file).
- Fix warnings in code rather than suppressing them (CLAUDE.md warning policy).
- Extra merge conditions: hosted CodeQL/security scans that ran passed; no coverage or performance gate weakened.
- Verification boundary: real RabbitMQ and PostgreSQL (Docker locally, otherwise the hosted integration jobs), two separate OS processes killed at declared points with the business ledger checked, as the acceptance doc requires. Feature map rows are F01–F25 and P01–P05 in `verify-heromessaging`.
- Known CI trap: `Quality Gates & Validation` fails whenever any integration job fails; a SQL Server container crash at startup ("This program has encountered a fatal error") is runner infrastructure — one re-run of that job is allowed.

## Docs scope
- Housekeeping covers all Markdown: README.md, CLAUDE.md, CONTRIBUTING.md, docs/**, package READMEs, tests/README.md.
- No new folders (no docs/guides/, no docs/archive/): finished or superseded plans and one-off analyses are folded into the live page that owns any still-true content and then deleted; `git mv` only into folders that already exist.
- Never delete or rewrite ADRs, acceptance evidence, retained benchmark results or CHANGELOG history; negative results stay findable.

## Report extras
- Acceptance rows moved by each merged PR; test counts per framework.
