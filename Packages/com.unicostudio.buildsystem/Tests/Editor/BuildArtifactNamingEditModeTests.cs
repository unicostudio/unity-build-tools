using System;
using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class BuildArtifactNamingEditModeTests
    {
        [Test]
        public void FileName_NoLabel_ReleaseAab()
        {
            Assert.AreEqual(
                "AnyGame_v1.2.7(18)_02.07.2026_RELEASE.aab",
                BuildArtifactNaming.FileName("AnyGame", "", "1.2.7", 18, new DateTime(2026, 7, 2), BuildKind.Release, "aab"));
        }

        [Test]
        public void FileName_WithLabel_TestApk()
        {
            Assert.AreEqual(
                "AnyGame_SocialLogin_v1.2.7(18)_02.07.2026_TEST.apk",
                BuildArtifactNaming.FileName("AnyGame", "SocialLogin", "1.2.7", 18, new DateTime(2026, 7, 2), BuildKind.Test, "apk"));
        }

        [Test]
        public void SanitizeLabel_StripsNonAlphanumeric()
        {
            Assert.AreEqual("SocialLogin", BuildArtifactNaming.SanitizeLabel("Social Login!"));
            Assert.AreEqual("v2Beta", BuildArtifactNaming.SanitizeLabel("v2-Beta"));
        }

        [Test]
        public void FileName_LabelIsSanitizedAndPrefixAlwaysPresent()
        {
            var name = BuildArtifactNaming.FileName("AnyGame", "a b/c", "1.0.0", 1, new DateTime(2026, 7, 2), BuildKind.Test, "apk");
            Assert.AreEqual("AnyGame_abc_v1.0.0(1)_02.07.2026_TEST.apk", name);
        }

        [Test]
        public void FileName_EmptyExtension_OmitsTrailingDot()
        {
            // iOS Xcode-project folder: no extension, no trailing dot.
            Assert.AreEqual(
                "AnyGame_v1.2.7(18)_02.07.2026_RELEASE",
                BuildArtifactNaming.FileName("AnyGame", "", "1.2.7", 18, new DateTime(2026, 7, 2), BuildKind.Release, ""));
        }
    }
}
