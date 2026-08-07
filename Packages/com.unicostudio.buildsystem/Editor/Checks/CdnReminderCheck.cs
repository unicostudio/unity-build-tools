namespace UnicoStudio.BuildSystem.Editor
{
    // The job builds content into a gitignored staging folder — named from the shared
    // UnicoBuildPaths build-path constant, not repeated here — and nothing uploads it. A
    // Release that ships before the upload points shipped players at content that is not there.
    // The build cannot verify the upload — but it can refuse to let the step go unacknowledged.
    public sealed class CdnReminderCheck : IPreflightCheck
    {
        // CI performs the CDN upload as its own pipeline step; this reminder must not make
        // strict Release builds impossible.
        public bool IsStrictExempt => true;

        public static bool ShouldRemind(BuildKind kind, bool buildAddressables) =>
            kind == BuildKind.Release && buildAddressables;

        // The staging folder's prefix belongs to UnicoBuildPaths; the reminder must not carry a
        // second copy of it. "{N}" stands in when the version store is missing.
        public static string FolderFor(int version) =>
            version >= 0
                ? $"{UnicoBuildPaths.BUILD_PATH_BASE}{version}"
                : $"{UnicoBuildPaths.BUILD_PATH_BASE}{{N}}";

        public CheckResult Run(BuildContext ctx)
        {
            if (!ShouldRemind(ctx.Request.Kind, ctx.Request.BuildAddressables))
                return CheckResult.Pass("CDN upload reminder not applicable.");

            var store = AddressablesVersionStore.LoadOrNull();
            var version = store ? store.Version + (ctx.Request.BumpAddressablesVersion ? 1 : 0) : -1;
            return CheckResult.Warn(
                $"Release content must be uploaded to the CDN before shipping ({FolderFor(version)} after the build).");
        }
    }
}
