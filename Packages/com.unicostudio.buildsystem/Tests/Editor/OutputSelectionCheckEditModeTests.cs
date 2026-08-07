using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class OutputSelectionCheckEditModeTests
    {
        private static CheckSeverity Severity(bool buildPlayer, OutputKind outputs,
            BuildPlatform platform = BuildPlatform.Android)
        {
            var req = new BuildRequest { BuildPlayer = buildPlayer, Outputs = outputs, Platform = platform };
            return new OutputSelectionCheck().Run(new BuildContext(req)).Severity;
        }

        [Test]
        public void PlayerBuildWithNoOutput_Blocks()
        {
            Assert.AreEqual(CheckSeverity.Block, Severity(buildPlayer: true, OutputKind.None));
        }

        [Test]
        public void AndroidWithoutApkOrAab_Blocks()
        {
            // XcodeProject alone matches no Android artifact branch: the job would finish "OK"
            // with zero artifacts after burning the version bumps.
            Assert.AreEqual(CheckSeverity.Block,
                Severity(buildPlayer: true, OutputKind.XcodeProject, BuildPlatform.Android));
        }

        [Test]
        public void ValidCombinations_Pass()
        {
            Assert.AreEqual(CheckSeverity.Pass, Severity(buildPlayer: true, OutputKind.Apk));
            Assert.AreEqual(CheckSeverity.Pass, Severity(buildPlayer: true, OutputKind.Apk | OutputKind.Aab));
            Assert.AreEqual(CheckSeverity.Pass,
                Severity(buildPlayer: true, OutputKind.XcodeProject, BuildPlatform.iOS));
            // Addressables-only job: no output needed.
            Assert.AreEqual(CheckSeverity.Pass, Severity(buildPlayer: false, OutputKind.None));
        }
    }
}
