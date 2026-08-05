// Dev-project-only headless smoke build — NOT part of any package. Its one job: produce a real
// Android player build so post-build hooks (IPostprocessBuildWithReport) demonstrably fire.
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DevBuild
{
    // Unity -batchmode -nographics -buildTarget Android -executeMethod DevBuild.BuildAndroid
    public static void BuildAndroid()
    {
        const string scenePath = "Assets/Scenes/Dev.unity";
        if (!System.IO.File.Exists(scenePath))
        {
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, scenePath);
        }

        // Mono, deliberately: this is a smoke build proving hooks fire, not a shippable artifact —
        // IL2CPP would only add minutes and an NDK dependency to every verification run. ARMv7
        // must be selected explicitly with it: a fresh project defaults to ARM64, which is
        // IL2CPP-only, so Mono alone leaves zero valid architectures ("Target architecture not
        // specified", measured).
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.Mono2x);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7;

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scenePath },
            locationPathName = "Builds/Android/dev.apk",
            target = BuildTarget.Android,
            options = BuildOptions.None,
        });

        var s = report.summary;
        Debug.Log($"[DevBuild] result={s.result} output={s.outputPath} errors={s.totalErrors}");
        EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
    }
}
