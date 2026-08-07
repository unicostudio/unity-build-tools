namespace UnicoStudio.BuildSystem.Editor
{
    // Bumping the addressables version without building addressables burns a version number for
    // nothing: a successful run keeps its bumps, and no content ever lands in the new v{N} folder.
    public sealed class BumpConsistencyCheck : IPreflightCheck
    {
        public CheckResult Run(BuildContext ctx)
        {
            return ctx.Request.BumpAddressablesVersion && !ctx.Request.BuildAddressables
                ? CheckResult.Warn("Addressables version bump requested without an addressables build — " +
                                   "a successful run would keep the bump with no content produced.")
                : CheckResult.Pass("Version bumps match the selected stages.");
        }
    }
}
