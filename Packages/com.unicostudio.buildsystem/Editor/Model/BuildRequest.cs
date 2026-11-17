using System;

namespace UnicoStudio.BuildSystem.Editor
{
    [Serializable]
    public sealed class BuildRequest
    {
        public BuildPlatform Platform = BuildPlatform.Android;
        public BuildKind Kind = BuildKind.Test;

        public string VersionName = "";       // e.g. "1.2.7"
        public bool BumpBuildCode = true;      // Android bundleVersionCode / iOS buildNumber +1
        public int BuildCode;                  // 0 = unset; >0 pins the exact build code (CI:
                                               // versions injected externally; wins over BumpBuildCode)
        public bool BumpAddressablesVersion;   // AddressablesVersionStore.Version +1

        public bool BuildAddressables = true;
        public AddressablesMode AddressablesMode = AddressablesMode.UpdatePrevious;

        public bool BuildPlayer = true;
        public OutputKind Outputs = OutputKind.Apk;   // Release/Android may be Apk|Aab
        public CompressionKind Compression = CompressionKind.LZ4HC;   // project standard

        // BuildOptions.Development for the player. Never allowed on Release (preflight blocks,
        // PlayerBuildStage refuses). The field defaults to false so a caller that never set it
        // cannot get a development player; the panel and the CLI seed it from
        // DefaultDevelopmentFor instead.
        public bool Development;

        public string OutputFolder = "";       // empty => default Builds/{platform}/{kind}
        public string Label = "";              // optional filename suffix (letters/digits)

        public static bool DefaultDevelopmentFor(BuildKind kind) => kind == BuildKind.Test;
    }
}
