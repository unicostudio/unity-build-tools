using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class BumpConsistencyCheckEditModeTests
    {
        private static CheckSeverity Severity(bool bumpAddressables, bool buildAddressables)
        {
            var req = new BuildRequest
            {
                BumpAddressablesVersion = bumpAddressables,
                BuildAddressables = buildAddressables,
            };
            return new BumpConsistencyCheck().Run(new BuildContext(req)).Severity;
        }

        [Test]
        public void BumpWithoutAddressablesBuild_Warns()
        {
            Assert.AreEqual(CheckSeverity.Warn, Severity(bumpAddressables: true, buildAddressables: false));
        }

        [Test]
        public void ConsistentCombinations_Pass()
        {
            Assert.AreEqual(CheckSeverity.Pass, Severity(bumpAddressables: true, buildAddressables: true));
            Assert.AreEqual(CheckSeverity.Pass, Severity(bumpAddressables: false, buildAddressables: false));
            Assert.AreEqual(CheckSeverity.Pass, Severity(bumpAddressables: false, buildAddressables: true));
        }
    }
}
