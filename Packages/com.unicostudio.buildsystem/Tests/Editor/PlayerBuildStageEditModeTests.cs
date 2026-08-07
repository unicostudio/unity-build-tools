using NUnit.Framework;
using UnityEditor;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    public sealed class PlayerBuildStageEditModeTests
    {
        // Scripted builds get compression exclusively from these flags; a wrong mapping would
        // silently change the shipped artifact's compression.
        [Test]
        public void OptionsFor_MapsCompressionToBuildOptions()
        {
            Assert.AreEqual(BuildOptions.CompressWithLz4HC, PlayerBuildStage.OptionsFor(CompressionKind.LZ4HC));
            Assert.AreEqual(BuildOptions.CompressWithLz4, PlayerBuildStage.OptionsFor(CompressionKind.LZ4));
            Assert.AreEqual(BuildOptions.None, PlayerBuildStage.OptionsFor(CompressionKind.Default));
        }

        // --- SelectSymbolsArchives: asking BuildReport instead of globbing the output folder ---
        //
        // Measured on a real 6000.0.62f1 AAB build (2026-08-07 gate): GetFiles() lists the
        // symbols archive exactly once, role "Symbols", at its OUTPUT-FOLDER absolute path — the
        // same file the old glob found. These pin the selection rule against that measured shape.

        private const string Root = "/proj";

        [Test]
        public void SymbolsRoleZipUnderRoot_IsSelected_AsProjectRelative()
        {
            var picked = PlayerBuildStage.SelectSymbolsArchives(new[]
            {
                ("Symbols", "/proj/Builds/Android/Test/Game_TEST-1.6.6-v48-IL2CPP.symbols.zip"),
            }, Root);
            CollectionAssert.AreEqual(
                new[] { "Builds/Android/Test/Game_TEST-1.6.6-v48-IL2CPP.symbols.zip" }, picked);
        }

        [Test]
        public void OtherRoles_AreIgnored_EvenWithSymbolsInThePath()
        {
            // The measured report carries ~1539 entries, including raw .so files under paths
            // containing "symbols" (role "so") and gradle scripts named *Symbols* (role "gradle").
            var picked = PlayerBuildStage.SelectSymbolsArchives(new[]
            {
                ("so", "/proj/Library/Bee/Android/Prj/IL2CPP/Gradle/unityLibrary/symbols/arm64-v8a/libil2cpp.so"),
                ("gradle", "/proj/Library/Bee/Android/Prj/IL2CPP/Gradle/launcher/setupSymbols.gradle"),
            }, Root);
            Assert.IsEmpty(picked);
        }

        [Test]
        public void SymbolsRoleWithoutTheZipSuffix_IsIgnored_Defensively()
        {
            // The suffix test is a GUARD on API-returned values, not a reconstruction: if a future
            // Unity classifies something else under "Symbols" (an iOS dSYM folder, a raw map
            // file), it must not be mislabeled as the Play-Console-uploadable archive.
            var picked = PlayerBuildStage.SelectSymbolsArchives(new[]
            {
                ("Symbols", "/proj/Builds/Android/Test/Game.dSYM"),
            }, Root);
            Assert.IsEmpty(picked);
        }

        [Test]
        public void PathOutsideTheProjectRoot_IsKeptAbsolute()
        {
            var picked = PlayerBuildStage.SelectSymbolsArchives(new[]
            {
                ("Symbols", "/elsewhere/Game.symbols.zip"),
            }, Root);
            CollectionAssert.AreEqual(new[] { "/elsewhere/Game.symbols.zip" }, picked);
        }

        [Test]
        public void MultipleArchives_AllReturned_InReportOrder()
        {
            var picked = PlayerBuildStage.SelectSymbolsArchives(new[]
            {
                ("Symbols", "/proj/Builds/a.symbols.zip"),
                ("Symbols", "/proj/Builds/b.symbols.zip"),
            }, Root);
            CollectionAssert.AreEqual(
                new[] { "Builds/a.symbols.zip", "Builds/b.symbols.zip" }, picked);
        }
    }
}
