using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class BundleIdCheckEditModeTests
    {
        [Test]
        public void NoExpectedId_Passes()
        {
            // Empty ExpectedBundleId (or missing config) disables the check — safe migration.
            Assert.AreEqual(CheckSeverity.Pass,
                BundleIdCheck.Evaluate("", "com.unicostudio.anygame").Severity);
            Assert.AreEqual(CheckSeverity.Pass,
                BundleIdCheck.Evaluate(null, "com.unicostudio.anygame").Severity);
        }

        [Test]
        public void MatchingId_Passes()
        {
            Assert.AreEqual(CheckSeverity.Pass,
                BundleIdCheck.Evaluate("com.unicostudio.anygame", "com.unicostudio.anygame").Severity);
        }

        [Test]
        public void MismatchedId_Blocks()
        {
            Assert.AreEqual(CheckSeverity.Block,
                BundleIdCheck.Evaluate("com.unicostudio.anygame", "com.unicostudio.experiment").Severity);
        }
    }
}
