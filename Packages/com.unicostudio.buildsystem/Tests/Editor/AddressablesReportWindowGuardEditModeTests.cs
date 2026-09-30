#if UNICO_HAS_ADDRESSABLES
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    /// <summary>
    /// Pins the precondition AddressablesStage restores before a content build.
    ///
    /// Measured 2026-09-30 on BT5 (Addressables 2.7.4, identical BuildReportWindow in 2.7.6):
    /// after one successful build the Addressables Report window stays open; across the next
    /// job's domain reloads it is re-enabled (OnEnable re-binds
    /// BuildLayoutGenerationTask.s_LayoutCompleteCallback) but its UI Toolkit CreateGUI never
    /// runs. At the end of the next content build BuildScriptBase.DisplayBuildReport focuses that
    /// GUI-less window and LoadNewestReport → Consume → MainPanelSummaryTab.ClearGUI throws a
    /// NullReferenceException, which BuildData turns into a failed result — the stage reported
    /// "New Build failed" although every file had been written. A build with NO window open
    /// (the first build of the session) opens a fresh window whose GUI is created
    /// synchronously and succeeds; the guard recreates exactly that state.
    ///
    /// The GUI-less window is produced the way the BT5 reproduction did:
    /// ScriptableObject.CreateInstance on the internal window type — OnEnable runs, CreateGUI
    /// does not.
    /// </summary>
    public sealed class AddressablesReportWindowGuardEditModeTests
    {
        private static readonly Assembly s_addressablesEditor = typeof(AddressableAssetSettings).Assembly;

        private static Type WindowType =>
            s_addressablesEditor.GetType("UnityEditor.AddressableAssets.BuildReportVisualizer.BuildReportWindow");

        private static FieldInfo CallbackField =>
            s_addressablesEditor
                .GetType("UnityEditor.AddressableAssets.Build.BuildPipelineTasks.BuildLayoutGenerationTask")
                ?.GetField("s_LayoutCompleteCallback", BindingFlags.Static | BindingFlags.NonPublic);

        private object _savedCallback;

        [SetUp]
        public void SaveCallback() => _savedCallback = CallbackField?.GetValue(null);

        [TearDown]
        public void RestoreCallbackAndCleanUp()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll(WindowType))
                UnityEngine.Object.DestroyImmediate(w);
            CallbackField?.SetValue(null, _savedCallback);
        }

        [Test]
        public void ReflectionTargets_ExistOnThisAddressablesVersion()
        {
            // If a future Addressables renames these, the guard degrades to a logged no-op —
            // this test is what makes that drift loud in CI instead of silent in production.
            Assert.IsNotNull(WindowType, "BuildReportWindow type not found");
            Assert.IsNotNull(CallbackField, "BuildLayoutGenerationTask.s_LayoutCompleteCallback not found");
        }

        [Test]
        public void GuiLessWindow_IsClosed_AndTheCallbackIsCleared()
        {
            ScriptableObject.CreateInstance(WindowType);   // OnEnable runs, CreateGUI never does
            Assume.That(Resources.FindObjectsOfTypeAll(WindowType).Length, Is.EqualTo(1));
            Assume.That(CallbackField.GetValue(null), Is.Not.Null, "OnEnable binds the callback");

            var outcome = AddressablesReportWindowGuard.Neutralize();

            Assert.AreEqual(0, Resources.FindObjectsOfTypeAll(WindowType).Length,
                "the stale window must be gone so DisplayBuildReport opens a fresh one");
            Assert.IsNull(CallbackField.GetValue(null),
                "nothing in Addressables clears the callback — a closed window would stay bound");
            Assert.AreEqual(1, outcome.WindowsClosed);
            Assert.IsTrue(outcome.CallbackCleared);
        }

        [Test]
        public void NoWindowOpen_IsANoOp_ThatStillClearsAStaleCallback()
        {
            CallbackField.SetValue(null, null);

            var outcome = AddressablesReportWindowGuard.Neutralize();

            Assert.AreEqual(0, outcome.WindowsClosed);
            Assert.IsNull(CallbackField.GetValue(null));
            Assert.IsNull(outcome.Warning, "a clean editor must not produce a warning");
        }
    }
}
#endif
