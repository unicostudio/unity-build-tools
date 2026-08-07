# Build UI

The single build control panel.

Contents:
- `BuildPanelWindow` — an `EditorWindow` opened from `UnicoStudio ▸ BuildPanel`. Presents the `BuildRequest` fields, live pre-flight results, an artifact-name preview, and the Build button.

Rules:
- Presentation only. The panel assembles a `BuildRequest`, renders the pre-flight results (from a once-built `PreflightRunner.Default()` set cached in a static field), and hands off to `UnicoBuildService.Start`. It must not run build stages, mutate defines, or touch Addressables directly.
- Passing pre-flight checks render as ONE compact line ("✓ N/M checks passed") with a details foldout; only Warn/Block results get full HelpBoxes. The foldout state is deliberately non-serialized (kept out of the Undo stack, collapses after reloads).
- The Build button is disabled while `UnicoBuildService.IsRunning` and when the request selects no stage (both Build Addressables and Build Player off). A `Block` pre-flight result stops the click with a dialog; any `Warn` results require one explicit acknowledgement dialog (listing every warning) before `Start` — this is the safety gate that replaces the suppressed `UnicoBuildPreprocessor` modals. A needed platform switch is folded into that dialog ("Switch & Build"). While a job is running the panel offers a confirm-gated "Reset stuck job" button (`UnicoBuildService.ResetStuckJob`) for wedged jobs.
- Read-only context (current build code, current Addressables version) is displayed but never editable; the `AddressablesVersionStore` is loaded once and cached in a field (a missing store shows "unavailable" — no per-event `AssetDatabase` load or exception-as-control-flow).
- The active-vs-target platform-switch warning is rendered once by `PlatformMatchCheck` in the pre-flight results — the panel does not add a second inline HelpBox for it.
- The artifact preview must use the same `BuildArtifactNaming.FileName` the stages use, so what the user sees matches the output.
- Field labels + tooltips are cached `GUIContent` in the nested `Tips` class (no per-`OnGUI` allocation); update the tooltip there when a field's behavior changes. Multi-part tooltips separate their parts with `\n`, not run-on spacing.
- Player-only fields (compression, output toggles, label, output folder, preview) are greyed via `DisabledScope(!BuildPlayer)`, mirroring how Addressables Mode follows Build Addressables.
- The form (`_req`) is `[SerializeField]` on the window: it survives domain reloads and is undo-enabled via `Undo.RecordObject(this)` taken BEFORE the widgets, but ONLY on events that can mutate the form (mouse, key, drag-perform, and `ExecuteCommand` other than `UndoRedoPerformed`). Never record on every event: Layout/Repaint passes — including the ones the undo system triggers while restoring — would re-record the state a Ctrl+Z just restored, so undo could never leave the panel and each record would clear the redo stack. Undo/redo drops text focus + repaints via `undoRedoPerformed`. This is entirely a window concern — `BuildRequest` and the core stay untouched.
- An empty output folder is valid and means the default `Builds/{Platform}/{Kind}/` — shown as an in-field placeholder hint.
