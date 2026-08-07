# Build Config

Per-target build configuration assets and their lookup.

Contents:
- `BuildTargetConfig` — a `ScriptableObject` describing one platform+kind combination: `Platform`, `Kind`, the Addressables `Profile` to use, an optional `ExpectedBundleId` guard, and the optional define lists `ExtraDefines` (player-build-only additions) / `StripDefines` (build-scoped global removals, auto-restored). Data only, no behavior.
- `BuildConfigCatalog` — `LoadAll()` discovers every config asset via `AssetDatabase.FindAssets`; `Find(configs, platform, kind)` returns the matching config (or null). Discovery lives here, not in the panel, so a future CLI caller resolves the same assets (`UnicoBuildService.Start(request)` uses it as the default). `ResolveProfile(all, platform, kind)` is the SINGLE implementation of "which Addressables profile does this build use": the matching config's `Profile`, else `Test`→`Test` / `Release`→`Production` by kind.

Rules:
- Config assets live under `Assets/Settings/Build/Configs/` and are discovered via `BuildConfigCatalog.LoadAll()` — their location is not hardcoded and they can be moved freely.
- `Profile` is consumed through `ResolveProfile`; `ExpectedBundleId` by `BundleIdCheck`; `ExtraDefines`/`StripDefines` by `ConfigureDefinesStage` and `DefinePlanCheck` (both self-load the catalog). `Platform` / `Kind` are the match keys. When no config matches, `ResolveProfile` falls back to `Test`→`Test` / `Release`→`Production` and the define lists are treated as empty.
- **EVERY caller resolves the profile into `BuildContext.Profile` BEFORE running pre-flight checks** — `UnicoBuildService.Start` and `BuildPanelWindow`'s display context both do. `Start` used to inline the resolution AFTER pre-flight, so every check that consulted the profile saw `BuildContext.Profile`'s initialiser instead: `Test`, whatever the request. Nobody noticed while no check read the field; `RemotePathCheck` reads it, and would have silently validated the Test profile's remote paths for a Release build.
- The test-mode define (default `TEST_MODE`, renamable via `BuildSystemSettings`) must NOT appear in the define lists — the build-kind rule owns it; entries naming it are ignored and `DefinePlanCheck` warns.
- Keep `BuildTargetConfig` free of build logic — it is a data record. Behavior belongs in stages/checks.
- `Profile` is the typed `AddressablesProfile` enum, never a profile-name string.
