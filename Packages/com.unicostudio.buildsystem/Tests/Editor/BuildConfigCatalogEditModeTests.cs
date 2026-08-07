using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class BuildConfigCatalogEditModeTests
    {
        private static BuildTargetConfig Make(BuildPlatform p, BuildKind k)
        {
            var c = ScriptableObject.CreateInstance<BuildTargetConfig>();
            c.Platform = p; c.Kind = k;
            return c;
        }

        [Test]
        public void Find_ReturnsMatchingConfig()
        {
            var all = new[]
            {
                Make(BuildPlatform.Android, BuildKind.Test),
                Make(BuildPlatform.Android, BuildKind.Release),
                Make(BuildPlatform.iOS, BuildKind.Release),
            };
            var found = BuildConfigCatalog.Find(all, BuildPlatform.Android, BuildKind.Release);
            Assert.IsNotNull(found);
            Assert.AreEqual(BuildKind.Release, found.Kind);
            Assert.AreEqual(BuildPlatform.Android, found.Platform);
        }

        [Test]
        public void Find_ReturnsNullWhenAbsent()
        {
            var all = new[] { Make(BuildPlatform.Android, BuildKind.Test) };
            Assert.IsNull(BuildConfigCatalog.Find(all, BuildPlatform.iOS, BuildKind.Test));
        }

        [Test]
        public void Find_MultipleMatches_LogsErrorAndReturnsTheFirst()
        {
            // A duplicated config (an editing accident, or a merge resurrecting an old path) must
            // not silently redirect a build: the sibling loaders (BuildSystemSettings,
            // AddressablesVersionStore) log an error and use the first — this must match them.
            var first = Make(BuildPlatform.Android, BuildKind.Release);
            first.name = "Android_Release";
            var second = Make(BuildPlatform.Android, BuildKind.Release);
            second.name = "Android_Release_stray";

            // The regex pins the NAMES, not just the fact of an error: the contract is that the
            // message identifies every duplicate so the developer knows which assets to delete.
            // Matching only "Multiple BuildTargetConfig" stayed green with the names dropped from
            // the message (measured).
            LogAssert.Expect(LogType.Error,
                new Regex("Multiple BuildTargetConfig.*Android_Release.*Android_Release_stray"));
            var found = BuildConfigCatalog.Find(new[] { first, second },
                BuildPlatform.Android, BuildKind.Release);

            Assert.AreSame(first, found);
        }

        [Test]
        public void ResolveProfile_UsesTheMatchingConfigsProfile()
        {
            var cfg = Make(BuildPlatform.Android, BuildKind.Release);
            cfg.Profile = AddressablesProfile.Development;
            Assert.AreEqual(AddressablesProfile.Development,
                BuildConfigCatalog.ResolveProfile(new[] { cfg }, BuildPlatform.Android, BuildKind.Release));
        }

        [Test]
        public void ResolveProfile_NoMatchingConfig_FallsBackByKind()
        {
            Assert.AreEqual(AddressablesProfile.Test,
                BuildConfigCatalog.ResolveProfile(null, BuildPlatform.Android, BuildKind.Test));
            Assert.AreEqual(AddressablesProfile.Production,
                BuildConfigCatalog.ResolveProfile(null, BuildPlatform.iOS, BuildKind.Release));
        }

        // NOTE: the LoadAll integration test (asserting the host game's four committed configs)
        // lives game-side — it verifies host data, not package logic.
    }
}
