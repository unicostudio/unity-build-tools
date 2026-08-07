using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class BuildPlatformExtensionsEditModeTests
    {
        [Test]
        public void Mappings_AreCorrect()
        {
            Assert.AreEqual(BuildTarget.Android, BuildPlatform.Android.ToBuildTarget());
            Assert.AreEqual(BuildTarget.iOS, BuildPlatform.iOS.ToBuildTarget());

            Assert.AreEqual(BuildTargetGroup.Android, BuildPlatform.Android.ToBuildTargetGroup());
            Assert.AreEqual(BuildTargetGroup.iOS, BuildPlatform.iOS.ToBuildTargetGroup());

            Assert.AreEqual(NamedBuildTarget.Android, BuildPlatform.Android.ToNamedBuildTarget());
            Assert.AreEqual(NamedBuildTarget.iOS, BuildPlatform.iOS.ToNamedBuildTarget());
        }

        [Test]
        public void EveryPlatform_MapsToADistinctTarget()
        {
            // The mappings are ternaries: a third BuildPlatform member would silently map to iOS,
            // and TestModeDefineRenameCheck enumerates the enum, so catch that here.
            var platforms = (BuildPlatform[])System.Enum.GetValues(typeof(BuildPlatform));
            var targets = platforms.Select(p => p.ToBuildTarget()).Distinct().ToArray();
            var named = platforms.Select(p => p.ToNamedBuildTarget()).Distinct().ToArray();
            Assert.AreEqual(platforms.Length, targets.Length);
            Assert.AreEqual(platforms.Length, named.Length);
        }
    }
}
