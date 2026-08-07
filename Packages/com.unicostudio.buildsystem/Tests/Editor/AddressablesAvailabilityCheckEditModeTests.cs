using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class AddressablesAvailabilityCheckEditModeTests
    {
        [Test] public void NotRequested_IsPass_EvenWhenUnavailable()
            => Assert.AreEqual(CheckSeverity.Pass,
                AddressablesAvailabilityCheck.Evaluate(false, false).Severity);

        [Test] public void Requested_Available_IsPass()
            => Assert.AreEqual(CheckSeverity.Pass,
                AddressablesAvailabilityCheck.Evaluate(true, true).Severity);

        [Test] public void Requested_Unavailable_IsBlock()
        {
            var r = AddressablesAvailabilityCheck.Evaluate(true, false);
            Assert.AreEqual(CheckSeverity.Block, r.Severity);
            StringAssert.Contains("com.unity.addressables", r.Message);
        }
    }
}
