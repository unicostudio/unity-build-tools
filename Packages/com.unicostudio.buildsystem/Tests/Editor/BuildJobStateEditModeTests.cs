using NUnit.Framework;
using UnityEngine;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class BuildJobStateEditModeTests
    {
        // The job state crosses domain reloads as SessionState JSON — every field must survive the
        // JsonUtility round-trip, or the resumer continues from a corrupted job.
        [Test]
        public void JsonRoundTrip_PreservesAllFields()
        {
            var state = new BuildJobState
            {
                Active = true,
                StepIndex = 2,
                RequestJson = "{\"Platform\":0}",
                SnapshotJson = "{\"BundleVersion\":\"1.6.6\"}",
                Profile = AddressablesProfile.Production,
                StartedTicksUtc = 638_000_000_000_000_000,
                ExtraDefines = { "TEST_MODE" },
                Steps = { "Version: 1.6.6 (49)", "Defines configured" },
                Artifacts = { "ServerData/v36/Test/Android" },
                ArtifactKinds = { 1, 3 },
                Warnings = { "w1" },
                StageTypeNames = { "T1, Asm", "T2, Asm" },
                StageDisplayNames = { "Apply version", "Hook X" },
            };

            var back = BuildJobState.FromJson(JsonUtility.ToJson(state));

            Assert.IsTrue(back.Active);
            Assert.AreEqual(2, back.StepIndex);
            Assert.AreEqual("{\"Platform\":0}", back.RequestJson);
            Assert.AreEqual("{\"BundleVersion\":\"1.6.6\"}", back.SnapshotJson);
            Assert.AreEqual(AddressablesProfile.Production, back.Profile);
            Assert.AreEqual(638_000_000_000_000_000, back.StartedTicksUtc);
            CollectionAssert.AreEqual(new[] { "TEST_MODE" }, back.ExtraDefines);
            CollectionAssert.AreEqual(new[] { "Version: 1.6.6 (49)", "Defines configured" }, back.Steps);
            CollectionAssert.AreEqual(new[] { "ServerData/v36/Test/Android" }, back.Artifacts);
            CollectionAssert.AreEqual(new[] { 1, 3 }, back.ArtifactKinds);
            CollectionAssert.AreEqual(new[] { "w1" }, back.Warnings);
            CollectionAssert.AreEqual(new[] { "T1, Asm", "T2, Asm" }, back.StageTypeNames);
            CollectionAssert.AreEqual(new[] { "Apply version", "Hook X" }, back.StageDisplayNames);
        }

        [Test]
        public void LegacyPayload_DeserializesWithNonNullCollections_OutOfTheBox()
        {
            // RAW JsonUtility, bypassing FromJson's ??= — deliberately. This is the tripwire for
            // the measured 6000.0 contract: FromJson RUNS field initializers, so a legacy payload
            // hydrates with non-null collections before any normalization. If a Unity upgrade
            // changes that, this fails and FromJson's coalescing becomes load-bearing.
            var raw = UnityEngine.JsonUtility.FromJson<BuildJobState>("{\"Active\":true,\"StepIndex\":1}");
            Assert.IsNotNull(raw.ExtraDefines);
            Assert.IsNotNull(raw.Steps);
            Assert.IsNotNull(raw.Warnings);
            Assert.IsNotNull(raw.StageTypeNames);
            Assert.IsNotNull(raw.DataKeys);
        }

        [Test]
        public void FromJson_GuaranteesNonNullCollections()
        {
            // The API contract the resumer relies on, regardless of WHICH mechanism provides it
            // (initializers today, the ??= if Unity ever stops running them). The old name said
            // "NormalizesNullCollections" — wrong: on this Unity the ??= are never the ones
            // producing the non-null lists (see FromJson's comment).
            var back = BuildJobState.FromJson("{\"Active\":true,\"StepIndex\":1}");

            Assert.IsTrue(back.Active);
            Assert.AreEqual(1, back.StepIndex);
            Assert.IsNotNull(back.ExtraDefines);
            Assert.IsNotNull(back.Steps);
            Assert.IsNotNull(back.Artifacts);
            Assert.IsNotNull(back.ArtifactKinds);
            Assert.IsNotNull(back.Warnings);
            Assert.IsNotNull(back.StageTypeNames);
            Assert.IsNotNull(back.StageDisplayNames);
            Assert.IsNotNull(back.DataKeys);
            Assert.IsNotNull(back.DataValues);
            Assert.AreEqual(0, back.StartedTicksUtc);   // legacy job: duration reads as unknown
        }

        [Test]
        public void DataDictionary_RoundTripsThroughParallelLists()
        {
            var state = new BuildJobState();
            state.SetData(new System.Collections.Generic.Dictionary<string, string>
            {
                ["git.hash"] = "abc123",
                ["empty"] = "",
                ["nullValue"] = null,
            });

            var back = BuildJobState.FromJson(UnityEngine.JsonUtility.ToJson(state));
            var data = back.GetData();

            Assert.AreEqual(3, data.Count);
            Assert.AreEqual("abc123", data["git.hash"]);
            Assert.AreEqual("", data["empty"]);
            Assert.AreEqual("", data["nullValue"]);
        }
    }
}
