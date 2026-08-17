using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    /// <summary>
    /// The strip window's last-writer guard (0.12.1). Measured 2026-08-17 (see CHANGELOG):
    /// a third-party package's UPM-event handler rewrote the platform defines in-session —
    /// after ConfigureDefinesStage's write, before the queued reload landed — leaving
    /// "define present, package gone", which fails every compile and wedges the job until
    /// the CI deadline. The DefineGuard (0.11.0) blocks SHIPPING such a state, but only a
    /// pre-reload counter keeps the build ALIVE.
    ///
    /// Armed by ConfigureDefinesStage after its mutations; on every editor tick it
    /// re-reads the platform's defines and rewrites the plan if anything changed them.
    /// The fight is bounded by construction: the subscription dies with this domain, and
    /// a stripped package's event handlers leave with it on the reload — the watcher only
    /// has to win until then. Disarmed by Finish before the dev-state restore so it never
    /// fights the restore itself (same-domain Finish paths: timeout, manual reset).
    /// </summary>
    internal static class DefineReassertWatcher
    {
        internal static bool IsArmed { get; private set; }
        internal static int ReassertCount { get; private set; }

        private static NamedBuildTarget s_target;
        private static string s_expected;

        internal static void Arm(NamedBuildTarget target, string expectedDefines)
        {
            s_target = target;
            s_expected = expectedDefines;
            ReassertCount = 0;
            if (!IsArmed) EditorApplication.update += OnUpdate;
            IsArmed = true;
        }

        internal static void Disarm()
        {
            if (IsArmed) EditorApplication.update -= OnUpdate;
            IsArmed = false;
        }

        /// <summary>Pure decision core (unit-tested): exact-string plan comparison.</summary>
        internal static bool NeedsReassert(string current, string expected) => current != expected;

        private static void OnUpdate() => TickCore(
            PlayerSettings.GetScriptingDefineSymbols(s_target), write: true);

        /// <summary>Test seam: the tick's decision+count path without touching PlayerSettings.</summary>
        internal static bool SimulateTickForTests(string currentDefines) => TickCore(currentDefines, write: false);

        private static bool TickCore(string current, bool write)
        {
            if (!IsArmed || !NeedsReassert(current, s_expected)) return false;
            ReassertCount++;
            Debug.LogWarning($"[Build] Define re-assert #{ReassertCount}: something rewrote the platform " +
                             $"defines inside the strip window ('{current}') — restoring the build's plan " +
                             $"('{s_expected}'). Third-party package-event handlers are the measured cause.");
            if (write) PlayerSettings.SetScriptingDefineSymbols(s_target, s_expected);
            return true;
        }

        internal static void ResetForTests()
        {
            Disarm();
            ReassertCount = 0;
            s_expected = null;
        }
    }
}
