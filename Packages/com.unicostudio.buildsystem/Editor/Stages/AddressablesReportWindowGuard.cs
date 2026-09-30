#if UNICO_HAS_ADDRESSABLES
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    // Restores the "no Addressables Report window" precondition before a content build.
    //
    // Measured 2026-09-30 (BT5, Addressables 2.7.4; BuildReportWindow is identical in 2.7.6): a
    // report window left open by an earlier build is re-enabled across this job's domain reloads
    // — OnEnable re-binds BuildLayoutGenerationTask.s_LayoutCompleteCallback — but its UI Toolkit
    // CreateGUI never runs. BuildScriptBase.DisplayBuildReport, the LAST line of the data build,
    // then focuses that GUI-less window and its summary tab throws a NullReferenceException;
    // BuildData turns it into a failed result and the stage fails a build whose content was
    // already fully written. With no window open (the first build of a session) DisplayBuildReport
    // opens a fresh window whose GUI is created synchronously, and the build succeeds. So: close
    // every report window and drop the callback (nothing in Addressables ever clears it, not even
    // closing the window). The report still auto-opens at the end of the build — fresh.
    //
    // Deliberately NOT done: flipping ProjectConfigData.AutoOpenAddressablesReport. It is persisted
    // to the user's settings file, so it would need a crash-safe restore, and it would suppress
    // the report the developer asked for. Nothing this guard touches outlives the session.
    //
    // Both targets are internal to com.unity.addressables and reached by reflection. If a future
    // version renames them the guard degrades to a logged no-op — never a failed build — and
    // AddressablesReportWindowGuardEditModeTests turns that drift red in CI.
    internal static class AddressablesReportWindowGuard
    {
        private const string WindowTypeName =
            "UnityEditor.AddressableAssets.BuildReportVisualizer.BuildReportWindow";
        private const string TaskTypeName =
            "UnityEditor.AddressableAssets.Build.BuildPipelineTasks.BuildLayoutGenerationTask";
        private const string CallbackFieldName = "s_LayoutCompleteCallback";

        internal readonly struct Outcome
        {
            public readonly int WindowsClosed;
            public readonly bool CallbackCleared;
            public readonly string Warning;   // null when both targets resolved

            public Outcome(int windowsClosed, bool callbackCleared, string warning)
            {
                WindowsClosed = windowsClosed;
                CallbackCleared = callbackCleared;
                Warning = warning;
            }
        }

        internal static Outcome Neutralize()
        {
            var assembly = typeof(AddressableAssetSettings).Assembly;
            string warning = null;

            var closed = 0;
            var windowType = assembly.GetType(WindowTypeName);
            if (windowType == null)
            {
                warning = $"{WindowTypeName} not found";
            }
            else
            {
                foreach (var obj in Resources.FindObjectsOfTypeAll(windowType))
                {
                    // Close() is the right path for a docked/visible window (it tears down the
                    // host tab), but it THROWS a NullReferenceException for a window that has no
                    // host view (measured: EditorWindow.cs:1097, 6000.0.62f1) — exactly the
                    // GUI-less state this guard exists for. Fall back to destroying it.
                    try
                    {
                        if (obj is EditorWindow window) window.Close();
                    }
                    catch (Exception)
                    {
                        // Unhosted window: DestroyImmediate below removes it.
                    }
                    if (obj) UnityEngine.Object.DestroyImmediate(obj);
                    closed++;
                }
            }

            var cleared = false;
            var field = assembly.GetType(TaskTypeName)
                ?.GetField(CallbackFieldName, BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null)
            {
                warning = (warning == null ? "" : warning + "; ") + $"{TaskTypeName}.{CallbackFieldName} not found";
            }
            else
            {
                field.SetValue(null, null);
                cleared = true;
            }

            return new Outcome(closed, cleared, warning);
        }
    }
}
#endif
