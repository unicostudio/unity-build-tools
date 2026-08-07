namespace UnicoStudio.BuildSystem.Editor
{
    // The panel's compression choice maps to real BuildOptions flags in PlayerBuildStage, so —
    // unlike the legacy eyeball-only checklist reminder — this checks the actual value: anything
    // other than the project-standard LZ4HC is a deliberate deviation the user must acknowledge
    // through the warning dialog.
    public sealed class CompressionCheck : IPreflightCheck
    {
        public CheckResult Run(BuildContext ctx)
        {
            if (!ctx.Request.BuildPlayer)
                return CheckResult.Pass("No player build; compression not applicable.");
            return ctx.Request.Compression == CompressionKind.LZ4HC
                ? CheckResult.Pass("Compression: LZ4HC.")
                : CheckResult.Warn($"Compression is {ctx.Request.Compression}; project standard is LZ4HC.");
        }
    }
}
