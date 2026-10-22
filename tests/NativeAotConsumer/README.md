# NativeAOT package consumer

This check packs a clean exact source commit and consumes those local NuGet
packages without project references. It publishes and executes a Linux x64
NativeAOT ELF; a JIT run or compile-only result cannot pass. No package is
published to a feed, no SDK is installed, and no external service is started.

The parameterized project is stored as `NativeAotConsumer.csproj.template`.
Only the verifier materializes it as a `.csproj` in its fresh owned output
directory, with explicit TFM and exact local package version arguments. This
keeps repository-wide standalone project discovery valid without fake default
package identities or excluding production projects from dependency scanning.
The manifest records the template hash and the identical materialized project hash.

From a clean checkout with the stated SDK and native toolchain already available:

```bash
python3 .github/scripts/verify_nativeaot.py \
  --source "$PWD" --commit "$(git rev-parse HEAD)" \
  --output "$PWD/artifacts/nativeaot-local" \
  --sdk 10.0.401 --tfm net10.0 --runtime 10.0.12 --rid linux-x64
```

Use a new output directory each time. The command rejects dirty/mismatched
source and an unexpected SDK. For a future GA validation, explicitly select the
installed exact stable SDK and matching TFM/runtime family; the helper never
downloads an SDK or treats this .NET 10 example as .NET 11 acceptance. Library
TFMs and APIs remain unchanged. Linux needs the normal NativeAOT linker and
development libraries; supported compiler discovery stays with the SDK.

The locally built package version is `0.0.0-aot.g<full-source-sha>`. The existing
release helper validates archive identities and repository commits; this command
also checks internal dependency versions and byte equality with the isolated
consumer package cache. `nativeaot-manifest.json` records command exit codes,
SDK/runtime/TFM/RID/OS, package and resolved dependency hashes, generated source
hashes, and the final ELF hash. `status: passed` is written only after actual
execution succeeds. Logs, packages and partial evidence remain on failure.
Build/probe commands have bounded timeouts and owned process groups. SIGINT or
SIGTERM cleans those groups and records failure; forcibly killing the verifier
itself with SIGKILL or losing the host is outside this cleanup guarantee.

Coverage is intentionally narrow:

- The real analyzer generates one transient `IEventHandler<ProbeEvent>` registration.
- A DI scope resolves it and invokes the strongly typed handler directly.
- `InMemoryMessageStorage` stores value 42, queries, updates to 84 and deletes it.
- At the exact TTL boundary, `RetrieveAsync` removes the expired message; this is
  not background expiry or a claim that Query/Count independently filter expiry.
- Scope disposal calls the transient handler's `DisposeAsync` once; its ValueTask
  completes immediately. Suspended/cancelled cleanup is not exercised.

EventBus dynamic routing, full messaging pipelines/receipts, other generators,
RingBuffer algorithms, databases, external services and performance are outside
this check. RingBuffer is installed as a transitive package, not exercised.
No assembly-wide AOT compatibility claim is added.

CI runs this check in the existing Ubuntu/net10 unit lane and retains evidence
on failure. Fast synthetic guard tests reuse the release package fixtures:

```bash
python3 -m unittest discover -s .github/scripts -p 'test_*.py' -v
```

Those fixture tests validate rejection paths; only the actual publish and ELF
execution establish that the selected consumer path works with NativeAOT.
