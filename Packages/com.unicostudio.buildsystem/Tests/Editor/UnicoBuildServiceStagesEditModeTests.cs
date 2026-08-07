using System.Collections.Generic;
using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    // The resume machinery persists the resolved pipeline and indexes into it by StepIndex —
    // the built-in order and toggle behavior are load-bearing, not cosmetic. Uses the hook-free
    // registry core so host-project [UnicoBuildStep] types can never alter these assertions.
    public sealed class UnicoBuildServiceStagesEditModeTests
    {
        private static List<System.Type> Pipeline(bool addressables, bool player) =>
            BuildStepRegistry.ResolvePipeline(
                new BuildRequest { BuildAddressables = addressables, BuildPlayer = player },
                new List<BuildStepRegistry.HookEntry>());

        [Test]
        public void FullRequest_RunsAllStagesInOrder()
        {
            CollectionAssert.AreEqual(new[]
            {
                typeof(ApplyVersionStage), typeof(ConfigureDefinesStage),
                typeof(AddressablesStage), typeof(PlayerBuildStage),
            }, Pipeline(addressables: true, player: true));
        }

        [Test]
        public void AddressablesOnly_SkipsPlayerStage()
        {
            var p = Pipeline(addressables: true, player: false);
            Assert.AreEqual(3, p.Count);
            Assert.AreEqual(typeof(AddressablesStage), p[2]);
        }

        [Test]
        public void PlayerOnly_SkipsAddressablesStage()
        {
            var p = Pipeline(addressables: false, player: true);
            Assert.AreEqual(3, p.Count);
            Assert.AreEqual(typeof(PlayerBuildStage), p[2]);
        }

        [Test]
        public void NoOptionalStages_KeepsVersionAndDefines()
        {
            // StageSelectionCheck blocks this request, but the factory must still behave sanely.
            var p = Pipeline(addressables: false, player: false);
            CollectionAssert.AreEqual(new[]
            {
                typeof(ApplyVersionStage), typeof(ConfigureDefinesStage),
            }, p);
        }
    }
}
