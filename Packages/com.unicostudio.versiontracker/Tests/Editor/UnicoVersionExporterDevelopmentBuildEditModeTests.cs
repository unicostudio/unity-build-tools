using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;

namespace UnicoStudio.UnicoLibs.VersionTracker.Tests
{
    /// <summary>
    /// The build-info file records whether the binary was a development build, read from the
    /// build's own options, so a shipped release's committed file can be audited later.
    /// </summary>
    public class UnicoVersionExporterDevelopmentBuildEditModeTests
    {
        private readonly TrackerOutputSandbox _sandbox = new();

        [SetUp]
        public void SetUp() => _sandbox.Enter();

        [TearDown]
        public void TearDown() => _sandbox.Exit();

        private static JObject ExportedProjectInfo(BuildOptions options)
        {
            var written = UnicoVersionExporter.ExportBuildInfo(TestBuildSummaries.ForAndroid(options));
            Assert.IsNotNull(written);
            return (JObject)JObject.Parse(File.ReadAllText(written))["projectInfo"];
        }

        [Test]
        public void DevelopmentOption_IsExportedAsTrue()
        {
            var info = ExportedProjectInfo(BuildOptions.Development | BuildOptions.CompressWithLz4HC);
            Assert.AreEqual(true, (bool)info["developmentBuild"]);
            Assert.AreEqual("LZ4HC", (string)info["compressionMethod"]);
        }

        [Test]
        public void NoDevelopmentOption_IsExportedAsFalse_NotOmitted()
        {
            var info = ExportedProjectInfo(BuildOptions.CompressWithLz4HC);
            Assert.IsTrue(info.ContainsKey("developmentBuild"));
            Assert.AreEqual(false, (bool)info["developmentBuild"]);
        }

        [Test]
        public async Task FileWrittenBeforeTheField_ReadsBackAsFalse()
        {
            var summary = TestBuildSummaries.ForAndroid(BuildOptions.Development);
            var written = UnicoVersionExporter.ExportBuildInfo(summary);
            var root = JObject.Parse(File.ReadAllText(written));
            ((JObject)root["projectInfo"]).Remove("developmentBuild");
            File.WriteAllText(written, root.ToString());

            var readBack = await UnicoVersionExporter.GetSavedBuildInfo(summary.platform);

            Assert.IsNotNull(readBack);
            Assert.IsFalse(readBack.ProjectInfo.DevelopmentBuild);
        }

        [Test]
        public async Task RoundTrip_KeepsDevelopmentBuild()
        {
            var summary = TestBuildSummaries.ForAndroid(BuildOptions.Development);
            UnicoVersionExporter.ExportBuildInfo(summary);

            var readBack = await UnicoVersionExporter.GetSavedBuildInfo(summary.platform);

            Assert.IsTrue(readBack.ProjectInfo.DevelopmentBuild);
        }
    }
}
