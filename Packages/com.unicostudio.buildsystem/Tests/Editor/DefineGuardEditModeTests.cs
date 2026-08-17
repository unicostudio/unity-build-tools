using System.Collections.Generic;
using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    /// <summary>
    /// The define guard exists because a domain reload queued by ConfigureDefinesStage's
    /// global write wakes third-party [InitializeOnLoad] code that can fight the plan —
    /// measured live 2026-08-13: a third-party dependency resolver re-added the stripped define
    /// on the very reload the strip triggered, and its bridge shipped into a real
    /// player's IL2CPP output. The guard re-verifies the plan at the last moment before
    /// BuildPipeline.BuildPlayer and fails LOUD instead of shipping.
    /// </summary>
    public class DefineGuardEditModeTests
    {
        private static List<string> L(params string[] items) => new(items);

        // --- FindViolation: pure decision core -----------------------------------------

        [Test]
        public void NoExpectations_NoViolation()
        {
            Assert.IsNull(DefineGuard.FindViolation(L(), L(), L("FOO", "BAR")));
        }

        [Test]
        public void StrippedDefineBack_InGlobals_IsViolation_NamingTheDefine()
        {
            var v = DefineGuard.FindViolation(
                mustBeAbsent: L("VENDOR_BRIDGE_READY"),
                mustBePresent: L(),
                currentGlobals: L("TEST_MODE", "VENDOR_BRIDGE_READY"));

            Assert.IsNotNull(v);
            StringAssert.Contains("VENDOR_BRIDGE_READY", v);
            StringAssert.Contains("re-added", v);
        }

        [Test]
        public void StrippedDefineStillAbsent_NoViolation()
        {
            Assert.IsNull(DefineGuard.FindViolation(
                L("VENDOR_BRIDGE_READY"), L(), L("TEST_MODE", "ADDRESSABLES_ENABLED")));
        }

        [Test]
        public void RequiredDefineVanished_IsViolation_NamingTheDefine()
        {
            var v = DefineGuard.FindViolation(
                mustBeAbsent: L(),
                mustBePresent: L("TEST_MODE"),
                currentGlobals: L("ADDRESSABLES_ENABLED"));

            Assert.IsNotNull(v);
            StringAssert.Contains("TEST_MODE", v);
            StringAssert.Contains("vanished", v);
        }

        [Test]
        public void RequiredDefinePresent_NoViolation()
        {
            Assert.IsNull(DefineGuard.FindViolation(
                L(), L("TEST_MODE"), L("TEST_MODE", "FOO")));
        }

        [Test]
        public void BothViolated_AbsentSideReportedFirst()
        {
            var v = DefineGuard.FindViolation(
                L("VENDOR_BRIDGE_READY"), L("TEST_MODE"), L("VENDOR_BRIDGE_READY"));

            Assert.IsNotNull(v);
            StringAssert.Contains("VENDOR_BRIDGE_READY", v);
            StringAssert.DoesNotContain("TEST_MODE", v);
        }

        [Test]
        public void MultipleOffenders_AllNamed()
        {
            var v = DefineGuard.FindViolation(
                L("VENDOR_BRIDGE_READY", "SECRET_TOOLING"), L(),
                L("VENDOR_BRIDGE_READY", "SECRET_TOOLING", "FOO"));

            StringAssert.Contains("VENDOR_BRIDGE_READY", v);
            StringAssert.Contains("SECRET_TOOLING", v);
        }

        // --- RecordPlan: what ConfigureDefinesStage persists across the reload ---------

        [Test]
        public void RecordPlan_WritesBothKeys_WhenListsNonEmpty()
        {
            var data = new Dictionary<string, string>();

            DefineGuard.RecordPlan(data,
                forbidInPlayer: new[] { "VENDOR_BRIDGE_READY", "X" },
                addToGlobal: new[] { "TEST_MODE" });

            Assert.AreEqual("VENDOR_BRIDGE_READY;X", data[DefineGuard.MustBeAbsentKey]);
            Assert.AreEqual("TEST_MODE", data[DefineGuard.MustBePresentKey]);
        }

        [Test]
        public void RecordPlan_OmitsKeys_WhenListsEmpty()
        {
            var data = new Dictionary<string, string>();

            DefineGuard.RecordPlan(data, new string[0], new string[0]);

            Assert.IsFalse(data.ContainsKey(DefineGuard.MustBeAbsentKey));
            Assert.IsFalse(data.ContainsKey(DefineGuard.MustBePresentKey));
        }

        [Test]
        public void ParsePlan_RoundTripsThroughRecordedData()
        {
            var data = new Dictionary<string, string>();
            DefineGuard.RecordPlan(data,
                new[] { "VENDOR_BRIDGE_READY" }, new[] { "TEST_MODE" });

            CollectionAssert.AreEqual(new[] { "VENDOR_BRIDGE_READY" },
                DefineGuard.ParsePlanList(data, DefineGuard.MustBeAbsentKey));
            CollectionAssert.AreEqual(new[] { "TEST_MODE" },
                DefineGuard.ParsePlanList(data, DefineGuard.MustBePresentKey));
        }

        [Test]
        public void ParsePlan_MissingKey_YieldsEmptyList_NotNull()
        {
            var list = DefineGuard.ParsePlanList(
                new Dictionary<string, string>(), DefineGuard.MustBeAbsentKey);

            Assert.IsNotNull(list);
            Assert.AreEqual(0, list.Count);
        }
    }
}
