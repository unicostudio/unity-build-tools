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
                 "afterwards — e.g. UNITY_MCP_READY, which otherwise compiles the MCP bridge into the game.")]
        public string[] StripDefines = System.Array.Empty<string>();
    }
}
