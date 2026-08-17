# Changelog

## [1.7.1] - 2026-08-17

* **FIX**: the `Tests/` folder shipped in 1.7.0 was missing its own `.meta` file, so
  consumers resolving the package from the git tag logged
  "Asset .../Tests has no meta file, but it's in an immutable folder" on every import —
  loud enough to fail host tests that assert clean error logs. 1.7.0's inner metas were
  committed; only the folder's own meta was missed (surfaced by BT5's suite the first
  time a host consumed 1.7.0).

## [1.7.0] - 2026-08-13

* **NEW**: EditMode test suite — the package's first (21 tests, assembly
  `UnicoVersionTracker.Editor.Tests`, gated by `UNITY_INCLUDE_TESTS`)
  * Path contract: `GetBuildInfoPath` composition (product/version prefix, platform suffix,
    folder at project root), filename sanitisation, cross-call stability, purity (no
    directory creation), per-platform distinctness.
  * Export contract: synchronous write at exactly the queried path, camelCase/indented JSON
    with nulls included, `GetSavedBuildInfo` round-trip, and the 1.6.0 `ExportBuildInfoAsync`
    shim completing its write before returning.
  * Read-path characterization: missing file logs the double "Error reading file" and yields
    null; corrupt JSON logs once and yields null.
  * Catalog/record contracts: the 10-SDK catalog shape, `SdkInfo`'s opt-in serialization
    (detection internals never reach the wire), record round-trips.
  * Every assertion family was mutation-probed (kill-then-red) before landing.
* **DOCUMENTED CONSTRAINT** (measured while building the suite): `GetSavedBuildInfo` /
  `GetSavedBuildInfoJson` compose their path with `Application.productName`, a
  main-thread-only Unity API. Await them on the main thread (Unity Test Framework `async`
  tests do this correctly); calling them from a worker thread fails the read with a
  `UnityException` logged as "Error reading file", and blocking the main thread on their
  task deadlocks the editor — await, never `Wait()`.
* Dev-project note: the monorepo's root manifest now lists the package under `testables`;
  the repo-wide EditMode baseline is 268 (buildsystem 246 + versiontracker 21 + 1 Unity
  Addressables doc stub).

## [1.6.0] - 2026-08-05

* **BEHAVIOUR CHANGE**: The post-build export is now **synchronous**
  * `ExportBuildInfoAsync` was `async void`, so `OnPostprocessBuild` returned before
    `await File.WriteAllTextAsync` completed. A consumer checking for the file right after the
    build could get a false "missing"; in batchmode CI, where the editor may exit immediately
    after the callback, the file could end up missing or truncated for real.
  * The post-process hook now calls the new synchronous `ExportBuildInfo`, so the JSON is
    guaranteed on disk by the time the build's post-process step finishes.
  * Why this is safe to adopt: `IPostprocessBuildWithReport.OnPostprocessBuild` returns `void`,
    so the tracker can never await its own export — a guaranteed write must be synchronous. The
    cost is negligible: everything expensive in the export (assembly reflection, `Assets/`
    directory scans, AppLovin's `LoadPluginDataSync` network call) already ran synchronously
    *before* the old `await`; only the few-KB file write itself changed.

* **NEW FEATURE**: `ExportBuildInfo(BuildSummary)` — synchronous export
  * Returns the path written, or `null` if the write failed.

* **NEW FEATURE**: `GetBuildInfoPath(BuildTarget platform)` — the build-info path is now public
  * Returns exactly the path `ExportBuildInfo` writes for that platform (same folder, same
    sanitised filename), composed by the same single code path, so the two cannot drift.
  * Pure query: it does **not** create the output directory, unlike the internal write-path
    helper. Consumers no longer need to reconstruct the filename themselves.

* **IMPROVEMENT**: Read paths no longer create the output directory
  * `GetSavedBuildInfo` / `GetSavedBuildInfoJson` only read, yet they used to create
    `UnicoVersionTracker/` as a side effect of composing the path. They now route through the
    pure path composer; `Directory.CreateDirectory` survives only on the write paths.

* **NOTE**: `ExportBuildInfoAsync(BuildSummary)` keeps its callable signature
  * It now delegates to `ExportBuildInfo` and completes synchronously (`async` was an
    implementation detail, not part of the signature). Its name is now slightly inaccurate —
    the deliberate price of not breaking existing callers.

## [1.5.1] - 2026-08-05

### Changed
- Repository metadata only — zero code changes. The package now lives in the
  `unicostudio/unity-build-tools` monorepo (`Packages/com.unicostudio.versiontracker`); the
  `documentationUrl` / `changelogUrl` / `licensesUrl` fields and the README install instructions
  now point there instead of the retired `unicostudio/UnicoVersionTracker` repo. (`licensesUrl`
  also pointed at a `master` branch that never existed — fixed in passing.) The
  `com.unicostudio.versiontracker/1.5.0` tag remains the byte-identical verbatim move of the old
  repo's `main`; this release exists so a fresh install's own metadata points at the right home.

All notable changes to this package will be documented in this file.

## [1.5.0] - 2026-02-02

* **NEW FEATURE**: Added UPM (Unity Package Manager) support for SDK version detection
  * SDKs installed via UPM are now automatically detected as fallback
  * Parses `packages-lock.json` for registry, tarball, git, and embedded packages
  * Extended `SdkInfo` with `UpmPackageNames` for SDK-level fallback

* **NEW FEATURE**: Firebase pluginVersionInfo UPM fallback
  * Added support for 12 Firebase packages (Analytics, Auth, Crashlytics, Database, etc.)
  * Detects Firebase versions when installed via UPM instead of Assets folder

* **NEW FEATURE**: AdMob Mediation UPM fallback
  * Added support for 18 AdMob mediation adapters (AppLovin, Meta, IronSource, etc.)
  * Detects mediation versions when installed via OpenUPM

* **IMPROVEMENT**: Consolidated error logging
  * Errors only log when both primary (Assets) and UPM fallback fail
  * Removed premature error messages for cleaner console output

* **FIX**: Fixed AppLovin `LoadPluginData` bug
  * Changed from async `LoadPluginData` to synchronous `LoadPluginDataSync` method
  * Resolves "LoadPluginData did not return any PluginData" error

* **FIX**: UnicoAPIClient case-insensitive package ID lookup
  * Supports both `unicoapiclient` (legacy) and `UnicoApiClient` (pascal case)

## [1.4.0] - 2025-12-15

* **NEW FEATURE**: Added UnicoConfig GameId integration to build information
  * Automatically retrieves GameId from UnicoConfig.Instance using reflection
  * Added `GameId` property to `ProjectInfo` class
  * Included in all exported build info JSON files

* **NEW FEATURE**: Added AdManagerSettings mediation type detection
  * Automatically detects Android and iOS mediation types from AdManagerSettings
  * Added `MediationTypes` property to `ProjectInfo` class
  * Reports configured mediation types (MaxAndAdmob, AdmobMediation, etc.)

* **NEW FEATURE**: Added Google ODM (On-Device Mediation) version tracking
  * Detects AdjustGoogleOdm and GoogleAdsOnDeviceConversion versions from XML
  * Added as separate SDK entry in sdkInfo list
  * iOS-only feature with proper platform detection
  * Extracts versions from AdjustGoogleODMDependencies.xml

* **NEW FEATURE**: Enhanced network version tracking with detailed metadata
  * Added unique network IDs for all mediation networks (e.g., `_applovin_`, `_facebook_`)
  * Added separate Android and iOS version fields for each network
  * Network IDs are fixed in code to prevent changes when network names change
  * Comprehensive ID mapping for 50+ networks (AppLovin MAX, AdMob, Firebase)
  * Combined version format: "android_X.X.X_ios_Y.Y.Y"

* **NEW FEATURE**: Added Unity Editor test build info export
  * New menu item: "UnicoStudio/Export Test BuildInfo"
  * Allows generating build info without actual build process
  * Uses current active build target from Editor settings
  * Useful for testing and verification purposes

* **IMPROVEMENT**: Restructured JSON output for better organization
  * Moved `MediationTypes` from root level into `ProjectInfo`
  * Converted `GoogleOdm` from separate block to SDK entry in `sdkInfo` list
  * More consistent and hierarchical JSON structure

* **IMPROVEMENT**: Updated deprecated Unity API usage
  * Replaced `PlayerSettings.GetManagedStrippingLevel(BuildTargetGroup)` with `NamedBuildTarget` version
  * Added `UnityEditor.Build` namespace import
  * Ensures compatibility with latest Unity versions

* **CODE QUALITY**: Enhanced reflection-based SDK detection
  * All SDK version retrieval uses consistent reflection patterns
  * Network ID mapping centralized in dictionary for easy maintenance
  * Improved error handling and logging for SDK detection failures

## [1.3.0] - 2025-06-18

* **NEW FEATURE**: Added AdMob Mediation adapter version tracking
  * Automatically detects and reports all installed AdMob mediation adapters
  * Extracts version information from mediation dependency XML files
  * Combines Android and iOS versions in format: "android_X.X.X_ios_Y.Y.Y"
  * Added `GetAdMobMediationVersions()` method to scan mediation adapters
  * Enhanced `GoogleAdMob` SDK info to include detailed mediation adapter versions
  * Supports all standard mediation adapters (AppLovin, IronSource, MetaAudienceNetwork, etc.)

* **IMPROVEMENT**: Enhanced directory search patterns for flexible project structures
  * Updated all SDK detection methods to use flexible directory search patterns
  * Changed from hardcoded paths to pattern-based searches (e.g., `*GoogleMobileAds`, `*Firebase`)
  * Improved compatibility with diverse Unity project folder organizations
  * Fixed issues where SDKs installed in non-standard locations weren't detected
  * Applied consistent search patterns across AdMob, Firebase, Adjust, and other SDK detection methods
  * Optimized performance while maintaining robust detection capabilities

## [1.2.0] - 2025-06-12

* **NEW FEATURE**: Added render pipeline detection to build information
  * Automatically detects and reports current render pipeline (Built-in, URP, HDRP, or Custom)
  * Added `RenderPipeline` property to `ProjectInfo` class

* **NEW FEATURE**: Added UnicoAPIClient NuGet package detection to SDK information
  * Automatically detects UnicoAPIClient main package version from packages.config
  * Added `GetUnicoAPIClientVersion()` method

## [1.1.1] - 2025-02-11

* Documentation and Changelog urls are updated

## [1.1.0] - 2025-02-11

* `UnicoVersionTrackerProgressBar` is implemented
* `UnicoVersionExporter.ExportBuildInfo` and `UnicoVersionExporter.ExportSdkInfo` methods are converted to async
* `GetSavedBuildInfo` and `GetSavedBuildInfoJson` public methods are added to UnicoVersionExporter

## [1.0.0] - 2025-01-24

* This is the first release of Unity Package UnicoVersionTracker.