using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class BuildPanelWindowEditModeTests
    {
        [Test]
        public void Running_AlwaysRepaints()
            => Assert.IsTrue(BuildPanelWindow.ShouldRepaint(running: true, wasRunning: true));

        [Test]
        public void IdleAfterRunning_RepaintsOnce_TheEdge()
            => Assert.IsTrue(BuildPanelWindow.ShouldRepaint(running: false, wasRunning: true));

        [Test]
        public void IdleAfterIdle_DoesNotRepaint()
            => Assert.IsFalse(BuildPanelWindow.ShouldRepaint(running: false, wasRunning: false));
    }
}
