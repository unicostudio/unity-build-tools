using NUnit.Framework;
using Unity.Android.Types;
using UnityEngine;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class DevStateSnapshotEditModeTests
    {
        // Tripwire for the measured 6000.0.62f1 contract Finish's null guard rests on: FromJson
        // returns NULL for a null or empty payload instead of throwing. (Whitespace is the odd
        // one out — it throws ArgumentException — which is why the guard checks the PARSE RESULT,
        // not the input string.) If a Unity upgrade changes this, the guard needs rework and this
        // fails first.
        [Test]
        public void FromJson_NullOrEmptyPayload_YieldsNull_NotAThrow()
        {
            Assert.IsNull(JsonUtility.FromJson<DevStateSnapshot>(null));
            Assert.IsNull(JsonUtility.FromJson<DevStateSnapshot>(""));
        }

        // The snapshot crosses domain reloads (SessionState) and editor restarts (EditorPrefs
        // mirror) as JSON — every field must survive the JsonUtility round-trip.
        [Test]
        public void JsonRoundTrip_PreservesAllFields()
        {
            var snap = new DevStateSnapshot
            {
                Platform = BuildPlatform.iOS,
                ScriptingDefines = "DOTWEEN;TEST_MODE",
                BuildAppBundle = true,
                AndroidSymbolMode = DebugSymbolLevel.SymbolTable,
                AddressablesProfileId = "profile-id",
                BundleVersion = "1.2.6",
                AndroidVersionCode = 17,
                IosBuildNumber = "9",
                AddressablesVersion = 11,
            };

            var back = JsonUtility.FromJson<DevStateSnapshot>(JsonUtility.ToJson(snap));

            Assert.AreEqual(BuildPlatform.iOS, back.Platform);
            Assert.AreEqual("DOTWEEN;TEST_MODE", back.ScriptingDefines);
            Assert.IsTrue(back.BuildAppBundle);
            Assert.AreEqual(DebugSymbolLevel.SymbolTable, back.AndroidSymbolMode);
            Assert.AreEqual("profile-id", back.AddressablesProfileId);
            Assert.AreEqual("1.2.6", back.BundleVersion);
            Assert.AreEqual(17, back.AndroidVersionCode);
            Assert.AreEqual("9", back.IosBuildNumber);
            Assert.AreEqual(11, back.AddressablesVersion);
        }

        [Test]
        public void LegacySnapshotWithoutVersionFields_HasEmptyBundleVersion()
        {
            // Pre-rollback snapshot JSON: RestoreVersions keys its skip-guard on BundleVersion
            // being empty, so a legacy payload must never fake real version values.
            var back = JsonUtility.FromJson<DevStateSnapshot>("{\"Platform\":0,\"ScriptingDefines\":\"X\"}");
            Assert.IsTrue(string.IsNullOrEmpty(back.BundleVersion));
        }
    }
}
