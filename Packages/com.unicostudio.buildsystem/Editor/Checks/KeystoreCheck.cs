using System.IO;
using UnityEditor;

namespace UnicoStudio.BuildSystem.Editor
{
    // An Android Release signed with the debug key (or not signable at all) is discovered only at
    // store upload — or 30 minutes into the build when signing fails. Verify the custom keystore
    // before the job starts. Test builds pass: debug signing produces an installable APK.
    public sealed class KeystoreCheck : IPreflightCheck
    {
        private const string IN_PROJECT_PREFIX = "{inproject}: ";

        public static CheckResult Evaluate(BuildPlatform platform, BuildKind kind, bool buildPlayer,
            bool useCustomKeystore, bool keystoreFileExists, bool passwordsSet)
        {
            if (platform != BuildPlatform.Android || kind != BuildKind.Release || !buildPlayer)
                return CheckResult.Pass("Keystore not applicable.");

            if (!useCustomKeystore)
                return CheckResult.Block(
                    "Android Release requires the custom keystore (Player Settings > Publishing Settings).");

            if (!keystoreFileExists)
                return CheckResult.Block(
                    "Keystore file not found — check Player Settings > Publishing Settings.");

            if (!passwordsSet)
                return CheckResult.Block(
                    "Keystore passwords are not set for this session. Enter them in Player Settings > " +
                    "Publishing Settings, provide UNICO_KEYSTORE_PASS / UNICO_KEYALIAS_PASS in the " +
                    "environment, or have an editor-side script inject them on load.");

            return CheckResult.Pass("Custom keystore configured.");
        }

        public CheckResult Run(BuildContext ctx)
        {
            return Evaluate(ctx.Request.Platform, ctx.Request.Kind, ctx.Request.BuildPlayer,
                PlayerSettings.Android.useCustomKeystore,
                KeystoreFileExists(PlayerSettings.Android.keystoreName),
                !string.IsNullOrEmpty(PlayerSettings.Android.keystorePass) &&
                !string.IsNullOrEmpty(PlayerSettings.Android.keyaliasPass));
        }

        // "{inproject}: Foo.keystore" is Unity's notation for a keystore at the project root.
        public static bool KeystoreFileExists(string keystoreName)
        {
            if (string.IsNullOrEmpty(keystoreName)) return false;
            var path = keystoreName.StartsWith(IN_PROJECT_PREFIX)
                ? keystoreName.Substring(IN_PROJECT_PREFIX.Length)
                : keystoreName;
            return File.Exists(path);
        }
    }
}
