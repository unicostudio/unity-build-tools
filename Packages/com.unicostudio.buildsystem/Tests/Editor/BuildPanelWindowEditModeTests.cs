using System.IO;
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

        // The artifact Show button: producers compose artifact paths their own way — the
        // tracker's GetBuildInfoPath legitimately records "Assets/../UnicoVersionTracker/…".
        // File.Exists resolves such a path against the project root, but the OS file viewer
        // behind RevealInFinder does not: the raw ".." segment lands it in the wrong folder
        // (surfaced live by BT5's C4 Gate 2). Check and reveal must share ONE normalized path.

        [Test]
        public void ParentSegmentPath_ResolvesRootedWithoutDotDot()
        {
            var resolved = BuildPanelWindow.ResolveArtifactPath("Assets/../UnicoVersionTracker/BuildInfo.json");
            Assert.IsTrue(Path.IsPathRooted(resolved), resolved);
            StringAssert.DoesNotContain("..", resolved);
            StringAssert.EndsWith(Path.Combine("UnicoVersionTracker", "BuildInfo.json"), resolved);
        }

        [Test]
        public void PlainRelativePath_ResolvesRooted()
        {
            var resolved = BuildPanelWindow.ResolveArtifactPath(Path.Combine("Builds", "a.aab"));
            Assert.IsTrue(Path.IsPathRooted(resolved), resolved);
            StringAssert.EndsWith(Path.Combine("Builds", "a.aab"), resolved);
        }

        [Test]
        public void AbsolutePath_IsReturnedEqual()
        {
            var absolute = Path.GetFullPath("Builds");
            Assert.AreEqual(absolute, BuildPanelWindow.ResolveArtifactPath(absolute));
        }

        [Test]
        public void UnresolvablePath_FallsBackToTheRawString()
        {
            // "" cannot be resolved; the raw value flows on so the not-found dialog names what
            // the record actually holds instead of the button throwing.
            Assert.AreEqual("", BuildPanelWindow.ResolveArtifactPath(""));
        }
    }
}
