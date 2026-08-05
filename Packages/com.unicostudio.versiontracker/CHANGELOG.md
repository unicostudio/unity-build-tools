# Changelog

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