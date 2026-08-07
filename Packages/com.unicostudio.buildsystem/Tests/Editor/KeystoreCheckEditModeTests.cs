using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class KeystoreCheckEditModeTests
    {
        [Test]
        public void NonAndroidReleasePlayerBuilds_AreNotApplicable()
        {
            Assert.AreEqual(CheckSeverity.Pass, KeystoreCheck.Evaluate(
                BuildPlatform.iOS, BuildKind.Release, buildPlayer: true,
                useCustomKeystore: false, keystoreFileExists: false, passwordsSet: false).Severity);
            Assert.AreEqual(CheckSeverity.Pass, KeystoreCheck.Evaluate(
                BuildPlatform.Android, BuildKind.Test, buildPlayer: true,
                useCustomKeystore: false, keystoreFileExists: false, passwordsSet: false).Severity);
            Assert.AreEqual(CheckSeverity.Pass, KeystoreCheck.Evaluate(
                BuildPlatform.Android, BuildKind.Release, buildPlayer: false,
                useCustomKeystore: false, keystoreFileExists: false, passwordsSet: false).Severity);
        }

        [Test]
        public void AndroidRelease_MissingPieces_Block()
        {
            Assert.AreEqual(CheckSeverity.Block, KeystoreCheck.Evaluate(
                BuildPlatform.Android, BuildKind.Release, buildPlayer: true,
                useCustomKeystore: false, keystoreFileExists: true, passwordsSet: true).Severity);
            Assert.AreEqual(CheckSeverity.Block, KeystoreCheck.Evaluate(
                BuildPlatform.Android, BuildKind.Release, buildPlayer: true,
                useCustomKeystore: true, keystoreFileExists: false, passwordsSet: true).Severity);
            Assert.AreEqual(CheckSeverity.Block, KeystoreCheck.Evaluate(
                BuildPlatform.Android, BuildKind.Release, buildPlayer: true,
                useCustomKeystore: true, keystoreFileExists: true, passwordsSet: false).Severity);
        }

        [Test]
        public void AndroidRelease_FullyConfigured_Passes()
        {
            Assert.AreEqual(CheckSeverity.Pass, KeystoreCheck.Evaluate(
                BuildPlatform.Android, BuildKind.Release, buildPlayer: true,
                useCustomKeystore: true, keystoreFileExists: true, passwordsSet: true).Severity);
        }

        [Test]
        public void KeystoreFileExists_HandlesInProjectNotationAndBlanks()
        {
            // Any committed file works as a stand-in for the keystore path checks.
            const string existing = "ProjectSettings/ProjectSettings.asset";
            Assert.IsTrue(KeystoreCheck.KeystoreFileExists(existing));
            Assert.IsTrue(KeystoreCheck.KeystoreFileExists("{inproject}: " + existing));
            Assert.IsFalse(KeystoreCheck.KeystoreFileExists("{inproject}: Missing.keystore"));
            Assert.IsFalse(KeystoreCheck.KeystoreFileExists(""));
            Assert.IsFalse(KeystoreCheck.KeystoreFileExists(null));
        }
    }
}
