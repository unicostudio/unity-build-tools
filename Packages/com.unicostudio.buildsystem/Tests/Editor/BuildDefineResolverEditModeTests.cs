using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class BuildDefineResolverEditModeTests
    {
        private const string TM = "TEST_MODE";
        private static readonly string[] WithTest = { "DOTWEEN", "TEST_MODE", "UNITY_MCP_READY" };
        private static readonly string[] WithoutTest = { "DOTWEEN", "UNITY_MCP_READY" };

        private static DefineDelta Resolve(BuildKind kind, string[] global,
            string[] extra = null, string[] strip = null, string testDefine = TM) =>
            BuildDefineResolver.Resolve(kind, global, extra, strip, testDefine);

        // --- kind rule (v0.1.0 behavior, config lists empty) ---

        [Test]
        public void Test_WithGlobalTestMode_NoChange()
        {
            var d = Resolve(BuildKind.Test, WithTest);
            Assert.IsEmpty(d.AddViaExtra);
            Assert.IsEmpty(d.AddToGlobal);
            Assert.IsEmpty(d.RemoveFromGlobal);
        }

        [Test]
        public void Release_RemovesTestModeFromGlobal()
        {
            var d = Resolve(BuildKind.Release, WithTest);
            Assert.IsEmpty(d.AddViaExtra);
            Assert.IsEmpty(d.AddToGlobal);
            Assert.AreEqual(new[] { TM }, d.RemoveFromGlobal);
        }

        [Test]
        public void Test_WithoutGlobalTestMode_AddsToGlobal_NeverBuildOnly()
        {
            // The whole point of the editor-wide kind rule: editor assemblies compile with the
            // active target's GLOBAL defines, so a build-only add (extraScriptingDefines) would
            // leave every editor-side build participant — plist/manifest post-processors, host
            // build steps, the Addressables content build — compiled for the opposite kind.
            var d = Resolve(BuildKind.Test, WithoutTest);
            Assert.AreEqual(new[] { TM }, d.AddToGlobal);
            Assert.IsEmpty(d.AddViaExtra);
            Assert.IsEmpty(d.RemoveFromGlobal);
        }

        [Test]
        public void Release_GlobalAlreadyClean_NoChange()
        {
            var d = Resolve(BuildKind.Release, WithoutTest);
            Assert.IsEmpty(d.AddViaExtra);
            Assert.IsEmpty(d.AddToGlobal);
            Assert.IsEmpty(d.RemoveFromGlobal);
        }

        [Test]
        public void GlobalAddAndRemove_NeverOverlap_AndTheKindRulePicksTheDirection()
        {
            // The old name claimed the two buckets are "never both populated" — false for this
            // test's own passing data: (Test, WithoutTest) yields AddToGlobal=[TEST_MODE] and
            // RemoveFromGlobal=[UNITY_MCP_READY] at once, which is FINE (the one global write
            // applies removals then additions). The real invariants: the SAME define never sits in
            // both buckets (write order would decide the outcome), and the kind rule only ever
            // feeds its define in the direction the kind demands.
            foreach (var kind in new[] { BuildKind.Test, BuildKind.Release })
            foreach (var global in new[] { WithTest, WithoutTest })
            {
                var d = Resolve(kind, global, extra: new[] { "FOO" }, strip: new[] { "UNITY_MCP_READY", TM });
                var label = $"{kind} on [{string.Join(",", global)}]";
                Assert.IsEmpty(d.AddToGlobal.Intersect(d.RemoveFromGlobal),
                    $"{label}: the same define landed in both global buckets.");
                if (kind == BuildKind.Test)
                    CollectionAssert.DoesNotContain(d.RemoveFromGlobal, TM,
                        $"{label}: a Test build removing its own define.");
                else
                    Assert.IsEmpty(d.AddToGlobal,
                        $"{label}: only the kind rule populates AddToGlobal, and Release never adds.");
            }
        }

        // --- config lists ---

        [Test]
        public void ExtraDefine_AbsentFromGlobal_IsAddedBuildOnly()
        {
            var d = Resolve(BuildKind.Test, WithTest, extra: new[] { "FOO" });
            Assert.AreEqual(new[] { "FOO" }, d.AddViaExtra);
            Assert.IsEmpty(d.AddToGlobal);
            Assert.IsEmpty(d.RemoveFromGlobal);
        }

        [Test]
        public void ExtraDefine_StaysBuildOnly_EvenWhenTheKindRuleGoesGlobal()
        {
            // The kind rule's global promotion is about the BUILD'S INTENT, not about defines in
            // general: ExtraDefines keep their documented player-only contract, so a config entry
            // must never be dragged into the global write (and its reload) alongside the kind rule.
            var d = Resolve(BuildKind.Test, WithoutTest, extra: new[] { "FOO" });
            Assert.AreEqual(new[] { "FOO" }, d.AddViaExtra);
            Assert.AreEqual(new[] { TM }, d.AddToGlobal);
        }

        [Test]
        public void ExtraDefine_AlreadyGlobal_IsNotDuplicated()
        {
            var d = Resolve(BuildKind.Test, WithTest, extra: new[] { "DOTWEEN" });
            Assert.IsEmpty(d.AddViaExtra);
        }

        [Test]
        public void StripDefine_PresentInGlobal_IsRemoved()
        {
            var d = Resolve(BuildKind.Test, WithTest, strip: new[] { "UNITY_MCP_READY" });
            Assert.IsEmpty(d.AddViaExtra);
            Assert.AreEqual(new[] { "UNITY_MCP_READY" }, d.RemoveFromGlobal);
        }

        [Test]
        public void StripDefine_AbsentFromGlobal_IsIgnored()
        {
            var d = Resolve(BuildKind.Test, WithTest, strip: new[] { "NOT_THERE" });
            Assert.IsEmpty(d.RemoveFromGlobal);
        }

        [Test]
        public void ReleaseStrip_MergesWithTestModeRemoval()
        {
            var d = Resolve(BuildKind.Release, WithTest, strip: new[] { "UNITY_MCP_READY" });
            Assert.IsEmpty(d.AddViaExtra);
            Assert.AreEqual(new[] { "UNITY_MCP_READY", TM }, d.RemoveFromGlobal);
        }

        [Test]
        public void TestModeDefine_InConfigLists_IsIgnored()
        {
            // The kind rule stays the sole authority: config entries naming the test-mode
            // define must never add it to a Release build or strip it from a Test build.
            var release = Resolve(BuildKind.Release, WithoutTest,
                extra: new[] { TM }, strip: new[] { TM });
            Assert.IsEmpty(release.AddViaExtra);
            Assert.IsEmpty(release.AddToGlobal);
            Assert.IsEmpty(release.RemoveFromGlobal);

            var test = Resolve(BuildKind.Test, WithTest, strip: new[] { TM });
            Assert.IsEmpty(test.RemoveFromGlobal);

            // An ExtraDefines entry naming it cannot downgrade the kind rule to a player-only add
            // either: the entry is dropped and the rule still writes the define globally.
            var testAdd = Resolve(BuildKind.Test, WithoutTest, extra: new[] { TM });
            Assert.IsEmpty(testAdd.AddViaExtra);
            Assert.AreEqual(new[] { TM }, testAdd.AddToGlobal);
        }

        [Test]
        public void OverlappingDefine_IsNeverAdded()
        {
            var d = Resolve(BuildKind.Test, WithTest,
                extra: new[] { "FOO" }, strip: new[] { "FOO" });
            Assert.IsEmpty(d.AddViaExtra);
            Assert.IsEmpty(d.RemoveFromGlobal);
        }

        [Test]
        public void InvalidAndBlankTokens_AreDroppedDefensively()
        {
            var d = Resolve(BuildKind.Test, WithTest,
                extra: new[] { "9BAD", "", "  ", null, "A-B", "OK_1" });
            Assert.AreEqual(new[] { "OK_1" }, d.AddViaExtra);
        }

        [Test]
        public void DuplicateConfigEntries_CollapseToOne()
        {
            var d = Resolve(BuildKind.Test, WithTest, extra: new[] { "FOO", "FOO", " FOO " });
            Assert.AreEqual(new[] { "FOO" }, d.AddViaExtra);
        }

        // --- ForbidInPlayer: the player-list eviction set, independent of current globals ---

        [Test]
        public void StripDefine_AbsentFromGlobal_IsStillForbiddenInPlayer()
        {
            // RemoveFromGlobal is rightly empty here (nothing to remove) — but the define must
            // still be evicted from ExtraScriptingDefines, or a PreBuild hook's copy would be the
            // ONE copy and ship straight through BuildPlayerOptions.extraScriptingDefines.
            var d = Resolve(BuildKind.Test, WithTest, strip: new[] { "NOT_THERE" });
            Assert.IsEmpty(d.RemoveFromGlobal);
            Assert.AreEqual(new[] { "NOT_THERE" }, d.ForbidInPlayer);
        }

        [Test]
        public void Release_ForbidsTestDefineInPlayer_EvenWhenNotGlobal()
        {
            // The exact iOS gap: TEST_MODE is not in the platform's globals, so the kind rule has
            // nothing to remove — but a Release player must still never receive it build-only.
            var d = Resolve(BuildKind.Release, WithoutTest);
            Assert.IsEmpty(d.RemoveFromGlobal);
            Assert.AreEqual(new[] { TM }, d.ForbidInPlayer);
        }

        [Test]
        public void TestBuild_DoesNotForbidTheTestDefine_ButStillForbidsStripEntries()
        {
            var d = Resolve(BuildKind.Test, WithoutTest, strip: new[] { "NOT_THERE" });
            CollectionAssert.DoesNotContain(d.ForbidInPlayer, TM);
            Assert.AreEqual(new[] { "NOT_THERE" }, d.ForbidInPlayer);
        }

        [Test]
        public void CustomTestModeDefine_DrivesKindRule()
        {
            var release = Resolve(BuildKind.Release, new[] { "DEV_MODE", "DOTWEEN" },
                testDefine: "DEV_MODE");
            Assert.AreEqual(new[] { "DEV_MODE" }, release.RemoveFromGlobal);

            var test = Resolve(BuildKind.Test, new[] { "DOTWEEN" }, testDefine: "DEV_MODE");
            Assert.AreEqual(new[] { "DEV_MODE" }, test.AddToGlobal);

            // With a custom name, TEST_MODE is an ordinary define — strippable via config.
            var strip = Resolve(BuildKind.Release, new[] { "TEST_MODE" },
                strip: new[] { "TEST_MODE" }, testDefine: "DEV_MODE");
            Assert.AreEqual(new[] { "TEST_MODE" }, strip.RemoveFromGlobal);
        }

        // --- ValidateConfig ---

        [Test]
        public void ValidateConfig_CleanLists_NoIssues()
        {
            var issues = BuildDefineResolver.ValidateConfig(
                new[] { "FOO", "" }, new[] { "UNITY_MCP_READY" }, TM);
            Assert.IsEmpty(issues);
        }

        [Test]
        public void ValidateConfig_InvalidToken_IsBlocking()
        {
            var issues = BuildDefineResolver.ValidateConfig(new[] { "9BAD" }, null, TM);
            Assert.AreEqual(1, issues.Count);
            Assert.IsTrue(issues[0].Blocking);
            StringAssert.Contains("9BAD", issues[0].Message);
        }

        [Test]
        public void ValidateConfig_OverlapAcrossLists_IsBlocking()
        {
            var issues = BuildDefineResolver.ValidateConfig(new[] { "FOO" }, new[] { "FOO" }, TM);
            Assert.AreEqual(1, issues.Count);
            Assert.IsTrue(issues[0].Blocking);
            StringAssert.Contains("both", issues[0].Message);
        }

        [Test]
        public void ValidateConfig_TestModeEntry_IsAdvisory()
        {
            var issues = BuildDefineResolver.ValidateConfig(new[] { TM }, new[] { TM }, TM);
            Assert.AreEqual(2, issues.Count);
            Assert.IsFalse(issues.Any(i => i.Blocking));
        }

        // ValidateConfig walks each list twice (token issues, then the intersection), so it must
        // materialize them first. A single-pass source is empty on the second walk, which would
        // silently drop the both-lists finding — the one issue no other check would catch.
        [Test]
        public void ValidateConfig_SinglePassSources_StillFindsOverlap()
        {
            var issues = BuildDefineResolver.ValidateConfig(SinglePass("FOO"), SinglePass("FOO"), TM);
            Assert.AreEqual(1, issues.Count);
            Assert.IsTrue(issues[0].Blocking);
            StringAssert.Contains("both", issues[0].Message);
        }

        // Yields its values on the FIRST enumeration only; every later walk sees an empty sequence.
        private static IEnumerable<string> SinglePass(params string[] values)
        {
            var spent = false;

            IEnumerable<string> Once()
            {
                if (spent) yield break;
                spent = true;
                foreach (var v in values) yield return v;
            }

            return Once();
        }

        // --- development-only defines (BuildSystemSettings.DevelopmentOnlyDefines) ---

        private const string DevLog = "SDK_TEST_LOG";
        private static readonly string[] DevList = { DevLog };

        private static DefineDelta ResolveDev(BuildKind kind, bool development, string[] global,
            string[] extra = null, string[] strip = null) =>
            BuildDefineResolver.Resolve(kind, global, extra, strip, TM, development, DevList);

        [Test]
        public void DevelopmentBuild_AddsDevOnlyDefine_PlayerOnly()
        {
            var d = ResolveDev(BuildKind.Test, true, WithTest);
            CollectionAssert.AreEqual(new[] { DevLog }, d.AddViaExtra);
            Assert.IsEmpty(d.AddToGlobal, "player-only: no global write, no reload");
            CollectionAssert.DoesNotContain(d.ForbidInPlayer, DevLog);
        }

        [Test]
        public void DevelopmentBuild_DevOnlyDefineAlreadyGlobal_NoChange()
        {
            var d = ResolveDev(BuildKind.Test, true, new[] { "TEST_MODE", DevLog });
            Assert.IsEmpty(d.AddViaExtra);
            Assert.IsEmpty(d.RemoveFromGlobal);
        }

        [Test]
        public void NonDevelopmentTest_ForbidsDevOnlyDefine_AndRemovesItFromGlobals()
        {
            var d = ResolveDev(BuildKind.Test, false, new[] { "TEST_MODE", DevLog });
            CollectionAssert.Contains(d.ForbidInPlayer, DevLog);
            CollectionAssert.AreEqual(new[] { DevLog }, d.RemoveFromGlobal);
            CollectionAssert.DoesNotContain(d.AddViaExtra, DevLog);
        }

        [Test]
        public void Release_ForbidsDevOnlyDefine_EvenWhenNotGlobal()
        {
            // ForbidInPlayer must not depend on the current globals: a hook's copy in the
            // player-only list would otherwise be the one copy that ships.
            var d = ResolveDev(BuildKind.Release, false, WithoutTest);
            CollectionAssert.Contains(d.ForbidInPlayer, DevLog);
            CollectionAssert.DoesNotContain(d.RemoveFromGlobal, DevLog);
        }

        [Test]
        public void ConfigListsNamingADevOnlyDefine_AreIgnored_TheDevelopmentRuleWins()
        {
            var nonDev = ResolveDev(BuildKind.Test, false, WithTest, extra: new[] { DevLog });
            CollectionAssert.DoesNotContain(nonDev.AddViaExtra, DevLog);
            CollectionAssert.Contains(nonDev.ForbidInPlayer, DevLog);

            var dev = ResolveDev(BuildKind.Test, true, WithTest, strip: new[] { DevLog });
            CollectionAssert.Contains(dev.AddViaExtra, DevLog);
            CollectionAssert.DoesNotContain(dev.ForbidInPlayer, DevLog);
        }

        [Test]
        public void DevOnlyListNamingTheTestModeDefine_IsIgnored_TheKindRuleWins()
        {
            var d = BuildDefineResolver.Resolve(BuildKind.Test, WithTest, null, null, TM, false, new[] { TM });
            CollectionAssert.DoesNotContain(d.ForbidInPlayer, TM);
            Assert.IsEmpty(d.RemoveFromGlobal);
        }

        [Test]
        public void ValidateConfig_DevOnlyList_ReportsInvalidTokenAndTestModeAsBlocking()
        {
            var issues = BuildDefineResolver.ValidateConfig(null, null, TM, new[] { "BAD-TOKEN", TM, DevLog });
            Assert.AreEqual(2, issues.Count, string.Join(" | ", issues.Select(i => i.Message)));
            Assert.IsTrue(issues.All(i => i.Blocking));
            StringAssert.Contains("BAD-TOKEN", issues[0].Message);
            StringAssert.Contains(TM, issues[1].Message);
        }

        [Test]
        public void ValidateConfig_ConfigEntryNamingADevOnlyDefine_IsAdvisory()
        {
            var issues = BuildDefineResolver.ValidateConfig(new[] { DevLog }, null, TM, DevList);
            Assert.AreEqual(1, issues.Count);
            Assert.IsFalse(issues[0].Blocking);
            StringAssert.Contains(DevLog, issues[0].Message);
            StringAssert.Contains("ExtraDefines", issues[0].Message);
        }

        // --- HasDefine ---

        [Test]
        public void HasDefine_MatchesExactTokenOnly()
        {
            Assert.IsTrue(BuildDefineResolver.HasDefine("TEST_MODE", TM));
            Assert.IsTrue(BuildDefineResolver.HasDefine("DOTWEEN; TEST_MODE ;UNITY_MCP_READY", TM));
            Assert.IsFalse(BuildDefineResolver.HasDefine("MY_TEST_MODE;TEST_MODE_LEGACY", TM));
            Assert.IsFalse(BuildDefineResolver.HasDefine("", TM));
            Assert.IsFalse(BuildDefineResolver.HasDefine(null, TM));
        }
    }
}
