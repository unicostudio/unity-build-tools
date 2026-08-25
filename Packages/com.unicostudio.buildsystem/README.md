# Build Automation System

Single-panel control for reproducible Test and Release builds of Unico mobile games on Android and iOS. Replaces the manual, error-prone version / define / Addressables juggling that used to happen before every build.

Install (UPM, git URL — pin to a tag):

```
https://github.com/unicostudio/unity-build-tools.git?path=Packages/com.unicostudio.buildsystem#com.unicostudio.buildsystem/0.13.0
```

The repo is public — the URL works in any Unity Package Manager with plain system git. Add
`"testables": ["com.unicostudio.buildsystem"]` to the manifest so the package's EditMode
tests run in the consumer (needs `com.unity.test-framework` in the project, which Hub
templates include). The monorepo README documents the per-package tag convention.

Entry point:
- `UnicoStudio ▸ BuildPanel` opens `BuildPanelWindow` (Editor/UI). It assembles a `BuildRequest`, runs pre-flight checks for display, and on Build calls `UnicoBuildService.Start`.
- Panel behavior worth knowing (0.13.0): the bump toggles are enabled by the stage that gives
  them meaning — `Bump Build Code` needs Build Player, `Bump Addressables Version` needs Build
  Addressables — and a gated-off toggle is forced false (a greyed row never smuggles a hidden
  bump into the job). `Version Name` stays always editable: every job stamps it, content-only
  builds included. The Target box shows which `BuildTargetConfig` the request resolved, and the
  Test/Release accent tint can be turned off per user via the window-tab context menu
  ("Accent Colors").

Folder map:
- `Runtime/` — runtime-assembly Addressables version store, path composition, the `AddressablesProfilePaths` binding surface the Addressables profile variables resolve against, and the project-level `BuildSystemSettings` (the only non-editor code here; consumed by both the game and the build stages).
- `Editor/Core/` — orchestration: the reload-safe build state machine and the pre-flight runner.
- `Editor/Model/` — serializable request/state, the runtime context carrier, and shared enums / interfaces / `CheckResult`.
- `Editor/Config/` — `BuildTargetConfig` ScriptableObjects (per platform+kind) and their lookup.
- `Editor/Support/` — pure helpers: artifact naming, define resolution, version bumping, dev-state snapshot.
- `Editor/Stages/` — the ordered `IBuildStage` pipeline (apply version → configure defines → addressables → player build).
- `Editor/Checks/` — `IPreflightCheck` advisories and blocks shown before a build.
- `Editor/UI/` — the build panel window.
- `Editor/Hooks/` — `UnicoBuildPreprocessor`, the legacy modal-dialog warning system. It runs for MANUAL (non-panel) builds only: it returns immediately in batchmode and whenever `BuildSession.IsBuildingViaPanel` is set, so panel and CLI builds never see its dialogs. Kept because manual `File > Build Settings > Build` still happens and its checks are the only guard on that path.
- `Tests/Editor/` — EditMode tests for the module (`UnicoStudio.BuildSystem.Tests`).
- `Samples~/` — importable host-side samples: `Hooks` (keystore env injection, symbols-upload
  skeleton) and `VersionTrackerGlue` (the PostSuccess step coupling this package with
  `com.unicostudio.versiontracker` — see that sample's README; import it if the project uses
  both packages, or metadata freshness-verification silently never runs).

End-to-end flow:
1. The panel builds a `BuildRequest` (platform, kind, version, bump toggles, addressables mode, outputs, compression, label, output folder).
2. `UnicoBuildService.Start` resolves the Addressables `Profile` FIRST — `BuildConfigCatalog.ResolveProfile`: a matching `BuildTargetConfig`, else `Test`/`Production` by kind — and only then runs pre-flight; a `Block` result aborts. It captures a `DevStateSnapshot`, persists a `BuildJobState`, marks the session, and calls `Advance`. The order is load-bearing: a check that consults `BuildContext.Profile` sees whatever the context carries, so resolving afterwards left every such check validating the field's initialiser (`Test`) even for a Release build.
3. `Advance` switches the active platform if needed (reimport + domain reload), then runs the stages from the saved `StepIndex`. Progress is persisted after each stage.
4. A stage that changes global scripting defines triggers a domain reload; `BuildJobResumer` re-enters `Advance` at the next step.
5. When all stages complete (or on error), `Finish` restores the captured dev state — on failure additionally rolling back the run's version bumps (name, build code, addressables version) — records `LatestResult`, ends the session, and clears the job.

Key rules:
- The system is reload-safe by design: switching platform and changing the `TEST_MODE` global define both cause domain reloads mid-build. All progress lives in the serialized `BuildJobState`, never in static fields.
- The build mutates real project state (active platform, scripting defines, `buildAppBundle`, Android debug-symbol level, active Addressables profile). Scripting defines, `buildAppBundle`, the symbol level, and the active profile are captured in `DevStateSnapshot` before the build and restored in `Finish` — on success and failure. The Addressables content-state file has its own record (`ContentStateGuard`), armed by `AddressablesStage` because only it can resolve the path authoritatively. The active platform is intentionally NOT restored: after a platform-switching build the editor stays on the build target (the snapshot's `Platform` field is only the `NamedBuildTarget` key for restoring that target's defines). Version writes are the deliberate exception: kept on success (they describe a real, produced build), rolled back on failure and crash recovery.
- The test-mode define (default `TEST_MODE`; renamable per game via the optional `BuildSystemSettings` asset, with fail-safe fallback) is written into the **global** defines in both directions: a Test build adds it, a Release build removes it. This kind rule is code, not data: config entries naming the test-mode define are ignored (`DefinePlanCheck` warns), so no config edit can ship test features in a Release build.
- **Why global and not `extraScriptingDefines`:** editor assemblies are compiled with the *active build target's* global defines, and `extraScriptingDefines` is a player-compile parameter that never reaches them. Everything that participates in a build from the editor side — `IPreprocessBuildWithReport` / `IPostprocessBuildWithReport` callbacks, host build steps, the Addressables content build — would otherwise compile for the opposite kind whenever the target's committed globals disagree with the requested kind. The classic damage: a Test build for a platform that never had the define committed, whose plist/manifest post-processor then writes production credentials into a test artifact. **No project has to commit anything** — the build makes the editor match its own intent and the `DevStateSnapshot` puts the developer's exact state back afterwards.
- The cost is paid only when reality disagrees with the plan. A Test build whose target already carries the define, and a Release build whose target does not, write nothing and reload nothing. Otherwise it is one recompile + reload before the build and one after — exactly what Release builds have always paid.
- Each `BuildTargetConfig` may additionally declare `ExtraDefines` (player-build-only additions, via `extraScriptingDefines`; deliberately NOT promoted to global — they describe the player, not the build's intent) and `StripDefines` (build-scoped global removals — e.g. `UNITY_MCP_READY`). The kind rule's global add and every global removal share ONE `SetScriptingDefineSymbols` call, and the snapshot in `Finish` restores them on success, failure, and crash recovery.
- `BuildTargetConfig.StripPackages` (0.12.0) is the package-level sibling of `StripDefines`: the
  listed UPM package ids are removed from `Packages/manifest.json` for the build's duration and
  restored **byte-exact** afterwards (`PackageStripGuard` — job-stamped backups, restore on
  success, failure and crash recovery). It exists for third-party packages whose editor code
  fights the define plan across reloads. Preconditions are preflight-checked: a listed package
  must be exact-pinned (Warn) and must have no dependents in the lock (Block). Cost ≈ +30 s per
  build. Spec with the measurements: `docs/specs/2026-08-17-strippackages-design.md`.
- Defense in depth around the define plan (because third-party `[InitializeOnLoad]` and UPM
  package-event handlers can rewrite defines while a build is in flight): `DefineGuard` (0.11.0)
  re-verifies the recorded plan against the ACTUAL globals right before `BuildPlayer` and fails
  the build rather than shipping a violated plan; `DefineReassertWatcher` (0.12.1) re-asserts
  the written plan every editor tick in the window between the stage's write and the reload
  landing, so an in-session counter-write can never leave a half-state. Both are internal — no
  configuration surface. The guard is a stateless verification (nothing to disarm); the
  watcher's subscription dies with the reload on the normal path, and `Finish` disarms it on
  the same-domain exit paths before the dev-state restore.
- Config discovery is visible in preflight: `ConfigPresenceCheck` names the resolved
  `BuildTargetConfig` on Pass, and Warns (advisory — strict-exempt) when nothing matches, because
  a config-less build otherwise runs "successfully" on the kind-based fallback with no
  ExtraDefines/StripDefines/StripPackages and nothing saying so.
- Panel builds set `BuildSession.IsBuildingViaPanel` so the legacy `UnicoBuildPreprocessor` modal dialogs are suppressed (the panel already ran non-blocking pre-flight).

Host build hooks:
- Any editor class can join the pipeline: implement `IBuildStage`, add
  `[UnicoBuildStep(BuildStepAnchor.PreBuild)]` (or `BeforeAddressables` / `BeforePlayer`), and
  it is discovered via TypeCache — no registration call. Ordering is anchor → `order` → full
  type name (ordinal), fully deterministic. Anchors are positional: they run at their position
  even when the optional stage behind them is disabled; filter via `ctx.Request` when you care.
- Hook stages get the pipeline's guarantees for free: reload-resume (the resolved pipeline is
  frozen by type name at job start), progress display, and job-failure rollback when they throw.
- `PostSuccess` steps implement `IPostSuccessStep` instead and run only after the WHOLE job
  succeeded (dev state restored, result recorded). Their failures are logged and shown as
  "Post step FAILED" without flipping the build's Success. Outcomes are recorded on the result of
  the run whose queue is draining; if a newer build has already replaced it (back-to-back
  programmatic runs), the outcome is logged instead of recorded. Queue survives reloads, not editor
  restarts, and is claimed one run per update tick — a post step that queues a domain reload costs
  that run only, never the runs queued behind it.
- Keep hooks stateless: instances are re-created after every domain reload; carry data in
  `ctx.Data` (string→string, persisted) instead of fields.
- A hook that queues a recompile WITHOUT a global-define delta (a manifest edit, an
  `AssetDatabase.Refresh` over changed sources) must call `ctx.RequestReload()`: the
  orchestrator detects pending reloads via the defines-hash, which cannot see a define-neutral
  recompile, and would otherwise resume the next stage on stale assemblies.
- Keep exactly one copy of a hook class: the same class reaching TypeCache from two assemblies
  (a Package Manager sample imported AND copied into an `Assets/Editor` folder) is logged and
  de-duplicated, but the copy is dead weight.
- Never mark TEST types with the attribute — the Tests assembly compiles in-editor and TypeCache
  would feed them into real builds.
- A hook may contribute build-only defines via `ctx.ExtraScriptingDefines`. `ConfigureDefinesStage`
  merges into that list rather than replacing it — but anything the build strips (the test-mode
  define on Release, or a `StripDefines` entry) is evicted from it and logged, so a hook can never
  re-add what the kind rule or `StripDefines` just removed.
- Host Unity callbacks (`IPreprocessBuildWithReport` etc.) keep working unchanged inside
  `BuildPipeline.BuildPlayer`; they can read `BuildSession.IsBuildingViaPanel` and
  `UnicoBuildService.ActiveRequest` to detect pipeline builds.
- Editor-side build code that branches on the build kind should prefer reading the RUN over reading
  its own compilation. `#if TEST_MODE` in an editor assembly answers "how was this assembly
  compiled", which the kind rule now keeps in sync — but `ActiveRequest` answers the question that
  was actually being asked, with no reload involved and no ambiguity for a manual `File > Build`:
  ```csharp
  var run = UnicoBuildService.ActiveRequest;               // null for a manual File > Build
  var isTest = run != null ? run.Kind == BuildKind.Test : DefaultForManualBuilds();
  ```

CI usage:
- Headless entry point: `UnicoBuildCli.Build`, invoked via `-executeMethod` with `-batchmode`.
  ```
  Unity -batchmode -projectPath <proj> -buildTarget android \
    -executeMethod UnicoStudio.BuildSystem.Editor.UnicoBuildCli.Build \
    -platform Android -kind Release -versionName 1.6.7 -bumpBuildCode -outputs Aab \
    -resultFile Builds/result.json -strictWarnings -timeoutMinutes 60
  ```
- NEVER pass `-quit`: the reload chain needs the editor alive across the job's domain reloads;
  `CiCompletionWatcher` (batchmode-only) is what exits the process once the job (and any
  PostSuccess queue) concludes or the timeout deadline is reached.
- Custom flag names avoid Unity's own argument namespace — notably Unity consumes `-version` itself (prints the editor version and exits), which is why the app version flag is `-versionName`.
- Exit codes:

  | Code | Meaning |
  | --- | --- |
  | 0 | Success |
  | 1 | Build failure (including a preflight `Block`) |
  | 2 | Timeout, CLI parse error, or a watcher failure while concluding |
- The result JSON (`-resultFile`, default `Builds/result.json`) is the machine-readable outcome —
  parse it, not the log. Fields (`BuildResult`, serialized by `JsonUtility`):

  | Field | Type | Meaning |
  | --- | --- | --- |
  | `Success` | bool | The job's outcome; PostSuccess step failures do NOT flip it |
  | `Error` | string | Failure message; empty on success. On exit 2, its prefix distinguishes timeout from conclusion trouble |
  | `DurationSeconds` | double | Wall time; `0` = legacy result |
  | `Steps` | string[] | Human-readable step log (defines written, packages stripped, bumps…) |
  | `Warnings` | string[] | Preflight warnings the run proceeded over |
  | `Artifacts` | string[] | Artifact paths only — kept for back-compat; prefer `TypedArtifacts` |
  | `TypedArtifacts` | {`Path`, `Kind`}[] | `Kind` is serialized as an INTEGER (`JsonUtility` writes enum values): `0`=Unknown, `1`=Apk, `2`=Aab, `3`=SymbolsZip, `4`=XcodeProject, `5`=AddressablesContent, `6`=Metadata (the mapping is pinned by a test — members are only ever appended) |
  | `VersionName` / `BuildCode` | string | On success: the produced build's values (post-bump). On failure they are read AFTER the rollback, so they carry the developer's original (pre-build) versions — only trust them when `Success` is true. `BuildCode` is Android version code / iOS build number as text |
  | `AddressablesVersion` | int | Content version; `-1` = store missing or Addressables unused |
  | `StartedUtc` / `EndedUtc` | string | ISO 8601 round-trip (`"o"`); empty = legacy result |
- Keystore secrets are read from the environment, never from committed sources:
  `UNICO_KEYSTORE_PASS` / `UNICO_KEYALIAS_PASS`. Unset, this is a no-op and `KeystoreCheck` still
  blocks an unsigned Android Release cleanly.
- CI defaults never bump unless flagged: `-bumpBuildCode` / `-bumpAddressables` are required
  explicitly (the panel's own defaults do not apply here).
- `-buildTarget` should match `-platform`. A mismatch still works — `UnicoBuildService` switches
  the active platform to match the request — but it costs a second full reimport, so pass the
  same platform to both flags to avoid paying for it twice. Under `-strictWarnings`, a mismatch
  also trips `PlatformMatchCheck`'s Warn, which blocks the build (it is not strict-exempt) — so
  matching flags is required, not just faster, in strict mode.
- Two versioning patterns are supported:
  1. **Pipeline commits back the bump diff** — pass `-bumpBuildCode` (and/or `-bumpAddressables`)
     and let the job bump; the CI pipeline then commits the resulting version files back to the
     repo after a successful build.
  2. **Versions injected externally** — pass `-versionName` and/or `-buildCode <n>` with no bump
     flags; the pipeline (or a release process outside it) owns version selection and the job
     just builds what it's told (`-buildCode` also disables `-bumpBuildCode` if both are given).

Assembly boundaries (UPM package with asmdefs):
- `Runtime/` — `UnicoStudio.BuildSystem` (autoReferenced): `AddressablesProfile`, `AddressablesProfilePaths`, `AddressablesVersionStore`, `BuildSystemSettings`, `UnicoBuildPaths`. Namespace `UnicoStudio.BuildSystem`.
- `Editor/` — `UnicoStudio.BuildSystem.Editor` (editor-only; refs Runtime + Unity.Addressables[.Editor]). Namespace `UnicoStudio.BuildSystem.Editor`; `UnicoBuildPreprocessor` (Hooks/) stays global-namespace by design.
- **A HOST GAME writes no code for the Addressables binding.** Its six profile variables name the package type directly — `[UnicoStudio.BuildSystem.AddressablesProfilePaths.Remote{Build,Load}{Development,Test,Production}Path]`. This works because Addressables splits a `[Type.Member]` token on the LAST dot and calls `Assembly.GetType` on the left half over every loaded assembly: a namespaced package type resolves exactly like the global-namespace class in Assembly-CSharp that these variables used to have to name. Adoption is therefore **a settings asset plus six profile strings, and nothing else**. The game still commits its `AddressablesVersionStore` asset and four `BuildTargetConfig` assets, and adds `"testables": ["com.unicostudio.buildsystem"]` to its manifest.
- **`BuildSystemSettings.RemoteLoadRoot` is REQUIRED for a host that builds Addressables content**, and there is no fallback — deliberately. `TestModeDefine` degrades to `TEST_MODE` because a wrong-but-present define is safer than none; a CDN root has no safe default, and a guessed one publishes content to (or loads it from) the wrong host. A missing asset and an unusable value are two separate throws with two separate instructions. The asset — and the root — remain **optional for player-only hosts**, which never evaluate the load-path bindings at all.

Config + data assets (live outside this folder):
- `BuildTargetConfig` assets, `AddressablesVersionStore.asset`, and `BuildSystemSettings.asset` (optional for player-only hosts; required once the host builds Addressables content, since that is where `RemoteLoadRoot` lives) live under `Assets/Settings/Build/` by convention; all are located via `AssetDatabase.FindAssets` scoped to `Assets/` (path-independent).

Addressables is optional (peer dependency):
- Hosts that build remote content install `com.unity.addressables` themselves (any version — 2.x
  is what this package is built and tested against); the asmdef versionDefines symbol
  `UNICO_HAS_ADDRESSABLES` then enables the Addressables stage. Those hosts must also set
  `BuildSystemSettings.RemoteLoadRoot` — `RemotePathCheck` blocks in pre-flight, and
  `AddressablesStage` refuses to build, rather than baking an unresolved binding into the catalog.
- Hosts without it get a player-only pipeline: the panel shows no Addressables rows,
  `AddressablesAvailabilityCheck` blocks API requests for the stage, and the stub stage throws.

Renaming the test-mode define in a host game:
1. Create the optional settings asset: `Assets > Create > Unico > Build > Build System Settings` (convention: `Assets/Settings/Build/`).
2. Set `TestModeDefine` to the game's name, e.g. `DEV_MODE`. That is the whole change — the resolver kind rule, `TestModeConsistencyCheck`, `TestModeDefineRenameCheck`, `DefinePlanCheck`, the legacy `UnicoBuildPreprocessor` dialog, and the panel's Build Type tooltip all resolve the name through `BuildSystemSettings.ResolveTestModeDefine()`.
3. The game's own code keeps consuming its define (`#if DEV_MODE`) as before; during development the define lives in the platform's global Player Settings and the panel manages it per build kind.

A missing asset, or a blank/invalid `TestModeDefine`, falls back to `TEST_MODE` — the kind rule itself can never be disabled by configuration. Finish a rename in the Player Settings too: `TestModeDefineRenameCheck` warns while the default `TEST_MODE` is still in any platform's global defines, because the package cannot tell a deliberate leftover from a half-finished rename (or a typo in the asset), and every other check would resolve the renamed symbol and pass. Clearing it means removing `TEST_MODE` from each platform's Scripting Define Symbols — `StripDefines` cannot silence the warning, since its removals are build-scoped and the snapshot restores them when the build ends.

Testing:
- Pure logic is covered by EditMode tests in `Tests/Editor/` under the `UnicoStudio.BuildSystem.Tests` namespace. Real device builds, reload-resume, and Addressables content are validated by a human via the panel — they are not automated.
