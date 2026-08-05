// Dev-project-only verification for the v1.6.0 brief's T9 — NOT part of any package.
// Prints GetBuildInfoPath for Android and iOS with [T9] markers so a headless run's log can be
// compared character-for-character against the "Build info saved to ..." line of a real build,
// and proves the query created no UnicoVersionTracker/ directory (run it on a clean checkout).
using System.IO;
using UnicoStudio.UnicoLibs.VersionTracker;
using UnityEditor;
using UnityEngine;

public static class T9Verify
{
    // Unity -batchmode -nographics -executeMethod T9Verify.PrintPathsAndCheckPurity
    public static void PrintPathsAndCheckPurity()
    {
        var existedBefore = Directory.Exists("UnicoVersionTracker");

        var androidPath = UnicoVersionExporter.GetBuildInfoPath(BuildTarget.Android);
        var iosPath = UnicoVersionExporter.GetBuildInfoPath(BuildTarget.iOS);

        var existsAfter = Directory.Exists("UnicoVersionTracker");

        Debug.Log($"[T9] GetBuildInfoPath(Android)={androidPath}");
        Debug.Log($"[T9] GetBuildInfoPath(iOS)={iosPath}");
        Debug.Log($"[T9] UnicoVersionTracker dir existed before calls: {existedBefore}");
        Debug.Log($"[T9] UnicoVersionTracker dir exists after calls: {existsAfter}");

        // Purity holds only if the directory was absent going in and is still absent afterwards.
        EditorApplication.Exit(!existedBefore && !existsAfter ? 0 : 1);
    }
}
