using NUnit.Framework;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class BuildSystemSettingsEditModeTests
    {
        private static BuildSystemSettings Make(string name)
        {
            var s = ScriptableObject.CreateInstance<BuildSystemSettings>();
            s.TestModeDefine = name;
            return s;
        }

        [Test]
        public void Default_IsTestMode()
            => Assert.AreEqual("TEST_MODE",
                ScriptableObject.CreateInstance<BuildSystemSettings>().EffectiveTestModeDefine);

        [Test]
        public void CustomValidName_IsUsedAndTrimmed()
        {
            Assert.AreEqual("DEV_MODE", Make("DEV_MODE").EffectiveTestModeDefine);
            Assert.AreEqual("DEV_MODE", Make(" DEV_MODE ").EffectiveTestModeDefine);
        }

        [Test]
        public void BlankOrInvalid_FallsBackToDefault()
        {
            // The kind rule must survive misconfiguration — never resolve to a broken name.
            Assert.AreEqual("TEST_MODE", Make("").EffectiveTestModeDefine);
            Assert.AreEqual("TEST_MODE", Make("   ").EffectiveTestModeDefine);
            Assert.AreEqual("TEST_MODE", Make(null).EffectiveTestModeDefine);
            Assert.AreEqual("TEST_MODE", Make("9BAD").EffectiveTestModeDefine);
            Assert.AreEqual("TEST_MODE", Make("A-B").EffectiveTestModeDefine);
            Assert.AreEqual("TEST_MODE", Make("A B").EffectiveTestModeDefine);
        }

        [Test]
        public void IsValidDefine_Rules()
        {
            Assert.IsTrue(BuildSystemSettings.IsValidDefine("TEST_MODE"));
            Assert.IsTrue(BuildSystemSettings.IsValidDefine("_A1"));
            Assert.IsTrue(BuildSystemSettings.IsValidDefine(" TRIMMED "));
            Assert.IsFalse(BuildSystemSettings.IsValidDefine(null));
            Assert.IsFalse(BuildSystemSettings.IsValidDefine(""));
            Assert.IsFalse(BuildSystemSettings.IsValidDefine("1A"));
            Assert.IsFalse(BuildSystemSettings.IsValidDefine("A-B"));
            Assert.IsFalse(BuildSystemSettings.IsValidDefine("A B"));
            Assert.IsFalse(BuildSystemSettings.IsValidDefine("A;B"));
        }
    }
}
