namespace UnicoStudio.BuildSystem.Editor
{
    // Release builds are never development builds. The Block covers requests the panel cannot
    // produce (a programmatic BuildRequest, a CLI caller bypassing Parse); PlayerBuildStage
    // refuses the same combination again right before BuildPlayer. The Pass line states the
    // outcome either way, so the preflight list always shows whether the player is development.
    public sealed class DevelopmentBuildCheck : IPreflightCheck
    {
        public static CheckResult Evaluate(BuildKind kind, bool development, bool buildPlayer, BuildPlatform platform)
        {
            if (kind == BuildKind.Release && development)
                return CheckResult.Block(
                    "Release builds cannot be development builds — turn Development Build off " +
                    "(CLI: omit -development or pass -development false).");

            if (!buildPlayer)
                return CheckResult.Pass("Development Build: not applicable (no player build).");

            if (!development)
                return CheckResult.Pass("Development Build: OFF.");

            var message = "Development Build: ON (BuildOptions.Development only — no profiler autoconnect, no script debugging).";
            if (platform == BuildPlatform.iOS)
                message += " Archive in Xcode and upload with Distribute App > TestFlight Internal Only " +
                           "(scripted uploads: testFlightInternalTestingOnly = true in ExportOptions.plist) — such a " +
                           "build can never be submitted to the App Store. At launch iOS asks to find devices on the " +
                           "local network: that is the development player connection, expected, and \"Don't Allow\" is fine.";
            return CheckResult.Pass(message);
        }

        public CheckResult Run(BuildContext ctx) =>
            Evaluate(ctx.Request.Kind, ctx.Request.Development, ctx.Request.BuildPlayer, ctx.Request.Platform);
    }
}
