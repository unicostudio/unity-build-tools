using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class ProfileBindingRuleEditModeTests
    {
        // Addressables' evaluator SWALLOWS a resolution failure and substitutes the token's own
        // text with the brackets stripped — "[A.B]" becomes "A.B". So the presence of '[' proves
        // nothing, and the only general signal is the raw token's inner text surviving.
        [Test]
        public void UnresolvedToken_IsReportedByItsInnerText()
        {
            Assert.AreEqual("UnicoStudio.BuildSystem.NoSuchType.NoSuchProp",
                ProfileBindingRule.FindUnresolvedToken(
                    "[UnicoStudio.BuildSystem.NoSuchType.NoSuchProp]",
                    "UnicoStudio.BuildSystem.NoSuchType.NoSuchProp"));
        }

        [Test]
        public void ResolvedToken_ReportsNothing()
        {
            Assert.IsNull(ProfileBindingRule.FindUnresolvedToken(
                "[UnicoStudio.BuildSystem.AddressablesProfilePaths.RemoteLoadTestPath]",
                "https://cdn.example.test/game/Content/v36/Test/Android"));
        }

        [Test]
        public void MultiTokenValue_PassesWhenEveryTokenResolved()
        {
            // Verified live against the real evaluator: "[...BuildPath]/[BuildTarget]" resolves to
            // "Library/com.unity.addressables/aa/Android/Android" — neither token name survives.
            Assert.IsNull(ProfileBindingRule.FindUnresolvedToken(
                "[UnityEngine.AddressableAssets.Addressables.BuildPath]/[BuildTarget]",
                "Library/com.unity.addressables/aa/Android/Android"));
        }

        [Test]
        public void MultiTokenValue_ReportsTheOneThatSurvived()
        {
            Assert.AreEqual("Broken.Type.Member",
                ProfileBindingRule.FindUnresolvedToken(
                    "[Broken.Type.Member]/[BuildTarget]",
                    "Broken.Type.Member/Android"));
        }

        [Test]
        public void DotlessToken_IsNotAFalsePositive()
        {
            // "[BuildTarget]" has no dot, so Addressables' property evaluator would return the name
            // — but it resolves as a PROFILE VARIABLE instead. Verified live: it yields "Android".
            Assert.IsNull(ProfileBindingRule.FindUnresolvedToken("[BuildTarget]", "Android"));
        }

        [Test]
        public void ValueWithoutTokens_ReportsNothing()
        {
            Assert.IsNull(ProfileBindingRule.FindUnresolvedToken("https://example.test/v", "https://example.test/v"));
            Assert.IsNull(ProfileBindingRule.FindUnresolvedToken("", ""));
            Assert.IsNull(ProfileBindingRule.FindUnresolvedToken(null, null));
        }

        [Test]
        public void EmptyToken_IsNotAMatch()
        {
            // string.Contains("") is true of EVERY string, so without the rule's `inner.Length > 0`
            // test an empty "[]" token would report a fully resolved value as unresolved and hard-
            // fail the build. Drop that clause and this is the only test that notices.
            Assert.IsNull(ProfileBindingRule.FindUnresolvedToken("[]/v36", "https://cdn.example.test/v36"));
        }

        [Test]
        public void UnbalancedRawValue_ReportsNothing_AndLeavesTheBracketToTheCaller()
        {
            // Verified live against the real evaluator: an unclosed '[' is not a token, so it is
            // passed through verbatim — "[Foo.Bar" evaluates to "[Foo.Bar". This rule extracts
            // nothing (no closing delimiter) and reports nothing, which is why the bracket test is
            // the OTHER half of Inspect (ProfileBindingProblem.MalformedDelimiters) rather than
            // something FindUnresolvedToken could ever catch. Drop that half and a typo'd profile
            // value would sail through as a build path for both callers at once.
            Assert.IsNull(ProfileBindingRule.FindUnresolvedToken("[Foo.Bar", "[Foo.Bar"));
        }

        [Test]
        public void EmptyEvaluatedResult_ReportsNothing_SoCallersMustTestEmptinessThemselves()
        {
            // Null here does NOT mean "every token resolved" — an empty evaluated value cannot
            // contain any token text. AddressablesStage is safe only because it throws on an empty
            // result before asking this rule; any new caller must do the same.
            Assert.IsNull(ProfileBindingRule.FindUnresolvedToken("[Broken.Type.Member]", ""));
        }

        // Inspect is the whole rule — both halves of it. AddressablesStage and RemotePathCheck each
        // act on its verdict rather than testing the two conditions themselves, which is what stops
        // preflight from passing a value the stage then refuses to build.

        [Test]
        public void Inspect_ReportsTheSurvivingToken_AndNamesIt()
        {
            var v = ProfileBindingRule.Inspect("[Broken.Type.Member]/[BuildTarget]", "Broken.Type.Member/Android");
            Assert.AreEqual(ProfileBindingProblem.UnresolvedToken, v.Problem);
            Assert.AreEqual("Broken.Type.Member", v.Token);
        }

        [Test]
        public void Inspect_ReportsMalformedDelimiters_WhichTheTokenRuleCannotSee()
        {
            // Exactly the gap documented above: no closing delimiter, so nothing to extract and
            // nothing to compare — FindUnresolvedToken alone would call this value clean.
            Assert.IsNull(ProfileBindingRule.FindUnresolvedToken("[Foo.Bar", "[Foo.Bar"));

            var v = ProfileBindingRule.Inspect("[Foo.Bar", "[Foo.Bar");
            Assert.AreEqual(ProfileBindingProblem.MalformedDelimiters, v.Problem);
            // No token to name: a malformed value is by definition not a token this rule extracts.
            Assert.IsNull(v.Token);
        }

        [Test]
        public void Inspect_ReportsNothing_WhenEveryTokenResolved()
        {
            var v = ProfileBindingRule.Inspect(
                "[UnicoStudio.BuildSystem.AddressablesProfilePaths.RemoteLoadTestPath]/[BuildTarget]",
                "https://cdn.example.test/game/Content/v36/Test/Android");
            Assert.AreEqual(ProfileBindingProblem.None, v.Problem);
            Assert.IsNull(v.Token);
        }

        [Test]
        public void Inspect_PrefersTheTokenVerdict_WhenAValueManagesBothProblems()
        {
            // "[A.B]" survived evaluation AND a stray '[' is left over. The token verdict names the
            // binding that broke, so it is the more actionable of the two and must win.
            var v = ProfileBindingRule.Inspect("[A.B][Foo", "A.B[Foo");
            Assert.AreEqual(ProfileBindingProblem.UnresolvedToken, v.Problem);
            Assert.AreEqual("A.B", v.Token);
        }

        [Test]
        public void Inspect_ReportsNothing_ForAnEmptyEvaluatedResult()
        {
            // Same trap as FindUnresolvedToken, inherited deliberately: an empty value contains
            // neither token text nor a bracket. Callers test emptiness themselves.
            Assert.AreEqual(ProfileBindingProblem.None,
                ProfileBindingRule.Inspect("[Broken.Type.Member]", "").Problem);
            Assert.AreEqual(ProfileBindingProblem.None, ProfileBindingRule.Inspect(null, null).Problem);
        }
    }
}
