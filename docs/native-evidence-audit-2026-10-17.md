# Native evidence audit and continuation plan — 2026-10-17

## Decision

Keep PRs [116](https://github.com/KoalaFacts/HeroMessaging/pull/116),
[117](https://github.com/KoalaFacts/HeroMessaging/pull/117) and
[118](https://github.com/KoalaFacts/HeroMessaging/pull/118) Draft. No new native
capture, benchmark execution, workflow dispatch, package publication, external
contact, database change, or merge was performed for this audit.

The original native traces are unavailable in this execution environment and
their checked GitHub artifacts return HTTP 404. Therefore the historical
loss-header count, lost payloads, originating rings and missing-marker cause
cannot be independently re-established here. Two original performance artifacts
were recovered and revalidated: the CPU tail regressions remain real blockers.

## Current state and ownership checks

- Local checkout and fetched `origin/main`: `eb6c2e87fd3651d22c507afa140b4c5f34bac927`.
- PR heads remain #116 `93520c260dcce62e1ab0c5e61cc0976a58ce5a83`,
  #117 `7ba436af1e22d74d3576c8dc59047888e0909fb2`,
  #118 `63482829411c0ffd1eee90f443b8ea705c3c2f20`.
- PR #118 is stacked on #117. The original comparison baseline was
  `509b9eebcdf043ef293e5c53252efc3f850a49d6`; it is not current main.
  The PR API still reports that older base SHA for #116/#117. Use freshly fetched
  refs and re-evaluate integration before changing the stack; do not infer
  current-main validation from historical PR checks or mergeability fields.
- No queued/in-progress Actions, PR assignees, or other local worker processes
  were found. All three inline review-thread lists were empty. Historical
  comments were authored by `ldsenow`; they describe saved private evidence but
  do not name a current executor or an accessible original directory. This is
  not proof that no work exists in another session or machine.
- No `AGENTS.md` or `.agents/skills` was present in this checkout or the fetched
  #118 tree; workspace `.agents` was empty. `CLAUDE.md` was read.
- Current main [scheduled run 39009546206](https://github.com/KoalaFacts/HeroMessaging/actions/runs/39009546206)
  succeeded, including its performance job; native was skipped. This is not
  native approval or a new test of PR #116. #118
  [push 36956910152](https://github.com/KoalaFacts/HeroMessaging/actions/runs/36956910152)
  succeeded with native and performance skipped.

## Recovered assets and precise gaps

The evidence directory for this execution is
`/workspace/HeroMessaging-evidence-20261017/`. This is a local deliverable, not a
durable shared archive or a claim that the earlier private directory was found.

| Asset | Result on 2026-10-17 |
| --- | --- |
| Ring TRACE run 36944040503, native artifact 11201318760 | Metadata and download return 404; run artifact listing contains only coverage |
| Original partial capture 36819775338, native artifact 11142699739 | Metadata returns 404; run listing retains comparison/coverage, not native |
| Logging run 36874140402, native artifact 11168293958 | Metadata returns 404; run listing contains coverage |
| Native job 110641884798 in run 36944040503 | Actions console log recovered through GitHub connector; native log/raw trace are not embedded |
| Performance artifact 11143777317, run 36818589861 | Original ZIP recovered; digest verified |
| Performance artifact 11143584491, run 36819775338 | Original ZIP recovered; digest verified |
| Prior independent Nettrace reader, raw verification JSON, native log, strace audit and source/binary mapping | Not found in accessible workspace assets; prior report only says ignored local artifacts |

The recovered console log verifies that native artifact 11201318760 was
successfully uploaded on October 2 with **29 files, 98,798,944 bytes** and SHA-256
`992114f80e06cd3951b7f52d1c97954253385282934d1994d493798e89c65a12`.
It also records **retention-days: 7**, the measurement guard failure, and exit 1.
Seven days elapsed before this audit. Expiration is consistent with the 404s;
the 404 response does not distinguish expiration from manual deletion.

The following expected hashes come from the historical
[review](https://github.com/KoalaFacts/HeroMessaging/pull/118#issuecomment-5943434848),
not a fresh read of those files:

- Native trace: `E2EB67A6CD621A212270EC85984F1E66996E51C5E0CDFED619DF259BF627C7DE`.
- Complete native log: `E147E4FCD57A0268992F8E749D05CE0318D553F2179D2CF3F4FC73AD972338BD`.
- Native library: `FCA0D0DAB5CDF81CC156A15BCF40ACA77E2B65744C11CC5DECC74E769A262860`.
- Independent reader: `5DE8B783AB48AF46043FDBE775EA5A189E294F909BA79E92BC19EC1652C29ED2`.

Recover those originals from the earlier workspace before claiming another raw
analysis. A matching hash is necessary; a comment quoting a hash is not the asset.

## Loss-header and originating-ring analysis

**What the historical report establishes, subject to unavailable raw inputs:**
six full-return user-event `writev` calls and six local observations; zero native
markers among 1,032,444 exported events; 670,884 type-2 header observations, with
sample headers of size 48. Independent readers reportedly agreed. That places
the missing records downstream of successful writes in this perturbed probe,
but does not distinguish kernel handling, ring collection/drain, or export.
It does not establish the original benchmark's root cause.

**What source inspection establishes:**

1. [Linux v6.17 UAPI](https://github.com/torvalds/linux/blob/v6.17/include/uapi/linux/perf_event.h#L901)
   defines record type 2 as `PERF_RECORD_LOST`: an 8-byte header, `u64 id`,
   `u64 lost`, followed by an optional configured sample-id trailer. The count
   comes from `lost`, not from the number of logged headers. Trailer decoding
   requires the actual `sample_type` and `sample_id_all` settings.
2. [Linux ring output](https://github.com/torvalds/linux/blob/v6.17/kernel/events/ring_buffer.c#L249)
   writes `event->id` and exchanges the ring's accumulated `rb->lost` counter.
   In a shared output ring this does not identify every lost event's provider.
   These are upstream reference paths, not verification of the Azure kernel's
   loaded build or this run's ring topology.
3. [OneCollect reference writer](https://github.com/microsoft/one-collect/blob/f3b6899c37f84de834060de6b4eeb148b7cbae5d/one_collect/src/perf_event/rb/mod.rs#L1295)
   can also synthesize a 48-byte type-2 record for its in-process ring, with zero
   payload/sample IDs. Thus size/type alone cannot choose kernel versus internal
   origin. The reference reader logs `header_start` modulo ring size, not an
   independently unique event identity. No deduplicated record count can be
   derived from the historical aggregate alone.
4. Upstream [commit 9a46bf0, October 9](https://github.com/microsoft/one-collect/commit/9a46bf0d2c2d84551bf7955c42f1a90054faee04)
   adds Linux `lost_event`/`lost_samples_event` aggregation and writes loss into
   nettrace sequence accounting. This is a concrete later upstream loss-export
   repair to investigate. It does **not** prove that the saved binary used its
   parent implementation, that every header reached those callbacks, or that
   upgrading would restore markers. No dependency upgrade was made.

The #118 adapter pins dotnet-trace
`10.0.745401+cef304c50763bf24f99566cb31d55540842e7ae9` and the native hash above.
The diagnostics source references `Microsoft.OneCollect.RecordTrace 0.1.34271`.
The package/native-hash-to-OneCollect-source mapping remains missing; the
reference source must not be called the exact shipped implementation.

**Missing data needed for a causal attribution:** consumed record bytes;
payload `id/lost`; configured trailer fields; stable ring ID and explicit
kernel/internal origin; perf FD/event ID/provider/PID/TID/CPU mappings and output
redirection; absolute read position/wrap generation and head/tail progression;
loss callback/export counts; and correlation with the six marker writes. Preserve
peek versus consumed observations separately. Do not multiply a header frequency
by an assumed loss count or attribute shared-ring loss to individual markers.

The historical 38,026 warnings and 970,621 invalid-register-length debug messages
also lack their original contexts here. Zero missing stacks after filtering to
zero complete windows is not unwind validation. Obtain warning sites, affected
samples/register ABI, symbol identities and target-window stack coverage before
assigning an unwind cause. No warnings or guard thresholds were suppressed.

## Independently revalidated performance blocker

`verify_saved_comparisons.py` in the local evidence directory only reads files;
it never launches saved binaries. It verifies archive digests, manifested library
and harness hashes, clean-source flags, exact configurations, alternating process
order, all 24 executions/72 batches per artifact, finite metrics, ordered
percentiles and >=10-second measured durations. Statistics are medians of
per-process batch medians, not pooled event percentiles.

| Saved run | Minimum batch seconds | CPU publish admission p99 ms | CPU receipt all-handler p99 ms |
| --- | ---: | --- | --- |
| 36818589861 | 11.4448308 | 0.74210 → 1.94245 | 0.10260 → 0.15495 |
| 36819775338 | 11.8628081 | 0.74115 → 1.17995 | 0.10210 → 0.15820 |

In both artifacts, every candidate CPU-publish batch's p99 exceeds every
baseline CPU-publish batch's p99. The first candidate is synthetic merge
`e198858b136f68468dbff07d1bc36bb19dc095d4`; its parents were rechecked as the
baseline `509b9ee` and candidate `93520c2`. The second uses `93520c2` directly.
These comparisons do not establish a scheduler/GC/JIT cause, a noise bound, or
behavior against today's main. A throughput/allocation improvement does not
cancel the tail gate.

ZIP SHA-256 values:

- 11143777317: `3d0b119369ddf274911c60158e4b9824f898302ad818db945ab6cb81a014e24f`.
- 11143584491: `1b5b3056cfbdfa4476788102b419d176bc62667df09eb2290e8346d7075a7962`.

## Minimal repair and ordered work

This local change raises native evidence retention **7 → 30 days**, matching the
comparison artifact window. It preserves `if: always()`, uploaded paths, capture
options and every validity guard. It cannot restore expired data, add absent loss
payloads or guarantee permanent archival. The #118 branch still needs this change
integrated deliberately; this audit does not rewrite its existing commits.

| Order / workstream | Concrete next deliverable | Acceptance gate |
| --- | --- | --- |
| 1. Evidence custodian | Recover the earlier private trace/log/reader/audit files and source mapping, verify expected hashes, record an accessible retained location | Original bytes exist; no reconstructed markers or replacement captures presented as originals |
| 2. Native investigator | Read saved headers with payloads and ring/cursor identity; account for consumed loss records through callbacks/export | Kernel/internal origin and unique count either demonstrated or explicitly unresolved; all three windows and real CPU/scheduler/stacks remain required |
| 3. Diagnostic maintainer | Characterize the later upstream loss-export fix against a mapped collector using bounded synthetic loss fixtures before considering an adapter/tool change | Positive/zero/truncated loss cases fail/pass correctly; no unsupported flags, blind package update or real capture launched in this audit |
| 4. PR #117 maintainer | Preserve exact-batch guard regression coverage while integrating current main | Full build, relevant guards and fresh normal CI; missing native evidence still blocks promotion under the current user gate |
| 5. PR #118 maintainer | Preserve syscall/audit/cleanup evidence and port workflow diagnostics to main's MTP deliberately | Do not copy historical VSTest switches into current MTP; Linux dump functionality and original timeout cause remain separate unknowns |
| 6. PR #116 maintainer | Keep runtime experiment isolated; withdraw it if tail benefit cannot be demonstrated with a defensible cause-level change | No merge on green CI, allocation alone, unchanged repeated sampling or a declared noise allowance |
| 7. Release integrator | Refresh refs/checks and resolve actual reviewer comments one by one only after these gates | Normal protected submit/review/merge process; no bypass, auto-merge or package publication |

These are work assignments by responsibility, not claims that outside executors
were contacted or started. The current executor completed recovery/audit and the
retention patch. Native attribution remains blocked on original assets.

## Verification performed

Commands ran from `/workspace/HeroMessaging` after sourcing
`/workspace/.heromessaging-setup/env.sh` when .NET was required:

```sh
git fetch origin
gh pr list --state open --json number,title,headRefName,isDraft,updatedAt,assignees
gh run list --status in_progress --json databaseId,headSha,headBranch,name,event,status
gh run list --status queued --json databaseId,headSha,headBranch,name,event,status
python3 /workspace/HeroMessaging-evidence-20261017/verify_saved_comparisons.py
dotnet build HeroMessaging.slnx -c Release -warnaserror --no-restore
dotnet test --project tests/HeroMessaging.Tests --configuration Release --no-build --no-restore --framework net10.0 --filter-class '*BatchMarkerPayloadTests' --report-trx --results-directory /workspace/HeroMessaging-evidence-20261017/marker-tests --no-progress
git diff --check
```

Results: strict full solution build passed, zero warnings/errors; focused marker
payload tests 4/4 passed with no skips; both saved performance validations passed
their data checks and retained the tail blocker. Python YAML parsing and structural
comparison confirmed that retention is the only semantic workflow change. No
full unit/integration matrix, PowerShell guard suite, actionlint, or new remote CI
was claimed for this local documentation/workflow change. No benchmark was run.
