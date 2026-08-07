using UnityEditor;

namespace UnicoStudio.BuildSystem.Editor
{
    public sealed class ApplyVersionStage : IBuildStage
    {
        public string Name => "Apply version";

        public void Execute(BuildContext ctx)
        {
            var req = ctx.Request;

            if (!string.IsNullOrWhiteSpace(req.VersionName))
                PlayerSettings.bundleVersion = req.VersionName.Trim();

            if (req.Platform == BuildPlatform.Android)
            {
                var next = req.BuildCode > 0 ? req.BuildCode
                    : req.BumpBuildCode
                        ? BuildVersioning.NextAndroidCode(PlayerSettings.Android.bundleVersionCode)
                        : PlayerSettings.Android.bundleVersionCode;
                PlayerSettings.Android.bundleVersionCode = next;
                ctx.AddStep($"Version {PlayerSettings.bundleVersion} (code {next})");
            }
            else
            {
                var next = req.BuildCode > 0 ? req.BuildCode.ToString()
                    : req.BumpBuildCode
                        ? BuildVersioning.NextIosBuildNumber(PlayerSettings.iOS.buildNumber)
                        : PlayerSettings.iOS.buildNumber;
                PlayerSettings.iOS.buildNumber = next;
                ctx.AddStep($"Version {PlayerSettings.bundleVersion} (build {next})");
            }

            if (req.BumpAddressablesVersion)
            {
                var store = AddressablesVersionStore.LoadOrNull();
                if (store)
                {
                    store.Version += 1;
                    EditorUtility.SetDirty(store);
                    AssetDatabase.SaveAssets();
                    ctx.AddStep($"Addressables Version -> {store.Version}");
                }
            }
        }
    }
}
