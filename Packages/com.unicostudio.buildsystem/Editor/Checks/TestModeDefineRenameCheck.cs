using System;
using System.Collections.Generic;
using UnityEditor;

namespace UnicoStudio.BuildSystem.Editor
{
    // A renamed test define is only safe once the rename reaches the game's own code. The package
    // cannot know which symbol that code consumes, but it can spot the telltale of a half-finished
    // rename (or a typo in the settings asset): a custom name configured while the DEFAULT define
    // is still committed in some platform's global defines. In that state the kind rule manages the
    // custom symbol and never touches the default one, so a Release build can ship with the test
    // features still compiled in — with every other check green, because each one resolves the same
    // wrong name. Advisory: a game may legitimately keep TEST_MODE around for something else.
    //
    // Two blind spots, by construction: (a) a host that never commits the test define globally —
    // relying only on the build-scoped global add the kind rule performs — never trips this check
    // at all, since there is no "default still committed" state to observe; (b) a typo
    // made AFTER the Player Settings cleanup (the default is already gone everywhere) passes
    // silently, because absence of the default looks identical to a completed rename.
    public sealed class TestModeDefineRenameCheck : IPreflightCheck
    {
        // configuredDefine is the EFFECTIVE name, i.e. already resolved via
        // BuildSystemSettings.ResolveTestModeDefine() (trimmed, fallback-applied) — not the raw asset field.
        public static CheckResult Evaluate(string configuredDefine, IReadOnlyList<string> platformsCarryingDefault)
        {
            var fallback = BuildSystemSettings.DEFAULT_TEST_MODE_DEFINE;
            var carrying = platformsCarryingDefault ?? Array.Empty<string>();
            if (configuredDefine == fallback)
                return CheckResult.Pass($"Test define is the default ({fallback}).");
            if (carrying.Count == 0)
                return CheckResult.Pass($"Test define renamed to {configuredDefine}; {fallback} is not in any global defines.");

            return CheckResult.Warn(
                $"BuildSystemSettings renames the test define to '{configuredDefine}', but '{fallback}' is still " +
                $"in the global defines of {string.Join(", ", carrying)}. If the game's code still " +
                $"uses '{fallback}', this build manages the wrong symbol and a Release can ship with test features " +
                "compiled in. Finish the rename (code + Player Settings) or clear the custom name.");
        }

        public CheckResult Run(BuildContext ctx)
        {
            // Every supported platform, not just the requested one: the leftover default usually
            // sits on whichever platform the team develops on, while the build targets another.
            // The sweep covers exactly BuildPlatform's members (Android + iOS) — a leftover parked
            // on another platform's Scripting Define Symbols (e.g. Standalone) is invisible here.
            // Harmless today since this package only ships those two, but worth knowing.
            var carrying = new List<string>();
            foreach (BuildPlatform platform in Enum.GetValues(typeof(BuildPlatform)))
            {
                var raw = PlayerSettings.GetScriptingDefineSymbols(platform.ToNamedBuildTarget());
                if (BuildDefineResolver.HasDefine(raw, BuildSystemSettings.DEFAULT_TEST_MODE_DEFINE))
                    carrying.Add(platform.ToString());
            }
            return Evaluate(BuildSystemSettings.ResolveTestModeDefine(), carrying);
        }
    }
}
