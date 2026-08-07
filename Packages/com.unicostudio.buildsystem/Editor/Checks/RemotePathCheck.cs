#if UNICO_HAS_ADDRESSABLES
using UnityEditor.AddressableAssets;
#endif

namespace UnicoStudio.BuildSystem.Editor
{
    // A profile binding that does not resolve bakes a garbage path into the catalog, and the
    // Addressables evaluator reports it by silently substituting the token's own text. The stage
    // already refuses to build on that — but only after the platform switch and its domain reload.
    // Checking here turns minutes into a Block before anything happens.
    //
    // This check reads through Addressables' domain-lifetime property cache and deliberately does
    // NOT clear it. Run executes on every panel repaint; clearing there would throw away the
    // caching for the whole editor session and would be this package reimplementing another
    // package's behaviour. The cost is that a Pass here can be optimistically stale — the profile
    // resolved once, and the binding has broken since. That is acceptable: AddressablesStage clears
    // the cache itself and re-validates before anything is written, so a stale Pass costs a late
    // failure, never a bad catalog.
    public sealed class RemotePathCheck : IPreflightCheck
    {
        // Pure core. `raw` is the profile's Remote.LoadPath as written, `loadPath` is what the
        // Addressables evaluator made of it. The verdict comes from ProfileBindingRule — the same
        // method AddressablesStage acts on — so preflight cannot pass a value the build rejects.
        public static CheckResult Evaluate(bool buildAddressablesRequested, AddressablesProfile profile,
            string raw, string loadPath)
        {
            if (!buildAddressablesRequested)
                return CheckResult.Pass("Addressables stage not requested.");

            var binding = ProfileBindingRule.Inspect(raw, loadPath);
            if (binding.Problem == ProfileBindingProblem.UnresolvedToken)
                return CheckResult.Block(
                    $"Addressables profile '{profile}': Remote.LoadPath binding '{binding.Token}' does " +
                    "not resolve, so the build would bake the token's own text into the catalog. Check " +
                    "the AddressablesVersionStore asset and BuildSystemSettings.RemoteLoadRoot.");

            if (binding.Problem == ProfileBindingProblem.MalformedDelimiters)
                return CheckResult.Block(
                    $"Addressables profile '{profile}': Remote.LoadPath has malformed binding " +
                    $"delimiters — raw '{raw}' evaluated to '{loadPath}', which still contains '[' or " +
                    "']'. Every binding must be a balanced '[Type.Member]' token; fix the brackets in " +
                    "the Addressables profile value.");

            // A None verdict is not proof of success — ProfileBindingRule cannot see a token in an
            // empty value — so emptiness is tested here, as its contract requires of every caller.
            if (string.IsNullOrEmpty(loadPath) || !loadPath.StartsWith("http"))
                return CheckResult.Block(
                    $"Addressables profile '{profile}': Remote.LoadPath evaluated to '{loadPath}', " +
                    "which is not a URL. Shipped players would have nowhere to fetch content from.");

            return CheckResult.Pass($"Remote paths resolve for profile '{profile}'.");
        }

        public CheckResult Run(BuildContext ctx)
        {
#if UNICO_HAS_ADDRESSABLES
            if (!ctx.Request.BuildAddressables)
                return CheckResult.Pass("Addressables stage not requested.");

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (!settings)
                return CheckResult.Block("Build Addressables was requested but AddressableAssetSettings was not found.");

            // The build's profile, not the active one: preflight must not switch anything.
            var profileId = settings.profileSettings.GetProfileId(ctx.Profile.ToString());
            if (string.IsNullOrEmpty(profileId))
                return CheckResult.Block($"Addressables profile '{ctx.Profile}' does not exist.");

            var raw = settings.profileSettings.GetValueByName(profileId, "Remote.LoadPath");
            if (string.IsNullOrEmpty(raw))
                return CheckResult.Block("Profile variable 'Remote.LoadPath' is not defined.");

            return Evaluate(true, ctx.Profile, raw, settings.profileSettings.EvaluateString(profileId, raw));
#else
            return CheckResult.Pass("Addressables package not installed; remote paths not applicable.");
#endif
        }
    }
}
