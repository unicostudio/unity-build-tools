using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    public sealed class BuildPanelWindow : EditorWindow, IHasCustomMenu
    {
        // Per-user presentation preference (EditorPrefs, NOT project data): kind accent tints.
        private const string AccentPrefKey = "UnicoBuild.Panel.AccentColors";
        private static bool AccentsEnabled => EditorPrefs.GetBool(AccentPrefKey, true);

        // Window-tab context menu (standard Unity pattern) — zero layout cost, discoverable.
        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("Accent Colors"), AccentsEnabled, () =>
            {
                EditorPrefs.SetBool(AccentPrefKey, !AccentsEnabled);
                Repaint();
            });
        }

        // IconContent lookups cached per name (an uncached lookup allocates every frame); the
        // name set itself lives in PanelDecisions and is pinned by PanelDecisionsEditModeTests.
        private static readonly Dictionary<string, GUIContent> s_icons = new();
        private static Texture IconTex(string name)
        {
            if (!s_icons.TryGetValue(name, out var content))
            {
                content = EditorGUIUtility.IconContent(name);
                s_icons[name] = content;
            }
            return content.image;
        }

        // Boxed section with an icon header; accent tints the box background only (color is
        // never the sole carrier — the header text and the kind popup stay authoritative).
        private static void BeginSection(string title, string iconName, Color background)
        {
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = background;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = prev;
            EditorGUILayout.LabelField(new GUIContent(" " + title, IconTex(iconName)), EditorStyles.boldLabel);
        }

        private static void EndSection()
        {
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        // Serialized so the form survives domain reloads (platform switch, define restore) and so
        // the Undo system can snapshot it. BuildRequest itself stays a plain core model — undo is
        // purely a window concern and adds nothing a future CLI caller would ever see.
        [SerializeField] private BuildRequest _req = new();
        private Vector2 _scroll;

        // Deliberately NOT serialized: keeps foldout toggles out of the Undo stack (RecordObject
        // snapshots serialized fields) and collapses back to the compact view after reloads.
        private bool _showPassedChecks;
        private AddressablesVersionStore _versionStore;
        // Last IsRunning seen by the repaint pump — a job that concludes inside an update tick
        // needs one more repaint, or the panel keeps showing "Building..." until the user
        // clicks it. Not serialized: a domain reload re-reads the real state anyway.
        private bool _wasRunning;

        // Stateless checks; build the set once instead of re-allocating them every OnGUI event.
        private static readonly IReadOnlyList<IPreflightCheck> s_checks = PreflightRunner.Default();

        // Cached label + tooltip content (avoids allocating a GUIContent every OnGUI event).
        private static class Tips
        {
            public static readonly GUIContent Platform = new("Platform",
                "Target platform.\nBuilding for a platform that isn't active triggers a slow reimport + domain reload.");
            public static readonly GUIContent BuildType = new("Build Type",
                $"Test → {BuildSystemSettings.ResolveTestModeDefine()} define on + Test addressables profile." +
                $"\nRelease → {BuildSystemSettings.ResolveTestModeDefine()} off + Production profile.");
            public static readonly GUIContent VersionName = new("Version Name",
                "App version (Android versionName / iOS CFBundleShortVersionString).\nMust be semantic versioning MAJOR.MINOR.PATCH, e.g. 1.2.7.");
            public static readonly GUIContent BumpBuildCode = new("Bump Build Code (+1)",
                "Increment the platform build number by 1 (Android bundleVersionCode / iOS buildNumber).\nApplied at build start; rolled back automatically if the build fails.");
            public static readonly GUIContent BumpAddressables = new("Bump Addressables Version",
                "Increment the remote content version by 1 — new bundles land in the v{N+1} folder.\nThe catalog stays at its original pinned path and is updated in place, so this is valid for player releases and content-only updates alike.");
            public static readonly GUIContent BuildAddressables = new("Build Addressables",
                "Run the Addressables content build stage (switch profile, clean, then Update Previous or New Build).");
            public static readonly GUIContent AddressablesMode = new("Addressables Mode",
                "Update Previous → incremental catalog update for already-shipped clients.\nNew Build → full rebuild; can break catalog continuity.");
            public static readonly GUIContent BuildPlayer = new("Build Player",
                "Run the actual app build (APK / AAB / Xcode project).\nUntick to run only the version / define / Addressables steps — e.g. a content-only update.");
            public static readonly GUIContent Compression = new("Compression",
                "Player-data compression for the app build (the Build Settings window's Compression Method does not apply to scripted builds).\nLZ4HC → project standard: smallest output, slower build.\nLZ4 → faster build, larger output.\nDefault → platform default.");
            public static readonly GUIContent OutputApk = new("Output APK",
                "Produce an .apk (direct install / sideload).");
            public static readonly GUIContent OutputAab = new("Output AAB",
                "Produce an .aab (Google Play upload; debug symbols are included for the AAB).");
            public static readonly GUIContent Label = new("Label (optional)",
                "Optional suffix in the artifact filename, kept to letters and digits.");
            public static readonly GUIContent OutputFolder = new("Output Folder",
                "Destination folder for the artifact.\nLeave empty to use Builds/{Platform}/{Kind}/.");
        }

        [MenuItem("UnicoStudio/BuildPanel", priority = 2000)]
        public static void Open() => GetWindow<BuildPanelWindow>("Build Panel");

        private void OnEnable() => Undo.undoRedoPerformed += OnUndoRedo;
        private void OnDisable() => Undo.undoRedoPerformed -= OnUndoRedo;

        // ~10 Hz repaint while a job runs, so the progress block advances between stages and
        // reloads without user interaction. Idle ticks answer from BuildJobState's static cache.
        private void OnInspectorUpdate()
        {
            var running = UnicoBuildService.IsRunning;
            if (ShouldRepaint(running, _wasRunning)) Repaint();
            _wasRunning = running;
        }

        // Pure repaint decision (unit-tested): repaint while running, plus once on the running ->
        // idle edge so the final result replaces the progress block without waiting for the next
        // user interaction.
        internal static bool ShouldRepaint(bool running, bool wasRunning) => running || running != wasRunning;

        // Pure path decision (unit-tested): producers compose artifact paths their own way — the
        // tracker's GetBuildInfoPath legitimately records "Assets/../UnicoVersionTracker/…".
        // File.Exists resolves that against the project root, but the OS file viewer behind
        // RevealInFinder does not: a raw ".." segment lands it in the wrong folder (surfaced live
        // by BT5's C4 Gate 2). Check and reveal must therefore share ONE normalized path; anything
        // GetFullPath cannot resolve falls back to the raw string, whose Exists() check then
        // routes honestly to the not-found dialog.
        internal static string ResolveArtifactPath(string path)
        {
            try { return Path.GetFullPath(path); }
            catch (Exception) { return path; }
        }

        private static string FormatDuration(double seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s"
                : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds}s"
                : $"{t.Seconds}s";
        }

        // Drop text-field focus so its edit buffer doesn't mask the reverted value, then redraw.
        private void OnUndoRedo()
        {
            GUIUtility.keyboardControl = 0;
            Repaint();
        }

        // One explicit acknowledgement for every Warn-severity pre-flight result. The legacy
        // UnicoBuildPreprocessor modals are suppressed for panel builds, so this dialog is the
        // safety gate that replaces them. A needed platform switch always comes with the
        // PlatformMatchCheck warning, so its confirmation is folded in via the button label.
        private static bool ConfirmWarnings(List<CheckResult> results, bool needsSwitch)
        {
            var warns = results.Where(r => r.Severity == CheckSeverity.Warn).ToArray();
            if (warns.Length == 0) return true;
            return EditorUtility.DisplayDialog(
                $"Build with {warns.Length} warning{(warns.Length > 1 ? "s" : "")}?",
                string.Join("\n\n", warns.Select(w => "• " + w.Message)),
                needsSwitch ? "Switch & Build" : "Build", "Cancel");
        }

        // One row: [☑ label] then the current value, shown as "current → next" when the bump is ticked (read-only).
        private static void VersionRow(GUIContent label, ref bool bump, string valueText)
        {
            var r = EditorGUILayout.GetControlRect();
            var toggleWidth = EditorGUIUtility.labelWidth + 18f;
            bump = EditorGUI.ToggleLeft(new Rect(r.x, r.y, toggleWidth, r.height), label, bump);
            using (new EditorGUI.DisabledScope(true))
                EditorGUI.LabelField(new Rect(r.x + toggleWidth + 4f, r.y, r.width - toggleWidth - 4f, r.height), valueText);
        }

        private void OnGUI()
        {
            EditorGUIUtility.labelWidth = 210f;
            // IMGUI safety: Start/Reset run synchronously inside button callbacks and flip this
            // state mid-event; every control after the flip would then diverge from the Layout
            // pass (layout-mismatch ArgumentException). Read once per pass and use throughout.
            var isRunning = UnicoBuildService.IsRunning;
            var last = UnicoBuildService.LatestResult;
            // Undo snapshot, taken before any widget can write to _req — but ONLY on events that
            // can actually mutate it. Recording on every event (Layout/Repaint included) re-records
            // the state an in-flight Ctrl+Z just restored, so undo can never escape the panel, and
            // every such record clears the redo stack. UndoRedoPerformed is excluded for the same
            // reason: it fires while the undo system is applying a restore.
            var evt = Event.current;
            if (evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag ||
                evt.type == EventType.MouseUp || evt.type == EventType.KeyDown ||
                evt.type == EventType.DragPerform ||
                (evt.type == EventType.ExecuteCommand && evt.commandName != "UndoRedoPerformed"))
            {
                Undo.RecordObject(this, "Build Panel");
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // Effective bump values FIRST, from last event's stage toggles: a greyed row must
            // never smuggle a hidden true into Start() or into the preflight run below (same
            // force-false rule as the Addressables-absent branch). One-frame convergence is
            // standard IMGUI — the toggle click itself triggers the next repaint.
            var gate = PanelDecisions.GateVersionRows(_req.BuildPlayer, _req.BuildAddressables,
                _req.BumpBuildCode, _req.BumpAddressablesVersion);
            _req.BumpBuildCode = gate.EffectiveBumpCode;
            _req.BumpAddressablesVersion = gate.EffectiveBumpAddressables;

            var accent = PanelDecisions.AccentFor(_req.Kind, AccentsEnabled);
            // One discovery for the window's own consumers (the config row and ResolveProfile);
            // checks that read configs still run their own LoadAll inside the preflight pass.
            var allConfigs = BuildConfigCatalog.LoadAll();

            BeginSection("Target", _req.Platform == BuildPlatform.Android
                ? PanelDecisions.IconAndroid : PanelDecisions.IconIos, accent);
            var prevPlatform = _req.Platform;
            var prevKind = _req.Kind;
            _req.Platform = (BuildPlatform)EditorGUILayout.EnumPopup(Tips.Platform, _req.Platform);
            _req.Kind = (BuildKind)EditorGUILayout.EnumPopup(Tips.BuildType, _req.Kind);
            // Re-seed output defaults when the target changes: Release ships APK+AAB,
            // Test defaults to a sideload APK (AAB stays selectable for special flows
            // like TEST_MODE verification on Play internal testing).
            if ((_req.Platform != prevPlatform || _req.Kind != prevKind) && _req.Platform == BuildPlatform.Android)
                _req.Outputs = _req.Kind == BuildKind.Release ? OutputKind.Apk | OutputKind.Aab : OutputKind.Apk;
            // Which config this build resolves, visible at a glance (previously only inside the
            // collapsed checks foldout). NOT BuildConfigCatalog.Find: its duplicate complaint is
            // a LogError, and this line renders every repaint — ConfigPresenceCheck owns the
            // messaging, this row only mirrors the resolution.
            var resolvedConfig = allConfigs.FirstOrDefault(c => c && c.Platform == _req.Platform && c.Kind == _req.Kind);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.LabelField("Config", resolvedConfig ? resolvedConfig.name : "none — kind-based fallback (see checks)");
            EndSection();

            // active/want feed needsSwitch below; the switch warning itself is rendered once by
            // PlatformMatchCheck in the pre-flight results (no duplicate inline HelpBox).
            var active = EditorUserBuildSettings.activeBuildTarget;
            var want = _req.Platform.ToBuildTarget();

            BeginSection("Version", PanelDecisions.IconVersion, Color.white);
            var code = _req.Platform == BuildPlatform.Android
                ? PlayerSettings.Android.bundleVersionCode
                : int.TryParse(PlayerSettings.iOS.buildNumber, out var n) ? n : 0;
            if (string.IsNullOrEmpty(_req.VersionName)) _req.VersionName = PlayerSettings.bundleVersion;
            // VersionName is deliberately NOT gated by the stage toggles: ApplyVersionStage runs
            // unconditionally in every job (content-only builds stamp the version too).
            _req.VersionName = EditorGUILayout.TextField(Tips.VersionName, _req.VersionName);

            using (new EditorGUI.DisabledScope(!gate.CodeRowEnabled))
                VersionRow(Tips.BumpBuildCode, ref _req.BumpBuildCode,
                    _req.BumpBuildCode ? $"{code}  →  {code + 1}" : code.ToString());

            if (UnicoBuildService.AddressablesAvailable)
            {
                if (!_versionStore) _versionStore = AddressablesVersionStore.LoadOrNull();
                string addrValue;
                if (_versionStore)
                {
                    var v = _versionStore.Version;
                    addrValue = _req.BumpAddressablesVersion ? $"{v}  →  {v + 1}" : v.ToString();
                }
                else addrValue = "unavailable (no version store)";
                using (new EditorGUI.DisabledScope(!gate.AddressablesRowEnabled))
                    VersionRow(Tips.BumpAddressables, ref _req.BumpAddressablesVersion, addrValue);
            }
            EndSection();

            BeginSection("Stages", PanelDecisions.IconStages, Color.white);
            if (UnicoBuildService.AddressablesAvailable)
            {
                _req.BuildAddressables = EditorGUILayout.Toggle(Tips.BuildAddressables, _req.BuildAddressables);
                using (new EditorGUI.DisabledScope(!_req.BuildAddressables))
                    _req.AddressablesMode = (AddressablesMode)EditorGUILayout.EnumPopup(Tips.AddressablesMode, _req.AddressablesMode);
                if (_req.BuildAddressables && _req.AddressablesMode == AddressablesMode.NewBuild)
                    EditorGUILayout.HelpBox("New Build can break catalog continuity for shipped clients.", MessageType.Warning);
            }
            else
            {
                // Package absent: the serialized defaults/stale values must never reach Start —
                // _req persists across sessions, so a host that removed Addressables could keep a
                // hidden BumpAddressablesVersion=true. Runs every OnGUI pass, BEFORE
                // PreflightRunner.Run below, so checks see the effective values.
                _req.BuildAddressables = false;
                _req.BumpAddressablesVersion = false;
            }
            _req.BuildPlayer = EditorGUILayout.Toggle(Tips.BuildPlayer, _req.BuildPlayer);
            // Everything below only shapes the player artifact — grey it out when there is none.
            using (new EditorGUI.DisabledScope(!_req.BuildPlayer))
            {
                _req.Compression = (CompressionKind)EditorGUILayout.EnumPopup(Tips.Compression, _req.Compression);

                if (_req.Platform == BuildPlatform.Android)
                {
                    // AAB is offered for every kind: TEST_MODE builds also go to Play
                    // internal testing, which only accepts app bundles.
                    var apk = EditorGUILayout.Toggle(Tips.OutputApk, (_req.Outputs & OutputKind.Apk) != 0);
                    var aab = EditorGUILayout.Toggle(Tips.OutputAab, (_req.Outputs & OutputKind.Aab) != 0);
                    _req.Outputs = (apk ? OutputKind.Apk : 0) | (aab ? OutputKind.Aab : 0);
                }
                else
                {
                    _req.Outputs = OutputKind.XcodeProject;
                }
            }
            EndSection();

            BeginSection("Output", PanelDecisions.IconOutput, Color.white);
            using (new EditorGUI.DisabledScope(!_req.BuildPlayer))
            {
                _req.Label = EditorGUILayout.TextField(Tips.Label, _req.Label);
                var cleanLabel = BuildArtifactNaming.SanitizeLabel(_req.Label);
                if (cleanLabel != _req.Label)
                    EditorGUILayout.HelpBox($"Unsupported characters removed — label will be '{cleanLabel}'.", MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _req.OutputFolder = EditorGUILayout.TextField(Tips.OutputFolder, _req.OutputFolder);
                    if (string.IsNullOrWhiteSpace(_req.OutputFolder) && Event.current.type == EventType.Repaint)
                    {
                        var fieldRect = GUILayoutUtility.GetLastRect();
                        var hintRect = new Rect(fieldRect.x + EditorGUIUtility.labelWidth + 2f, fieldRect.y,
                            fieldRect.width - EditorGUIUtility.labelWidth - 4f, fieldRect.height);
                        var prev = GUI.color;
                        GUI.color = new Color(1f, 1f, 1f, 0.4f);
                        GUI.Label(hintRect, $"Builds/{_req.Platform}/{_req.Kind}/  (default when empty)");
                        GUI.color = prev;
                    }
                    if (GUILayout.Button("...", GUILayout.Width(30)))
                    {
                        var picked = EditorUtility.OpenFolderPanel("Output Folder", "Builds", "");
                        if (!string.IsNullOrEmpty(picked)) _req.OutputFolder = picked;
                    }
                }
            }
            EndSection();

            var results = PreflightRunner.Run(
                new BuildContext(_req)
                {
                    Profile = BuildConfigCatalog.ResolveProfile(allConfigs, _req.Platform, _req.Kind),
                },
                s_checks);
            // Passing checks collapse into one line (expandable for details) — screen space goes
            // to what needs attention: Warn/Block keep their full HelpBoxes.
            var passCount = results.Count(r => r.Severity == CheckSeverity.Pass);
            if (passCount > 0)
            {
                _showPassedChecks = EditorGUILayout.Foldout(_showPassedChecks, $"✓ {passCount}/{results.Count} checks passed", toggleOnLabelClick: true);
                if (_showPassedChecks)
                {
                    using (new EditorGUI.IndentLevelScope())
                    using (new EditorGUI.DisabledScope(true))
                    {
                        foreach (var r in results)
                        {
                            if (r.Severity == CheckSeverity.Pass)
                                EditorGUILayout.LabelField("• " + r.Message, EditorStyles.miniLabel);
                        }
                    }
                }
            }
            foreach (var r in PanelDecisions.OrderNonPassForDisplay(results))
            {
                EditorGUILayout.HelpBox(r.Message,
                    r.Severity == CheckSeverity.Block ? MessageType.Error : MessageType.Warning);
            }

            var block = results.FirstOrDefault(r => r.Severity == CheckSeverity.Block);
            var needsSwitch = active != want;
            // Both stages off = nothing to run; StageSelectionCheck's Block box explains why.
            var nothingToDo = !_req.BuildAddressables && !_req.BuildPlayer;
            // The artifact-name preview sits right above the button it describes — the last
            // thing read before committing. Player builds only: a content-only job has no
            // player artifact to preview. ApplyVersionStage bumps before naming, so the
            // preview must show the post-bump code.
            if (_req.BuildPlayer)
            {
                EditorGUILayout.Space();
                var previewCode = _req.BumpBuildCode ? code + 1 : code;
                EditorGUILayout.LabelField("Preview", BuildArtifactNaming.FileName(
                    BuildArtifactNaming.ProductPrefix, _req.Label, _req.VersionName, previewCode, DateTime.Now, _req.Kind,
                    _req.Platform == BuildPlatform.iOS ? ""
                        : (_req.Outputs & OutputKind.Aab) != 0 ? "aab" : "apk"));
            }
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = accent;
            using (new EditorGUI.DisabledScope(isRunning || nothingToDo))
            {
                if (GUILayout.Button(isRunning ? "Building..." : "Build", GUILayout.Height(44)))
                {
                    if (block.Severity == CheckSeverity.Block)
                    {
                        EditorUtility.DisplayDialog("Cannot build", block.Message, "OK");
                    }
                    else if (ConfirmWarnings(results, needsSwitch))
                    {
                        UnicoBuildService.Start(_req);
                    }
                }
            }
            GUI.backgroundColor = prevBg;

            if (isRunning && GUILayout.Button("Reset stuck job")
                && EditorUtility.DisplayDialog("Reset build job",
                    "Clear the active build job, restore the captured dev state, roll back its version bumps, " +
                    "and roll back the addressables content state this job armed (restoring the backup, or " +
                    "deleting the file if the job created it)? That file is git-ignored, so there is no undo. " +
                    "Use this only when a build appears stuck.",
                    "Reset", "Cancel"))
            {
                UnicoBuildService.ResetStuckJob();
            }

            if (isRunning)
            {
                var progress = UnicoBuildService.GetProgress();
                if (progress != null)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField(
                        $"Building — stage {progress.StageNumber}/{progress.StageCount}: {progress.CurrentStageName}",
                        EditorStyles.boldLabel);
                    foreach (var s in progress.Steps) EditorGUILayout.LabelField("• " + s);
                }
            }

            if (last != null)
            {
                EditorGUILayout.Space();
                var duration = last.DurationSeconds > 0 ? $"  ({FormatDuration(last.DurationSeconds)})" : "";
                var started = PanelDecisions.FormatStartedLocal(last.StartedUtc);
                EditorGUILayout.LabelField(new GUIContent(
                        (last.Success ? " Last build: OK" : " Last build: FAILED") + duration +
                        (started.Length > 0 ? $"  —  {started}" : ""),
                        IconTex(last.Success ? PanelDecisions.IconResultOk : PanelDecisions.IconResultFailed)),
                    EditorStyles.boldLabel);
                foreach (var s in last.Steps) EditorGUILayout.LabelField("• " + s);
                if (last.Artifacts.Count > 0)
                {
                    EditorGUILayout.LabelField("Artifacts", EditorStyles.boldLabel);
                    foreach (var a in last.Artifacts)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField("• " + a);
                            if (GUILayout.Button("Show", GUILayout.Width(48)))
                            {
                                var resolved = ResolveArtifactPath(a);
                                if (File.Exists(resolved) || Directory.Exists(resolved)) EditorUtility.RevealInFinder(resolved);
                                else EditorUtility.DisplayDialog("Not found", "Artifact no longer exists:\n" + resolved, "OK");
                            }
                        }
                    }
                }
                if (!last.Success) EditorGUILayout.HelpBox(last.Error, MessageType.Error);
            }

            // Bottom padding so the last row (artifact Show buttons) doesn't sit flush against
            // the window edge.
            EditorGUILayout.Space(24f);

            EditorGUILayout.EndScrollView();
        }
    }
}
