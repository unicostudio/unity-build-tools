using UnityEditor;

namespace UnicoStudio.BuildSystem.Editor
{
    public sealed class SplashLogoCheck : IPreflightCheck
    {
        public CheckResult Run(BuildContext ctx)
        {
            if (ctx.Request.Kind == BuildKind.Release
                && PlayerSettings.SplashScreen.show
                && PlayerSettings.SplashScreen.showUnityLogo)
                return CheckResult.Warn("Unity splash logo is ON for a Release build.");
            return CheckResult.Pass("Splash logo state OK.");
        }
    }
}
