using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnicoStudio.BuildSystem;
using UnicoStudio.BuildSystem.Editor;

public class UnicoBuildPreprocessor : IPreprocessBuildWithReport
{
    private const string TEST_MODE_TITLE = "Test Mode Enabled!";
    private const string SPLASH_LOGO_TITLE = "Splash Unity Logo ON!";
    private const string CHECKLIST_TITLE = "CHECKLIST";
    private const string TEST_MODE_MESSAGE = "You are trying to build while in test mode.";
    private const string SPLASH_LOGO_MESSAGE = "Unity logo is shown on the Splash Screen.";
    private const string ARE_YOU_SURE = "\n\nAre you sure you want to continue?";
    private const string OK = "Yes";
    private const string CANCEL = "Cancel";

    private const string COMMON_CHECKLIST_MESSAGE = "- Unity Services connected" +
                                                    "\n- Addressables build is updated with correct version no" +
                                                    "\n- Compression Method: LZ4HC";

    private const string ANDROID_CHECKLIST_MESSAGE = "\n- Bundle Version Code is increased";
    private const string IOS_CHECKLIST_MESSAGE = "";

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        // Batchmode has no one to answer a modal; today DisplayDialog silently auto-affirms there.
        // Make that intentional: log and skip the legacy dialog flow entirely.
        if (UnityEngine.Application.isBatchMode)
        {
            UnityEngine.Debug.Log("[Build] Batchmode: legacy pre-build dialogs skipped.");
            return;
        }

        // Panel-driven builds run their own non-blocking pre-flight; suppress modal dialogs.
        if (BuildSession.IsBuildingViaPanel)
            return;

        NamedBuildTarget namedBuildTarget;
        var checklistMessage = COMMON_CHECKLIST_MESSAGE;
        switch (report.summary.platform)
        {
            case BuildTarget.Android:
                namedBuildTarget = NamedBuildTarget.Android;
                checklistMessage += ANDROID_CHECKLIST_MESSAGE;
                break;
            case BuildTarget.iOS:
                namedBuildTarget = NamedBuildTarget.iOS;
                checklistMessage += IOS_CHECKLIST_MESSAGE;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        var isTestMode = BuildDefineResolver.HasDefine(
            PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget),
            BuildSystemSettings.ResolveTestModeDefine());
        if (isTestMode)
        {
            DisplayDialog(TEST_MODE_TITLE, TEST_MODE_MESSAGE, OK, CANCEL);
        }
        else
        {
            if (PlayerSettings.SplashScreen.show && PlayerSettings.SplashScreen.showUnityLogo)
            {
                DisplayDialog(SPLASH_LOGO_TITLE, SPLASH_LOGO_MESSAGE, OK, CANCEL);
            }

            DisplayDialog(CHECKLIST_TITLE, checklistMessage, OK, CANCEL);
        }

        return;

        void DisplayDialog(string title, string message, string ok, string cancel)
        {
            var result = EditorUtility.DisplayDialog(title, message + ARE_YOU_SURE, ok, cancel);
            if (!result)
            {
                throw new BuildFailedException("Build canceled!");
            }
        }
    }
}