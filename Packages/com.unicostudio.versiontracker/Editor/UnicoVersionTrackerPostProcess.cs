using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UnicoStudio.UnicoLibs.VersionTracker
{
    public class UnicoVersionTrackerPostProcess : IPostprocessBuildWithReport
    {
        // Priority for execution order of the build process.
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.result is BuildResult.Failed or BuildResult.Cancelled)
            {
                Debug.LogError("Build is failed or cancelled, so UnicoVersionTracker is not executed!");
                return;
            }

            UnicoVersionExporter.ExportBuildInfoAsync(report.summary);
        }
    }
}