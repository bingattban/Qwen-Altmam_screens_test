# SECTION 05 / Phase 01 — Windows CI build time optimization (2026-10-30)

## Approved scope and verified baseline

This change implements the owner's SECTION 05.01–05.05 **inside the existing Phase 01 GitHub CI and Phases 13–15 Windows testing/release jobs**; it adds no implementation phase, business policy or permission change.

Baseline PR #141 at exact head `3383b4ce242a3168e5f772611b6b47ed3996233c`:
- Windows Core [run 41086170240](https://github.com/bingattban2/Qwen-Altmam/actions/runs/41086170240): successful; wall time **8m46s** (16:02:05–16:10:51 UTC).
- Windows Release [run 41086170216](https://github.com/bingattban2/Qwen-Altmam/actions/runs/41086170216): successful; wall time **12m58s** (16:02:05–16:15:03 UTC); preflight ~1m15s; full .NET tests/build ~7m46s; subsequent x86 and x64 packaging run **in parallel**, finishing ~3m46s after the tests. Both genuine Portable and Installer artifacts and SHA-256 evidence were present for both architectures.
- Most time is spent in the full native test suite and two Windows runner startup/restore/compile cycles, not in pure JavaScript validation. Tests, idempotency, RBAC and cryptographic package integrity must never be bypassed for speed.

## Concrete compatible optimizations

| Area | Before | After (candidate) | Guard / tradeoff |
| --- | --- | --- | --- |
| Core (.github/workflows/windows-build.yml) | `compile-core` (fresh runner: restore + compile), then `core-tests` (another fresh runner: restore + implicit test compile), then re-run 10k diagnostics with implicit build | Single `core-tests` Windows runner after manifest preflight: restore once, build the **entire solution including the test project** once, run the **full suite with --no-build --no-restore**, then the original 10k hardware benchmark with those same validated binaries | Keep original `core-tests` job ID and logs, add nonzero TRX test count; remove redundant `compile-core` job; do not alter which C# tests are executed. Existing branch protection or external required-check configuration, if any, must be reviewed for the retired compile-core context. |
| NuGet | Core jobs and Release preflight did not use `setup-dotnet` global package caching | `cache: true` with `cache-dependency-path: Directory.Packages.props` in Core and Release preflight; Release build/test and both packaging jobs already had it | Cache immutable dependency downloads, **not compiled code, business data or test results**. Every job still executes its own required restore/security check. |
| Release artifacts | `upload-artifact@v4` default deflate compression re-compressed already compressed ZIP and EXE files | `compression-level: 0` for Portable ZIP and Inno Setup EXE upload; `compression-level: 1` for retained uncompressed raw publish directories | Keep four final assets, metadata, SHA-256, WebView2 Microsoft signature verification, offline installer, package-install tests and acceptance evidence. Upload bytes may increase for raw dir; compare timing before further tuning. |
| CI safety | Guard checks only immediate exit status for JS scripts | New test-first `build/test-ci-performance-contract.py` runs in **BOTH** existing preflights; it checks Core build reuse/full suite/count, cache, Release test-before-packaging and architecture/artifact expectations, and fast ZIP/EXE uploads, with deliberate negative self-tests | Preflight fails closed on drift. It complements, never replaces, `build/test-ci-exit-guards.py`. |
| Governance wording | Two historical workflow comments referred to unapproved Phase 16 | Refer to existing Phase 15 owner acceptance instead | No history or production policy alteration. |

## Security/reliability boundary (SECTION 05.04)

- **Owning layer:** GitHub Actions Phase 01 orchestration / Phase 13–15 release, plus its dedicated CI contract test. No WebView2/DTO/C# domain or SQLCipher schema changes.
- **DTOs / service calls / permissions:** unchanged. No new UI or C# action; existing authorized bridge paths and role guards stay intact.
- **Validation and errors:** CI fails immediately on a failed Python/Node command or missing/zero-count TRX, unsuccessful restore/compilation, or failed package verification. Release package jobs retain `needs: build-test`.
- **Transactions and rollback:** no business state writes; failure rolls back only candidate promotion by blocking merge, and cancels obsolete PR CI via existing concurrency policy.
- **Testing:** existing full C# core and Release suites, all Node regressions, hardware benchmark, artifact signatures, live Windows WebView2 selfcheck and package install/uninstall/reinstall tests continue. The new Python guard tests its own rejection paths.

## Future operating procedure

1. Review wall time **and per-job duration** on the most recent fully green Core/Release head, including first-run cache misses vs warmed runs. Only compare equivalent code/test counts, runner images and test suites.
2. Group related changes and push once per verified batch; avoid a series of intermediate pushes cancelling repeated 13-minute releases. Do local static checks/tests before pushing.
3. Favor caching **immutable NuGet packages** and eliminating repeated work within one runner; never cache published binaries across SHAs, skip a failing test, disable vulnerability/signature checks, or reuse a pass from another commit.
4. For each optimization, write a failing CI contract regression first, run both workflows concurrently, check the exact final PR SHA and all four x86/x64 packages/checksums, then merge and recheck both main workflows.
5. Do not declare a precise percentage improvement until **both** new candidate runs have completed successfully and comparable results have been measured. Clean-machine offline Windows acceptance and signed/public release are separate owner gates.

**Status of this document:** implementation candidate, not evidence that the improved build has finished or that all SECTION 05 standalone screens are complete.

## 2026-11-25 — current native migration CI audit and targeted fixture optimization

Measured successful PRs #62–66 on Qwen-Altmam_screens_test, from first job start to last successful job end (includes inter-job waiting, excludes initial queue and skipped manual acceptance):

| PR | Core | Release | Release run |
|---|---:|---:|---|
| 62 | 10m51s | 18m54s | 45657727284 |
| 63 | 7m25s | 25m48s | 45663054333 |
| 64 | 7m43s | 19m03s | 45669325514 |
| 65 | 9m03s | 18m03s | 45673769791 |
| 66 | 9m41s | 20m16s | 45677389444 |

Exact head for baseline #66: `5dc2f99b0402d4d8fe43dcda55c265eb216b7295`.
Its Core run 45677389462 passed 18 independent integration, 410 independent unit and 530 focused smoke cases. Real pre-package E2E occupied 236s; restore 13s and full compile 32s. Release jobs: static/advisory preflight 77s; slowest shard performance 394s (17 tests, xUnit reported 5m19s); nine independent restores/builds 24–56s each; independent pre-package E2E 282s; x86 packaging 260s and x64 214s (parallel); package acceptance x86 Portable 413s, x86 Installer 477s, x64 Portable 148s, x64 Installer 195s; independent SHA audit 28s. Nine shards and two workflows overlap, so their individual times are not additive.

The x86 Installer acceptance log (job 139435410304) measured the real bundled native bridge subprocess at 414.83s and the independent 69-route subprocess at 5.69s; x86 Portable (139435410239) measured 332.07s and 4.77s. x64 Installer (139435410298) measured 134.76s and 3.66s. The slowest x86 operation is the actual native browser/SQLCipher roundtrip rather than ZIP extraction, Inno Setup, or route navigation. The x86 Installer trace locates most elapsed time between modal UI PASS and the final native bridge authorization/SQLCipher PASS. This is a measured bottleneck, not a claim that every second is encryption cost. The NuGet cache hit in Core and performance-shard logs; cache is already configured on Core, preflight, matrix, E2E and packaging runners. Do not add redundant caching or cache compiled/test artifacts across commits.

### Implemented first candidate

The existing WebView2 synthetic database disabled SQLCipher connection pooling in all modes, even when recovery/reset was explicitly split out. Use the production-equivalent encrypted connection pool ONLY for the explicit split-recovery bridge-only process, excluding recovery-only. Retain nonpooled connections in recovery-capable processes. This changes neither production connection defaults nor SQLCipher key, cipher, KDF, WAL, foreign keys, authorization, test assertions, package timeouts or test coverage. Drain the synthetic gateway, clear only its exact connection pool (no process-wide ClearAllPools), dispose, then delete temporary directories. New real SQLCipher cleanup/reopen regression and strengthened existing WebView2 preflight run in both workflows. Every bridge role, attendance, audit, 69-route, recovery, install/reinstall/uninstall, AppData-preservation and four-package checksum gate remains unchanged. No YAML workflow or distribution changes.

### Remaining opportunities, not implemented

1. Compare the next successful candidate against this baseline and at least two subsequent green runs, including x86/x64 subprocess timings; retain current bounded timeouts. Do not claim a numerical speedup until measured. If pooling causes errors, revert the isolated candidate rather than loosening gates.
2. Review actual TRX timings per test/class (especially performance and slower R01/security shards). Rebalance existing exact partitions only after verifying coverage, no duplicates and evidence contract; keep the full 10k-person three-year benchmark. Separate independent test suites only if the measured critical path warrants it.
3. Assess runner queue variability and the cost of the nine builds versus transferring exact-head build outputs. Avoid serializing all shards behind an extra compile/upload/download dependency without demonstrated benefit; retain independent native execution on Windows.
4. Keep already-compressed ZIP/EXE artifact upload at compression level 0 and raw publish artifact at level 1; retain all four acceptance jobs. Investigate independent bridge/route retry boundaries only if traces demonstrate wasted re-execution.

Native historical range UI integration remains the next functional slice after this independent CI optimization is validated. Manual clean-machine Windows acceptance remains separate and unfulfilled.
