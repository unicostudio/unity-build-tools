using System;
using NUnit.Framework;
using UnityEditor;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    /// <summary>
    /// The panel's pure/testable surface (PanelDecisions), extracted so the 0.13.0 UX pass
    /// stays measurable:
    ///   - Version-row gating: bump toggles are enabled by (and forced false without) the
    ///     stage that gives them meaning — BumpBuildCode needs Build Player, BumpAddressables
    ///     needs Build Addressables. A greyed row must never smuggle a hidden true into
    ///     Start(), so the core returns EFFECTIVE values, not just enabled flags.
    ///   - Icon names: every editor icon the panel renders must resolve on THIS Unity
    ///     version — IconContent silently returns an empty content for a bad name, so a
    ///     typo'd icon ships as an invisible hole unless something asserts the load.
    ///   - StartedUtc formatting: ISO round-trip in, short local timestamp out; empty and
    ///     garbage degrade to "" (legacy results carry no timestamp).
    /// </summary>
    public sealed class PanelDecisionsEditModeTests
    {
        [Test]
        public void Gating_BothStagesOn_KeepsRequestedBumps()
        {
            var g = PanelDecisions.GateVersionRows(
                buildPlayer: true, buildAddressables: true,
                requestedBumpCode: true, requestedBumpAddressables: true);

            Assert.IsTrue(g.CodeRowEnabled);
            Assert.IsTrue(g.AddressablesRowEnabled);
            Assert.IsTrue(g.EffectiveBumpCode);
            Assert.IsTrue(g.EffectiveBumpAddressables);
        }

        [Test]
        public void Gating_PlayerOff_DisablesAndForcesCodeBumpFalse()
        {
            var g = PanelDecisions.GateVersionRows(
                buildPlayer: false, buildAddressables: true,
                requestedBumpCode: true, requestedBumpAddressables: true);

            Assert.IsFalse(g.CodeRowEnabled);
            Assert.IsFalse(g.EffectiveBumpCode, "a greyed row must not smuggle a hidden true into Start()");
            Assert.IsTrue(g.AddressablesRowEnabled);
            Assert.IsTrue(g.EffectiveBumpAddressables);
        }

        [Test]
        public void Gating_AddressablesOff_DisablesAndForcesAddressablesBumpFalse()
        {
            var g = PanelDecisions.GateVersionRows(
                buildPlayer: true, buildAddressables: false,
                requestedBumpCode: true, requestedBumpAddressables: true);

            Assert.IsTrue(g.CodeRowEnabled);
            Assert.IsTrue(g.EffectiveBumpCode);
            Assert.IsFalse(g.AddressablesRowEnabled);
            Assert.IsFalse(g.EffectiveBumpAddressables);
        }

        [Test]
        public void Gating_UnrequestedBumps_StayFalse_EvenWithStagesOn()
        {
            var g = PanelDecisions.GateVersionRows(
                buildPlayer: true, buildAddressables: true,
                requestedBumpCode: false, requestedBumpAddressables: false);

            Assert.IsFalse(g.EffectiveBumpCode);
            Assert.IsFalse(g.EffectiveBumpAddressables);
        }

        [Test]
        public void EveryPanelIcon_ResolvesOnThisUnityVersion()
        {
            Assert.IsNotEmpty(PanelDecisions.IconNames, "the panel declares its icon set here");
            foreach (var name in PanelDecisions.IconNames)
            {
                var icon = EditorGUIUtility.IconContent(name);
                Assert.IsNotNull(icon?.image,
                    $"icon '{name}' did not resolve — IconContent degrades to an empty content, " +
                    "so a typo ships as an invisible hole");
            }
        }

        [Test]
        public void FormatStartedLocal_RoundTripIso_YieldsNonEmptyLocalStamp()
        {
            var iso = new DateTime(2026, 8, 25, 11, 30, 0, DateTimeKind.Utc).ToString("o");

            var text = PanelDecisions.FormatStartedLocal(iso);

            Assert.IsNotEmpty(text);
            StringAssert.Contains("2026", text);
        }

        [Test]
        public void FormatStartedLocal_EmptyOrGarbage_DegradesToEmpty()
        {
            Assert.AreEqual("", PanelDecisions.FormatStartedLocal(""));
            Assert.AreEqual("", PanelDecisions.FormatStartedLocal(null));
            Assert.AreEqual("", PanelDecisions.FormatStartedLocal("not-a-date"));
        }
    }
}
