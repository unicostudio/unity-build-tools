using UnityEditor;
using UnityEditor.Build;

namespace UnicoStudio.BuildSystem.Editor
{
    // Single source for the BuildPlatform → Unity build-target mappings. Every stage, check and
    // UI that needs a BuildTarget / NamedBuildTarget / BuildTargetGroup goes through here, so a
    // new platform (or a mapping fix) is one edit, not a hunt-and-miss across the module.
    public static class BuildPlatformExtensions
    {
        public static BuildTarget ToBuildTarget(this BuildPlatform p) =>
            p == BuildPlatform.Android ? BuildTarget.Android : BuildTarget.iOS;

        public static BuildTargetGroup ToBuildTargetGroup(this BuildPlatform p) =>
            p == BuildPlatform.Android ? BuildTargetGroup.Android : BuildTargetGroup.iOS;

        public static NamedBuildTarget ToNamedBuildTarget(this BuildPlatform p) =>
            p == BuildPlatform.Android ? NamedBuildTarget.Android : NamedBuildTarget.iOS;
    }
}
