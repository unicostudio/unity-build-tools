# Adoption guide — new project checklist

The shortest path from an empty project to reproducible builds, with every shipped sample in
its right place. Each step links the doc that owns the details; this page is deliberately just
the order and the defaults. Both packages also work standalone — skip what you don't use.

## 1. Install

Package Manager > "Add package from git URL", pinned to a tag:

```
https://github.com/unicostudio/unity-build-tools.git?path=Packages/com.unicostudio.buildsystem#com.unicostudio.buildsystem/0.12.4
https://github.com/unicostudio/unity-build-tools.git?path=Packages/com.unicostudio.versiontracker#com.unicostudio.versiontracker/1.7.3
```

To run the packages' own EditMode tests in your project, add them to `"testables"` in
`Packages/manifest.json` (needs `com.unity.test-framework`; Hub templates include it).
Developed and tested on Unity 6000.0.62f1.

## 2. Create the build settings assets (buildsystem)

All discovered path-independently under `Assets/`; convention is `Assets/Settings/Build/`.

- **Four `BuildTargetConfig` assets** — `Assets > Create > Unico > Build > Build Target Config`,
  one per platform+kind (Android/iOS × Test/Release), under `Assets/Settings/Build/Configs/`.
  Fill `ExpectedBundleId` per config; `ExtraDefines`/`StripDefines`/`StripPackages` as needed
  (tooltips explain scope and restore semantics). You can build with zero configs — the
  preflight then WARNS that the kind-based fallback is running with no config protections;
  a Pass names the config the build resolved.
- **`BuildSystemSettings`** — `Assets > Create > Unico > Build > Build System Settings`.
  Optional for player-only projects. Required once you build Addressables content
  (`RemoteLoadRoot` lives here). Also where the test-mode define is renamed (default
  `TEST_MODE` — the build adds it globally on Test and removes it on Release; gate your test
  features behind `#if TEST_MODE`).
- **`AddressablesVersionStore`** — `Assets > Create > Unico > Build > Addressables Version
  Store`. Only for Addressables projects; fresh assets start at v1.

## 3. Import the samples you need

Package Manager > Unico Build System > Samples. Keep exactly ONE copy of each imported class.

- **Build Hooks / `KeystoreEnvInjection`** — Android Release signing without committed
  passwords: an on-load injector reading `UNICO_KEYSTORE_PASS` / `UNICO_KEYALIAS_PASS` from
  the environment. Import, then set your keystore path/alias in it. (It must stay an on-load
  injector, not a build hook — preflight reads the passwords before any hook runs; the file's
  comments explain.)
- **Build Hooks / `UploadSymbolsPostStep`** — optional skeleton for a post-success symbols
  upload; copy the shape for any of your own `[UnicoBuildStep]` hooks.
- **VersionTracker Glue** — import ONLY if the project uses both packages. Attaches the
  tracker's per-build metadata to the build result and fails loudly when an export silently
  didn't happen. Without it nothing breaks visibly — you just lose that verification. Details
  in the sample's own README.

## 4. versiontracker conventions

- It exports automatically after every successful build to `UnicoVersionTracker/` at the
  project root; manual export via `UnicoStudio > Export SdkInfo`.
- **Commit the `UnicoVersionTracker/*.json` outputs per shipped release** — they record which
  SDK versions a shipped binary carried. Never regenerate a shipped release's file.

## 5. Addressables wiring (only for remote content)

Install `com.unity.addressables` yourself (peer dependency — the Addressables stage enables
itself when present). Then:

- Profiles must be named exactly `Development`, `Test`, `Production`.
- Set each profile's remote variables to these bindings (copy-paste; the host writes no code):

```
RemoteBuildPath: [UnicoStudio.BuildSystem.AddressablesProfilePaths.RemoteBuildDevelopmentPath]
RemoteLoadPath:  [UnicoStudio.BuildSystem.AddressablesProfilePaths.RemoteLoadDevelopmentPath]
RemoteBuildPath: [UnicoStudio.BuildSystem.AddressablesProfilePaths.RemoteBuildTestPath]
RemoteLoadPath:  [UnicoStudio.BuildSystem.AddressablesProfilePaths.RemoteLoadTestPath]
RemoteBuildPath: [UnicoStudio.BuildSystem.AddressablesProfilePaths.RemoteBuildProductionPath]
RemoteLoadPath:  [UnicoStudio.BuildSystem.AddressablesProfilePaths.RemoteLoadProductionPath]
```

  (one Build/Load pair per profile, matching the profile's name)
- Set `BuildSystemSettings.RemoteLoadRoot` to your CDN root — there is no fallback, preflight
  blocks without it.
- Commit `addressables_content_state.bin`: it anchors content-update chains and exists only
  where it was built — treat it like source.

## 6. First build

`UnicoStudio > BuildPanel`. Read the preflight list before pressing Build — every Warn/Block
message names the setting and the fix. The build restores your defines, profile and settings
afterwards; after a platform-switching build the editor deliberately stays on the build
target.

## 7. CI (later phase)

The pipeline runs headless end-to-end (`UnicoBuildCli.Build`; exit codes, result JSON, env-var
secrets). The contract lives in the buildsystem README's "CI usage" section — nothing to
prepare project-side beyond the steps above.
