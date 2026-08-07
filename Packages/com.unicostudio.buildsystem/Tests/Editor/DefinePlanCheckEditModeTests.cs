using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class DefinePlanCheckEditModeTests
    {
        private static readonly DefineConfigIssue[] None = { };
        private static readonly string[] Nothing = new string[0];

        private static DefineDelta Delta(string[] add, string[] remove, string[] addGlobal = null) =>
            new(add, addGlobal ?? Nothing, remove, forbidInPlayer: Nothing);

        [Test]
        public void BlockingIssue_Blocks()
        {
            var issues = new[] { new DefineConfigIssue(true, "bad token") };
            var r = DefinePlanCheck.Evaluate(issues, Delta(new string[0], new string[0]));
            Assert.AreEqual(CheckSeverity.Block, r.Severity);
            StringAssert.Contains("bad token", r.Message);
        }

        [Test]
        public void AdvisoryIssue_Warns()
        {
            var issues = new[] { new DefineConfigIssue(false, "ignored entry") };
            var r = DefinePlanCheck.Evaluate(issues, Delta(new string[0], new string[0]));
            Assert.AreEqual(CheckSeverity.Warn, r.Severity);
            StringAssert.Contains("ignored entry", r.Message);
        }

        [Test]
        public void BlockingWins_OverAdvisory()
        {
            var issues = new[]
            {
                new DefineConfigIssue(false, "ignored entry"),
                new DefineConfigIssue(true, "bad token"),
            };
            Assert.AreEqual(CheckSeverity.Block,
                DefinePlanCheck.Evaluate(issues, Delta(new string[0], new string[0])).Severity);
        }

        [Test]
        public void CleanPlan_PassListsChanges()
        {
            var r = DefinePlanCheck.Evaluate(None,
                Delta(new[] { "FOO" }, new[] { "UNITY_MCP_READY", "TEST_MODE" }));
            Assert.AreEqual(CheckSeverity.Pass, r.Severity);
            StringAssert.Contains("+FOO", r.Message);
            StringAssert.Contains("-UNITY_MCP_READY,TEST_MODE", r.Message);
            StringAssert.Contains("restored", r.Message);
        }

        [Test]
        public void GlobalAdd_IsShownAsGlobalAndRestored()
        {
            // A Test build for a target whose committed globals lack the define: the panel must say
            // the build changes GLOBAL defines (a domain reload, and the developer's state comes
            // back afterwards) — not "build-only", which is what it used to be.
            var r = DefinePlanCheck.Evaluate(None,
                Delta(new[] { "FOO" }, new string[0], addGlobal: new[] { "TEST_MODE" }));
            Assert.AreEqual(CheckSeverity.Pass, r.Severity);
            StringAssert.Contains("+FOO (build-only)", r.Message);
            StringAssert.Contains("+TEST_MODE (global, restored after the build)", r.Message);
        }

        [Test]
        public void NoChanges_PassSaysSo()
        {
            var r = DefinePlanCheck.Evaluate(None, Delta(new string[0], new string[0]));
            Assert.AreEqual(CheckSeverity.Pass, r.Severity);
            StringAssert.Contains("no changes", r.Message);
        }
    }
}
