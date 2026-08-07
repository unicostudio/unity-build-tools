using NUnit.Framework;
using UnicoStudio.BuildSystem;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class RemotePathCheckEditModeTests
    {
        [Test]
        public void NotRequested_IsPass()
        {
            Assert.AreEqual(CheckSeverity.Pass,
                RemotePathCheck.Evaluate(false, AddressablesProfile.Test, null, "irrelevant").Severity);
        }

        [Test]
        public void UnresolvedBinding_Blocks_AndNamesTheToken()
        {
            var r = RemotePathCheck.Evaluate(true, AddressablesProfile.Production,
                "[UnicoStudio.BuildSystem.AddressablesProfilePaths.RemoteLoadProductionPath]/[BuildTarget]",
                "UnicoStudio.BuildSystem.AddressablesProfilePaths.RemoteLoadProductionPath/Android");
            Assert.AreEqual(CheckSeverity.Block, r.Severity);
            StringAssert.Contains("RemoteLoadProductionPath", r.Message);
        }

        // The case the inner-text rule cannot see: an unbalanced '[' is not a token, so
        // FindUnresolvedToken reports nothing — measured live, "[Foo.Bar" evaluates to "[Foo.Bar".
        // AddressablesStage rejects it, so preflight must too, or the check would pass a value the
        // build then dies on. The message must be the delimiter one: it is a different fix.
        [Test]
        public void MalformedDelimiters_Block_WithTheirOwnMessage()
        {
            var r = RemotePathCheck.Evaluate(true, AddressablesProfile.Test, "[Foo.Bar", "[Foo.Bar");
            Assert.AreEqual(CheckSeverity.Block, r.Severity);
            StringAssert.Contains("delimiters", r.Message);
        }

        [Test]
        public void ResolvedButNotAUrl_Blocks()
        {
            var r = RemotePathCheck.Evaluate(true, AddressablesProfile.Test,
                "ServerData/v36/Test/[BuildTarget]", "ServerData/v36/Test/Android");
            Assert.AreEqual(CheckSeverity.Block, r.Severity);
            // Severity alone would not pin WHICH rule fired — this input is fully resolved and has
            // balanced brackets, so if the delimiter branch ever swallowed it the test would still
            // be green while reporting an unrelated fix. The not-a-URL wording is the assertion.
            StringAssert.Contains("not a URL", r.Message);
        }

        // ProfileBindingRule returns "no problem" for an empty evaluated value — it contains no
        // token text and no bracket — so every caller must test emptiness itself. (An earlier
        // version of this comment claimed deleting the IsNullOrEmpty clause is caught only by this
        // test — false: "" already blocks via !StartsWith("http"), so this test stays green with
        // the clause deleted. Measured. Null is what the clause actually guards; see below.)
        [Test]
        public void EmptyEvaluatedValue_Blocks()
        {
            var r = RemotePathCheck.Evaluate(true, AddressablesProfile.Production, "[Broken.Type.Member]", "");
            Assert.AreEqual(CheckSeverity.Block, r.Severity);
        }

        [Test]
        public void NullEvaluatedValue_Blocks_InsteadOfThrowing()
        {
            // The IsNullOrEmpty clause's only OBSERVABLE job: without it a null loadPath throws
            // NRE out of preflight instead of reporting a Block. ProfileBindingRule itself passes
            // null through cleanly (FindUnresolvedToken's own IsNullOrEmpty guard), so this is the
            // first line that would crash.
            var r = RemotePathCheck.Evaluate(true, AddressablesProfile.Production, "[Broken.Type.Member]", null);
            Assert.AreEqual(CheckSeverity.Block, r.Severity);
        }

        [Test]
        public void ResolvedUrl_Passes_AndNamesTheProfileItChecked()
        {
            var r = RemotePathCheck.Evaluate(true, AddressablesProfile.Production,
                "[UnicoStudio.BuildSystem.AddressablesProfilePaths.RemoteLoadProductionPath]/[BuildTarget]",
                "https://cdn.example.test/game/Content/v36/Production/Android");
            Assert.AreEqual(CheckSeverity.Pass, r.Severity);
            // The profile must appear in the message: the whole point of this check is that it
            // validates the profile the BUILD will use, not whatever the context defaulted to.
            StringAssert.Contains("Production", r.Message);
        }
    }
}
