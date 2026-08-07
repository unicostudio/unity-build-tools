using UnityEditor;

namespace UnicoStudio.BuildSystem.Editor
{
    // Project binding to Unity Services (Services General Settings). A non-empty
    // CloudProjectSettings.projectId proves the binding exists; the editor's sign-in/online state
    // is not readable through public API, so that part stays on the human checklist.
    public sealed class UnityServicesCheck : IPreflightCheck
    {
        public CheckResult Run(BuildContext ctx)
        {
            // Batchmode never initializes the editor's cloud-services state, so projectId reads
            // empty regardless of the real binding — the probe is blind here, not the project
            // unbound. Interactive sessions (the panel) remain the real guard for this check.
            if (UnityEngine.Application.isBatchMode)
                return CheckResult.Pass("Unity Services binding not verifiable in batchmode (checked interactively).");

            var projectId = CloudProjectSettings.projectId;
            if (string.IsNullOrEmpty(projectId))
                return CheckResult.Block("Project is not bound to Unity Services (Services General Settings).");
            return CheckResult.Pass($"Unity Services bound (project {projectId}).");
        }
    }
}
