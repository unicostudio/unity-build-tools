using System.Text.RegularExpressions;
using UnityEditor;

namespace UnicoStudio.BuildSystem.Editor
{
    // Enforces Semantic Versioning (MAJOR.MINOR.PATCH) on the version the build will actually
    // ship. Blocks the build on a malformed version so a bad version can never reach the stores.
    public sealed class VersionFormatCheck : IPreflightCheck
    {
        // Semver core: three non-negative integers, no leading zeros.
        private static readonly Regex s_semVerCore = new(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$");

        public CheckResult Run(BuildContext ctx) =>
            Verdict(EffectiveVersion(ctx.Request.VersionName, PlayerSettings.bundleVersion));

        // Pure core (unit-tested): the semver gate itself. Extracted so the Block on a bad — or
        // absent — effective version is actually executable from a test: Run's project-version
        // fallback reads live PlayerSettings, which a test must not mutate, so before this split
        // the empty-version Block was asserted by NAME only and letting empty versions Pass left
        // the whole suite green (measured).
        internal static CheckResult Verdict(string version) =>
            s_semVerCore.IsMatch(version)
                ? CheckResult.Pass($"Version {version} follows MAJOR.MINOR.PATCH.")
                : CheckResult.Block(
                    $"Version Name '{version}' must be semantic versioning MAJOR.MINOR.PATCH (e.g. 1.2.7).");

        // Pure core (unit-tested): the string this check validates. Version Name is an OPTIONAL
        // override — ApplyVersionStage writes bundleVersion only for a non-blank request, so a
        // blank one means "ship the project's version" and THAT is what must pass the regex.
        // Treating blank as malformed broke the CLI contract: README's CI recipes omit
        // -versionName, and "just bump the build code" was inexpressible. The panel never hit it,
        // because it seeds the field from PlayerSettings — which also meant the project's real
        // version was never validated on the one path that relies on it.
        internal static string EffectiveVersion(string requested, string projectVersion) =>
            string.IsNullOrWhiteSpace(requested)
                ? (projectVersion ?? string.Empty).Trim()
                : requested.Trim();
    }
}
