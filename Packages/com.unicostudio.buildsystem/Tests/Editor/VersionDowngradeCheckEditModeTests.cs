using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class VersionDowngradeCheckEditModeTests
    {
        [Test]
        public void LowerVersion_IsDowngrade()
        {
            Assert.IsTrue(VersionDowngradeCheck.IsDowngrade("1.0.0", "1.2.7"));
            Assert.IsTrue(VersionDowngradeCheck.IsDowngrade("1.2.6", "1.2.7"));
        }

        [Test]
        public void SameOrHigherVersion_IsNotDowngrade()
        {
            Assert.IsFalse(VersionDowngradeCheck.IsDowngrade("1.2.7", "1.2.7"));
            Assert.IsFalse(VersionDowngradeCheck.IsDowngrade("1.2.8", "1.2.7"));
            Assert.IsFalse(VersionDowngradeCheck.IsDowngrade("2.0.0", "1.9.9"));
        }

        [Test]
        public void UnparseableVersions_AreNotFlagged()
        {
            // Shape validity is VersionFormatCheck's job; this check never blocks on garbage.
            Assert.IsFalse(VersionDowngradeCheck.IsDowngrade("", "1.2.7"));
            Assert.IsFalse(VersionDowngradeCheck.IsDowngrade("abc", "1.2.7"));
            Assert.IsFalse(VersionDowngradeCheck.IsDowngrade("1.0.0", "not-a-version"));
            Assert.IsFalse(VersionDowngradeCheck.IsDowngrade(null, "1.2.7"));
        }
    }
}
