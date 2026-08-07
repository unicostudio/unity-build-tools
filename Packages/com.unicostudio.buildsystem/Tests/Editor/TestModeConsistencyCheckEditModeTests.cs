using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class TestModeConsistencyCheckEditModeTests
    {
        private const string TM = "TEST_MODE";

        [Test] public void Test_On_IsPass()
            => Assert.AreEqual(CheckSeverity.Pass, TestModeConsistencyCheck.Evaluate(BuildKind.Test, true, TM).Severity);

        [Test] public void Test_Off_IsWarn()
            => Assert.AreEqual(CheckSeverity.Warn, TestModeConsistencyCheck.Evaluate(BuildKind.Test, false, TM).Severity);

        [Test] public void Release_Off_IsPass()
            => Assert.AreEqual(CheckSeverity.Pass, TestModeConsistencyCheck.Evaluate(BuildKind.Release, false, TM).Severity);

        [Test] public void Release_On_IsWarn()
            => Assert.AreEqual(CheckSeverity.Warn, TestModeConsistencyCheck.Evaluate(BuildKind.Release, true, TM).Severity);

        [Test] public void Message_UsesConfiguredDefineName()
            => StringAssert.Contains("DEV_MODE",
                TestModeConsistencyCheck.Evaluate(BuildKind.Release, true, "DEV_MODE").Message);
    }
}
