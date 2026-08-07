namespace UnicoStudio.BuildSystem.Editor
{
    // A player build whose outputs match no artifact branch in PlayerBuildStage would still finish
    // "OK" — after burning the version bumps. Block it before the job starts. The panel cannot
    // produce the invalid combinations, but the API surface can.
    public sealed class OutputSelectionCheck : IPreflightCheck
    {
        public CheckResult Run(BuildContext ctx)
        {
            if (!ctx.Request.BuildPlayer)
                return CheckResult.Pass("Output selection is valid.");

            if (ctx.Request.Outputs == OutputKind.None)
                return CheckResult.Block("Player build requested but no output is selected.");

            // Android consumes only the Apk/Aab flags; XcodeProject alone would build nothing.
            if (ctx.Request.Platform == BuildPlatform.Android &&
                (ctx.Request.Outputs & (OutputKind.Apk | OutputKind.Aab)) == 0)
                return CheckResult.Block("Android build requires APK and/or AAB output.");

            return CheckResult.Pass("Output selection is valid.");
        }
    }
}
