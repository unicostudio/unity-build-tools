using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class BuildContextEditModeTests
    {
        [Test]
        public void AddStep_AppendsInOrder()
        {
            var ctx = new BuildContext(new BuildRequest());
            ctx.AddStep("a");
            ctx.AddStep("b");
            Assert.AreEqual(new[] { "a", "b" }, ctx.Steps.ToArray());
        }

        [Test]
        public void AddArtifact_Appends()
        {
            var ctx = new BuildContext(new BuildRequest());
            ctx.AddArtifact("path/x.apk");
            Assert.AreEqual(1, ctx.Artifacts.Count);
            Assert.AreEqual("path/x.apk", ctx.Artifacts[0]);
        }

        [Test]
        public void AddArtifact_KeepsArtifactKindsIndexAligned()
        {
            // Finish pairs Artifacts[i] with ArtifactKinds[i] to build TypedArtifacts. The
            // parallel kinds write was never asserted anywhere: deleting it silently turned every
            // artifact Unknown while the whole suite stayed green (measured). Both overloads must
            // append — the untyped one as Unknown.
            var ctx = new BuildContext(new BuildRequest());
            ctx.AddArtifact("path/x.apk");
            ctx.AddArtifact("path/y.aab", ArtifactKind.Aab);
            CollectionAssert.AreEqual(
                new[] { (int)ArtifactKind.Unknown, (int)ArtifactKind.Aab }, ctx.ArtifactKinds);
        }
    }
}
