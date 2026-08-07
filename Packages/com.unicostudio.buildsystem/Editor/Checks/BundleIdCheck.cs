using UnityEditor;

namespace UnicoStudio.BuildSystem.Editor
{
    // A wrong application identifier (left over from an experiment) is discovered only at store
    // upload. When the target's BuildTargetConfig declares an expected id, enforce it up front.
    // An empty ExpectedBundleId (or no config) disables the check, so configs migrate safely.
    public sealed class BundleIdCheck : IPreflightCheck
    {
        public static CheckResult Evaluate(string expected, string actual)
        {
            if (string.IsNullOrEmpty(expected))
                return CheckResult.Pass("No expected bundle id configured.");

            return expected == actual
                ? CheckResult.Pass($"Bundle id matches {expected}.")
                : CheckResult.Block($"Bundle id is '{actual}' but this target expects '{expected}'.");
        }

        public CheckResult Run(BuildContext ctx)
        {
            var cfg = BuildConfigCatalog.Find(BuildConfigCatalog.LoadAll(), ctx.Request.Platform, ctx.Request.Kind);
            return Evaluate(cfg ? cfg.ExpectedBundleId : string.Empty,
                PlayerSettings.GetApplicationIdentifier(ctx.Request.Platform.ToNamedBuildTarget()));
        }
    }
}
