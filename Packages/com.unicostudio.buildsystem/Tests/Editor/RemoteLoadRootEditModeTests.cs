using System.Collections.Generic;
using NUnit.Framework;
using UnicoStudio.BuildSystem;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class RemoteLoadRootEditModeTests
    {
        private readonly List<BuildSystemSettings> _created = new List<BuildSystemSettings>();

        // ScriptableObject.CreateInstance instances are not garbage-collected on their own; they
        // linger for the rest of the editor session unless destroyed.
        [TearDown]
        public void DestroyCreatedInstances()
        {
            foreach (var settings in _created)
                Object.DestroyImmediate(settings);

            _created.Clear();
        }

        private BuildSystemSettings MakeDefault()
        {
            var s = ScriptableObject.CreateInstance<BuildSystemSettings>();
            _created.Add(s);
            return s;
        }

        private BuildSystemSettings Make(string root)
        {
            var s = MakeDefault();
            s.RemoteLoadRoot = root;
            return s;
        }

        // Unlike the test-mode define, a CDN root has no safe default: guessing one would publish
        // content to, or load it from, the wrong host. So the grammar is a gate, not a fallback.
        [Test]
        public void IsValidRemoteLoadRoot_AcceptsHttpAndHttps()
        {
            Assert.IsTrue(BuildSystemSettings.IsValidRemoteLoadRoot("https://cdn.unicostudio.co/btlegacy/Content/v"));
            Assert.IsTrue(BuildSystemSettings.IsValidRemoteLoadRoot("http://example.test/x/v"));
            Assert.IsTrue(BuildSystemSettings.IsValidRemoteLoadRoot("  https://trimmed.test/v  "));
            // URL schemes are case-insensitive per RFC 3986, so an upper-cased one is a real root.
            Assert.IsTrue(BuildSystemSettings.IsValidRemoteLoadRoot("HTTPS://cdn.unicostudio.co/btlegacy/Content/v"));
        }

        [Test]
        public void IsValidRemoteLoadRoot_RejectsAnythingElse()
        {
            Assert.IsFalse(BuildSystemSettings.IsValidRemoteLoadRoot(null));
            Assert.IsFalse(BuildSystemSettings.IsValidRemoteLoadRoot(""));
            Assert.IsFalse(BuildSystemSettings.IsValidRemoteLoadRoot("   "));
            Assert.IsFalse(BuildSystemSettings.IsValidRemoteLoadRoot("ServerData/v"));
            Assert.IsFalse(BuildSystemSettings.IsValidRemoteLoadRoot("ftp://example.test/v"));
            // A leading zero-width space is ignorable under culture-sensitive prefix matching, so a
            // CurrentCulture StartsWith would accept this URL that no CDN can serve; ordinal rejects it.
            Assert.IsFalse(BuildSystemSettings.IsValidRemoteLoadRoot("\u200Bhttps://cdn.unicostudio.co/btlegacy/Content/v"));
        }

        [Test]
        public void EndingIsDeliberatelyUnconstrained()
        {
            // Both shipped games happen to end in "v" because Compose appends the version number
            // directly, but that is a convention, not a requirement — rejecting other shapes would
            // block a host for no correctness reason. A wrong-but-well-formed root is caught by the
            // human gate, not by string validation.
            Assert.IsTrue(BuildSystemSettings.IsValidRemoteLoadRoot("https://example.test/no-v-suffix/"));
        }

        // RequireRemoteLoadRoot hard-calls LoadOrNull, so it cannot be driven from a CreateInstance
        // instance; EffectiveRemoteLoadRoot is the part it delegates to and the part worth pinning.
        [Test]
        public void ValidRoot_IsUsedAndTrimmed()
        {
            // The trim is load-bearing: a root pasted with a trailing space composes to
            // "https://cdn…/v 36/Production/Android", a broken URL baked into the shipped catalog.
            Assert.AreEqual("https://cdn.unicostudio.co/btlegacy/Content/v",
                Make("https://cdn.unicostudio.co/btlegacy/Content/v").EffectiveRemoteLoadRoot);
            Assert.AreEqual("https://cdn.unicostudio.co/btlegacy/Content/v",
                Make("  https://cdn.unicostudio.co/btlegacy/Content/v ").EffectiveRemoteLoadRoot);
        }

        [Test]
        public void BlankOrMalformed_IsReportedUnusable()
        {
            // Null means "not usable" — there is no safe root to degrade to, so the caller throws.
            Assert.IsNull(MakeDefault().EffectiveRemoteLoadRoot);
            Assert.IsNull(Make(null).EffectiveRemoteLoadRoot);
            Assert.IsNull(Make("").EffectiveRemoteLoadRoot);
            Assert.IsNull(Make("   ").EffectiveRemoteLoadRoot);
            Assert.IsNull(Make("ServerData/v").EffectiveRemoteLoadRoot);
            Assert.IsNull(Make("ftp://example.test/v").EffectiveRemoteLoadRoot);
        }
    }
}
