# Build Core

The reload-safe build orchestrator and the pre-flight runner. This is the heart of the system.

Contents:
- `UnicoBuildService` — the static orchestrator. `Start` validates, snapshots dev state, and persists a job; `Advance` runs stages from the saved `StepIndex`; `Finish` restores dev state and clears the job. `LatestResult` (SessionState-backed so it survives the end-of-build define-restore reload) and `IsRunning` are the panel's read surface; `ResetStuckJob` is the manual recovery for a wedged job — it restores dev state through the normal `Finish` path.
- `BuildJobState` — the serialized, reload-surviving job record (active flag, step index, request JSON, dev-snapshot JSON, resolved profile, accumulated steps / artifacts / extra-defines). Backed by `SessionState`.
- `BuildJobResumer` — `[InitializeOnLoad]` hook that calls `UnicoBuildService.Advance` after every domain reload so the pipeline continues across platform switches and define changes.
- `BuildSession` — a static flag (`IsBuildingViaPanel`) with `Begin()`/`End()`, set while a panel build runs so the manual-build preprocessor suppresses its modal dialogs.
- `PreflightRunner` — runs an ordered `IPreflightCheck` set; `Default()` is the canonical list used by both the panel and `Start`.
- `OrphanedDevStateRecovery` — EditorPrefs mirror of the dev-state snapshot (armed in `Start`, disarmed in `Finish` once the restore succeeded). SessionState dies with the editor process, so after a crash/quit mid-build this is the only surviving undo record. Its entry condition is *no active job AND (the mirror is armed OR `ContentStateGuard` has a record)* — it checks the two records independently and offers whichever is actually there, naming it in the dialog. Batchmode never auto-restores: it logs both records verbatim and clears them. `Offer` contains the two restores in SEPARATE `try`/`catch` blocks — they are independent records, so a dev-state restore that throws (read-only file, vanished volume, a VCS-locked version store) must not skip the content-state restore whose record the `finally` is about to delete — and disarms BOTH records in that `finally`, so a restore that throws cannot make the dialog reappear on every subsequent domain reload. Every path that clears a record without applying it logs it verbatim, including a `ContentStateGuard.Restore` that returns false.
- `ContentStateGuard` — rollback record for the Addressables content-state file, held in `EditorPrefs` and nowhere else. Unlike `OrphanedDevStateRecovery`, which MIRRORS a `SessionState` snapshot, this record has no second copy: lose it and the rollback is gone, since the file is git-ignored and its `Library/UnicoBuild/` backup is overwritten by the next Addressables run. Armed by `AddressablesStage` immediately after it switches the active profile, and before any content operation — the only window where `ContentUpdateScript.GetContentStateDataPath` resolves correctly, since it evaluates through the ACTIVE profile and falls back to a default derived from the ACTIVE build target (both still wrong when `DevStateSnapshot` is captured in `Start`), while the clean and build steps below it are what overwrite the file. Every record carries the arming job's `BuildJobState.StartedTicksUtc` as a stamp: `Arm(path, existedBefore, backupPath, jobStamp)` rejects a stamp no `Finish` could ever match (zero — what `BuildJobState.Load()` reports outside a live job — negative, or the `AnyJobStamp` sentinel) and normalizes `\` to `/` in both stored paths; `Restore(expectedJobStamp)` applies the record only when the stamp matches, logging and skipping a record armed by a different run; `IsArmedFor` / `DisarmIfOwnedBy` let a caller act on its own record without touching a foreign one. `AnyJobStamp` is the apply-side sentinel meaning "regardless of owner", used ONLY by `OrphanedDevStateRecovery.Offer`, which restores a record left by a run that died in a previous editor session and so has no live job to compare against. `internal` by design — a public surface would let any host script call `Restore(AnyJobStamp)` and unconditionally apply whatever is armed; the test fixture reaches it through `InternalsVisibleTo` (`Editor/AssemblyInfo.cs`). Consulted by `Finish` on the failure path and by `OrphanedDevStateRecovery` after a crash. Deliberately UNGATED so those callers need no `#if`; only the gated stage arms it, and the local-vs-remote decision uses Addressables' own `ShouldPathUseWebRequest` inside that stage. `Restore` uses `AssetDatabase` only for paths under `Assets/` and raw file I/O otherwise, since the content-state path can legitimately sit outside the project (`Local.BuildPath` under `Library/`, `Remote.BuildPath` under `ServerData/`). Reading the record NEVER throws: an unparseable payload is logged verbatim (the log line is the last copy) and erased, because both readers sit where a throw is unrecoverable — `Finish`'s `finally` reaches it upstream of `BuildJobState.Clear()` (a throw would wedge the job `Active` forever), and `OrphanedDevStateRecovery`'s `[InitializeOnLoad]` constructor reaches it before its `Disarm()` calls (a throw would become a `TypeInitializationException` on every domain reload). Erasing rather than keeping it is the same trade `PostSuccessRunner.Parse` makes: a record that cannot be parsed protects nothing, and keeping it would hold `IsArmed` true and re-offer recovery forever.
- `BuildStepRegistry` — TypeCache discovery of `[UnicoBuildStep]` host steps, deterministic ordering (anchor → order → full name, ordinal), splicing around the built-ins, and name-based re-materialization. The pipeline is FROZEN into `BuildJobState.StageTypeNames` at Start; a vanished type fails the job cleanly. Registration rules live in the pure `RejectionReason` (abstract, open generic, wrong interface for the anchor, no parameterless constructor — logged and skipped, never coerced); `Deduplicate` collapses one class reaching TypeCache from two assemblies (sample imported AND copied) so it cannot run twice. `Materialize` reports a throwing constructor's ROOT cause, not `TargetInvocationException`'s placeholder text.
- `PostSuccessRunner` — SessionState-queued `IPostSuccessStep` execution after a fully successful job; failures are isolated ("Post step FAILED" on the result of the run the queue was armed for — an outcome whose run is no longer current is logged only) and never flip Success. The queue is claimed ONE run per update tick (`Claim` returns the run plus the JSON to re-persist for the runs behind it), so a post step that queues a domain reload — or an editor crash mid-drain — costs that one run instead of every run behind it. `Parse` never throws: `Arm` runs inside `Finish`'s `finally`, where an unreadable payload would otherwise escape and skip the summary log of an already-successful build. Host steps contribute back through `UnicoBuildService.AppendPostArtifact(stamp, path, kind)` — the one PUBLIC member of that pair; `AppendPostStep` stays `internal` because a host that needs to report a problem throws instead, which the runner already turns into "Post step FAILED". The stamp a host passes is `result.StartedUtc` off the `BuildResult` it was handed, which is the same value the runner attributes by, and a wrong one is rejected by `ShouldRecordPostStep` rather than mis-filed. Its list-mutating half, `AppendArtifactTo`, is a pure tested core that pads `TypedArtifacts` up to `Artifacts` before appending — `Finish` keeps the two index-aligned, and a legacy payload hydrates with fewer typed entries than paths.
- `UnicoBuildCli` — headless entry point (`Build`, invoked via `-executeMethod`, never `-quit`)
  that parses CI command-line flags into a `BuildRequest` and starts the job; CI defaults
  deliberately differ from the panel — nothing bumps unless explicitly flagged; also injects
  keystore secrets from `UNICO_KEYSTORE_PASS` / `UNICO_KEYALIAS_PASS`.
- `CiCompletionWatcher` — concludes a CLI-started job: writes the machine-readable result file and
  exits the process with 0 (success), 1 (failure), or 2 (timeout). Armed only by `UnicoBuildCli`,
  every action additionally guarded by `Application.isBatchMode` so a stray SessionState record
  can never terminate an interactive editor.

Rules:
- NEVER hold build progress in static fields. Domain reloads (platform switch, global define removal) wipe statics; all cross-reload state must live in `BuildJobState` (`SessionState`-backed) and be re-loaded in `Advance`.
- A stage that mutates global scripting defines must let `Advance` detect the change (defines-hash comparison) and stop, so the resumer re-enters at the next step. This is the path the test-mode kind rule takes in BOTH directions (Test adds the define, Release removes it) — a Test build for a target whose committed globals lack the define now reloads exactly like a Release build always has. Player-only, no-reload defines go through `ctx.ExtraScriptingDefines` instead.
- The queued-reload window is guarded by the `s_reloadPending` sentinel: `SetScriptingDefineSymbols`/`SwitchActiveBuildTarget` only QUEUE the recompile, and `isCompiling` stays false until it starts — a bare update tick could otherwise re-enter `Advance` and run stages on stale assemblies. The sentinel is a non-serialized static, so precisely the domain reload we are waiting for is what clears it. Any new reload-causing path must set it before returning.
- `Advance` returns early while `EditorApplication.isCompiling` / `isUpdating`; the resumer will call again.
- A rejected platform switch (module not installed) must `Finish(fail)`, never busy-loop — `SwitchActiveBuildTarget` returning false leaves the target unchanged forever.
- `Finish` must always run `DevStateSnapshot.Restore()`, on both the success and failure paths; on failure it additionally runs `RestoreVersions()` so a failed run's version bumps roll back (a retry then starts from clean values).
- `Start` re-runs pre-flight and aborts on the first `Block`; the panel is responsible for `Warn` acknowledgement before it calls `Start`.
- A stage can NEVER patch `BuildJobState.SnapshotJson`: `Advance` holds the state in memory and calls `Save()` after every stage, which would overwrite the patch at the next stage boundary — and `IBuildStage.Execute(BuildContext)` gives stages no access to it anyway. State a stage must hand to `Finish` goes through `ctx.Data` (session-scoped) or its own EditorPrefs record (crash-surviving), as `ContentStateGuard` does.
- The dev-state mirror and the content-state record are INDEPENDENT — there is no *guard armed ⟹ dev-state mirror armed* invariant, and nothing may be written that assumes one. `Finish` disarms `OrphanedDevStateRecovery` on its `restored` flag, but disarms the content-state record only through `DisarmIfOwnedBy(state.StartedTicksUtc)` — never a bare `Disarm()`, which would delete a foreign run's only rollback a line after `Restore` deliberately declined to apply it. It does so whenever the job itself succeeded as well as when the restore ran (`jobSucceeded || restored`, with `jobSucceeded` pinned BEFORE the `try` because the `catch` overwrites `success`): a successful run's record holds pre-build state, so leaving it armed lets crash recovery revert or delete the content state of a build that actually shipped. Either record can therefore outlive the other, which is exactly why `OrphanedDevStateRecovery` checks each one on its own.
- **Reachability audit (2026-07-31), recorded so the same ghosts are not chased twice.** Two
  structural suspicions about `Finish`'s restore block were audited to closure: (a) "a successful
  run's version bumps rolled back by orphan recovery" and (b) "a next `Start` overwriting a
  previous run's un-recovered rollback records". Both require an exception to escape `Finish`'s
  try with specific `success` values — and a four-lens hunt (13 candidate throwers, every one
  refuted adversarially, key editor APIs verified by disassembling the shipped 6000.0.62f1
  assemblies) found NO realistic thrower. Decisive facts: `RestoreVersions()` and
  `ContentStateGuard.Restore()` are both `!success`-gated, so the success path executes only
  define reads, one `FromJson` of a string this code wrote, `snap.Restore()` (native setters that
  log rather than throw) and a `List.Add`; and `AssetDatabase.SaveAssets` surfaces unwritable
  assets as console errors, not managed exceptions (the old "VCS-locked store" example was
  unmeasured, and this project runs no VCS provider). Verdict: both suspicions are structurally
  real asymmetries but UNREACHABLE on the targeted Unity; recorded here instead of "fixed". If the
  restore block ever gains a call that can genuinely throw, re-run that audit before shipping it.
- **Open questions from the 2026-07-31 audit — looked at, NOT settled; each needs a live
  experiment, and none is a confirmed defect.** Recorded because the only other copy lived in a
  temp workflow output; an unrecorded question gets re-discovered at full price or never.
  (1) ~~Does `BuildPipeline.BuildPlayer`~~ — or a modal `EditorUtility.DisplayDialog` — ~~pump
  `EditorApplication.update`?~~ **BuildPlayer half MEASURED 2026-08-13** (batchmode, real BTA
  iOS Test player build via the CLI pipeline, Unity 6000.0.62f1): an `[InitializeOnLoad]`
  update handler counting ticks inside an `IPreprocessBuildWithReport` →
  `IPostprocessBuildWithReport` window (callbackOrder -10000) observed **zero** update ticks
  during the whole `BuildPlayer` call. In batchmode, `Advance`'s missing re-entrancy guard is
  not reachable through BuildPlayer pumping. The MODAL-DIALOG half stays OPEN: batchmode never
  shows modals, so an interactive-editor panel build could still differ — re-run the same probe
  interactively before relying on it there. If yes there, `Advance` has no re-entrancy guard
  and `OrphanedDevStateRecovery.Offer` does not re-check `BuildJobState.Active`; both would be
  serious.
  (2) ~~The `[InitializeOnLoad]` registration order of `CiCompletionWatcher` vs `PostSuccessRunner`~~
  **MEASURED 2026-08-25** (batchmode, Unity 6000.0.62f1, reflection over
  `EditorApplication.update`'s invocation list, stable across two sessions):
  `CiCompletionWatcher.OnUpdate` sits at index 0, `PostSuccessRunner.OnUpdate` at index 1 —
  **the watcher decides BEFORE the runner drains within every tick**. Consequence: on the exact
  tick the deadline passes, still-queued post-success runs are dropped by `TimeoutReset` rather
  than drained (a successful job concludes as exit 2). This is a one-tick-wide boundary and is
  ACCEPTED BY DESIGN: the deadline exists precisely to kill queues that cannot drain, and
  `Decide`'s concluded-job-outranks-deadline rule still protects every case where the queue
  emptied first. On ticks before the deadline the order only costs +1 tick of conclusion
  latency (runner drains at index 1, watcher concludes next tick).
  (3) ~~Does the Editor asmdef's unconditional `Unity.Addressables*` reference list break
  compilation in a host WITHOUT Addressables installed?~~ **MEASURED 2026-08-07, C3 phase 1**:
  the unity-build-tools dev project opened WITHOUT Addressables — compile clean (exit 0, zero
  CS errors), suite green at 239/243 (the gated tests contribute nothing, exactly as designed).
  The versionDefines premise holds: Unity skips name-based asmdef references to absent
  assemblies, for the Tests asmdef's ungated reference too. Adding Addressables 2.7.6 restored
  the full 242-package-test parity plus Unity's own DocExampleCode stub.
  (4) An exception escaping `Finish`'s **finally** (the 2026-07-31 reachability audit covered the
  TRY only) would wedge the job `Active` forever, beyond even `ResetStuckJob`. No concrete
  thrower found; the question is whether one can exist.
  (5) `ProjectDefinesHash` proves "the define string changed", not "a domain reload was queued" —
  a host hook that triggers a recompile any other way (asmdef edit, `ImportAsset`) would run the
  remaining stages on stale assemblies, invisibly. No such hook exists today.
  (6) ~~Anything escaping `Start` turns an instant failure into a full `-timeoutMinutes` hang
  and exit 2.~~ **MEASURED 2026-08-25 — the hang does not exist** (batchmode, Unity
  6000.0.62f1; probe: `throw` injected at the top of the CLI-facing `Start` overload in a
  clone, run via `ci/run-player-build.sh` with a 2-minute deadline): Unity itself aborts
  batchmode when the `-executeMethod` target throws ("Aborting batchmode due to failure") —
  **exit 1 in ~10 s wall time**, no deadline wait, no watcher tick, no stale SessionState
  record (it dies with the session). The real cost is only that NO result JSON is written —
  the failure cause lives in the editor log; `ci/run-player-build.sh` fails loudly on the
  missing result and names the log. Optional nicety (not a defect): wrapping the `Start`
  call in `UnicoBuildCli.Build` with a try/catch that writes a failure result before
  `Exit(1)` would make the cause machine-readable.
  (7) Neither `PostSuccessRunner` nor `CiCompletionWatcher` observes the private
  `s_reloadPending` sentinel (they poll `isCompiling`/`isUpdating` only), so post-success steps
  can run before the restore reload lands; the audit judged `PostSuccessRunner`'s own header
  comment wrong on this point but articulated no concrete harm — treat that comment as an
  UNVERIFIED claim until measured.
