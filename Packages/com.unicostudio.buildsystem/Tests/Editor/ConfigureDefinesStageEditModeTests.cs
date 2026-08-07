using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class ConfigureDefinesStageEditModeTests
    {
        private static BuildContext Ctx() => new(new BuildRequest());

        [Test]
        public void ApplyExtraDefines_KeepsWhatPreBuildHooksAlreadyAdded()
        {
            // The documented hook contract: a PreBuild step adds build-only defines through
            // ctx.ExtraScriptingDefines. The stage must merge into that list, never replace it.
            var ctx = Ctx();
            ctx.ExtraScriptingDefines.Add("HOOK_DEFINE");

            ConfigureDefinesStage.ApplyExtraDefines(ctx, new[] { "CONFIG_DEFINE" }, new string[0]);

            CollectionAssert.AreEqual(new[] { "HOOK_DEFINE", "CONFIG_DEFINE" }, ctx.ExtraScriptingDefines);
        }

        [Test]
        public void ApplyExtraDefines_DoesNotDuplicateAnExistingDefine()
        {
            var ctx = Ctx();
            ctx.ExtraScriptingDefines.Add("CONFIG_DEFINE");

            ConfigureDefinesStage.ApplyExtraDefines(ctx, new[] { "CONFIG_DEFINE" }, new string[0]);

            CollectionAssert.AreEqual(new[] { "CONFIG_DEFINE" }, ctx.ExtraScriptingDefines);
        }

        [Test]
        public void ApplyExtraDefines_EmptyAddition_LeavesTheListUntouched()
        {
            var ctx = Ctx();
            ctx.ExtraScriptingDefines.Add("HOOK_DEFINE");

            ConfigureDefinesStage.ApplyExtraDefines(ctx, new string[0], new string[0]);

            CollectionAssert.AreEqual(new[] { "HOOK_DEFINE" }, ctx.ExtraScriptingDefines);
        }

        [Test]
        public void ApplyExtraDefines_EvictsAHookAddedDefineThisBuildStrips()
        {
            // A PreBuild hook added TEST_MODE (or any StripDefines entry) to ExtraScriptingDefines;
            // this build's resolved delta strips it. Without eviction it would ship straight through
            // BuildPlayerOptions.extraScriptingDefines, defeating the kind rule / StripDefines.
            var ctx = Ctx();
            ctx.ExtraScriptingDefines.Add("TEST_MODE");
            ctx.ExtraScriptingDefines.Add("HOOK_DEFINE");

            var evicted = ConfigureDefinesStage.ApplyExtraDefines(ctx, new string[0], new[] { "TEST_MODE" });

            CollectionAssert.AreEqual(new[] { "TEST_MODE" }, evicted);
            CollectionAssert.AreEqual(new[] { "HOOK_DEFINE" }, ctx.ExtraScriptingDefines);
        }

        [Test]
        public void Eviction_DrivenByTheResolvedDelta_WorksWhenTheDefineIsNotGlobal()
        {
            // The composition the two pure cores must survive TOGETHER, driven exactly like the
            // stage drives them (Execute:23,31). Platform globals WITHOUT the test define (this
            // repo's committed iOS state), Release build, a PreBuild hook added TEST_MODE.
            // RemoveFromGlobal is empty here — feeding it to ApplyExtraDefines (the old wiring)
            // evicts nothing and the hook's define ships in the player.
            var ctx = Ctx();
            ctx.ExtraScriptingDefines.Add("TEST_MODE");
            ctx.ExtraScriptingDefines.Add("HOOK_DEFINE");

            var delta = BuildDefineResolver.Resolve(BuildKind.Release,
                new[] { "ADDRESSABLES_ENABLED", "ODIN_INSPECTOR" },
                extraDefines: null, stripDefines: null, testModeDefine: "TEST_MODE");
            var evicted = ConfigureDefinesStage.ApplyExtraDefines(ctx, delta.AddViaExtra, delta.ForbidInPlayer);

            CollectionAssert.AreEqual(new[] { "TEST_MODE" }, evicted);
            CollectionAssert.AreEqual(new[] { "HOOK_DEFINE" }, ctx.ExtraScriptingDefines);
        }

        // --- NextGlobalDefines: the single global write shared by the kind rule and StripDefines ---

        private static readonly string[] Current = { "DOTWEEN", "UNITY_MCP_READY" };
        private static readonly string[] Nothing = new string[0];

        [Test]
        public void NextGlobalDefines_AddsTheKindRuleDefine_PreservingOrder()
        {
            Assert.AreEqual("DOTWEEN;UNITY_MCP_READY;TEST_MODE",
                ConfigureDefinesStage.NextGlobalDefines(Current, new[] { "TEST_MODE" }, Nothing));
        }

        [Test]
        public void NextGlobalDefines_RemovesStrippedDefines()
        {
            Assert.AreEqual("DOTWEEN",
                ConfigureDefinesStage.NextGlobalDefines(Current, Nothing, new[] { "UNITY_MCP_READY" }));
        }

        [Test]
        public void NextGlobalDefines_AddAndRemove_ShareOneWrite()
        {
            // A Test build for a target that strips UNITY_MCP_READY: both directions land in the
            // same string, so the pipeline pays exactly one recompile + reload, not two.
            Assert.AreEqual("DOTWEEN;TEST_MODE",
                ConfigureDefinesStage.NextGlobalDefines(Current, new[] { "TEST_MODE" }, new[] { "UNITY_MCP_READY" }));
        }

        [Test]
        public void NextGlobalDefines_EmptyDelta_ReproducesTheCurrentSet()
        {
            // The caller compares against string.Join(";", current) and skips the write when they
            // match — this is what keeps an already-correct Test build reload-free.
            Assert.AreEqual(string.Join(";", Current),
                ConfigureDefinesStage.NextGlobalDefines(Current, Nothing, Nothing));
        }

        [Test]
        public void NextGlobalDefines_NeverDuplicatesADefineThatIsAlreadyThere()
        {
            Assert.AreEqual("DOTWEEN;UNITY_MCP_READY",
                ConfigureDefinesStage.NextGlobalDefines(Current, new[] { "DOTWEEN" }, Nothing));
        }

        [Test]
        public void NextGlobalDefines_EmptyResult_IsAnEmptyString()
        {
            // PlayerSettings takes "" for "no defines"; a stray ";" would parse as a blank symbol.
            Assert.AreEqual("", ConfigureDefinesStage.NextGlobalDefines(
                new[] { "ONLY" }, Nothing, new[] { "ONLY" }));
        }
    }
}
