using System.Collections.Generic;
using NUnit.Framework;
using UnicoStudio.BuildSystem.Editor;

namespace UnicoStudio.BuildSystem.Tests
{
    /// <summary>
    /// Pins the post-build check that the Xcode project's CODE_SIGN_ENTITLEMENTS resolves to a
    /// real file the way Xcode resolves it: relative to the folder holding the .xcodeproj.
    ///
    /// Measured twice on real exports (BT5 2026-09-15, BTAS 1.6.8 2026-10-05): Unity's build
    /// graph stores the value relative to the UNITY project, a capability post-processor reads it
    /// back and re-writes it, and the export ends up with
    /// "Builds/iOS/Release/&lt;folder&gt;/&lt;Name&gt;.entitlements" — Xcode appends that to the Xcode root
    /// and Archive fails ("could not be opened"). The Unity build itself reported success both
    /// times. Fixture lines below copy the real pbxproj shapes (quoted and unquoted).
    /// </summary>
    public sealed class XcodeEntitlementsCheckEditModeTests
    {
        private const string Root = "/out/Game_v1.6.8(0)_05.10.2026_RELEASE";

        private static string Pbx(params string[] values)
        {
            var lines = new List<string> { "// !$*UTF8*$!", "{" };
            foreach (var v in values) lines.Add($"\t\t\t\tCODE_SIGN_ENTITLEMENTS = {v};");
            lines.Add("}");
            return string.Join("\n", lines);
        }

        private static string Check(string pbxproj, params string[] existingFiles)
        {
            var files = new HashSet<string>(existingFiles);
            return XcodeEntitlementsCheck.FindProblem(pbxproj, Root, files.Contains);
        }

        [Test]
        public void BareFileName_PresentInXcodeRoot_Passes()
        {
            Assert.IsNull(Check(Pbx("Game.entitlements", "Game.entitlements"), Root + "/Game.entitlements"));
        }

        [Test]
        public void UnityProjectRelativeValue_IsReported_WithValueAndResolvedPath()
        {
            // The exact BTAS 1.6.8 shape: the file sits in the Xcode root, the value does not point there.
            const string broken = "\"Builds/iOS/Release/Game_v1.6.8(0)_05.10.2026_RELEASE/Game.entitlements\"";
            var problem = Check(Pbx(broken), Root + "/Game.entitlements");

            Assert.IsNotNull(problem);
            StringAssert.Contains("Builds/iOS/Release/Game_v1.6.8(0)_05.10.2026_RELEASE/Game.entitlements", problem);
            StringAssert.Contains(Root + "/Builds/iOS/Release/", problem, "names where Xcode will look");
        }

        [Test]
        public void OneBadConfiguration_AmongGoodOnes_IsReported()
        {
            var problem = Check(Pbx("Game.entitlements", "Sub/Missing.entitlements", "Game.entitlements"),
                Root + "/Game.entitlements");
            StringAssert.Contains("Sub/Missing.entitlements", problem);
        }

        [Test]
        public void NoEntitlementsSetting_Passes()
        {
            Assert.IsNull(Check("// !$*UTF8*$!\n{\n\t\t\t\tPRODUCT_NAME = Game;\n}"));
        }

        [Test]
        public void EmptyQuotedValue_Passes()
        {
            Assert.IsNull(Check(Pbx("\"\"")));
        }

        [Test]
        public void SrcRootVariable_IsResolvedAgainstTheXcodeRoot()
        {
            Assert.IsNull(Check(Pbx("\"$(SRCROOT)/Game.entitlements\"", "\"${SRCROOT}/Game.entitlements\""),
                Root + "/Game.entitlements"));
            // A missing file behind SRCROOT must be REPORTED — proves the value is resolved, not
            // skipped by the unresolvable-variable rule (measured: without this line, deleting the
            // SRCROOT handling left the suite green).
            StringAssert.Contains(Root + "/Missing.entitlements",
                Check(Pbx("\"$(SRCROOT)/Missing.entitlements\""), Root + "/Game.entitlements"));
        }

        [Test]
        public void AbsolutePath_IsCheckedAsIs()
        {
            Assert.IsNull(Check(Pbx("/abs/Game.entitlements"), "/abs/Game.entitlements"));
            Assert.IsNotNull(Check(Pbx("/abs/Missing.entitlements")));
        }

        [Test]
        public void UnresolvableVariable_IsSkipped_NotAFalseFailure()
        {
            // A value built from another build setting cannot be resolved without Xcode; skipping it
            // keeps the check from failing a project it cannot reason about.
            Assert.IsNull(Check(Pbx("\"$(PROJECT_DIR)/$(TARGET_NAME).entitlements\"")));
        }

        [Test]
        public void QuotedValueWithSpaces_IsParsed()
        {
            Assert.IsNull(Check(Pbx("\"My Game.entitlements\""), Root + "/My Game.entitlements"));
        }
    }
}
