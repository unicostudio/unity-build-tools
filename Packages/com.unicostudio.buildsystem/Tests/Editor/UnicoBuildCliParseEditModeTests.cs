using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class UnicoBuildCliParseEditModeTests
    {
        private static UnicoBuildCli.CliOptions Parse(params string[] args) => UnicoBuildCli.Parse(args);

        [Test]
        public void FullReleaseInvocation_MapsEveryField()
        {
            var o = Parse("-platform", "Android", "-kind", "Release", "-versionName", "1.6.7",
                "-bumpBuildCode", "-outputs", "Apk,Aab", "-resultFile", "Builds/r.json",
                "-strictWarnings", "-timeoutMinutes", "45", "-label", "CI");
            Assert.AreEqual("", o.Error);
            Assert.AreEqual(BuildPlatform.Android, o.Request.Platform);
            Assert.AreEqual(BuildKind.Release, o.Request.Kind);
            Assert.AreEqual("1.6.7", o.Request.VersionName);
            Assert.IsTrue(o.Request.BumpBuildCode);
            Assert.AreEqual(OutputKind.Apk | OutputKind.Aab, o.Request.Outputs);
            Assert.AreEqual("Builds/r.json", o.ResultFile);
            Assert.IsTrue(o.StrictWarnings);
            Assert.AreEqual(45, o.TimeoutMinutes);
            Assert.AreEqual("CI", o.Request.Label);
        }

        [Test]
        public void UnityOwnArgs_AreIgnored()
        {
            var o = Parse("-batchmode", "-projectPath", "/x/y", "-platform", "iOS", "-kind", "Test");
            Assert.AreEqual("", o.Error);
            Assert.AreEqual(BuildPlatform.iOS, o.Request.Platform);
        }

        [Test]
        public void CiDefault_NoBump_UnlessFlagged()
        {
            var o = Parse("-platform", "Android", "-kind", "Test");
            Assert.IsFalse(o.Request.BumpBuildCode);   // panel default is true; CI must be explicit
        }

        [Test]
        public void BuildCode_PinsAndDisablesBump()
        {
            // -buildCode first: order independence here is enforced by -bumpBuildCode's
            // BuildCode==0 GUARD, not by -buildCode's unconditional disable.
            var o = Parse("-platform", "Android", "-kind", "Release", "-buildCode", "51", "-bumpBuildCode");
            Assert.AreEqual(51, o.Request.BuildCode);
            Assert.IsFalse(o.Request.BumpBuildCode);
        }

        [Test]
        public void BuildCode_DisablesBump_EvenWhenBumpCameFirst()
        {
            // The other order: -bumpBuildCode has already set true (BuildCode is still 0, so its
            // guard passes), and -buildCode's unconditional disable is the ONLY thing that wins.
            // That disable was previously unasserted in any order — deleting it left the whole
            // suite green (measured) while this order produced a contradictory request.
            var o = Parse("-platform", "Android", "-kind", "Release", "-bumpBuildCode", "-buildCode", "51");
            Assert.AreEqual(51, o.Request.BuildCode);
            Assert.IsFalse(o.Request.BumpBuildCode);
        }

        [Test]
        public void MissingPlatformOrKind_IsError()
        {
            StringAssert.Contains("-platform is required", Parse("-kind", "Test").Error);
            StringAssert.Contains("-kind is required", Parse("-platform", "Android").Error);
        }

        [Test]
        public void BadEnumAndMissingValue_AreErrors()
        {
            StringAssert.Contains("unknown platform", Parse("-platform", "Switch", "-kind", "Test").Error);
            StringAssert.Contains("-timeoutMinutes requires a value",
                Parse("-platform", "Android", "-kind", "Test", "-timeoutMinutes").Error);
        }

        [Test]
        public void NumericEnumValues_AreRejected_NotSilentlyAccepted()
        {
            // Enum.TryParse accepts any integer-formatted string, so "-platform 5" used to parse
            // into an undefined enum value and build for nothing. Names only.
            StringAssert.Contains("unknown platform", Parse("-platform", "5", "-kind", "Test").Error);
            StringAssert.Contains("unknown kind", Parse("-platform", "Android", "-kind", "7").Error);
            StringAssert.Contains("unknown output",
                Parse("-platform", "Android", "-kind", "Test", "-outputs", "3").Error);
            StringAssert.Contains("unknown addressablesMode",
                Parse("-platform", "Android", "-kind", "Test", "-addressablesMode", "9").Error);
        }

        [Test]
        public void BooleanValueFlags_Parse()
        {
            var o = Parse("-platform", "Android", "-kind", "Test",
                "-buildAddressables", "false", "-buildPlayer", "true", "-bumpAddressables");
            Assert.IsFalse(o.Request.BuildAddressables);
            Assert.IsTrue(o.Request.BuildPlayer);
            Assert.IsTrue(o.Request.BumpAddressablesVersion);
        }
    }
}
