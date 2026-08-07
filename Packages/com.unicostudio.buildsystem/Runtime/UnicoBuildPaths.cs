using System;

namespace UnicoStudio.BuildSystem
{
    // Composes the versioned remote-content paths: {pathBase}{version}/{profile}/{buildTarget}.
    // The game-specific CDN root is not code any more, it is data: BuildSystemSettings.RemoteLoadRoot,
    // read through RequireRemoteLoadRoot. The surface the Addressables profile variables actually
    // bind to is AddressablesProfilePaths, in this same assembly, and it delegates here. This class
    // stays game-free.
    public static class UnicoBuildPaths
    {
        // Local staging folder convention, shared by every Unico game.
        public const string BUILD_PATH_BASE = "ServerData/v";

        public static string Compose(int version, string profile, string buildTarget, string pathBase) =>
            $"{pathBase}{version}/{profile}/{buildTarget}";

#if UNITY_EDITOR
        // Reads the writable store. Throws (fail loud) if the asset is missing — a silent fallback
        // would publish addressables content to the wrong remote version path.
        public static int RequireVersion()
        {
            var store = AddressablesVersionStore.LoadOrNull();
            if (!store)
                throw new InvalidOperationException(
                    "AddressablesVersionStore asset not found under Assets/. " +
                    "Create it via Assets > Create > Unico > Build > Addressables Version Store.");

            return store.Version;
        }

        // What AddressablesProfilePaths calls: {pathBase}{storeVersion}/{profile}/{activeBuildTarget}.
        public static string ComposeForActiveTarget(AddressablesProfile profile, string pathBase) =>
            Compose(RequireVersion(), profile.ToString(),
                UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString(), pathBase);
#endif
    }
}
