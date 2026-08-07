using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class BuildStepRegistryEditModeTests
    {
        // Registration-rule fixtures. None carries [UnicoBuildStep]: the rules are checked through
        // the pure RejectionReason seam, because an attributed type in this assembly would be
        // discovered by TypeCache and spliced into REAL builds.
        private abstract class FakeAbstract : IBuildStage { public string Name => "abs"; public void Execute(BuildContext ctx) { } }
        private sealed class FakeGeneric<T> : IBuildStage { public string Name => "gen"; public void Execute(BuildContext ctx) { } }
        private sealed class FakePost : IPostSuccessStep { public string Name => "post"; public void Execute(BuildResult result) { } }
        private sealed class FakeNoDefaultCtor : IBuildStage
        {
            public FakeNoDefaultCtor(int _) { }
            public string Name => "noctor";
            public void Execute(BuildContext ctx) { }
        }
        private sealed class FakeThrowingCtor : IBuildStage
        {
            public FakeThrowingCtor() => throw new InvalidOperationException("ctor blew up");
            public string Name => "throws";
            public void Execute(BuildContext ctx) { }
        }

        private sealed class FakeA : IBuildStage { public string Name => "A"; public void Execute(BuildContext ctx) { } }
        private sealed class FakeB : IBuildStage { public string Name => "B"; public void Execute(BuildContext ctx) { } }
        private sealed class FakeC : IBuildStage { public string Name => "C"; public void Execute(BuildContext ctx) { } }

        private static BuildRequest Full => new() { BuildAddressables = true, BuildPlayer = true };
        private static List<BuildStepRegistry.HookEntry> Hooks(params BuildStepRegistry.HookEntry[] h) => h.ToList();
        private static BuildStepRegistry.HookEntry Hook<T>(BuildStepAnchor anchor, int order = 0) =>
            new(typeof(T), anchor, order);

        [Test]
        public void NoHooks_YieldsBuiltInPipeline()
        {
            var p = BuildStepRegistry.ResolvePipeline(Full, Hooks());
            CollectionAssert.AreEqual(new[]
            {
                typeof(ApplyVersionStage), typeof(ConfigureDefinesStage),
                typeof(AddressablesStage), typeof(PlayerBuildStage),
            }, p);
        }

        [Test]
        public void PreBuildHooks_RunFirst()
        {
            var p = BuildStepRegistry.ResolvePipeline(Full, Hooks(Hook<FakeA>(BuildStepAnchor.PreBuild)));
            Assert.AreEqual(typeof(FakeA), p[0]);
            Assert.AreEqual(typeof(ApplyVersionStage), p[1]);
        }

        [Test]
        public void BeforeAddressablesHook_SitsBetweenDefinesAndAddressables()
        {
            var p = BuildStepRegistry.ResolvePipeline(Full, Hooks(Hook<FakeA>(BuildStepAnchor.BeforeAddressables)));
            Assert.AreEqual(typeof(ConfigureDefinesStage), p[1]);
            Assert.AreEqual(typeof(FakeA), p[2]);
            Assert.AreEqual(typeof(AddressablesStage), p[3]);
        }

        [Test]
        public void AnchorsArePositional_EvenWhenOptionalStageIsOff()
        {
            var req = new BuildRequest { BuildAddressables = false, BuildPlayer = false };
            var p = BuildStepRegistry.ResolvePipeline(req, Hooks(
                Hook<FakeA>(BuildStepAnchor.BeforeAddressables), Hook<FakeB>(BuildStepAnchor.BeforePlayer)));
            CollectionAssert.AreEqual(new[]
            {
                typeof(ApplyVersionStage), typeof(ConfigureDefinesStage), typeof(FakeA), typeof(FakeB),
            }, p);
        }

        [Test]
        public void SameAnchor_OrdersByOrderThenFullName()
        {
            var p = BuildStepRegistry.ResolvePipeline(Full, Hooks(
                Hook<FakeC>(BuildStepAnchor.PreBuild, 1),
                Hook<FakeB>(BuildStepAnchor.PreBuild, 0),
                Hook<FakeA>(BuildStepAnchor.PreBuild, 1)));
            // Order 0 first; then order 1 tie broken by FullName ordinal (FakeA < FakeC).
            CollectionAssert.AreEqual(new[] { typeof(FakeB), typeof(FakeA), typeof(FakeC) }, p.Take(3));
        }

        [Test]
        public void Materialize_RoundTripsTypeNames()
        {
            // FakeA is the load-bearing entry: it lives in THIS test assembly, not the package's.
            // Type.GetType resolves a bare FullName only from mscorlib and the CALLING assembly
            // (the package), so only a foreign-assembly type can detect ToTypeNames losing the
            // assembly qualification — the persist format's whole reason to exist. Measured:
            // with FullName instead of AssemblyQualifiedName, the package-type-only round-trip
            // stayed green while resume would break for every host-assembly hook.
            var names = BuildStepRegistry.ToTypeNames(
                new[] { typeof(ApplyVersionStage), typeof(ConfigureDefinesStage), typeof(FakeA) });
            var stages = BuildStepRegistry.Materialize(names);
            Assert.IsInstanceOf<ApplyVersionStage>(stages[0]);
            Assert.IsInstanceOf<ConfigureDefinesStage>(stages[1]);
            Assert.IsInstanceOf<FakeA>(stages[2]);
        }

        [Test]
        public void Materialize_MissingType_Throws()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => BuildStepRegistry.Materialize(new[] { "No.Such.Type, NoSuchAssembly" }));
            StringAssert.Contains("No.Such.Type", ex.Message);
        }

        [Test]
        public void Materialize_NonStageType_Throws()
        {
            // typeof(object) HAS a parameterless ctor, so construction succeeds and only the
            // interface guard can throw. The old input was typeof(string), which has none:
            // Activator threw MissingMethodException first (measured live), the catch normalized
            // it to the same exception type, and the guard this test is named after was never
            // reached. The message assert is what pins the right branch.
            var ex = Assert.Throws<InvalidOperationException>(
                () => BuildStepRegistry.Materialize(new[] { typeof(object).AssemblyQualifiedName }));
            StringAssert.Contains("does not implement IBuildStage", ex.Message);
        }

        [Test]
        public void Materialize_ThrowingConstructor_ReportsTheRootCause()
        {
            // Activator wraps a throwing ctor in TargetInvocationException, whose own message says
            // nothing useful — the hook author needs THEIR exception's text.
            var ex = Assert.Throws<InvalidOperationException>(
                () => BuildStepRegistry.Materialize(new[] { typeof(FakeThrowingCtor).AssemblyQualifiedName }));
            StringAssert.Contains("ctor blew up", ex.Message);
        }

        [Test]
        public void RejectionReason_ValidStage_IsRegisterable()
            => Assert.IsNull(BuildStepRegistry.RejectionReason(typeof(FakeA), BuildStepAnchor.PreBuild));

        [Test]
        public void RejectionReason_AbstractType_IsRejected()
            => StringAssert.Contains("abstract",
                BuildStepRegistry.RejectionReason(typeof(FakeAbstract), BuildStepAnchor.PreBuild));

        [Test]
        public void RejectionReason_OpenGeneric_IsRejected()
        {
            // Passes both the interface and the ctor probe, but Activator cannot construct it —
            // undetected it would fail EVERY build at Start instead of being skipped here.
            StringAssert.Contains("generic",
                BuildStepRegistry.RejectionReason(typeof(FakeGeneric<>), BuildStepAnchor.PreBuild));
        }

        [Test]
        public void RejectionReason_NoParameterlessConstructor_IsRejected()
            => StringAssert.Contains("constructor",
                BuildStepRegistry.RejectionReason(typeof(FakeNoDefaultCtor), BuildStepAnchor.PreBuild));

        [Test]
        public void RejectionReason_AnchorInterfacePairing_IsEnforcedBothWays()
        {
            StringAssert.Contains("IPostSuccessStep",
                BuildStepRegistry.RejectionReason(typeof(FakeA), BuildStepAnchor.PostSuccess));
            StringAssert.Contains("IBuildStage",
                BuildStepRegistry.RejectionReason(typeof(FakePost), BuildStepAnchor.PreBuild));
            Assert.IsNull(BuildStepRegistry.RejectionReason(typeof(FakePost), BuildStepAnchor.PostSuccess));
        }

        [Test]
        public void Deduplicate_KeepsDistinctTypes_AndCollapsesRepeats()
        {
            // The realistic shape is one class reaching TypeCache from two assemblies (a sample
            // imported AND copied into Assets/Editor); a repeated entry is the same collapse path.
            var entries = Hooks(Hook<FakeA>(BuildStepAnchor.PreBuild), Hook<FakeB>(BuildStepAnchor.PreBuild),
                Hook<FakeA>(BuildStepAnchor.PreBuild, 5));
            var kept = BuildStepRegistry.Deduplicate(entries);

            Assert.AreEqual(2, kept.Count);
            // The FIRST occurrence must survive — Order 0, not the later duplicate's Order 5.
            var keptA = kept.Single(e => e.Type == typeof(FakeA));
            Assert.AreEqual(0, keptA.Order);
            Assert.IsTrue(kept.Any(e => e.Type == typeof(FakeB)));
        }
    }
}
