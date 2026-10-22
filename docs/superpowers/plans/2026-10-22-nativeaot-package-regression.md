# NativeAOT Package Regression Implementation Plan

> Execute inline using the executing-plans and test-driven-development skills; use an independent final review.

**Goal:** Preserve the proven generator/typed-handler/in-memory-storage NativeAOT package consumer as a repeatable repository check.

**Architecture:** Reuse `.github/scripts/prepare_release.py` package identity parsing. A Linux validation command packs a clean exact checkout, copies the consumer to a fresh output directory, restores only those HeroMessaging packages, publishes and executes a real ELF, and records source/package/toolchain/executable identities. Run it in the existing Ubuntu/net10 CI test lane and retain evidence even on failure.

**Tech stack:** Python standard library, existing .NET SDK and NuGet dependencies, NativeAOT, existing source generator, DI and in-memory storage.

**Spec:** The delegated request and the scope in `tests/NativeAotConsumer/README.md`; no production/API/old-TFM/performance-protocol changes.

## Constraints and review focus

- Reject dirty or mismatched source, package identity/dependency/cache mismatches, and reused output directories.
- Require explicit exact stable SDK/runtime, TFM and linux-x64 RID; record the actual versions, never install an SDK.
- Reject JIT/compile-only/failed executions; success requires actual runtime assertions and a matching native ELF.
- Keep the transient handler/typed call/storage/TTL-on-read/scope disposal scope; no EventBus routing claim.
- Preserve old evidence and all 17 runner files; no DB/service/schema/native-perf experiments or package publication.

## Tasks

- [ ] Write focused failing guards for missing reusable verifier, identity mismatch and invalid native completion; reuse existing package fixtures.
- [ ] Port the proven consumer and implement minimal orchestration/manifest, reusing package parsing.
- [ ] Run guard red/green and actual JIT-negative / NativeAOT-positive execution from a clean committed source.
- [ ] Wire the existing Ubuntu/net10 lane and document exact rerun commands and scope.
- [ ] Final strict solution build, all applicable local test frameworks, real AOT check, independent review, comment resolution.
- [ ] Normal PR CI, normal merge, and final main AOT/CI evidence. No rerunning old performance PRs.

Progress/evidence lives outside the checkout in `/workspace/HeroMessaging-nativeaot-regression-20261022`; preserve it for handoff. Existing user authorization covers implementation and normal PR/merge; no additional plan approval is required.
