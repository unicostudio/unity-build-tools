using NUnit.Framework;
using UnityEngine;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    /// <summary>
    /// Preflight visibility for the config-discovery fallback, pure core driven with
    /// in-memory configs:
    ///   - zero BuildTargetConfig assets -> Warn naming the Create-menu path (a config-less
    ///     project builds "successfully" with no ExtraDefines/StripDefines/StripPackages —
    ///     invisible until this check said so)
    ///   - configs exist but none matches the requested platform+kind -> Warn naming the
    ///     miss and what IS present
    ///   - a match -> Pass naming the asset, so the panel shows WHICH config the build
    ///     resolved instead of leaving the fallback indistinguishable from a config run
    /// The check is advisory by design (IsStrictExempt): it must never make a strict CI
    /// build of a deliberately config-less project impossible.
    /// </summary>
    public sealed class ConfigPresenceCheckEditModeTests
    {
        private static BuildTargetConfig Make(BuildPlatform p, BuildKind k, string name)
        {
            var c = ScriptableObject.CreateInstance<BuildTargetConfig>();
            c.Platform = p; c.Kind = k; c.name = name;
            return c;
        }

        [Test]
        public void NoConfigs_Warns_NamingTheCreateMenuPath()
        {
            var result = ConfigPresenceCheck.Evaluate(
                BuildPlatform.Android, BuildKind.Test, new BuildTargetConfig[0]);

            Assert.AreEqual(CheckSeverity.Warn, result.Severity);
            StringAssert.Contains("Unico/Build/Build Target Config", result.Message);
        }

        [Test]
        public void NullList_IsTreatedAsEmpty_AndWarns()
        {
            var result = ConfigPresenceCheck.Evaluate(
                BuildPlatform.Android, BuildKind.Test, null);

            Assert.AreEqual(CheckSeverity.Warn, result.Severity);
        }

        [Test]
        public void NoMatch_Warns_NamingTheRequestedPairAndThePresentConfigs()
        {
            var result = ConfigPresenceCheck.Evaluate(
                BuildPlatform.Android, BuildKind.Test,
                new[] { Make(BuildPlatform.Android, BuildKind.Release, "Cfg_Android_Release") });

            Assert.AreEqual(CheckSeverity.Warn, result.Severity);
            StringAssert.Contains("Android/Test", result.Message);
            StringAssert.Contains("Cfg_Android_Release", result.Message);
        }

        [Test]
        public void SameKindOtherPlatformOnly_IsNotAMatch()
        {
            var result = ConfigPresenceCheck.Evaluate(
                BuildPlatform.Android, BuildKind.Test,
                new[] { Make(BuildPlatform.iOS, BuildKind.Test, "Cfg_iOS_Test") });

            Assert.AreEqual(CheckSeverity.Warn, result.Severity);
        }

        [Test]
        public void Match_Passes_NamingTheResolvedAsset()
        {
            var result = ConfigPresenceCheck.Evaluate(
                BuildPlatform.iOS, BuildKind.Release,
                new[]
                {
                    Make(BuildPlatform.Android, BuildKind.Release, "Cfg_Android_Release"),
                    Make(BuildPlatform.iOS, BuildKind.Release, "Cfg_iOS_Release"),
                });

            Assert.AreEqual(CheckSeverity.Pass, result.Severity);
            StringAssert.Contains("Cfg_iOS_Release", result.Message);
        }

        [Test]
        public void NullEntries_AreIgnored_NotMatchedAndNotCrashedOn()
        {
            var result = ConfigPresenceCheck.Evaluate(
                BuildPlatform.Android, BuildKind.Test,
                new[] { null, Make(BuildPlatform.Android, BuildKind.Test, "Cfg_Android_Test") });

            Assert.AreEqual(CheckSeverity.Pass, result.Severity);
            StringAssert.Contains("Cfg_Android_Test", result.Message);
        }
    }
}
