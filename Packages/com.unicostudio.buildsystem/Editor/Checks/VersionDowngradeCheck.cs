using UnityEditor;

namespace UnicoStudio.BuildSystem.Editor
{
    // VersionFormatCheck validates the shape; this catches transposed digits — entering 1.0.0 when
    // the project is at 1.2.7 is almost always a typo, and the stores reject the binary much later.
    public sealed class VersionDowngradeCheck : IPreflightCheck
    {
        public static bool IsDowngrade(string requested, string current)
        {
            if (!System.Version.TryParse((requested ?? string.Empty).Trim(), out var req)) return false;
            if (!System.Version.TryParse((current ?? string.Empty).Trim(), out var cur)) return false;
            return req < cur;
        }

        public CheckResult Run(BuildContext ctx)
        {
            return IsDowngrade(ctx.Request.VersionName, PlayerSettings.bundleVersion)
                ? CheckResult.Warn($"Version Name '{ctx.Request.VersionName.Trim()}' is lower than the " +
                                   $"current {PlayerSettings.bundleVersion} — transposed digits?")
                : CheckResult.Pass("Version Name is not a downgrade.");
        }
    }
}
