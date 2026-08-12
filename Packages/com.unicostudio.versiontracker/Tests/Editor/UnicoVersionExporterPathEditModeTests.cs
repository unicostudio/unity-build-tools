using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UnicoStudio.UnicoLibs.VersionTracker.Tests
{
    /// <summary>
    /// GetBuildInfoPath is the sanctioned way for glue/CI code to locate the build-info
    /// artifact (1.6.0 contract): same composer as the write path, pure query. These tests
    /// pin the composition rules a consumer may rely on.
    /// </summary>
    public class UnicoVersionExporterPathEditModeTests
    {
        private readonly TrackerOutputSandbox _sandbox = new();

        [SetUp]
        public void SetUp() => _sandbox.Enter();

        [TearDown]
        public void TearDown() => _sandbox.Exit();

        [Test]
        public void Path_EndsWithPlatformAndBuildInfoSuffix()
        {
            var path = UnicoVersionExporter.GetBuildInfoPath(BuildTarget.Android);
            StringAssert.EndsWith("_Android_BuildInfo.json", path);
        }

        [Test]
        public void Path_ParentFolderIsUnicoVersionTrackerAtProjectRoot()
        {
            var full = Path.GetFullPath(UnicoVersionExporter.GetBuildInfoPath(BuildTarget.Android));
            var parent = new DirectoryInfo(Path.GetDirectoryName(full));
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            Assert.AreEqual("UnicoVersionTracker", parent.Name);
            Assert.AreEqual(projectRoot, parent.Parent!.FullName);
        }

        [Test]
        public void Path_EmbedsProductNameAndVersionPrefix()
        {
            var oldName = PlayerSettings.productName;
            var oldVersion = PlayerSettings.bundleVersion;
            try
            {
                PlayerSettings.productName = "LineageProbe";
                PlayerSettings.bundleVersion = "9.9.9";
                var file = Path.GetFileName(UnicoVersionExporter.GetBuildInfoPath(BuildTarget.iOS));
                Assert.AreEqual("LineageProbe_9.9.9_iOS_BuildInfo.json", file);
            }
            finally
            {
                PlayerSettings.productName = oldName;
                PlayerSettings.bundleVersion = oldVersion;
            }
        }

        [Test]
        public void Path_SanitizesInvalidCharsAndRemovesSpaces()
        {
            var oldName = PlayerSettings.productName;
            try
            {
                // '/' is an invalid filename char on every platform -> '-';
                // spaces are removed by MakeFileNameFriendly.
                PlayerSettings.productName = "Probe A/B";
                var file = Path.GetFileName(UnicoVersionExporter.GetBuildInfoPath(BuildTarget.Android));
                StringAssert.StartsWith("ProbeA-B_", file);
            }
            finally
            {
                PlayerSettings.productName = oldName;
            }
        }

        [Test]
        public void Path_DiffersPerPlatform()
        {
            Assert.AreNotEqual(
                UnicoVersionExporter.GetBuildInfoPath(BuildTarget.Android),
                UnicoVersionExporter.GetBuildInfoPath(BuildTarget.iOS));
        }

        [Test]
        public void Path_IsStableAcrossCalls()
        {
            Assert.AreEqual(
                UnicoVersionExporter.GetBuildInfoPath(BuildTarget.Android),
                UnicoVersionExporter.GetBuildInfoPath(BuildTarget.Android));
        }

        [Test]
        public void Purity_GetBuildInfoPath_DoesNotCreateTheOutputFolder()
        {
            Assert.IsFalse(Directory.Exists(_sandbox.Dir), "sandbox should start absent");
            UnicoVersionExporter.GetBuildInfoPath(BuildTarget.Android);
            UnicoVersionExporter.GetBuildInfoPath(BuildTarget.iOS);
            Assert.IsFalse(Directory.Exists(_sandbox.Dir),
                "GetBuildInfoPath is a pure query and must not create UnicoVersionTracker/");
        }
    }
}
