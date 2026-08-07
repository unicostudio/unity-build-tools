using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class UnicoBuildServicePostStepEditModeTests
    {
        private const string RunA = "2026-07-28T09:05:23.2103510Z";
        private const string RunB = "2026-07-28T10:41:02.8830000Z";

        [Test]
        public void SameRun_IsRecorded()
            => Assert.IsTrue(UnicoBuildService.ShouldRecordPostStep(RunA, RunA));

        [Test]
        public void LaterRunReplacedTheResult_IsNotRecorded()
        {
            // A second build started before the queue drained: its result is current now, and the
            // draining queue's outcomes belong to the earlier run — never to this one.
            Assert.IsFalse(UnicoBuildService.ShouldRecordPostStep(RunB, RunA));
        }

        [Test]
        public void NoCurrentResult_IsNotRecorded()
            => Assert.IsFalse(UnicoBuildService.ShouldRecordPostStep(null, RunA));

        [Test]
        public void UnstampedCurrentResult_DoesNotAbsorbAStampedQueue()
            // Exactly what Start's preflight-block early return leaves behind: a result with no
            // stamp. It must not collect a real run's post-step outcomes.
            => Assert.IsFalse(UnicoBuildService.ShouldRecordPostStep("", RunA));

        [Test]
        public void UnstampedQueue_IsNeverRecorded()
        {
            // An empty stamp identifies nothing (unreadable payload, or a legacy job whose state
            // carried no start time), so it must not match — not even another empty stamp.
            Assert.IsFalse(UnicoBuildService.ShouldRecordPostStep(RunA, ""));
            Assert.IsFalse(UnicoBuildService.ShouldRecordPostStep("", ""));
            Assert.IsFalse(UnicoBuildService.ShouldRecordPostStep(null, null));
        }

        // --- AppendPostArtifact's pure core ---

        [Test]
        public void AppendArtifactTo_AddsToBothLists_KeepingThemAligned()
        {
            var result = new BuildResult();
            result.Artifacts.Add("Builds/app.aab");
            result.TypedArtifacts.Add(new BuildArtifact { Path = "Builds/app.aab", Kind = ArtifactKind.Aab });

            UnicoBuildService.AppendArtifactTo(result, "Builds/meta/x_BuildInfo.json", ArtifactKind.Metadata);

            Assert.AreEqual(2, result.Artifacts.Count);
            Assert.AreEqual(2, result.TypedArtifacts.Count);
            Assert.AreEqual("Builds/meta/x_BuildInfo.json", result.Artifacts[1]);
            Assert.AreEqual("Builds/meta/x_BuildInfo.json", result.TypedArtifacts[1].Path);
            Assert.AreEqual(ArtifactKind.Metadata, result.TypedArtifacts[1].Kind);
        }

        [Test]
        public void AppendArtifactTo_LegacyResultWithFewerTypedEntries_IsPaddedBeforeAppending()
        {
            // A pre-v0.5.0 payload hydrates with paths but no typed entries (Finish pads with
            // Unknown for exactly this reason). Appending without padding would leave every later
            // reader pairing the new artifact's kind with an older artifact's path.
            var result = new BuildResult();
            result.Artifacts.Add("Builds/old.apk");

            UnicoBuildService.AppendArtifactTo(result, "meta.json", ArtifactKind.Metadata);

            Assert.AreEqual(2, result.Artifacts.Count);
            Assert.AreEqual(2, result.TypedArtifacts.Count);
            Assert.AreEqual("Builds/old.apk", result.TypedArtifacts[0].Path);
            Assert.AreEqual(ArtifactKind.Unknown, result.TypedArtifacts[0].Kind);
            Assert.AreEqual("meta.json", result.TypedArtifacts[1].Path);
            Assert.AreEqual(ArtifactKind.Metadata, result.TypedArtifacts[1].Kind);
        }

        [Test]
        public void MetadataKind_IsAppendedLast_SoPersistedIntegersDoNotShift()
        {
            // BuildResult round-trips through JsonUtility, which writes enums as integers. A member
            // inserted anywhere but the end would silently re-map every artifact kind already
            // persisted in SessionState or in a CI result file.
            Assert.AreEqual(0, (int)ArtifactKind.Unknown);
            Assert.AreEqual(1, (int)ArtifactKind.Apk);
            Assert.AreEqual(2, (int)ArtifactKind.Aab);
            Assert.AreEqual(3, (int)ArtifactKind.SymbolsZip);
            Assert.AreEqual(4, (int)ArtifactKind.XcodeProject);
            Assert.AreEqual(5, (int)ArtifactKind.AddressablesContent);
            Assert.AreEqual(6, (int)ArtifactKind.Metadata);
        }
    }
}
