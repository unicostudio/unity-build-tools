using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using NUnit.Framework;

namespace UnicoStudio.UnicoLibs.VersionTracker.Tests
{
    /// <summary>
    /// Pins the SDK catalog shape and the serialization contract of the public records.
    /// The serializer settings replicated here (camelCase, include nulls, indented) ARE the
    /// documented on-disk contract — if the exporter's private settings drift, the export
    /// tests catch it on the file; these tests catch it at record level.
    /// </summary>
    public class SdkCatalogAndRecordsEditModeTests
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            NullValueHandling = NullValueHandling.Include,
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            Formatting = Formatting.Indented,
        };

        [Test]
        public void Catalog_HasTenUniquelyNamedSdks()
        {
            var names = UnicoVersionExporter.s_sdkInfo.Select(s => s.Name).ToList();
            Assert.AreEqual(10, names.Count);
            CollectionAssert.AllItemsAreUnique(names);
        }

        [Test]
        public void Catalog_TracksTheDocumentedSdkSet()
        {
            var expected = new[]
            {
                "UnicoAPIClient", "AppLovinMAX", "GoogleAdMob", "GoogleImmersiveAds",
                "GoogleODM", "Odeeo", "AmazonSdk", "AdjustSdk", "FacebookSdk", "Firebase",
            };
            CollectionAssert.AreEquivalent(expected,
                UnicoVersionExporter.s_sdkInfo.Select(s => s.Name).ToList());
        }

        [Test]
        public void SdkInfo_Serialization_IsOptIn_NoDetectionInternalsLeak()
        {
            var json = JsonConvert.SerializeObject(
                new UnicoVersionExporter.SdkInfo("ProbeSdk", "1.2.3"), Settings);
            var keys = JObject.Parse(json).Properties().Select(p => p.Name).ToList();

            CollectionAssert.IsSubsetOf(keys, new[] { "name", "version", "pluginVersionInfo" },
                "MemberSerialization.OptIn must keep VersionGetter/UpmPackageNames off the wire");
            CollectionAssert.Contains(keys, "name");
            CollectionAssert.Contains(keys, "version");
        }

        [Test]
        public void VersionInfo_RoundTrip_PreservesAllFields()
        {
            var original = new UnicoVersionExporter.VersionInfo(
                "_google_admob_", "Google AdMob", "android_10.3.0_ios_12.2.0", "10.3.0", "12.2.0");

            var back = JsonConvert.DeserializeObject<UnicoVersionExporter.VersionInfo>(
                JsonConvert.SerializeObject(original, Settings), Settings);

            Assert.AreEqual(original, back, "records carry value equality — every field must survive");
        }

        [Test]
        public void MediationTypes_RoundTrip_PreservesBothPlatforms()
        {
            var original = new UnicoVersionExporter.MediationTypes("MAX", "AdMob");

            var back = JsonConvert.DeserializeObject<UnicoVersionExporter.MediationTypes>(
                JsonConvert.SerializeObject(original, Settings), Settings);

            Assert.AreEqual(original, back);
        }

        [Test]
        public void BuildInfo_JsonConstructor_RoundTripsWithoutTouchingTheLiveCatalog()
        {
            var original = new UnicoVersionExporter.BuildInfo(
                projectInfo: null,
                sdkInfo: new List<UnicoVersionExporter.SdkInfo>
                {
                    new("ProbeSdk", "9.9.9"),
                });

            var back = JsonConvert.DeserializeObject<UnicoVersionExporter.BuildInfo>(
                JsonConvert.SerializeObject(original, Settings), Settings);

            Assert.IsNull(back.ProjectInfo, "explicit null must round-trip (NullValueHandling.Include)");
            Assert.AreEqual(1, back.SdkInfo.Count);
            Assert.AreEqual("ProbeSdk", back.SdkInfo[0].Name);
            Assert.AreEqual("9.9.9", back.SdkInfo[0].Version);
        }

        [Test]
        public void RecordJson_UsesCamelCaseKeys_WithNullsIncluded()
        {
            var json = JsonConvert.SerializeObject(
                new UnicoVersionExporter.VersionInfo("id1", "Name1", null), Settings);
            var root = JObject.Parse(json);

            Assert.IsNotNull(root.Property("id"), "camelCase 'id' expected");
            Assert.IsNotNull(root.Property("version"), "null-valued 'version' must still be present");
            Assert.AreEqual(JTokenType.Null, root["version"]!.Type);
            Assert.IsNull(root.Property("Id"), "PascalCase keys must not appear");
        }
    }
}
