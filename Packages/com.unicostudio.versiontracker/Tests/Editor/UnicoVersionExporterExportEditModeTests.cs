using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UnicoStudio.UnicoLibs.VersionTracker.Tests
{
    /// <summary>
    /// End-to-end export through the public 1.6.0 API, driven by a reflection-built
    /// Android BuildSummary (see TestBuildSummaries). SDK detection misses in this repo
    /// log as plain (red-colored) Debug.Log lines by design, which do not fail tests.
    /// The read-back test awaits on the main thread: the exporter's async read API
    /// composes its path with main-thread-only Unity calls, so it must never be pushed
    /// to a worker thread, and blocking the main thread on it would deadlock.
    /// </summary>
    public class UnicoVersionExporterExportEditModeTests
    {
        private readonly TrackerOutputSandbox _sandbox = new();

        [SetUp]
        public void SetUp() => _sandbox.Enter();

        [TearDown]
        public void TearDown() => _sandbox.Exit();

        [Test]
        public void Export_WritesFileAtGetBuildInfoPath_AndReturnsThatPath()
        {
            var summary = TestBuildSummaries.ForAndroid();

            var written = UnicoVersionExporter.ExportBuildInfo(summary);

            Assert.IsNotNull(written, "export returned null — the synchronous write failed");
            Assert.AreEqual(UnicoVersionExporter.GetBuildInfoPath(summary.platform), written,
                "ExportBuildInfo and GetBuildInfoPath must compose the same path (1.6.0 contract)");
            Assert.IsTrue(File.Exists(written), "the write is synchronous — file must exist on return");
        }

        [Test]
        public void Export_Json_IsCamelCasedIndented_WithNullsIncluded()
        {
            var written = UnicoVersionExporter.ExportBuildInfo(TestBuildSummaries.ForAndroid());
            Assert.IsNotNull(written);

            var text = File.ReadAllText(written);
            var root = JObject.Parse(text);

            Assert.IsNotNull(root["projectInfo"], "camelCase key 'projectInfo' expected");
            Assert.IsNotNull(root["sdkInfo"], "camelCase key 'sdkInfo' expected");
            StringAssert.Contains("\n", text, "Formatting.Indented expected");
            // NullValueHandling.Include: the host-only gameId resolves to null in this repo
            // and must still be present as an explicit null.
            Assert.IsTrue(((JObject)root["projectInfo"]).ContainsKey("gameId"),
                "null-valued keys must be serialized (NullValueHandling.Include)");
        }

        [Test]
        public async Task Export_RoundTrip_GetSavedBuildInfo_ReadsBackCoreFields()
        {
            var summary = TestBuildSummaries.ForAndroid();
            var written = UnicoVersionExporter.ExportBuildInfo(summary);
            Assert.IsNotNull(written);

            var readBack = await UnicoVersionExporter.GetSavedBuildInfo(summary.platform);

            Assert.IsNotNull(readBack, "round-trip read failed");
            Assert.AreEqual(Application.unityVersion, readBack.ProjectInfo.UnityVersion);
            Assert.AreEqual("Android", readBack.ProjectInfo.Platform);
            Assert.AreEqual(UnicoVersionExporter.s_sdkInfo.Count, readBack.SdkInfo.Count,
                "every catalog SDK entry must survive the round-trip");
        }

        [Test]
        public void ExportAsyncShim_CompletesTheWriteBeforeReturning()
        {
            var summary = TestBuildSummaries.ForAndroid();

            UnicoVersionExporter.ExportBuildInfoAsync(summary);

            Assert.IsTrue(File.Exists(UnicoVersionExporter.GetBuildInfoPath(summary.platform)),
                "the 1.6.0 shim delegates to the synchronous export — file must exist on return");
        }
    }
}
