using System.Collections.Generic;
using System.Linq;

namespace UnicoStudio.BuildSystem.Editor
{
    public static class PreflightRunner
    {
        public static List<CheckResult> Run(BuildContext ctx, IEnumerable<IPreflightCheck> checks)
        {
            return checks.Select(c => c.Run(ctx)).ToList();
        }

        // Decision core for Start: which message blocks, which warnings get recorded. Pure so the
        // policy matrix is unit-testable; results[i] pairs with strictExempt[i]. First blocking
        // message in registration order wins (deterministic, matches the panel's display order).
        internal static (string blockMessage, List<string> warnings) Gate(
            IReadOnlyList<CheckResult> results, IReadOnlyList<bool> strictExempt, WarnPolicy policy)
        {
            string block = null;
            var warnings = new List<string>();
            for (var i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (r.Severity == CheckSeverity.Block)
                    block ??= r.Message;
                else if (r.Severity == CheckSeverity.Warn)
                {
                    warnings.Add(r.Message);
                    if (policy == WarnPolicy.Strict && !strictExempt[i])
                        block ??= $"[strict] {r.Message}";
                }
            }
            return (block, warnings);
        }

        public static IReadOnlyList<IPreflightCheck> Default() => new IPreflightCheck[]
        {
            new StageSelectionCheck(),
            new AddressablesAvailabilityCheck(),
            // After availability, deliberately: Gate reports the FIRST blocking message, and "the
            // package is not installed" is the clearer one when both would fire.
            new RemotePathCheck(),
            new VersionFormatCheck(),
            new VersionDowngradeCheck(),
            new OutputSelectionCheck(),
            // Before the config consumers (BundleIdCheck, DefinePlanCheck, StripPackagesCheck):
            // its Pass names the config they are all about to read, its Warn explains why they
            // are all about to no-op.
            new ConfigPresenceCheck(),
            new BundleIdCheck(),
            new KeystoreCheck(),
            new PlatformMatchCheck(),
            new UnityServicesCheck(),
            new CompressionCheck(),
            new SplashLogoCheck(),
            new TestModeConsistencyCheck(),
            new DefinePlanCheck(),
            new StripPackagesCheck(),
            new TestModeDefineRenameCheck(),
            new BumpConsistencyCheck(),
            new CdnReminderCheck(),
        };
    }
}
