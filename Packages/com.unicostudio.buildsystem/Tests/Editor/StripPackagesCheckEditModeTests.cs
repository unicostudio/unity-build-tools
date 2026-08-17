using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    /// <summary>
    /// Preflight for StripPackages (spec: docs/specs/2026-08-17-strippackages-design.md),
    /// pure core driven with fixture manifest/lock text:
    ///   - listed but absent from the manifest -> Pass (per-package no-op, noted)
    ///   - listed but not exact-pinned -> Warn (byte-deterministic restore holds only
    ///     for exact pins; strict runs block)
    ///   - listed with dependents in the lock -> Block (dependency-graph inversion is
    ///     out of scope; removing the package would break its dependents mid-build)
    /// </summary>
    public class StripPackagesCheckEditModeTests
    {
        private const string Manifest =
            "{\n  \"dependencies\": {\n" +
            "    \"com.pinned.pkg\": \"1.2.3\",\n" +
            "    \"com.floating.pkg\": \"^2.0.0\",\n" +
            "    \"com.parent.pkg\": \"3.0.0\"\n" +
            "  }\n}\n";

        private const string Lock =
            "{\n  \"dependencies\": {\n" +
            "    \"com.pinned.pkg\": { \"version\": \"1.2.3\", \"dependencies\": {} },\n" +
            "    \"com.floating.pkg\": { \"version\": \"2.1.0\", \"dependencies\": {} },\n" +
            "    \"com.parent.pkg\": { \"version\": \"3.0.0\", \"dependencies\": { \"com.pinned.pkg\": \"1.2.3\" } }\n" +
            "  }\n}\n";

        private static CheckResult Eval(params string[] strip)
            => StripPackagesCheck.Evaluate(strip, Manifest, Lock);

        [Test]
        public void NothingListed_Passes()
        {
            Assert.AreEqual(CheckSeverity.Pass, Eval().Severity);
        }

        [Test]
        public void ListedButAbsent_Passes_AndNamesTheNoOp()
        {
            var result = Eval("com.not.installed");

            Assert.AreEqual(CheckSeverity.Pass, result.Severity);
            StringAssert.Contains("com.not.installed", result.Message);
        }

        [Test]
        public void FloatingPin_Warns_NamingThePackage()
        {
            var result = Eval("com.floating.pkg");

            Assert.AreEqual(CheckSeverity.Warn, result.Severity);
            StringAssert.Contains("com.floating.pkg", result.Message);
            StringAssert.Contains("exact", result.Message);
        }

        [Test]
        public void DependentsInLock_Blocks_NamingThePackage()
        {
            var result = Eval("com.pinned.pkg"); // com.parent.pkg depends on it

            Assert.AreEqual(CheckSeverity.Block, result.Severity);
            StringAssert.Contains("com.pinned.pkg", result.Message);
        }

        [Test]
        public void ExactPinned_NoDependents_Passes()
        {
            var result = Eval("com.parent.pkg");

            Assert.AreEqual(CheckSeverity.Pass, result.Severity);
        }

        [Test]
        public void BlockOutranksWarn_WhenBothPresent()
        {
            var result = Eval("com.floating.pkg", "com.pinned.pkg");

            Assert.AreEqual(CheckSeverity.Block, result.Severity);
        }
    }
}
