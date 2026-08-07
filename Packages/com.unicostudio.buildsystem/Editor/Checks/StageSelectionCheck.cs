namespace UnicoStudio.BuildSystem.Editor
{
    // A request with both stages off would still bump versions and mutate defines, then finish
    // "OK" having produced nothing. Block it in core so any caller (panel or a future CLI) is
    // covered; the panel additionally disables the Build button for this case.
    public sealed class StageSelectionCheck : IPreflightCheck
    {
        public CheckResult Run(BuildContext ctx)
        {
            return !ctx.Request.BuildAddressables && !ctx.Request.BuildPlayer
                ? CheckResult.Block("Nothing to build: enable Build Addressables and/or Build Player.")
                : CheckResult.Pass("Stage selection is valid.");
        }
    }
}
