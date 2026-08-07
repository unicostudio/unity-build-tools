# Build Automation System

Single-panel control for reproducible Test and Release builds of Unico mobile games on Android and iOS. Replaces the manual, error-prone version / define / Addressables juggling that used to happen before every build.

Entry point:
- `UnicoStudio ▸ BuildPanel` opens `BuildPanelWindow` (Editor/UI). It assembles a `BuildRequest`, runs pre-flight checks for display, and on Build calls `UnicoBuildService.Start`.

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
