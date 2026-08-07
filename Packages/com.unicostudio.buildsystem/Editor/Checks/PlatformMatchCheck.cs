using UnityEditor;

namespace UnicoStudio.BuildSystem.Editor
{
    public sealed class PlatformMatchCheck : IPreflightCheck
    {
        public CheckResult Run(BuildContext ctx)
        {
            var want = ctx.Request.Platform.ToBuildTarget();
            var active = EditorUserBuildSettings.activeBuildTarget;
            if (active == want)
                return CheckResult.Pass($"Active platform is {want}.");
            // Warn (not Block): the orchestrator performs the switch; the panel confirms it.
            return CheckResult.Warn(
                $"Active platform is {active}, target is {want}. Building will switch (reimports assets, slow).");
        }
    }
}
