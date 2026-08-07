using System;
using UnityEditor;
#if UNICO_HAS_ADDRESSABLES
using UnityEditor.AddressableAssets;
#endif
using UnityEditor.Android;
using Unity.Android.Types;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    [Serializable]
    public sealed class DevStateSnapshot
    {
        public BuildPlatform Platform;
        public string ScriptingDefines;  // ';'-joined for the target
        public bool BuildAppBundle;
        public DebugSymbolLevel AndroidSymbolMode;
        public string AddressablesProfileId;
        // Pre-build version values — rolled back ONLY on failure (RestoreVersions); success keeps
        // the bumps, since they then describe a real, produced build.
        public string BundleVersion;
        public int AndroidVersionCode;
        public string IosBuildNumber;
        public int AddressablesVersion = -1;   // -1 = version store missing at capture

        public static DevStateSnapshot Capture(BuildPlatform platform)
        {
            var versionStore = AddressablesVersionStore.LoadOrNull();
            var snap = new DevStateSnapshot
            {
                Platform = platform,
                ScriptingDefines = PlayerSettings.GetScriptingDefineSymbols(platform.ToNamedBuildTarget()),
                BuildAppBundle = EditorUserBuildSettings.buildAppBundle,
                AndroidSymbolMode = UserBuildSettings.DebugSymbols.level,
                BundleVersion = PlayerSettings.bundleVersion,
                AndroidVersionCode = PlayerSettings.Android.bundleVersionCode,
                IosBuildNumber = PlayerSettings.iOS.buildNumber,
                AddressablesVersion = versionStore ? versionStore.Version : -1,
            };
#if UNICO_HAS_ADDRESSABLES
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            snap.AddressablesProfileId = settings ? settings.activeProfileId : null;
#endif
            return snap;
        }

        public void Restore()
        {
            PlayerSettings.SetScriptingDefineSymbols(Platform.ToNamedBuildTarget(), ScriptingDefines);
            EditorUserBuildSettings.buildAppBundle = BuildAppBundle;
            UserBuildSettings.DebugSymbols.level = AndroidSymbolMode;
#if UNICO_HAS_ADDRESSABLES
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings && !string.IsNullOrEmpty(AddressablesProfileId))
                settings.activeProfileId = AddressablesProfileId;
#endif
        }

        // A failed run produced nothing — its version writes (bundleVersion, the platform build
        // code, the addressables VERSION_NO store) roll back so a retry starts from clean values
        // instead of double-bumping. Success keeps them. The addressables CONTENT STATE is NOT part
        // of this: its path is owned by Addressables and only resolvable inside AddressablesStage,
        // so it rolls back through ContentStateGuard (Editor/Core), which Finish calls on the same
        // failure path immediately after this — and which crash recovery calls on its own.
        // Returns true only when something actually changed, so callers log honestly.
        public bool RestoreVersions()
        {
            // Legacy/partial snapshot JSON (pre version-rollback fields) carries no version data;
            // skip rather than write empty values into PlayerSettings.
            if (string.IsNullOrEmpty(BundleVersion)) return false;

            var changed = false;
            if (PlayerSettings.bundleVersion != BundleVersion)
            {
                PlayerSettings.bundleVersion = BundleVersion;
                changed = true;
            }

            if (PlayerSettings.Android.bundleVersionCode != AndroidVersionCode)
            {
                PlayerSettings.Android.bundleVersionCode = AndroidVersionCode;
                changed = true;
            }

            if (PlayerSettings.iOS.buildNumber != IosBuildNumber)
            {
                PlayerSettings.iOS.buildNumber = IosBuildNumber;
                changed = true;
            }

            var versionStore = AddressablesVersionStore.LoadOrNull();
            if (versionStore && AddressablesVersion >= 0 && versionStore.Version != AddressablesVersion)
            {
                versionStore.Version = AddressablesVersion;
                EditorUtility.SetDirty(versionStore);
                AssetDatabase.SaveAssets();
                changed = true;
            }

            return changed;
        }
    }
}
