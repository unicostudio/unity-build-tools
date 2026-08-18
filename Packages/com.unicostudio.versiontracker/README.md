# UnicoVersionTracker

**UnicoVersionTracker** is a Unity Editor tool designed to automatically track and save version information of third-party SDKs (e.g., AdMob, Firebase, Adjust, AppLovin) and build settings into a `.json` file. This tool also provides a manual export option via the Unity Editor for quick access to SDK version information.

---

## Features

- **Automatic SDK Detection:** Detects third-party SDKs in the project and retrieves their versions dynamically.
- **Build Metadata Logging:** Captures platform, compression method, and other relevant build details after each build.
- **Editor Integration:** Adds a **UnicoStudio** menu in Unity's top toolbar with a one-click **Export SdkInfo** button for manual version export.
- **Flexible Output:** Saves version and build metadata to `.json` files for better readability and structure.

---

## Installation

- Add this package to your project using Unity's Package Manager.  
   Paste the following Git URL into the "Add package from Git URL" option (pin to a tag for
   anything past local experimentation):
   `https://github.com/unicostudio/unity-build-tools.git?path=Packages/com.unicostudio.versiontracker#com.unicostudio.versiontracker/1.7.2`
- The repo is private: UPM resolves git URLs through the system git, so the machine needs
   GitHub read access to `unicostudio/unity-build-tools` with non-interactive credentials
   (a PAT in the git credential helper, or the SSH URL form).
- To run the package's EditMode tests (21) in your project, add
   `"testables": ["com.unicostudio.versiontracker"]` to `Packages/manifest.json`.

---

## How It Works

### Automatic Export (Post-Build)
After every build, the tool automatically collects:

- Third-party SDK versions
- Build settings, such as platform and compression method

The information is saved in a `.json` file located at: Assets/../UnicoVersionTracker/

> **Failed or cancelled builds** log a deliberate red error line ("Build is failed or
> cancelled") and export nothing — the red color is intentional visibility, not a bug.

> **Convention for shipping projects:** commit the `UnicoVersionTracker/*.json` outputs to git
> per shipped release — they record which SDK versions a SHIPPED binary carried, which is the
> whole point of the tool. Never regenerate a shipped release's file into a commit; only a dev
> playground should gitignore the folder.

### Manual Export (Editor)
You can manually export the SDK version information via the Unity Editor:

1. After adding the package, you'll see a **UnicoStudio** menu at the top of Unity.
2. Click the **Export SdkInfo** button to export current SDK versions.
3. The exported file is saved to: Assets/../UnicoVersionTracker/SdkInfo/
> **Note:** The manual export only includes SDK version information, not build-specific metadata.

---

## Public API (for host tooling)

`UnicoStudio.UnicoLibs.VersionTracker.UnicoVersionExporter` (editor assembly) — the surface a
build pipeline or host glue consumes (all of it since 1.6.0):

- `string GetBuildInfoPath(BuildTarget platform)` — the exact path the post-build export
  writes for that platform. Pure composition (no directory creation); use it instead of
  reconstructing the path, so sanitisation and layout can never drift.
- `string ExportBuildInfo(BuildSummary buildSummary)` — synchronous export, returns the
  written path, or `null` if the write failed (logged). This is what the automatic post-build
  hook calls; the file is on disk when it returns non-null. (`ExportBuildInfoAsync` remains as
  a 1.6.0 compatibility shim and also completes its write before returning.)
- `async Task<BuildInfo> GetSavedBuildInfo(BuildTarget platform)` /
  `async Task<string> GetSavedBuildInfoJson(BuildTarget platform)` — read back a saved
  export; a missing or corrupt file logs and yields `null`.

> **Threading constraint (measured, 1.7.0):** the read APIs compose their path with
> `Application.productName`, a main-thread-only Unity API. `await` them on the main thread
> (Unity Test Framework `async` tests do this correctly). Both wrong patterns fail
> differently: calling them from a worker thread (`Task.Run`) fails the read with a logged
> `UnityException` and yields `null`; blocking the MAIN thread on their task
> (`.Wait()`/`.Result`) deadlocks the editor — the awaited continuation posts back to the
> very sync context the block is holding. Await, never block.

If the project also uses `com.unicostudio.buildsystem`, import that package's
**VersionTracker Glue** sample: a PostSuccess step that verifies the export was freshly
written for the run and attaches it to the build result. Without it, a silently failed export
goes undetected (committed per-release outputs make file existence lie for rebuilds of the
same version).

---

## Configuration

No manual configuration is needed. The tool automatically detects installed SDKs and collects their version details. Supported SDKs include:

- **Google AdMob**
- **Firebase**
- **Adjust**
- **AppLovin**
- And more!

If an SDK is not present in the project, it is simply skipped.

---

## Contributing

Feel free to open issues or create pull requests to improve this package. Contributions are welcome!