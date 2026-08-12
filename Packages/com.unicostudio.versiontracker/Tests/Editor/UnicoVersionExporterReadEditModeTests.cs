using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnicoStudio.UnicoLibs.VersionTracker.Tests
{
    /// <summary>
    /// Read-path failure characterization: missing/corrupt files must log real
    /// Debug.LogError lines (unlike SDK-detection misses, which are plain red-colored
    /// Debug.Log by design) and yield null instead of throwing.
    /// These tests await on the MAIN thread deliberately: the exporter's read API
    /// composes its path with Application.productName, a main-thread-only Unity call —
    /// pushing the call to a worker thread makes it fail for the wrong reason.
    /// </summary>
    public class UnicoVersionExporterReadEditModeTests
    {
        private readonly TrackerOutputSandbox _sandbox = new();

        [SetUp]
        public void SetUp() => _sandbox.Enter();

        [TearDown]
        public void TearDown() => _sandbox.Exit();

        [Test]
        public async Task GetSavedBuildInfoJson_MissingFile_LogsErrorAndReturnsNull()
        {
            LogAssert.Expect(LogType.Error, new Regex("Error reading file"));

            var json = await UnicoVersionExporter.GetSavedBuildInfoJson(BuildTarget.Android);

            Assert.IsNull(json);
        }

        [Test]
        public async Task GetSavedBuildInfo_MissingFile_LogsTwiceAndReturnsNull()
        {
            // Characterized double-error: the json read fails (first error), then the
            // deserializer receives null and throws (second error, same catch message).
            LogAssert.Expect(LogType.Error, new Regex("Error reading file"));
            LogAssert.Expect(LogType.Error, new Regex("Error reading file"));

            var info = await UnicoVersionExporter.GetSavedBuildInfo(BuildTarget.Android);

            Assert.IsNull(info);
        }

        [Test]
        public async Task GetSavedBuildInfo_CorruptJson_LogsErrorAndReturnsNull()
        {
            var path = UnicoVersionExporter.GetBuildInfoPath(BuildTarget.Android);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ this is not json");
            LogAssert.Expect(LogType.Error, new Regex("Error reading file"));

            var info = await UnicoVersionExporter.GetSavedBuildInfo(BuildTarget.Android);

            Assert.IsNull(info);
        }
    }
}
