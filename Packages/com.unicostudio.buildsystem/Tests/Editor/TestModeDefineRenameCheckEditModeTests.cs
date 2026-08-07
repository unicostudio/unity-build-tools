using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class TestModeDefineRenameCheckEditModeTests
    {
        private static readonly string[] None = { };
        private static readonly string[] Android = { "Android" };
        private static readonly string[] Both = { "Android", "iOS" };

        [Test]
        public void DefaultName_IsPass_EvenWhenDefaultIsEverywhere()
            => Assert.AreEqual(CheckSeverity.Pass,
                TestModeDefineRenameCheck.Evaluate("TEST_MODE", Both).Severity);

        [Test]
        public void RenamedAndDefaultGone_IsPass()
            => Assert.AreEqual(CheckSeverity.Pass,
                TestModeDefineRenameCheck.Evaluate("DEV_MODE", None).Severity);

        [Test]
        public void RenamedButDefaultStillCommitted_IsWarn()
        {
            var r = TestModeDefineRenameCheck.Evaluate("DEV_MODE", Android);
            Assert.AreEqual(CheckSeverity.Warn, r.Severity);
            StringAssert.Contains("DEV_MODE", r.Message);
            StringAssert.Contains("TEST_MODE", r.Message);
            StringAssert.Contains("Android", r.Message);
        }

        [Test]
        public void MultiplePlatformsCarryingDefault_AreAllNamed()
        {
            // The check cannot tell a typo apart from a deliberate rename that hasn't reached
            // Player Settings yet — both look identical (custom name, default still committed),
            // and both warn. This just verifies every carrying platform is named, not just one.
            var r = TestModeDefineRenameCheck.Evaluate("TESTMODE", Both);
            Assert.AreEqual(CheckSeverity.Warn, r.Severity);
            StringAssert.Contains("Android", r.Message);
            StringAssert.Contains("iOS", r.Message);
        }

        [Test]
        public void DefaultName_WithNoPlatformCarryingIt_IsPass()
            => Assert.AreEqual(CheckSeverity.Pass,
                TestModeDefineRenameCheck.Evaluate("TEST_MODE", None).Severity);
    }
}
