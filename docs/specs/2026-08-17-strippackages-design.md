# StripPackages — build-scoped UPM package removal (buildsystem 0.12.0)

Decision (2026-08-17, Tolgahan): hosts keep `com.ivanmurzak.unity.mcp` committed; the build
system removes listed packages for the build's duration and restores them afterwards — the
package-level sibling of `StripDefines`. Supersedes the remove-the-package host branches
(`feature/remove-unity-mcp` in both hosts), which will be reworked, not merged.

## Measured foundations (2026-08-17, BTA clone, warm Library)

- Marginal cost ≈ **+30 s per player build**: baseline no-change batch run 19 s; atomic
  remove cycle 34 s; atomic re-add cycle 33 s.
- **Atomicity is mandatory and sufficient**: removing the package while `UNITY_MCP_READY`
  was still in the globals broke compilation ("Scripts have compiler errors" — gated host
  code references MCP types with the define set and assemblies gone). Removing the dep and
  the define TOGETHER before the reload lands: 0 CS errors, gated code compiles out.
- **Restore is byte-deterministic**: after a full remove/re-add cycle, `manifest.json`,
  `packages-lock.json` and `ProjectSettings.asset` all came back byte-identical (exact-pin
  dependency; a floating version would not be — StripPackages therefore requires the
  listed dependency to be exact-pinned, validated at preflight).
- Context (2026-08-13): the Unity-MCP resolver re-adds its define on every reload where its
  DLL set is healthy and has no off switch; with the package absent during the build window
  the re-add is impossible, and the 0.11.0 DefineGuard remains as defense in depth.

## Design

### Config surface
`BuildTargetConfig.StripPackages: string[]` — UPM package ids removed from
`Packages/manifest.json` for the build's duration. Host data, package mechanism (iron rule
2 shape, same as StripDefines). Scoped-registry entries are NOT touched (inert without the
dep).

### Pipeline mechanics
- New concern lives inside **ConfigureDefinesStage** (renamed responsibility: "configure
  compilation surface"), NOT a separate stage: the measured atomicity constraint means the
  manifest edit and the global-define write MUST land in the same tick, before the single
  reload they jointly queue. A separate stage would reintroduce the split-brain compile.
  - Order within Execute: edit manifest file (remove listed deps) → existing define write →
    `Client.Resolve()` — one combined recompile/reload.
- **Q5 signal path (prerequisite)**: Advance currently detects a queued reload ONLY via the
  defines-hash. A stripped package with no define delta would resume on stale assemblies.
  Fix: `BuildJobState` gains a persisted `ReloadRequested` flag a stage can set;
  `Advance` treats it exactly like a defines-hash change (stop, save, `s_reloadPending`).
  This closes Q5 for stage-initiated reloads (host-hook-initiated recompiles stay open —
  note in _Info.md).
- **T-probe RESULT (2026-08-17, BTA clone, live single session)**: manifest edit +
  `SetScriptingDefineSymbols` + `Client.Resolve` in one tick → session converges to the
  consistent state (probe exit 0: package unregistered, define gone, resolver never fired;
  ~44 s vs 19 s baseline). One hazard measured: a TRANSIENT failed compile pass (CS2001,
  stale source list racing the PackageCache removal) occurs before Unity replans; it does
  not swap the domain and self-heals, but the implementation should bracket the three
  mutations (`AssetDatabase.DisallowAutoRefresh`/`StartAssetEditing`) so only one compile
  is planned, and CI log scanners must not treat the transient pass as fatal (our runner
  already keys off result/exit, not log grep).

### Restore & crash safety
- `DevStateSnapshot` does NOT grow; a new **PackageStripGuard** mirrors ContentStateGuard:
  before the manifest edit, back up `manifest.json` + `packages-lock.json` to
  `Library/UnicoBuild/manifest_backup*` with a job-stamped EditorPrefs record.
  - Finish (success AND failure): write bytes back + `Client.Resolve()`; the re-add reload
    lands before CiCompletionWatcher can exit (it already waits out isCompiling and
    pending post-success work — verify in E2E).
  - Interactive crash recovery: restore offered via the existing OrphanedDevStateRecovery
    dialog path (separate record, own try/catch, same containment rules).
  - Batchmode crash: log verbatim + clear WITHOUT restoring (existing policy); the drift is
    visible as tracked-file diffs and CI treats workspaces as disposable.
- Preflight additions: listed package must be exact-pinned in the manifest (else Warn,
  strict-blockable) and absence of a listed package is a per-package no-op logged as a step.

### Out of scope
- Packages OTHER packages depend on (dependency-graph inversion) — validated at preflight:
  a listed package with dependents in the lock is a Block.
- Registry scope pruning, Assets/ DLL handling (MCP's committed NuGet DLLs live under the
  package's own install folder only when MCP is present-by-manifest; with the dep stripped
  mid-build they are gone with it — measured: zero MCP traces in IL2CPP output).

## Rollout
1. buildsystem 0.12.0: Q5 signal path → PackageStripGuard → config field + stage wiring →
   preflight checks. TDD (red-first), mutation probes, suite re-baseline (257 → +N).
2. E2E in host clones WITH MCP present: panel-equivalent CLI iOS Test build → green, player
   rsp carries no `UNITY_MCP_READY`, IL2CPP output carries no MCP trace, post-build tree
   byte-clean (git status empty).
3. Host branches: rework `feature/remove-unity-mcp` → `feature/strip-packages-mcp` (keep
   MCP committed; add `StripPackages: [com.ivanmurzak.unity.mcp]` to all four configs; pins
   → 0.12.0/1.7.0; defines stay as today). Old branches closed unmerged.
4. Tag + pin ritual as usual; new host baselines named at that point.
