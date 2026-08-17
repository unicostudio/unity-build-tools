using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    [CreateAssetMenu(fileName = "BuildTargetConfig", menuName = "Unico/Build/Build Target Config")]
    public sealed class BuildTargetConfig : ScriptableObject
    {
        public BuildPlatform Platform;
        public BuildKind Kind;
        public AddressablesProfile Profile = AddressablesProfile.Test;  // Addressables profile to activate

        [Tooltip("Optional. When set, pre-flight blocks the build if PlayerSettings' bundle id differs.")]
        public string ExpectedBundleId = "";

        [Tooltip("Optional. Extra scripting defines compiled into the PLAYER build only " +
                 "(extraScriptingDefines — the editor and the Addressables build do not see them). " +
                 "The test-mode define is managed by build kind; do not list it here.")]
        public string[] ExtraDefines = System.Array.Empty<string>();

        [Tooltip("Optional. Defines removed from this platform's global Player Settings for the " +
                 "duration of the build (triggers a mid-build recompile) and restored automatically " +
                 "afterwards — e.g. a third-party integration's readiness define, which would otherwise compile its bridge into the game.")]
        public string[] StripDefines = System.Array.Empty<string>();

        [Tooltip("Optional. UPM package ids removed from Packages/manifest.json for the duration of " +
                 "the build — atomically with the define write, one combined reload — and restored " +
                 "byte-exact afterwards. For packages whose [InitializeOnLoad] code fights StripDefines " +
                 "by re-adding its readiness define on every reload while the package is present. Listed " +
                 "packages must be exact-pinned and dependency-free; pre-flight enforces both.")]
        public string[] StripPackages = System.Array.Empty<string>();
    }
}
