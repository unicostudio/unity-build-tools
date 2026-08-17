using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace UnicoStudio.BuildSystem.Editor
{
    // StripPackages preconditions (spec: docs/specs/2026-08-17-strippackages-design.md).
    // The byte-deterministic restore the feature relies on was measured with exact pins
    // only, and removing a package something else depends on would break the dependents
    // mid-build — both are caught here, before any manifest mutation.
    public sealed class StripPackagesCheck : IPreflightCheck
    {
        public CheckResult Run(BuildContext ctx)
        {
            var cfg = BuildConfigCatalog.Find(
                BuildConfigCatalog.LoadAll(), ctx.Request.Platform, ctx.Request.Kind);
            var strip = cfg ? cfg.StripPackages : null;
            if (strip == null || strip.Length == 0)
                return CheckResult.Pass("No build-scoped package strips configured.");

            var manifest = File.ReadAllText("Packages/manifest.json");
            var lockText = File.Exists("Packages/packages-lock.json")
                ? File.ReadAllText("Packages/packages-lock.json")
                : "";
            return Evaluate(strip, manifest, lockText);
        }

        // Pure core (unit-tested). Block outranks Warn; absent packages are per-package
        // no-ops named in the Pass message so a typo'd id stays visible.
        internal static CheckResult Evaluate(
            IReadOnlyList<string> strip, string manifestText, string lockText)
        {
            var blocked = new List<string>();
            var floating = new List<string>();
            var absent = new List<string>();

            foreach (var id in strip)
            {
                var match = Regex.Match(manifestText,
                    $"\"{Regex.Escape(id)}\"\\s*:\\s*\"([^\"]*)\"");
                if (!match.Success)
                {
                    absent.Add(id);
                    continue;
                }
                if (PackageStripGuard.CountKeyOccurrences(lockText, id) > 1)
                    blocked.Add(id);
                else if (!PackageStripGuard.IsExactPinned(match.Groups[1].Value))
                    floating.Add(id);
            }

            if (blocked.Count > 0)
                return CheckResult.Block(
                    $"StripPackages: {string.Join(", ", blocked)} has dependents in packages-lock.json — " +
                    "removing it mid-build would break them; strip the dependents too or drop the entry.");
            if (floating.Count > 0)
                return CheckResult.Warn(
                    $"StripPackages: {string.Join(", ", floating)} is not exact-pinned — the " +
                    "byte-deterministic restore holds only for exact pins (exact semver or a '#'-anchored git URL).");
            return CheckResult.Pass(absent.Count > 0
                ? $"StripPackages ready ({strip.Count - absent.Count} to strip; not installed, no-op: {string.Join(", ", absent)})."
                : $"StripPackages ready ({strip.Count} to strip).");
        }
    }
}
