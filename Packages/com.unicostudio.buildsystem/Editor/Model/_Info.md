# Build Model

Serializable request/state, the runtime context carrier, and shared enums / interfaces.

Contents:
- `BuildRequest` — the panel's build input (platform, kind, version name, bump toggles, addressables build flag + mode, player build flag, outputs, compression, label, output folder). Serialized into `BuildJobState`.
- `BuildContext` — the per-run carrier passed to checks and stages. Wraps the immutable `Request`, plus the resolved `Profile` and the accumulating `Steps`, `Artifacts`, and `ExtraScriptingDefines`.
- `BuildResult` — the outcome surfaced to the panel (success, error, steps, artifacts). Exposed as `UnicoBuildService.LatestResult`, backed by SessionState so it survives the end-of-build reload.
- `BuildTypes` — shared enums (`BuildPlatform`, `BuildKind`, `AddressablesMode`, `CompressionKind { LZ4HC, LZ4, Default }`, `CheckSeverity`, `[Flags] OutputKind`), the `CheckResult` readonly struct, and the `IPreflightCheck` / `IBuildStage` interfaces.
- `BuildPlatformExtensions` — the single source of the `BuildPlatform` → `BuildTarget` / `NamedBuildTarget` / `BuildTargetGroup` mappings; every stage, check, and UI goes through it.
- `UnicoBuildStepAttribute` + `BuildStepAnchor` — the hook registration surface (see README "Host build hooks").
- `IPostSuccessStep` — post-success contract: `Execute(BuildResult)`, runs outside the pipeline.

Rules:
- Types round-tripped through `JsonUtility` (`BuildRequest`, and the `BuildJobState` / `DevStateSnapshot` it embeds) MUST use public FIELDS, not auto-properties — `JsonUtility` serializes fields only. Breaking this silently loses state across the domain reloads mid-build.
- `BuildContext` is NOT serialized (it is rebuilt each `Advance` from `BuildJobState`), so it uses properties.
- `BuildPlatform.iOS` carries `[InspectorName("iOS")]` so Unity renders it as "iOS" instead of the auto-nicified "I OS" in the inspector and panel enum popups.
- `CheckResult` is created only via `Pass` / `Warn` / `Block`; severity drives the panel (Block = red + build disabled, Warn = yellow, Pass = info).
