using NUnit.Framework;
using UnityEngine;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class BuildResultEditModeTests
    {
        // LatestResult survives the end-of-build domain reload via a SessionState JSON round-trip,
        // so BuildResult must stay JsonUtility-compatible (public fields, [Serializable]).
        [Test]
        public void JsonRoundTrip_PreservesAllFields()
        {
            var result = new BuildResult
            {
                // true, deliberately: false is bool's CLR default, so asserting it cannot detect
                // a field that was never serialized at all. Measured: [NonSerialized] on Success
                // left the whole suite green while every reload-surviving result read "failed".
                Success = true,
                Error = "boom",
                DurationSeconds = 272.5,
                Steps = { "step a", "step b" },
                Artifacts = { "Builds/x.apk" },
            };

            var back = JsonUtility.FromJson<BuildResult>(JsonUtility.ToJson(result));

            Assert.IsTrue(back.Success);
            Assert.AreEqual("boom", back.Error);
            Assert.AreEqual(272.5, back.DurationSeconds, 0.001);
            CollectionAssert.AreEqual(new[] { "step a", "step b" }, back.Steps);
            CollectionAssert.AreEqual(new[] { "Builds/x.apk" }, back.Artifacts);
        }

        [Test]
        public void V2Fields_RoundTrip()
        {
            var result = new BuildResult
            {
                Success = true,
                Warnings = { "w" },
                VersionName = "1.6.7",
                BuildCode = "50",
                AddressablesVersion = 37,
                StartedUtc = "2026-07-28T10:00:00Z",
                EndedUtc = "2026-07-28T10:12:00Z",
            };
            result.TypedArtifacts.Add(new BuildArtifact { Path = "Builds/a.aab", Kind = ArtifactKind.Aab });

            var back = UnityEngine.JsonUtility.FromJson<BuildResult>(UnityEngine.JsonUtility.ToJson(result));

            CollectionAssert.AreEqual(new[] { "w" }, back.Warnings);
            Assert.AreEqual("1.6.7", back.VersionName);
            Assert.AreEqual("50", back.BuildCode);
            Assert.AreEqual(37, back.AddressablesVersion);
            Assert.AreEqual("2026-07-28T10:00:00Z", back.StartedUtc);
            Assert.AreEqual("2026-07-28T10:12:00Z", back.EndedUtc);
            Assert.AreEqual(1, back.TypedArtifacts.Count);
            Assert.AreEqual("Builds/a.aab", back.TypedArtifacts[0].Path);
            Assert.AreEqual(ArtifactKind.Aab, back.TypedArtifacts[0].Kind);
        }

        [Test]
        public void LegacyJson_HydratesV2Defaults_OutOfTheBox()
        {
            // RAW FromJson, no Normalize — deliberately. The old shape called .Normalize() in the
            // arrange line, so three of its four asserts were guaranteed by Normalize's
            // unconditional null-coalescing and could never trip on an initializer-behavior
            // change. These pin the measured 6000.0 contract itself: FromJson RUNS field
            // initializers, so a legacy payload comes back with non-null collections and the
            // documented -1 sentinel out of the box. If a Unity upgrade breaks this, Normalize's
            // coalescing becomes load-bearing — and this test is the tripwire that says so.
            var raw = UnityEngine.JsonUtility.FromJson<BuildResult>("{\"Success\":true,\"Error\":\"\"}");
            Assert.IsNotNull(raw.Warnings);
            Assert.IsNotNull(raw.TypedArtifacts);
            Assert.AreEqual("", raw.VersionName);
            Assert.AreEqual(-1, raw.AddressablesVersion);
        }
    }
}
