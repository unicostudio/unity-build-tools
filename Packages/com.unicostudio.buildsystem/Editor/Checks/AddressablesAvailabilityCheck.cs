namespace UnicoStudio.BuildSystem.Editor
{
    // Blocks a request that asks for the Addressables stage in a host without the
    // com.unity.addressables package. The panel already hides the addressables UI when
    // unavailable; this protects direct UnicoBuildService.Start callers, and the throwing
    // stub AddressablesStage remains as belt-and-braces behind it.
    public sealed class AddressablesAvailabilityCheck : IPreflightCheck
    {
        public static CheckResult Evaluate(bool buildAddressablesRequested, bool available)
        {
            if (!buildAddressablesRequested)
                return CheckResult.Pass("Addressables stage not requested.");
            return available
                ? CheckResult.Pass("Addressables package present.")
                : CheckResult.Block(
                    "Build Addressables was requested but com.unity.addressables is not installed in this project.");
        }

        public CheckResult Run(BuildContext ctx) =>
            Evaluate(ctx.Request.BuildAddressables, UnicoBuildService.AddressablesAvailable);
    }
}
