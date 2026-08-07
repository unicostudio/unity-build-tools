using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class StageSelectionCheckEditModeTests
    {
        private static CheckSeverity Severity(bool addressables, bool player)
        {
            var req = new BuildRequest { BuildAddressables = addressables, BuildPlayer = player };
            return new StageSelectionCheck().Run(new BuildContext(req)).Severity;
        }

        [Test]
        public void NoStageSelected_Blocks()
        {
            Assert.AreEqual(CheckSeverity.Block, Severity(addressables: false, player: false));
        }

        [Test]
        public void AnyStageSelected_Passes()
        {
            Assert.AreEqual(CheckSeverity.Pass, Severity(addressables: true, player: false));
            Assert.AreEqual(CheckSeverity.Pass, Severity(addressables: false, player: true));
            Assert.AreEqual(CheckSeverity.Pass, Severity(addressables: true, player: true));
        }
    }
}
