using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;
using UnityEditor;

namespace UnicoStudio.BuildSystem.Tests
{
    /// <summary>
    /// StripPackages pure cores (spec: docs/specs/2026-08-17-strippackages-design.md).
    /// The strip direction edits the manifest structurally (byte fidelity is NOT needed
    /// there — restore comes from the byte backup), but the edit must stay valid JSON,
    /// including the dangling-comma case when the removed dependency is the last entry.
    /// </summary>
    public class PackageStripGuardEditModeTests
    {
        private const string Manifest =
            "{\n" +
            "  \"dependencies\": {\n" +
            "    \"com.a.first\": \"1.0.0\",\n" +
            "    \"com.vendor.bridge\": \"1.4.2\",\n" +
            "    \"com.z.last\": \"2.0.0\"\n" +
            "  }\n" +
            "}\n";

        // --- RemoveDependencies: manifest editor -------------------------------------

        [Test]
        public void RemoveDependencies_MiddleEntry_RemovesAndStaysValid()
        {
            var (text, removed) = PackageStripGuard.RemoveDependencies(
                Manifest, new[] { "com.vendor.bridge" });

            CollectionAssert.AreEqual(new[] { "com.vendor.bridge" }, removed);
            StringAssert.DoesNotContain("vendor.bridge", text);
            StringAssert.Contains("com.a.first", text);
            StringAssert.Contains("com.z.last", text);
            Assert.IsFalse(text.Contains(",\n  }"), "no dangling comma may remain");
        }

        [Test]
        public void RemoveDependencies_LastEntry_RepairsDanglingComma()
        {
            var (text, removed) = PackageStripGuard.RemoveDependencies(
                Manifest, new[] { "com.z.last" });

            CollectionAssert.AreEqual(new[] { "com.z.last" }, removed);
            StringAssert.DoesNotContain("com.z.last", text);
            Assert.IsFalse(text.Contains(",\n  }"), "previous entry's comma must be repaired");
            StringAssert.Contains("\"com.vendor.bridge\": \"1.4.2\"\n", text);
        }

        [Test]
        public void RemoveDependencies_AbsentId_IsNoOp()
        {
            var (text, removed) = PackageStripGuard.RemoveDependencies(
                Manifest, new[] { "com.not.there" });

            Assert.AreEqual(0, removed.Count);
            Assert.AreEqual(Manifest, text, "absent package must not alter the manifest");
        }

        [Test]
        public void RemoveDependencies_MultipleIds_AllRemoved()
        {
            var (text, removed) = PackageStripGuard.RemoveDependencies(
                Manifest, new[] { "com.a.first", "com.z.last" });

            Assert.AreEqual(2, removed.Count);
            StringAssert.Contains("vendor.bridge", text);
            Assert.IsFalse(text.Contains("com.a.first") || text.Contains("com.z.last"));
        }

        // --- IsExactPinned: preflight validation core --------------------------------

        [Test]
        public void ExactSemver_IsPinned()
            => Assert.IsTrue(PackageStripGuard.IsExactPinned("1.4.2"));

        [Test]
        public void GitUrlWithTagAnchor_IsPinned()
            => Assert.IsTrue(PackageStripGuard.IsExactPinned(
                "https://github.com/x/y.git?path=Packages/z#com.z/1.2.3"));

        [Test]
        public void FloatingRange_IsNotPinned()
        {
            Assert.IsFalse(PackageStripGuard.IsExactPinned("^1.4.2"));
            Assert.IsFalse(PackageStripGuard.IsExactPinned("~1.2.0"));
        }

        [Test]
        public void UnanchoredGitUrl_IsNotPinned()
            => Assert.IsFalse(PackageStripGuard.IsExactPinned(
                "https://github.com/x/y.git?path=Packages/z"));

        // --- CountKeyOccurrences: dependents heuristic over the lock -----------------
        // A package id appears once as its own lock entry and once per dependent's
        // "dependencies" map; count > 1 therefore means dependents exist. Documented
        // heuristic: text-level, no JSON parser available to this package.

        private const string Lock =
            "{\n" +
            "  \"dependencies\": {\n" +
            "    \"com.solo\": { \"version\": \"1.0.0\", \"dependencies\": {} },\n" +
            "    \"com.parent\": { \"version\": \"1.0.0\", \"dependencies\": { \"com.child\": \"1.0.0\" } },\n" +
            "    \"com.child\": { \"version\": \"1.0.0\", \"dependencies\": {} }\n" +
            "  }\n" +
            "}\n";

        [Test]
        public void KeyOccurrences_NoDependents_CountsOne()
            => Assert.AreEqual(1, PackageStripGuard.CountKeyOccurrences(Lock, "com.solo"));

        [Test]
        public void KeyOccurrences_WithDependent_CountsTwo()
            => Assert.AreEqual(2, PackageStripGuard.CountKeyOccurrences(Lock, "com.child"));

        [Test]
        public void KeyOccurrences_IdAsSubstringOfAnotherId_NotCounted()
            => Assert.AreEqual(0, PackageStripGuard.CountKeyOccurrences(Lock, "com.chi"));

        // --- Record ownership (EditorPrefs, ContentStateGuard fixture pattern) -------

        private string _savedRecord;

        [SetUp]
        public void SaveRecord()
        {
            _savedRecord = EditorPrefs.GetString(PackageStripGuard.Key, "");
            EditorPrefs.DeleteKey(PackageStripGuard.Key);
        }

        [TearDown]
        public void RestoreRecord()
        {
            if (string.IsNullOrEmpty(_savedRecord)) EditorPrefs.DeleteKey(PackageStripGuard.Key);
            else EditorPrefs.SetString(PackageStripGuard.Key, _savedRecord);
        }

        [Test]
        public void Arm_ThenIsArmedFor_MatchesOwningStampOnly()
        {
            PackageStripGuard.Arm("m.bak", "l.bak", jobStamp: 111);

            Assert.IsTrue(PackageStripGuard.IsArmedFor(111));
            Assert.IsFalse(PackageStripGuard.IsArmedFor(222));
        }

        [Test]
        public void DisarmIfOwnedBy_ForeignStamp_KeepsTheRecord()
        {
            PackageStripGuard.Arm("m.bak", "l.bak", jobStamp: 111);

            PackageStripGuard.DisarmIfOwnedBy(222);
            Assert.IsTrue(PackageStripGuard.IsArmed, "a foreign job must not clear the record");

            PackageStripGuard.DisarmIfOwnedBy(111);
            Assert.IsFalse(PackageStripGuard.IsArmed);
        }

        [Test]
        public void Disarm_ClearsUnconditionally()
        {
            PackageStripGuard.Arm("m.bak", "l.bak", jobStamp: 111);

            PackageStripGuard.Disarm();

            Assert.IsFalse(PackageStripGuard.IsArmed);
        }
    }
}
