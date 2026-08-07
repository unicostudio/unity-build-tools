using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class BuildVersioningEditModeTests
    {
        [Test] public void NextAndroidCode_Increments() => Assert.AreEqual(18, BuildVersioning.NextAndroidCode(17));

        [Test] public void NextIos_Numeric() => Assert.AreEqual("6", BuildVersioning.NextIosBuildNumber("5"));

        [Test] public void NextIos_Empty_DefaultsToOne() => Assert.AreEqual("1", BuildVersioning.NextIosBuildNumber(""));

        [Test] public void NextIos_NonNumeric_DefaultsToOne() => Assert.AreEqual("1", BuildVersioning.NextIosBuildNumber("abc"));
    }
}
