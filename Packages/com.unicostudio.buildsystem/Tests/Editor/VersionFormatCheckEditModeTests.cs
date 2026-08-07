using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class VersionFormatCheckEditModeTests
    {
        private static CheckSeverity Severity(string version)
        {
            var req = new BuildRequest { VersionName = version };
            return new VersionFormatCheck().Run(new BuildContext(req)).Severity;
        }

        [Test]
        public void ValidSemver_Passes()
        {
            Assert.AreEqual(CheckSeverity.Pass, Severity("1.2.7"));
            Assert.AreEqual(CheckSeverity.Pass, Severity("0.0.1"));
            Assert.AreEqual(CheckSeverity.Pass, Severity("10.20.30"));
        }

        [Test]
        public void MalformedVersion_Blocks()
        {
            Assert.AreEqual(CheckSeverity.Block, Severity("1.2"));       // too few parts
            Assert.AreEqual(CheckSeverity.Block, Severity("1.2.3.4"));   // too many parts
            Assert.AreEqual(CheckSeverity.Block, Severity("1.2.x"));     // non-numeric
            Assert.AreEqual(CheckSeverity.Block, Severity("v1.2.3"));    // prefix
            Assert.AreEqual(CheckSeverity.Block, Severity("1.2.03"));    // leading zero
            // An EMPTY Version Name is deliberately not in this list: it is not a malformed
            // version but an absent override — ApplyVersionStage leaves bundleVersion untouched
            // for it, so the check validates the PROJECT's version instead (see EffectiveVersion).
        }

        // --- EffectiveVersion: which string the check actually validates ---

        [Test]
        public void EffectiveVersion_EmptyOrWhitespaceRequest_FallsBackToTheProjectVersion()
        {
            // The CLI contract: -versionName is optional, and README's CI recipes omit it.
            // An absent override means "ship the project's version" (ApplyVersionStage writes
            // bundleVersion only for a non-blank request) — so that is what must be validated,
            // not the empty string.
            Assert.AreEqual("1.6.6", VersionFormatCheck.EffectiveVersion("", "1.6.6"));
            Assert.AreEqual("1.6.6", VersionFormatCheck.EffectiveVersion("   ", "1.6.6"));
            Assert.AreEqual("1.6.6", VersionFormatCheck.EffectiveVersion(null, "1.6.6"));
        }

        [Test]
        public void EffectiveVersion_ExplicitRequest_WinsOverTheProjectVersion()
        {
            Assert.AreEqual("1.2.7", VersionFormatCheck.EffectiveVersion("1.2.7", "9.9.9"));
            Assert.AreEqual("1.2.7", VersionFormatCheck.EffectiveVersion(" 1.2.7 ", "9.9.9"));
        }

        [Test]
        public void BothAbsent_Blocks()
        {
            // The old name claimed the Block while asserting only the helper's "" — the Block on
            // an empty effective version was never executed by any test, and letting empty
            // versions Pass left the whole suite green (measured). Verdict is the actual gate.
            Assert.AreEqual("", VersionFormatCheck.EffectiveVersion("", null));
            Assert.AreEqual(CheckSeverity.Block,
                VersionFormatCheck.Verdict(VersionFormatCheck.EffectiveVersion("", null)).Severity);
        }
    }
}
