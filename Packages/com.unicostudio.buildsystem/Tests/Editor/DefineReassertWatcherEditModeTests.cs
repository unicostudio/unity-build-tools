using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;
using UnityEditor.Build;

namespace UnicoStudio.BuildSystem.Tests
{
    /// <summary>
    /// The strip window's last-writer guard (0.12.1). Measured 2026-08-17: a third-party
    /// package's UPM-event handler can rewrite the platform defines in-session, AFTER the
    /// stage's write but BEFORE the reload lands — the surviving half-state (define back,
    /// package gone) fails every compile and wedges the job to the CI deadline. The
    /// watcher re-asserts the written plan every editor tick until the reload lands (its
    /// subscription dies with the domain, and the adversary's code leaves with its
    /// package), so the last writer inside the window is always the build.
    /// </summary>
    public class DefineReassertWatcherEditModeTests
    {
        [TearDown]
        public void TearDown() => DefineReassertWatcher.ResetForTests();

        // --- NeedsReassert: pure decision core -----------------------------------------

        [Test]
        public void MatchingDefines_NeedNoReassert()
        {
            Assert.IsFalse(DefineReassertWatcher.NeedsReassert("A;B", "A;B"));
        }

        [Test]
        public void FoughtDefines_NeedReassert()
        {
            Assert.IsTrue(DefineReassertWatcher.NeedsReassert("A;B;VENDOR_BRIDGE_READY", "A;B"));
        }

        [Test]
        public void RemovedRequiredDefine_NeedsReassert()
        {
            Assert.IsTrue(DefineReassertWatcher.NeedsReassert("A", "A;TEST_MODE"));
        }

        // --- Arm/Disarm lifecycle ------------------------------------------------------

        [Test]
        public void FreshState_IsNotArmed()
        {
            Assert.IsFalse(DefineReassertWatcher.IsArmed);
        }

        [Test]
        public void Arm_SetsArmed_AndResetsCount()
        {
            DefineReassertWatcher.Arm(NamedBuildTarget.iOS, "A;B");

            Assert.IsTrue(DefineReassertWatcher.IsArmed);
            Assert.AreEqual(0, DefineReassertWatcher.ReassertCount);
        }

        [Test]
        public void Disarm_ClearsArmed_AndIsIdempotent()
        {
            DefineReassertWatcher.Arm(NamedBuildTarget.iOS, "A;B");

            DefineReassertWatcher.Disarm();
            DefineReassertWatcher.Disarm();

            Assert.IsFalse(DefineReassertWatcher.IsArmed);
        }

        [Test]
        public void Rearm_ReplacesExpectationAndResetsCount()
        {
            DefineReassertWatcher.Arm(NamedBuildTarget.iOS, "A;B");
            DefineReassertWatcher.SimulateTickForTests("A;B;INTRUDER");
            Assert.AreEqual(1, DefineReassertWatcher.ReassertCount);

            DefineReassertWatcher.Arm(NamedBuildTarget.iOS, "A;B;C");

            Assert.AreEqual(0, DefineReassertWatcher.ReassertCount);
        }

        // --- Tick behavior through the test seam (no PlayerSettings mutation) ----------

        [Test]
        public void Tick_WithExpectedState_DoesNotCount()
        {
            DefineReassertWatcher.Arm(NamedBuildTarget.iOS, "A;B");

            var rewrote = DefineReassertWatcher.SimulateTickForTests("A;B");

            Assert.IsFalse(rewrote);
            Assert.AreEqual(0, DefineReassertWatcher.ReassertCount);
        }

        [Test]
        public void Tick_WithFoughtState_CountsEachRewrite()
        {
            DefineReassertWatcher.Arm(NamedBuildTarget.iOS, "A;B");

            Assert.IsTrue(DefineReassertWatcher.SimulateTickForTests("A;B;INTRUDER"));
            Assert.IsTrue(DefineReassertWatcher.SimulateTickForTests("INTRUDER"));

            Assert.AreEqual(2, DefineReassertWatcher.ReassertCount);
        }

        [Test]
        public void Tick_WhenNotArmed_IsNoop()
        {
            var rewrote = DefineReassertWatcher.SimulateTickForTests("ANYTHING");

            Assert.IsFalse(rewrote);
            Assert.AreEqual(0, DefineReassertWatcher.ReassertCount);
        }
    }
}
