using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class PreflightGateEditModeTests
    {
        private static (string block, List<string> warnings) Gate(
            WarnPolicy policy, params (CheckResult result, bool exempt)[] entries)
        {
            var results = new List<CheckResult>();
            var exempts = new List<bool>();
            foreach (var (result, exempt) in entries)
            {
                results.Add(result);
                exempts.Add(exempt);
            }
            return PreflightRunner.Gate(results, exempts, policy);
        }

        [Test]
        public void Interactive_WarnsAreRecordedButNeverBlock()
        {
            var (block, warnings) = Gate(WarnPolicy.Interactive,
                (CheckResult.Pass("ok"), false),
                (CheckResult.Warn("advisory"), false));
            Assert.IsNull(block);
            CollectionAssert.AreEqual(new[] { "advisory" }, warnings);
        }

        [Test]
        public void Strict_NonExemptWarn_Blocks()
        {
            var (block, warnings) = Gate(WarnPolicy.Strict, (CheckResult.Warn("advisory"), false));
            Assert.AreEqual("[strict] advisory", block);
            CollectionAssert.AreEqual(new[] { "advisory" }, warnings);
        }

        [Test]
        public void Strict_ExemptWarn_DoesNotBlock_ButIsRecorded()
        {
            var (block, warnings) = Gate(WarnPolicy.Strict, (CheckResult.Warn("cdn reminder"), true));
            Assert.IsNull(block);
            CollectionAssert.AreEqual(new[] { "cdn reminder" }, warnings);
        }

        [Test]
        public void Strict_ExemptFlag_IsMatchedByIndex_NotOnlyAtSlotZero()
        {
            // Production hands Gate a 19-entry list where the exempt checks sit at indexes 6, 13
            // and 18 — never 0. Every other case in this fixture puts the exempt entry at index 0,
            // where a strictExempt[0] typo behaves identically to strictExempt[i] (measured: that
            // mutation left the whole suite green). These two shapes pin the per-index pairing.
            var (block, _) = Gate(WarnPolicy.Strict,
                (CheckResult.Pass("ok"), false),
                (CheckResult.Warn("cdn reminder"), true));
            Assert.IsNull(block);

            var (mirror, _) = Gate(WarnPolicy.Strict,
                (CheckResult.Warn("cdn reminder"), true),
                (CheckResult.Warn("advisory"), false));
            Assert.AreEqual("[strict] advisory", mirror);
        }

        [Test]
        public void Block_WinsOverStrictWarn_WhenEarlier()
        {
            var (block, _) = Gate(WarnPolicy.Strict,
                (CheckResult.Block("hard stop"), false),
                (CheckResult.Warn("advisory"), false));
            Assert.AreEqual("hard stop", block);
        }

        [Test]
        public void FirstBlockingMessage_Wins_InRegistrationOrder()
        {
            var (block, _) = Gate(WarnPolicy.Strict,
                (CheckResult.Warn("first"), false),
                (CheckResult.Block("second"), false));
            Assert.AreEqual("[strict] first", block);
        }

        [Test]
        public void StrictExemptChecks_AreExactlyTheThreeAdvisoryOnes()
        {
            // Enumerate the real registration: a new check defaults to non-exempt (it blocks under
            // Strict), so this must fail loudly the day someone adds one that opts out.
            // Current set: CdnReminder (CI uploads itself), TestModeConsistency (self-healing),
            // ConfigPresence (config absence is a legitimate adoption stage, never a gate).
            var exempt = PreflightRunner.Default().Where(c => c.IsStrictExempt)
                .Select(c => c.GetType().Name).OrderBy(n => n, System.StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(
                new[] { nameof(CdnReminderCheck), nameof(ConfigPresenceCheck),
                        nameof(TestModeConsistencyCheck) }, exempt);
        }
    }
}
