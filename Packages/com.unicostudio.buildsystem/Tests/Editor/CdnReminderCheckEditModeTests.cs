using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class CdnReminderCheckEditModeTests
    {
        [Test]
        public void ReleaseWithAddressables_Reminds()
        {
            Assert.IsTrue(CdnReminderCheck.ShouldRemind(BuildKind.Release, buildAddressables: true));
        }

        [Test]
        public void OtherCombinations_DoNotRemind()
        {
            Assert.IsFalse(CdnReminderCheck.ShouldRemind(BuildKind.Release, buildAddressables: false));
            Assert.IsFalse(CdnReminderCheck.ShouldRemind(BuildKind.Test, buildAddressables: true));
            Assert.IsFalse(CdnReminderCheck.ShouldRemind(BuildKind.Test, buildAddressables: false));
        }
    }
}
