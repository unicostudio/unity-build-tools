using System.Collections.Generic;
using System.Linq;

namespace UnicoStudio.BuildSystem.Editor
{
    // Makes the config-discovery fallback visible. A project with no BuildTargetConfig for the
    // requested platform+kind builds "successfully" on the kind-based profile fallback — with no
    // ExtraDefines, StripDefines or StripPackages applied and nothing saying so. The failure mode
    // is absence, which no config-consuming check can flag (they all no-op on a missing config),
    // so this check names the resolved config on Pass and the fallback on Warn.
    public sealed class ConfigPresenceCheck : IPreflightCheck
    {
        // Advisory by design: a deliberately config-less project (player-only, all defaults) is a
        // legitimate adoption stage, and a strict CI run of one must stay possible. The Warn is
        // for humans reading the panel and CI logs, never a gate.
        public bool IsStrictExempt => true;

        public CheckResult Run(BuildContext ctx)
            => Evaluate(ctx.Request.Platform, ctx.Request.Kind, BuildConfigCatalog.LoadAll());

        // Pure core (unit-tested). Null entries are skipped, not matched: LoadAll already filters
        // deleted assets, but a caller-assembled list must not crash the panel's check pass.
        internal static CheckResult Evaluate(BuildPlatform platform, BuildKind kind,
            IReadOnlyList<BuildTargetConfig> configs)
        {
            var present = (configs ?? System.Array.Empty<BuildTargetConfig>())
                .Where(c => c).ToList();

            if (present.Count == 0)
                return CheckResult.Warn(
                    "No BuildTargetConfig assets exist under Assets/ — builds run on the " +
                    "kind-based profile fallback, and no ExtraDefines/StripDefines/StripPackages " +
                    "apply. Create one per platform+kind via Assets > Create > " +
                    "Unico/Build/Build Target Config (convention: Assets/Settings/Build/Configs/).");

            var match = present.FirstOrDefault(c => c.Platform == platform && c.Kind == kind);
            if (!match)
                return CheckResult.Warn(
                    $"No BuildTargetConfig matches {platform}/{kind} — this build falls back to " +
                    "the kind-based profile with no config-declared defines or package strips. " +
                    $"Configs present: {string.Join(", ", present.Select(c => c.name))}.");

            return CheckResult.Pass($"Config: {match.name} ({platform}/{kind}).");
        }
    }
}
