#if UNITY_EDITOR
namespace UnicoStudio.BuildSystem
{
    // The six properties the Addressables profile variables bind to by name. Addressables resolves
    // "[A.B.C.Member]" by splitting on the LAST dot and calling Assembly.GetType over every loaded
    // assembly, so a namespaced package type resolves exactly like a global-namespace host class —
    // which is why this lives here instead of in a shim each game has to copy. Adopting the package
    // is therefore a settings asset plus six profile strings, with no host code at all.
    //
    // UNITY_EDITOR-only for the same reason the shim was: these are '[...]' variables, evaluated at
    // BUILD time in the editor and baked into the catalog, never resolved in a player.
    public static class AddressablesProfilePaths
    {
        public static string RemoteBuildDevelopmentPath => Build(AddressablesProfile.Development);
        public static string RemoteLoadDevelopmentPath => Load(AddressablesProfile.Development);

        public static string RemoteBuildTestPath => Build(AddressablesProfile.Test);
        public static string RemoteLoadTestPath => Load(AddressablesProfile.Test);

        public static string RemoteBuildProductionPath => Build(AddressablesProfile.Production);
        public static string RemoteLoadProductionPath => Load(AddressablesProfile.Production);

        // Local staging is the package's own convention; the remote root is the host's data.
        private static string Build(AddressablesProfile profile) => UnicoBuildPaths.ComposeForActiveTarget(profile, UnicoBuildPaths.BUILD_PATH_BASE);

        private static string Load(AddressablesProfile profile) => UnicoBuildPaths.ComposeForActiveTarget(profile, BuildSystemSettings.RequireRemoteLoadRoot());
    }
}
#endif
