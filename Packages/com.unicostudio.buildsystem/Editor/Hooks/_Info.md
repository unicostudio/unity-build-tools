# Build Hooks

The legacy manual-build guard, kept working alongside the panel.

Contents:
- `UnicoBuildPreprocessor` — `IPreprocessBuildWithReport`. On a build it shows modal confirmation dialogs (test-mode warning, splash-logo warning, and a release checklist) for **manual** builds started outside the panel (`File ▸ Build`, `Ctrl+B`, etc.).

Rules:
- This runs for every build, so it must early-out for panel builds: `if (BuildSession.IsBuildingViaPanel) return;`. Panel builds already run non-blocking pre-flight, and modal dialogs would stall an automated run.
- Detect `TEST_MODE` via `BuildDefineResolver.TEST_MODE_DEFINE`, not a string literal.
- `UnicoBuildPreprocessor` lives in the global namespace (historical) but compiles into `Assembly-CSharp-Editor` via the `Editor` folder; it reaches the build namespace through `using UnicoStudio.BuildSystem.Editor;`.
- Keep this thin — it is a safety net for humans building manually, not part of the orchestrated pipeline.
