using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    /// <summary>
    /// Pins the Development Build option: Test builds default to it, Release builds can never
    /// carry it (refused at preflight and again right before BuildPlayer), the player gets
    /// BuildOptions.Development and nothing else from it, and the outcome is visible.
    /// </summary>
    public sealed class DevelopmentBuildEditModeTests
    {
        // --- request defaults ---

        [Test]
        public void DefaultDevelopmentFor_IsOnForTest_OffForRelease()
        {
            Assert.IsTrue(BuildRequest.DefaultDevelopmentFor(BuildKind.Test));
            Assert.IsFalse(BuildRequest.DefaultDevelopmentFor(BuildKind.Release));
        }

        [Test]
        public void BareRequest_IsNotADevelopmentBuild()
        {
            // A caller that never heard of the field must not get a development player.
            Assert.IsFalse(new BuildRequest().Development);
            Assert.IsFalse(JsonUtility.FromJson<BuildRequest>("{\"Kind\":1}").Development);
        }

        // --- preflight ---

        [Test]
        public void Check_ReleaseWithDevelopment_Blocks()
        {
            var r = DevelopmentBuildCheck.Evaluate(BuildKind.Release, development: true, buildPlayer: true, BuildPlatform.Android);
            Assert.AreEqual(CheckSeverity.Block, r.Severity);
            StringAssert.Contains("Release", r.Message);
        }

        [Test]
        public void Check_ReleaseWithDevelopment_Blocks_EvenWithoutAPlayerBuild()
        {
            var r = DevelopmentBuildCheck.Evaluate(BuildKind.Release, development: true, buildPlayer: false, BuildPlatform.Android);
            Assert.AreEqual(CheckSeverity.Block, r.Severity);
        }

        [Test]
        public void Check_TestDevelopment_PassesAndSaysOn()
        {
            var r = DevelopmentBuildCheck.Evaluate(BuildKind.Test, development: true, buildPlayer: true, BuildPlatform.Android);
            Assert.AreEqual(CheckSeverity.Pass, r.Severity);
            StringAssert.Contains("Development Build: ON", r.Message);
            StringAssert.DoesNotContain("TestFlight", r.Message);
        }

        [Test]
        public void Check_iOSDevelopment_NamesTestFlightInternalOnly()
        {
            var r = DevelopmentBuildCheck.Evaluate(BuildKind.Test, development: true, buildPlayer: true, BuildPlatform.iOS);
            Assert.AreEqual(CheckSeverity.Pass, r.Severity);
            StringAssert.Contains("TestFlight", r.Message);
            StringAssert.Contains("Internal Only", r.Message);
            // The development player connection makes iOS ask for local network access on launch;
            // the message must say that is expected so testers decline it instead of reporting it.
            StringAssert.Contains("local network", r.Message);
        }

        [Test]
        public void Check_NonDevelopment_PassesAndSaysOff()
        {
            StringAssert.Contains("Development Build: OFF",
                DevelopmentBuildCheck.Evaluate(BuildKind.Test, false, true, BuildPlatform.Android).Message);
            StringAssert.Contains("Development Build: OFF",
                DevelopmentBuildCheck.Evaluate(BuildKind.Release, false, true, BuildPlatform.iOS).Message);
        }

        [Test]
        public void Check_IsRegisteredInTheDefaultPreflight()
        {
            CollectionAssert.Contains(
                System.Linq.Enumerable.Select(PreflightRunner.Default(), c => c.GetType()),
                typeof(DevelopmentBuildCheck));
        }

        // --- player options ---

        private static BuildRequest Req(BuildKind kind, bool development, CompressionKind c = CompressionKind.LZ4HC) =>
            new BuildRequest { Kind = kind, Development = development, Compression = c };

        [Test]
        public void PlayerOptions_TestDevelopment_AddsOnlyTheDevelopmentFlag()
        {
            var opts = PlayerBuildStage.PlayerOptionsFor(Req(BuildKind.Test, true));
            Assert.AreEqual(BuildOptions.CompressWithLz4HC | BuildOptions.Development, opts);
            // Profiler autoconnect and script debugging stay off: both open a player connection
            // the brief rules out.
            Assert.AreEqual(BuildOptions.None, opts & BuildOptions.ConnectWithProfiler);
            Assert.AreEqual(BuildOptions.None, opts & BuildOptions.AllowDebugging);
            Assert.AreEqual(BuildOptions.None, opts & BuildOptions.WaitForPlayerConnection);
        }

        [Test]
        public void PlayerOptions_NonDevelopment_HasNoDevelopmentFlag()
        {
            Assert.AreEqual(BuildOptions.CompressWithLz4, PlayerBuildStage.PlayerOptionsFor(Req(BuildKind.Test, false, CompressionKind.LZ4)));
            Assert.AreEqual(BuildOptions.CompressWithLz4HC, PlayerBuildStage.PlayerOptionsFor(Req(BuildKind.Release, false)));
        }

        [Test]
        public void PlayerOptions_ReleaseWithDevelopment_Throws()
        {
            var e = Assert.Throws<BuildFailedException>(() => PlayerBuildStage.PlayerOptionsFor(Req(BuildKind.Release, true)));
            StringAssert.Contains("Release", e.Message);
        }

        [Test]
        public void ReportCheck_FlagsAMismatchBetweenRequestAndBuiltPlayer()
        {
            Assert.IsNull(PlayerBuildStage.DevelopmentMismatch(BuildOptions.Development | BuildOptions.CompressWithLz4HC, true));
            Assert.IsNull(PlayerBuildStage.DevelopmentMismatch(BuildOptions.CompressWithLz4HC, false));
            Assert.IsNotNull(PlayerBuildStage.DevelopmentMismatch(BuildOptions.CompressWithLz4HC, true));
            Assert.IsNotNull(PlayerBuildStage.DevelopmentMismatch(BuildOptions.Development, false));
        }

        // --- panel ---

        [Test]
        public void PanelGate_RowEnabledOnlyForTestPlayerBuilds_AndReleaseIsForcedOff()
        {
            var test = PanelDecisions.GateDevelopment(BuildKind.Test, buildPlayer: true, requested: true);
            Assert.IsTrue(test.Enabled);
            Assert.IsTrue(test.Effective);

            var release = PanelDecisions.GateDevelopment(BuildKind.Release, buildPlayer: true, requested: true);
            Assert.IsFalse(release.Enabled);
            Assert.IsFalse(release.Effective);

            var contentOnly = PanelDecisions.GateDevelopment(BuildKind.Test, buildPlayer: false, requested: true);
            Assert.IsFalse(contentOnly.Enabled);
            Assert.IsFalse(contentOnly.Effective);

            Assert.IsFalse(PanelDecisions.GateDevelopment(BuildKind.Test, true, requested: false).Effective);
        }

        // --- result ---

        [Test]
        public void Result_DevelopmentBuild_RoundTripsThroughJson_AndDefaultsFalse()
        {
            var json = JsonUtility.ToJson(new BuildResult { DevelopmentBuild = true });
            StringAssert.Contains("\"DevelopmentBuild\":true", json);
            Assert.IsTrue(JsonUtility.FromJson<BuildResult>(json).DevelopmentBuild);
            Assert.IsFalse(JsonUtility.FromJson<BuildResult>("{\"Success\":true}").DevelopmentBuild);
        }

        [Test]
        public void Result_DevelopmentBuild_IsTrueOnlyWhenAPlayerWasBuilt()
        {
            Assert.IsTrue(UnicoBuildService.ResultDevelopmentBuild(new BuildRequest { BuildPlayer = true, Development = true }));
            Assert.IsFalse(UnicoBuildService.ResultDevelopmentBuild(new BuildRequest { BuildPlayer = false, Development = true }));
            Assert.IsFalse(UnicoBuildService.ResultDevelopmentBuild(new BuildRequest { BuildPlayer = true, Development = false }));
        }
    }
}
