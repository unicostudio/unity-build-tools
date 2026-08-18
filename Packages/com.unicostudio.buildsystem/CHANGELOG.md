# Changelog

## [0.12.2] - 2026-08-18

Adoption-readiness release (measured against a three-way audit of what a newcomer can
self-discover): no behavior change for correctly configured projects.

### Added
- **`ConfigPresenceCheck`** (preflight, strict-exempt): Pass names the `BuildTargetConfig`
  the build resolved; Warn explains the fallback when no config matches (or none exist).
  The zero-config trap was invisible before: a config-less project builds "successfully"
  on the kind-based profile fallback with no ExtraDefines/StripDefines/StripPackages and
  nothing saying so — absence is the one failure mode no config-consuming check can flag.
  Advisory by design (`IsStrictExempt`): a deliberately config-less project is a
  legitimate adoption stage, so strict CI builds of one must stay possible. Suite
  290 -> 296 (the strict-exempt-set guard test caught the policy change, as built to).
- **VersionTracker Glue sample** (`Samples~/VersionTrackerGlue`): the host-side
  PostSuccess step coupling this package with `com.unicostudio.versiontracker`
  (freshness-verified metadata attached to the build result). Previously the glue existed
  only in consumer repos and a new adopter could not know it should exist; without it the
  degradation is silent (no Stale/Missing verdicts, no Metadata artifact). Requires the
  tracker >= 1.6.0; the sample README documents the committed-per-release convention that
  gives `Stale` its meaning.
- Package Manager metadata: `documentationUrl` / `changelogUrl` / `licensesUrl` and an
  in-package `LICENSE.md` (matching the versiontracker package's surface), so the UPM
  window links to the docs instead of dead-ending.

### Fixed
- `AddressablesVersionStore` fresh-asset default is now `1` (was `11` — a shipped game's
  live counter had leaked into the template default, so a new adopter's content lineage
  started at v11 unexplained). Existing assets are unaffected: serialized values win.

### Docs
- README caught up from the 0.10.x surface to 0.12.1: `StripPackages` (with its preflight
  preconditions and cost), the `DefineGuard` + `DefineReassertWatcher` defense stack,
  `ctx.RequestReload()` for hook authors, the result-JSON schema table (previously
  readable only in `BuildResult.cs`), an Install section (git URL + pin + testables +
  private-repo access prerequisite), and the `Samples~/` map entry.

## [0.12.1] - 2026-08-17

### Fixed
- **Strip-window last-writer guard** (`DefineReassertWatcher`): newer third-party
  dependency resolvers subscribe to UPM package-change events and can rewrite the
  platform defines IN-SESSION — after ConfigureDefinesStage's write, before the queued
  reload lands (measured on a host: the surviving "define present, package gone"
  half-state failed every compile and wedged the job to the CI deadline; whether the
  reload or the event handler wins is a race). The stage now arms a same-domain watcher
  that re-asserts the written define plan on every editor tick until the reload lands;
  the fight is bounded by construction — the watcher's subscription and the adversary's
  code both die with the reload, the adversary cannot restore the stripped package, and
  Finish disarms the watcher before the dev-state restore on same-domain exit paths so
  it never fights the restore itself. Suite 280 -> 290.

## [0.12.0] - 2026-08-17

### Added
- **StripPackages**: `BuildTargetConfig.StripPackages` lists UPM package ids removed from
  `Packages/manifest.json` for the build's duration and restored byte-exact afterwards —
  the package-level sibling of `StripDefines`, for packages whose `[InitializeOnLoad]`
  code fights the define plan (measured: Unity-MCP re-adds `UNITY_MCP_READY` on every
  reload and has no off switch). Spec with the feasibility measurements:
  `docs/specs/2026-08-17-strippackages-design.md` (~+30 s per build; restore
  byte-determinism proven for exact pins).
  - The manifest edit lands ATOMICALLY with the define write — one combined reload —
    inside an auto-refresh bracket (measured: split steps break compilation; unbracketed,
    the define write's compile races the PackageCache removal into a transient failed
    pass). `ConfigureDefinesStage` owns both mutations for exactly this reason, and pumps
    an explicit `AssetDatabase.Refresh()` after closing the bracket: AllowAutoRefresh does
    not replay the refresh it suppressed and batchmode has no ticks that would — measured
    E2E, the unpumped bracket wedged the job to the CI deadline with the recompile never
    starting, for plain StripDefines builds too.
  - `PackageStripGuard` (ContentStateGuard's shape): job-stamped record + byte backups of
    manifest/lock under `Library/UnicoBuild/`; restore is UNCONDITIONAL in `Finish`
    (success and failure alike), offered by the interrupted-build dialog with its own
    containment, logged-and-cleared in batchmode (tracked files —
    `git checkout -- Packages/` heals a crashed checkout), and pre-cleaned by the CLI.
  - New preflight `StripPackagesCheck`: a listed package with dependents in the lock is a
    Block (dependency-graph inversion is out of scope); a non-exact pin is a Warn (the
    byte-deterministic restore was measured for exact pins only); absent ids are named
    no-ops.
- **Reload-request signal path (Q5, partial)**: `BuildContext.RequestReload()` lets a
  stage (or hook) tell `Advance` about a queued reload the defines-hash cannot see — the
  StripPackages resolve is the first user. Host-initiated recompiles outside the pipeline
  remain Q5-open.
- Suite: 257 -> 280 (Q5 flag semantics, PackageStripGuard cores incl. dangling-comma
  repair and exact-pin/dependents heuristics, StripPackagesCheck evaluation), all
  red-first with mutation probes.

## [0.11.0] - 2026-08-13

### Added
- **Define guard**: the build's define plan is re-verified against the platform's ACTUAL
  global defines at the last moment before `BuildPipeline.BuildPlayer`, and a violation fails
  the build loudly with the offending define(s) named (`BuildFailedException`, plus a
  `Define guard BLOCKED the player build` step in the result).
  - Why: the global define write in `ConfigureDefinesStage` queues a domain reload, and that
    reload wakes third-party `[InitializeOnLoad]` code that can silently fight the plan.
    Measured live (2026-08-13, BTA iOS Test build): Unity-MCP's DependencyResolver re-added
    `UNITY_MCP_READY` on the very reload the strip triggered, and the MCP bridge shipped into
    the player's IL2CPP output while the stage's own report truthfully said the define had
    been stripped. Nothing in the pipeline observed the world changing after the write.
  - Mechanics: `ConfigureDefinesStage` records the plan into `BuildContext.Data` (persisted
    through `BuildJobState`, so it survives the reload). The must-be-absent side uses the
    resolver's `ForbidInPlayer` set — not `RemoveFromGlobal`, which is empty exactly when the
    define was already absent at stage time, i.e. when a later third-party re-add would
    otherwise go unguarded. The must-be-present side guards the kind-rule define the same way
    in the opposite direction.
  - Pure decision core (`DefineGuard.FindViolation`) plus plan record/parse helpers are
    unit-tested (11 tests, mutation-probed kill-then-red); suite goes 246 -> 257.

## [0.10.2] - 2026-08-07

### Fixed
- Panel artifact "Show" now reveals the normalized path. Producers compose artifact paths their
  own way — the VersionTracker glue legitimately records `Assets/../UnicoVersionTracker/…` — and
  `File.Exists` resolves that against the project root, but the OS file viewer behind
  `RevealInFinder` does not: the raw `..` segment opened the wrong folder (surfaced live by BT5's
  C4 Gate 2). Check, reveal and the not-found dialog now share one `Path.GetFullPath`-normalized
  path (`ResolveArtifactPath`, unit-tested; unresolvable strings fall back raw so the dialog names
  what the record holds).

## [0.10.1] - 2026-08-07

### Changed
- Documentation only — zero code changes. The package now lives in the `unicostudio/unity-build-tools`
  monorepo (`Packages/com.unicostudio.buildsystem`; history stays in `g-brain_test_legacy`). The
  README's opening line no longer names a specific game, and the audit's last open portability
  question is closed with a measurement: the dev project compiled the package WITHOUT Addressables
  (clean, suite green at the reduced count), proving the `versionDefines` contract — an
  Addressables-less host works out of the box, gated features simply absent.

## [0.10.0] - 2026-08-07

### Changed
- **The symbols archive is now taken from the build's own report, not a glob.** The §6.1 AAB gate
  measured what the slice-3 spec had left open: on a real 6000.0.62f1 AAB, `report.GetFiles()`
  lists the uploadable archive exactly once, role `"Symbols"`, at its output-folder path — the
  same file the old `*.symbols.zip` glob found. `PlayerBuildStage` now asks the report (pure,
  tested `SelectSymbolsArchives`, project-relative output) and the glob plus its mtime freshness
  filter are deleted: identity now comes from the report belonging to THIS `BuildPlayer` call, so
  no timestamp heuristic is needed. The `.symbols.zip` suffix survives only as a guard on
  API-returned values. With this, the LAST carve-out of the boundary program's acceptance
  criterion closes — the package no longer reconstructs any convention it does not own.

### Removed
- **`PlayerBuildStage` no longer knows the version tracker exists.** `AddVersionTrackerStep` — the
  package reconstructing a third-party tool's filename convention (`UnicoVersionTracker/{product}_
  {version}_{platform}_BuildInfo.json`) and probing it with a bare `File.Exists` — is deleted,
  together with the tracker citation in `BuildArtifactNaming`'s comment. This closes carve-out (2)
  of the boundary program's acceptance criterion. The replacement lives where the knowledge
  belongs: HOST glue (`Assets/02_Scripts/Editor/VersionTrackerArtifactStep.cs` in this game), a
  `PostSuccess` step that asks the tracker's own `GetBuildInfoPath` (tracker ≥ 1.6.0, whose hook
  writes synchronously), rejects a MISSING file loudly and — unlike the deleted step — also
  rejects a STALE one: the outputs are committed per release and the version does not
  auto-increment, so a rebuild of the same version finds the previous build's file on disk even
  when this run's hook silently failed. Freshness = mtime at-or-after the run's `StartedUtc`, the
  same pattern this stage already applies to the symbols archive. A found-and-fresh file is
  registered via `AppendPostArtifact` and appears under the panel's Artifacts with a Show button.
  The package itself requires nothing: a host without the tracker simply does not have the glue
  file.

## [0.9.6] - 2026-07-31

### Changed
- **The two remaining audit suspicions about `Finish`'s restore block are closed as UNREACHABLE,
  with the record of why in `Editor/Core/_Info.md`.** A dedicated four-lens reachability hunt
  (13 candidate throwers, all adversarially refuted, key editor APIs verified by disassembling the
  shipped 6000.0.62f1 assemblies) found no realistic exception that can escape the restore try.
  Decisive facts: `RestoreVersions()` and `ContentStateGuard.Restore()` are `!success`-gated, so
  the success path executes nothing that can throw; and `AssetDatabase.SaveAssets` surfaces
  unwritable assets as console errors, not managed exceptions. The structural asymmetries the
  audit found are real but cannot fire, so they are recorded instead of "fixed" — and if the
  restore block ever gains a call that can genuinely throw, that audit must be re-run first.
- `Finish` gains a null guard over the parsed `DevStateSnapshot`: `JsonUtility.FromJson` returns
  NULL for a null/empty payload rather than throwing (measured; whitespace is the odd one out and
  throws `ArgumentException`, equally contained — a tripwire test pins the contract), so the
  unreachable-today empty-`SnapshotJson` case degrades to a clear exception instead of an NRE
  wearing a misleading message.
- Two comments corrected: `BuildJobState`'s "callers null-check them" claim was audited FALSE —
  the caller relies on a parse-result guard instead, and the claim had been introduced by this
  very audit cycle's earlier fix, so claims about callers now stay at call sites.
  `OrphanedDevStateRecovery`'s "VCS-locked SaveAssets" example is flagged as unmeasured: the
  containment stays, but the sentence can no longer be cited as evidence that a specific call
  throws — which is exactly how it misled one audit already.

## [0.9.5] - 2026-07-31

### Fixed
- **The keystore sample could never work on the panel path — it is an on-load injector now.**
  `KeystoreInjectionStep` was a `[UnicoBuildStep(PreBuild)]` hook, but preflight runs before every
  hook anchor and `KeystoreCheck` reads the live keystore passwords during preflight (Unity never
  serializes them, so a fresh session holds them empty). For Android + Release + Build Player the
  check therefore blocked the job before the hook ever executed — while its message told the
  developer to provide the very environment variables the hook was already reading. The sample is
  now `Samples~/Hooks/Editor/KeystoreEnvInjection.cs`, an `[InitializeOnLoad]` injector, so the
  values exist before any preflight can run. No new anchor was added, deliberately: a "PreCheck"
  stage would grow the resume/StepIndex surface for exactly one consumer. The general rule is now
  recorded in `Editor/Checks/_Info.md`: code that feeds state preflight READS must run on editor
  load, never as a hook. The CLI path was never affected — `UnicoBuildCli` injects before `Start`.
  (Samples are not compiled with the package; the rewritten file was compile-verified by
  temporarily importing it into the host project.)

## [0.9.4] - 2026-07-31

### Changed
- **The six remaining suspicions from the assertion audit, confirmed and closed.** Same discipline
  as 0.9.3: each behaviour was killed in production code with the suite measuring green (the proof
  of blindness), then the strengthened test was proven load-bearing by re-running the same mutation
  and watching it fail BY NAME. What was unprotected: assembly qualification in the persisted
  pipeline type names (a `FullName` regression would break resume for every host-assembly hook —
  only a foreign-assembly type in the round-trip can see it), the Block on an empty effective
  version (previously asserted by test NAME only; `VersionFormatCheck` gains a pure `Verdict` core
  so it is executable without mutating live `PlayerSettings`), `RemotePathCheck`'s null guard
  ("" already blocks via `StartsWith`, so null is the clause's only observable job), the parallel
  `ArtifactKinds` write (deleting it silently turned every artifact `Unknown`), the CLI's
  unconditional `-buildCode` disable (only the guard-decided flag order was tested), and the
  duplicate-config error naming both assets (the regex ignored the names). Two factually wrong
  test comments corrected. Production behaviour unchanged except the `Verdict` extraction, which
  is a pure refactor of `VersionFormatCheck.Run`.

## [0.9.3] - 2026-07-31

### Changed
- **Eight blind assertions strengthened after a suite-wide audit.** Each protected behaviour was
  first KILLED in production code and the suite measured GREEN (229/0) — the proof the tests were
  decorative — then the strengthened test was proven load-bearing by re-running the same mutation
  and watching it fail. The behaviours that were unprotected: the per-index pairing of preflight's
  strict-exempt flags (a `[0]` typo would block CI Release builds on the CDN reminder), the
  serialization of `BuildResult.Success` (a `[NonSerialized]` regression would show every
  reload-surviving build as failed), post-success run attribution in BOTH directions (recording on
  the matching run, rejecting a superseded one — previously a single test that passed with
  attribution disabled entirely), `ContentStateGuard`'s empty-path guard (only observable with a
  live backup planned for restore), `BuildStepRegistry`'s non-stage guard (the old input had no
  parameterless ctor, so `Activator` threw before the guard was ever reached — measured), and the
  define resolver's direction rule (the old test's name claimed "never both populated", which its
  own passing data contradicts). Production behaviour is unchanged except comments: `BuildJobState`
  carried the same factually-wrong "JsonUtility skips field initializers" claim corrected in
  `BuildResult` at 0.9.2, now fixed the same way, with raw-hydration tripwire tests pinning the
  measured contract in both types.

## [0.9.2] - 2026-07-31

### Fixed
- **An omitted `-versionName` no longer blocks a CLI build.** Version Name is an optional
  override — `ApplyVersionStage` writes `bundleVersion` only for a non-blank request, so a blank
  one means "ship the project's version". `VersionFormatCheck` nevertheless validated the raw
  request string and blocked on empty, which broke both of README's CI recipes (they omit
  `-versionName`) and made "just bump the build code" inexpressible. The check now validates the
  version the build will actually ship: the request when present, `PlayerSettings.bundleVersion`
  otherwise — which also means the project's real version is finally validated on the one path
  that relies on it. Both absent still blocks: an empty effective version fails the semver regex.

### Changed
- `BuildResult.Normalize()`'s comment claimed JsonUtility skips field initializers, leaving a
  legacy payload's collections null and `AddressablesVersion` at `0`. Measured FALSE on
  6000.0.62f1: `FromJson` runs the initializers, so a legacy payload deserializes with non-null
  collections and the documented `-1` sentinel out of the box. The false claim sent a code audit
  chasing a phantom defect; the comment now records the measurement, `Normalize` stays as cheap
  insurance, and the legacy-payload test asserts on the deserialized object (it previously
  asserted on a freshly constructed one, which proved nothing) so any future Unity behavior
  change trips it.

## [0.9.1] - 2026-07-31

### Fixed
- **A PreBuild hook's build-only define could ship in the player on platforms whose globals never
  carried it.** `ConfigureDefinesStage` evicted hook-added defines using `RemoveFromGlobal`, which
  is strip ∩ current globals — so the eviction worked exactly when the define was already global
  (and about to be removed anyway) and silently no-oped otherwise. With this repo's committed
  defines: iOS globals carry no `TEST_MODE`, so iOS + Release + a hook adding `TEST_MODE` shipped
  the define straight through `extraScriptingDefines`, with no error and no summary line — while
  the identical hook on Android was evicted correctly. `DefineDelta` gains `ForbidInPlayer`
  (strip plus the test define on non-Test builds, independent of the current globals) and the
  stage evicts from that. The suite missed it because the eviction test fed `ApplyExtraDefines` a
  hand-built list; a new test drives it through `Resolve`, exactly like the stage does.
- **A duplicated `BuildTargetConfig` could silently redirect a build.** `LoadAll` ran `FindAssets`
  unscoped over an unordered GUID list and `Find` took the first match, so a stray copy (an
  editing accident, a merge resurrecting an old path) could swap the build's Addressables profile,
  expected bundle id and `StripDefines` with nothing logged — and "first" was a different asset on
  a different machine. Discovery now matches the sibling loaders (`BuildSystemSettings`,
  `AddressablesVersionStore`): scoped to `Assets/`, ordinal-sorted by path, and `Find` logs an
  error naming every duplicate for a platform+kind before using the first.

## [0.9.0] - 2026-07-30

### Added
- `UnicoBuildService.AppendPostArtifact(resultStamp, path, kind)` — the public counterpart of the
  internal `AppendPostStep`. A host `IPostSuccessStep` can now register a file it produced or
  verified on the already-recorded result, so it appears in the panel's **Artifacts** list with a
  working "Show" button rather than only as a line of text. Same run-attribution guard as
  `AppendPostStep`: an outcome whose run is no longer current is logged, never recorded. It is
  public where `AppendPostStep` is not, because contributing an artifact is precisely what a host
  post-success step exists to do — and the stamp costs the host nothing, since `PostSuccessRunner`
  hands the step the `BuildResult` whose `StartedUtc` IS the stamp it attributes by. A host
  reporting a PROBLEM still needs no API: a throwing step is already surfaced as
  `"Post step FAILED: …"`.
- `ArtifactKind.Metadata`, for build metadata a host tool emits alongside the build. Appended at
  the END of the enum, deliberately: `BuildResult` round-trips through `JsonUtility`, which writes
  enum values as integers, so a member inserted anywhere else would silently re-map every artifact
  kind already persisted in `SessionState` or in a CI result file.

## [0.8.0] - 2026-07-29

### Added
- `AddressablesProfilePaths` (Runtime) — the six properties an Addressables profile binds its remote
  paths to: `Remote{Build,Load}{Development,Test,Production}Path`. **Adopting the package for
  Addressables no longer needs host code at all.** It used to require a global-namespace class in
  Assembly-CSharp holding the game's CDN root, because that is what the profile variables named.
  They can name the package type instead: Addressables splits a `[Type.Member]` token on the LAST
  dot and calls `Assembly.GetType` on the left half over every loaded assembly, so
  `[UnicoStudio.BuildSystem.AddressablesProfilePaths.RemoteLoadProductionPath]` resolves exactly as
  the host class did. Adoption is now a settings asset plus six profile strings. The whole file is
  `#if UNITY_EDITOR` for the same reason the host class had to be: profile variables are evaluated
  at BUILD time and baked into the catalog, never resolved in a player.
- `BuildSystemSettings.RemoteLoadRoot` — the host's remote content root, with `IsValidRemoteLoadRoot`
  (an `http(s)` scheme test, ORDINAL and case-insensitive: schemes are case-insensitive per RFC 3986,
  while culture-sensitive matching treats some zero-width characters as ignorable and would accept a
  root no CDN can serve), `EffectiveRemoteLoadRoot`, and `RequireRemoteLoadRoot()`.
- `RemotePathCheck` (Editor/Checks) — pre-flight **Block** when the build's profile has a
  `Remote.LoadPath` that does not resolve, has malformed `[`/`]` delimiters, or resolves to something
  that is not a URL. `AddressablesStage` already refused to build on this, but only after the
  platform switch and its domain reload; checking in pre-flight turns minutes into a Block before
  anything happens. Registered immediately after `AddressablesAvailabilityCheck`, deliberately —
  `PreflightRunner.Gate` reports the first blocking message, and "the package is not installed" is
  the clearer one when both would fire.
- `ProfileBindingRule` (Editor/Support) — the shared verdict `AddressablesStage` and `RemotePathCheck`
  both act on, so pre-flight can never pass a value the build then rejects.
- `BuildConfigCatalog.ResolveProfile` — the single implementation of "which Addressables profile does
  this build use".

### Changed
- **CONTRACT CHANGE: `BuildSystemSettings.RemoteLoadRoot` is now REQUIRED for a host that builds
  Addressables content.** Previously the whole asset was optional and every knob on it degraded to a
  safe default. This one does not, deliberately: `TestModeDefine` falls back to `TEST_MODE` because a
  wrong-but-present define is safer than none, whereas a guessed CDN root would publish content to,
  or load it from, the wrong host. A missing asset and an unusable value throw with two different
  messages, since "create the asset" is useless advice to someone who already has one. The asset
  stays fully OPTIONAL for player-only hosts, which never evaluate the load-path bindings.
  **Migration for an existing host:** create `BuildSystemSettings` (convention
  `Assets/Settings/Build/`), move the CDN root out of the host-side shim into `RemoteLoadRoot`,
  repoint the six profile values at `UnicoStudio.BuildSystem.AddressablesProfilePaths.*`, and delete
  the shim.
- The stage's binding guard no longer names a host class. It used to detect an unresolved binding by
  testing the evaluated value for one host game's class name as a string literal — a thing a shared
  package must not know, and a test that stops working the moment the binding moves anywhere else.
  `ProfileBindingRule` derives the same answer from the RAW token's own inner text, so
  it holds for package-owned and host-owned bindings alike. The bracket test is NOT redundant and was
  kept as a second, distinct verdict: a well-formed token that fails to resolve loses its delimiters
  (`"[A.B]"` evaluates to `"A.B"`), so a surviving `[` or `]` can only mean the raw value's own
  delimiters are unbalanced — a different problem with a different fix, and one no token extraction
  can see.
- The Addressables profile is now resolved BEFORE pre-flight runs, by every caller —
  `UnicoBuildService.Start` and `BuildPanelWindow`'s display context both call
  `BuildConfigCatalog.ResolveProfile` when they construct the `BuildContext`. `Start` used to inline
  the resolution AFTER pre-flight, which left every check that consulted the profile reading
  `BuildContext.Profile`'s initialiser — `Test`, whatever the request. It was latent while no check
  read the field; with `RemotePathCheck` reading it, a Release build would have had its Test
  profile's remote paths validated and its Production paths never looked at.
- `AddressablesStage` now calls `AddressablesRuntimeProperties.ClearCachedPropertyValues()` before it
  arms the content-state guard and before it evaluates any profile variable. That cache is
  domain-lifetime, and the only thing that clears it is Addressables' own `BuildScriptBase.BuildData`
  — which runs AFTER this stage validates. Without the clear, a value warmed earlier in the session
  (`ApplyVersionStage` bumping the store in this same run, or `RemotePathCheck` evaluating
  `Remote.LoadPath` on every panel repaint) would be what the stage validated: it would pass, then
  `BuildData` would clear the cache, re-evaluate, fail — and because the evaluator swallows failures
  and substitutes the token's own text, bake that text into the shipped catalog as the remote URL.
  Clearing here is what makes the stage's validation and the catalog write see the same state.

### Fixed
- `BuildDefineResolver.ValidateConfig` materializes both define lists before use. It walks each one
  twice — once for token issues, once for the both-lists intersection — and the parameters are
  `IEnumerable<string>`. Every caller today passes an array, so nothing was broken; but a host
  passing a lazy or single-pass sequence would have had the second walk come back EMPTY, silently
  dropping the "this define is in both ExtraDefines and StripDefines" finding — the one issue here
  that no other check would catch. Pinned by `ValidateConfig_SinglePassSources_StillFindsOverlap`,
  which fails against the previous implementation.
- `AddressablesStage.TryBackupContentState` and `PlayerBuildStage`'s symbols glob now null-guard
  `Path.GetDirectoryName` before consuming it, matching `CiCompletionWatcher` and `ContentStateGuard`.
  Neither input can actually produce `null` (both are `Path.Combine` results with a non-empty file
  component, so never a root path), so this is defensive only — it makes the guard uniform across the
  package and clears the analyzer warning.

## [0.7.2] - 2026-07-29

### Fixed
- The Addressables content-state rollback now protects the file the build actually uses.
  `AddressablesStage` reads and writes the content state at the path
  `ContentUpdateScript.GetContentStateDataPath` resolves, while `DevStateSnapshot` backed up and
  restored a path reconstructed from Addressables' default layout. They agree under default
  settings, so the split was latent — but a host that points `Content State Build Path` elsewhere
  had a rollback that silently protected a file the build never touched, and a failed run that
  left the real content state describing a build that never shipped.
- A remote (`http`) content-state path is now recognised as remote instead of reading as "the file
  did not exist". Nothing was destroyed before: `File.Exists` is false for a URL, so `PlanRestore`
  resolved to `None` and the rollback silently did nothing. What the run now buys is honesty — the
  stage skips arming altogether for a remote path (a URL cannot be copied, and a record that could
  never restore anything is worse than none) and says so in the step log, so a build with no
  content-state rollback reports that instead of appearing protected.

### Changed
- Content-state rollback moved from `DevStateSnapshot` to the new `ContentStateGuard`
  (`Editor/Core/`), armed by `AddressablesStage` immediately after it switches the active profile
  and before any content operation — the only window where the path resolves correctly, since
  `GetContentStateDataPath` evaluates through the active profile and falls back to a default
  derived from the active build target, and `DevStateSnapshot` is captured before both switches.
  `DevStateSnapshot` loses
  `ContentStatePath`, `ContentStateBackupPath`, `ContentStateExisted`, `ContentStatePathFor`,
  `PlanContentStateRestore`, `ContentStateRestoreAction` and `RestoreContentState`. The guard is
  `internal` (the test fixture reaches it via `InternalsVisibleTo`) — hosts never call it. A job
  started on 0.7.1 and resumed on 0.7.2 loses its content-state rollback; the record is
  session-scoped, so this is only reachable across an in-session package upgrade.
- **The content-state rollback record is now owned by the job that armed it**, stamped with that
  job's `BuildJobState.StartedTicksUtc`. This is a behaviour change, not just plumbing. A record
  left behind by one run (its `Finish` crashed before disarming) is now SKIPPED and logged by a
  later, unrelated run's `Finish`, where before it would have been applied — restoring or deleting
  a content-state file on behalf of a job that never captured it. `Finish` clears only records this
  job owns (`DisarmIfOwnedBy`), so a foreign record survives for the crash-recovery flow instead of
  being deleted a line after `Restore` declined to apply it; the file is git-ignored and its
  `Library/` backup dies with the next Addressables run, so that deletion destroyed the only
  rollback that existed. A SUCCESSFUL run's record is now discarded even when the dev-state restore
  threw, so recovery can never revert or delete the content state of a build that actually shipped.
  `OrphanedDevStateRecovery` is the one exempt caller: it applies a crashed run's record through the
  `AnyJobStamp` sentinel, having no live job to compare against. `Arm` rejects stamps no `Finish`
  could ever match — `0` (what `BuildJobState.Load()` reports outside a live job), negatives, and
  the sentinel itself — rather than arming a rollback nothing would ever apply.
- The dev-state mirror and the content-state record are no longer disarmed together on one flag;
  they are independent. `OrphanedDevStateRecovery` checks each record on its own, so a content-state
  record left by a crash is offered even when no dev-state snapshot survives (and vice versa), and
  its dialog names whichever is actually armed. Each of the two restores is wrapped in its OWN
  `try`/`catch`, with both records cleared in a `finally`: a restore that throws no longer re-opens
  the dialog on every subsequent domain reload, and — because the records are independent — a
  dev-state restore that throws (say `AssetDatabase.SaveAssets` failing on a VCS-locked version
  store) can no longer skip the content-state restore whose record the `finally` then deletes, which
  would have destroyed a rollback without ever attempting it. A `ContentStateGuard.Restore` that
  applies nothing now logs the record verbatim too, matching every other path in that file that
  clears a record without applying it.
- Reading the content-state record never throws. `JsonUtility.FromJson` raises `ArgumentException`
  on a malformed payload, and both readers sit where that is unrecoverable: `Finish`'s `finally`
  reaches it through `DisarmIfOwnedBy` upstream of `BuildJobState.Clear()`, so a throw would leave
  the job `Active` forever (the resumer re-enters `Finish` and throws again every tick, and
  `ResetStuckJob` routes through the same `Finish`), and `OrphanedDevStateRecovery`'s
  `[InitializeOnLoad]` constructor reaches it before its `Disarm()` calls, so a throw would become a
  `TypeInitializationException` on every domain reload with the records never cleared. An unreadable
  record is now logged verbatim (that log line is the last copy) and erased — keeping it would hold
  `IsArmed` true and re-trigger the recovery dialog forever.
- Player-only builds no longer copy the content-state file. `Start` used to back it up on every
  run, including runs where Addressables never executes.
- `KeystoreCheck`'s "passwords not set" message no longer names `AutoKeystore`, a script that
  exists in one host game. It names the mechanisms the package supports: Player Settings, the
  `UNICO_KEYSTORE_PASS` / `UNICO_KEYALIAS_PASS` environment variables, or a host-side editor
  injector.
- `CdnReminderCheck` derives its folder name from `UnicoBuildPaths.BUILD_PATH_BASE` via the new
  `FolderFor(version)` instead of repeating the literal.

## [0.7.1] - 2026-07-28

### Fixed
- A domain reload (or crash) part-way through draining the post-success queue no longer drops the
  runs behind the one executing. `OnUpdate` erased the whole payload before executing anything, so
  a post step that queued a reload took every queued run with it. It now claims ONE run per update
  tick and re-persists the remainder first (`Claim`), bounding the loss to the single run that was
  actually in flight. Only reachable with back-to-back programmatic builds, which is exactly what
  CI does.
- `Appended` no longer throws on an unreadable queue payload. `Arm` calls it from `Finish`'s
  `finally`, so the exception escaped `Finish` and skipped the build summary log — for a run that
  had already succeeded. An unreadable dev-convenience queue is now logged as a warning and
  dropped.

## [0.7.0] - 2026-07-28

### Fixed
- **The build kind is now visible to editor-side build participants.** Unity compiles editor
  assemblies with the ACTIVE BUILD TARGET's global defines, and `extraScriptingDefines` is a
  player-compile parameter that never reaches them. The kind rule's "add" direction used only that
  parameter, so on any target whose committed globals did not already carry the test-mode define, a
  **Test** build ran its entire editor side compiled as **Release**: `IPreprocessBuildWithReport` /
  `IPostprocessBuildWithReport` callbacks, host `[UnicoBuildStep]` steps, and the Addressables
  content build. The concrete damage this closes: a post-processor that picks credentials with
  `#if TEST_MODE` and writes them into the generated Xcode project / Info.plist shipped a Test
  artifact wired to **production** services. `ConfigureDefinesStage` now writes the kind rule into
  the global defines in both directions (`DefineDelta.AddToGlobal`), sharing the removal's single
  `SetScriptingDefineSymbols` call.
- As a consequence, the Addressables content build and the player build of the same run now always
  compile with the same define set. They could previously disagree: the player got the define via
  `extraScriptingDefines`, the content build never did.

### Changed
- A build may now trigger a domain reload where it previously did not — but only when the target's
  committed defines disagree with the requested kind. A Test build for a target that already
  carries the define, and a Release build for one that does not, write nothing and reload nothing
  (the stage skips a write whose resulting define set equals the current one). In practice this
  adds one recompile + reload before the build and one after, on exactly the platform/kind
  combinations that were silently building the wrong thing — the same cost Release builds have
  always paid.
- **Nothing has to be committed to get this.** No project needs the test define in its Player
  Settings, no developer has to remember to toggle anything: the build makes the editor match its
  own intent and `DevStateSnapshot` restores the developer's exact define string afterwards, on
  success, on failure, and via crash recovery.
- `DefinePlanCheck`'s Pass message distinguishes `+X (build-only)` from
  `+X (global, restored after the build)`.
- Config `ExtraDefines` are deliberately NOT promoted to global — they describe the player, not the
  build's intent, and keep their documented player-only contract (`BuildTargetConfig`'s tooltip).
- `DefineDelta`'s constructor takes three arrays (`addViaExtra`, `addToGlobal`, `removeFromGlobal`).
  Source-breaking only for code constructing it directly; `Resolve` callers are unaffected.

## [0.6.0] - 2026-07-28

### Added
- `TestModeDefineRenameCheck` — **Warn** when `BuildSystemSettings` renames the test define while
  the default `TEST_MODE` is still committed in some platform's global defines. That state is a
  half-finished rename (or a typo in the asset): every other check resolves the same wrong symbol
  and passes, so a Release could ship with the test features compiled in. Hosts already running
  `-strictWarnings` CI with a renamed define and the default still committed will see this build
  fail until the leftover define is removed from Player Settings — a behavior change on upgrade
  with no host code change.

### Fixed
- `CiCompletionWatcher.Decide` now concludes before consulting the deadline. A build that had
  already finished — artifacts written, post-success uploads executed — was reported as
  `Success=false` / exit 2 whenever the observing tick landed past the deadline. The deadline
  remains the escape hatch for states that cannot conclude on their own.
- The CI timeout path flushes **after** `ResetStuckJob`. The single flush ran before the rollback,
  so a wedged run persisted its bumped versions and stripped defines and then discarded the
  in-memory rollback at `Exit(2)`, leaving a persistent CI workspace with a consumed version code.
- The build panel records Undo only on events that can mutate the form. `Undo.RecordObject` ran on
  every OnGUI event, including the repaints the undo system itself triggers, so each Ctrl+Z
  re-recorded the state it had just restored (undo could never leave the panel) and cleared redo.
- CLI enum flags reject values the enum does not define. `Enum.TryParse` accepts any
  integer-formatted string, so `-platform 5` parsed into an undefined enum and built for nothing
  instead of failing the parse.
- `ConfigureDefinesStage` merges into `ctx.ExtraScriptingDefines` instead of clearing it — the old
  `Clear()` silently discarded defines a PreBuild hook had added, contradicting the hook contract.
- Hook discovery rejects open generic types (they pass every probe but cannot be constructed, so
  they blocked every build at Start) and de-duplicates a class reaching TypeCache from two
  assemblies (a sample imported AND copied into `Assets/Editor` ran twice).
- A throwing hook constructor now reports its own message; `TargetInvocationException`'s
  placeholder text hid the root cause.
- The CI watcher concludes even if the rollback or the flush throws: the record is already claimed
  at that point, so an exception used to leave the batchmode process hanging with no result file
  and no exit code. It now logs, writes a failure result, and exits 2.
- The panel repaints once on the running → idle edge, so a job that concludes inside an update tick
  no longer leaves "Building..." on screen until the next click.
- Post-success outcomes are recorded on the result of the run they belong to. `AppendPostStep`
  wrote into whatever `LatestResult` was current, so a build started before the queue drained
  collected the previous job's post-step lines; outcomes whose run is no longer current are now
  logged instead.

## [0.5.0] - 2026-07-28

### Added
- **CI entry point**: `UnicoBuildCli.Build` (`-executeMethod`, never `-quit`) parses `-platform`,
  `-kind`, `-versionName`, `-bumpBuildCode`, `-buildCode <n>`, `-bumpAddressables`,
  `-buildAddressables`, `-addressablesMode`, `-buildPlayer`, `-outputs`, `-label`,
  `-outputFolder`, `-resultFile`, `-strictWarnings`, `-timeoutMinutes`. CI defaults never bump
  unless flagged. `CiCompletionWatcher` (batchmode-only) writes the result JSON and exits
  0 (success) / 1 (failure) / 2 (timeout or parse error), waiting for the PostSuccess queue and
  resetting a wedged job at the deadline. Keystore secrets come from `UNICO_KEYSTORE_PASS` /
  `UNICO_KEYALIAS_PASS`.
- `WarnPolicy` third parameter on `UnicoBuildService.Start`: `Strict` treats any non-exempt Warn
  as Block; warnings are recorded into `BuildResult.Warnings` in every mode
  (`CdnReminderCheck` and `TestModeConsistencyCheck` are strict-exempt — CI owns the CDN upload,
  and the test-mode define check is self-healing).
- `BuildResult` v2 (additive): `Warnings`, `TypedArtifacts` (path + kind), `VersionName`,
  `BuildCode`, `AddressablesVersion`, `StartedUtc`/`EndedUtc`.
- `BuildRequest.BuildCode` — externally pinned build code (wins over `BumpBuildCode`).
- Batchmode guards: legacy pre-build dialogs are skipped with a log; orphaned dev-state
  snapshots are never auto-restored in batchmode.

### Fixed
- `PostSuccessRunner.Arm` now APPENDS to an undrained queue instead of overwriting it
  (deferred v0.4.0 finding) — back-to-back programmatic builds keep every job's post steps.

## [0.4.0] - 2026-07-28

### Added
- **Hook API**: host projects register build steps with `[UnicoBuildStep(anchor, order)]`.
  Anchors `PreBuild` / `BeforeAddressables` / `BeforePlayer` implement `IBuildStage` and run
  inside the pipeline with full reload-resume and failure-rollback semantics; `PostSuccess`
  implements `IPostSuccessStep` and runs from a SessionState queue only after the whole job
  succeeded — its failures never flip the build's Success.
- The resolved pipeline (built-ins + hooks) is frozen into `BuildJobState.StageTypeNames` at
  Start and re-materialized by name on every Advance — a mid-job recompile can never shift
  `StepIndex` positions; a vanished type fails the job cleanly.
- `BuildContext.Data` — reload-surviving string dictionary for cross-stage data.
- `UnicoBuildService.ActiveRequest` — read-only running-job request for host Unity callbacks.
- Package Manager sample "Build Hooks": keystore injection from environment variables
  (PreBuild) and a post-success symbols-upload skeleton.

## [0.3.0] - 2026-07-27

### Changed
- `com.unity.addressables` is no longer a hard dependency (peer pattern): hosts that want the
  Addressables stage install the package themselves; hosts without it get a player-only pipeline.
  `UNICO_HAS_ADDRESSABLES` (asmdef versionDefines) gates the stage body, the snapshot's profile
  capture/restore, and the stage tests. No serialized shape changed.

### Added
- `UnicoBuildService.AddressablesAvailable` — single availability probe (panel hides the
  Addressables rows when false and forces `BuildAddressables` off before Start).
- `AddressablesAvailabilityCheck` — **Block** when a request asks for the Addressables stage in
  a host without the package; the gated stage's stub `Execute` throws as belt-and-braces.

## [0.2.0] - 2026-07-24

### Added
- `BuildTargetConfig.ExtraDefines` — per-target scripting defines compiled into the player build only (`extraScriptingDefines`; the editor and the Addressables build do not see them).
- `BuildTargetConfig.StripDefines` — per-target defines removed from the platform's global Player Settings for the duration of the build and restored automatically afterwards (crash-safe via the existing dev-state snapshot). Primary use: keeping dev-tooling defines like `UNITY_MCP_READY` out of device builds.
- `BuildSystemSettings` — optional project-level settings asset (discovered under `Assets/`, same contract as `AddressablesVersionStore`). `TestModeDefine` renames the kind-ruled test define per game; blank/invalid values fall back to `TEST_MODE`, so the Test/Release safety rule survives any misconfiguration.
- `DefinePlanCheck` — preflight that surfaces the build's effective define plan; **Block** on invalid tokens or a define listed in both config lists, **Warn** when a config lists the kind-ruled define (the entry is ignored).

### Changed
- `BuildDefineResolver.Resolve(kind, global, extra, strip, testModeDefine)` merges the config lists with the test-mode kind rule (which remains the sole authority over the test-mode define). `HasTestMode` / `TEST_MODE_DEFINE` replaced by `HasDefine(raw, define)` + `BuildSystemSettings.ResolveTestModeDefine()`.

## [0.1.0] - 2026-07-24

- Initial extraction of the Brain Test AS build automation system into the `com.unicostudio.buildsystem` package: reload-safe build jobs, 13 preflight checks, versioned Addressables content updates, artifact naming, dev-state snapshot/rollback, and crash recovery.
