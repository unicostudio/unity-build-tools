namespace UnicoStudio.BuildSystem
{
    // Addressables profile / environment. Each value's name MUST match an Addressables profile name
    // (see the game's Assets/AddressableAssetsData profiles: Development, Test, Production). Shared
    // by AddressablesProfilePaths (path building, one property per profile) and the build system
    // (BuildTargetConfig / BuildContext), replacing fragile "Test"/"Production" magic strings with
    // a compile-time-checked enum.
    public enum AddressablesProfile
    {
        Development,
        Test,
        Production
    }
}
