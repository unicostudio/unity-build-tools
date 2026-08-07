using System.Collections.Generic;
using System.Linq;

namespace UnicoStudio.BuildSystem.Editor
{
    public readonly struct DefineDelta
    {
        public readonly string[] AddViaExtra;
        public readonly string[] AddToGlobal;
        public readonly string[] RemoveFromGlobal;

        // The player-list eviction set: every define this build must keep out of
        // BuildPlayerOptions.extraScriptingDefines, INDEPENDENT of the current globals.
        // RemoveFromGlobal cannot serve that role — it is strip ∩ current globals (plus the kind
        // rule's removal only when the define IS global), so it goes empty exactly when a PreBuild
        // hook's copy would be the ONE copy and ship straight through to the player.
        public readonly string[] ForbidInPlayer;

        public DefineDelta(string[] addViaExtra, string[] addToGlobal, string[] removeFromGlobal,
            string[] forbidInPlayer)
        {
            AddViaExtra = addViaExtra;
            AddToGlobal = addToGlobal;
            RemoveFromGlobal = removeFromGlobal;
            ForbidInPlayer = forbidInPlayer;
        }
    }

    // One validation finding about a config's define lists. Blocking findings must stop the build
    // (DefinePlanCheck maps them to Block); advisory ones surface as Warn.
    public readonly struct DefineConfigIssue
    {
        public readonly bool Blocking;
        public readonly string Message;

        public DefineConfigIssue(bool blocking, string message)
        {
            Blocking = blocking;
            Message = message;
        }
    }

    public static class BuildDefineResolver
    {
        // Exact-token membership in a raw ';'-joined defines string. A plain string.Contains would
        // false-positive on defines that merely embed the token (e.g. MY_TEST_MODE) — every
        // presence probe in the module must go through here so the parse cannot drift.
        public static bool HasDefine(string rawDefines, string define) =>
            !string.IsNullOrEmpty(rawDefines) &&
            rawDefines.Split(';').Select(s => s.Trim()).Contains(define);

        // The full define plan for one build:
        //   AddViaExtra      — config ExtraDefines not already global. Player-only via
        //                      BuildPlayerOptions.extraScriptingDefines; no reload.
        //   AddToGlobal      — the test-mode define on a Test build that lacks it. Mutates global
        //                      defines (reload); restored by the DevStateSnapshot after the build.
        //   RemoveFromGlobal — config StripDefines that are actually global, plus the test-mode
        //                      define on Release builds. Same global write, same restore.
        //
        // Why the KIND RULE goes global while config ExtraDefines stay player-only: editor
        // assemblies are compiled with the ACTIVE BUILD TARGET's global defines, and
        // extraScriptingDefines is a player-compile parameter that never reaches them. Every
        // editor-side build participant — IPreprocessBuildWithReport / IPostprocessBuildWithReport
        // callbacks, host build steps, the Addressables content build — would therefore compile
        // against the OPPOSITE build kind on any platform whose committed globals disagree with the
        // requested kind (the classic case: a Test build for a platform that never had the define
        // committed, where a plist/manifest post-processor then writes production credentials into
        // a test artifact). The build's intent must be visible to everything that participates in
        // it, so the kind rule owns global defines in BOTH directions. Config ExtraDefines keep
        // their documented player-only contract — they describe the player, not the build's intent.
        //
        // The cost is paid only when reality disagrees with the plan: a Test build whose target
        // already carries the define (and a Release build whose target does not) writes nothing and
        // reloads nothing, exactly as before.
        //
        // The test-mode define obeys ONLY the kind rule: config entries naming it are dropped here
        // (ValidateConfig reports them), so a config can never fight the Test/Release invariant.
        // Invalid tokens are dropped defensively too — preflight blocks on them before any build.
        public static DefineDelta Resolve(BuildKind kind, IEnumerable<string> currentGlobalDefines,
            IEnumerable<string> extraDefines, IEnumerable<string> stripDefines, string testModeDefine)
        {
            var global = new HashSet<string>(currentGlobalDefines ?? Enumerable.Empty<string>());
            var extra = Sanitize(extraDefines, testModeDefine);
            var strip = Sanitize(stripDefines, testModeDefine);

            // Never add a define the same build strips (contradiction; preflight blocks it).
            var add = extra.Where(d => !global.Contains(d) && !strip.Contains(d)).ToList();
            var addGlobal = new List<string>();
            var remove = strip.Where(global.Contains).ToList();

            var wantTestMode = kind == BuildKind.Test;
            var hasTestMode = global.Contains(testModeDefine);
            if (wantTestMode && !hasTestMode)
                addGlobal.Add(testModeDefine);
            else if (!wantTestMode && hasTestMode)
                remove.Add(testModeDefine);

            // ForbidInPlayer deliberately ignores the current globals: whether the define happens
            // to be committed for this platform says nothing about whether the player may carry it.
            // (Sanitize already dropped strip entries naming the test define, so no duplicate.)
            var forbid = new List<string>(strip);
            if (!wantTestMode) forbid.Add(testModeDefine);

            return new DefineDelta(add.ToArray(), addGlobal.ToArray(), remove.ToArray(), forbid.ToArray());
        }

        // Config-authoring mistakes, surfaced at preflight. Blocking: invalid tokens (they would
        // corrupt the ';'-joined defines string) and the same define in both lists (contradiction).
        // Advisory: the test-mode define in either list (the kind rule owns it; entries are ignored).
        public static List<DefineConfigIssue> ValidateConfig(
            IEnumerable<string> extraDefines, IEnumerable<string> stripDefines, string testModeDefine)
        {
            // Materialize once: both lists are walked TWICE below (token issues, then the
            // intersection). Today's callers pass arrays, but the parameters are IEnumerable — a
            // lazy or single-pass source would come back empty on the second walk and silently drop
            // the both-lists finding, the one issue here that no other check would catch.
            var extra = (extraDefines ?? Enumerable.Empty<string>()).ToList();
            var strip = (stripDefines ?? Enumerable.Empty<string>()).ToList();

            var issues = new List<DefineConfigIssue>();
            CollectTokenIssues(extra, "ExtraDefines", testModeDefine, issues);
            CollectTokenIssues(strip, "StripDefines", testModeDefine, issues);

            foreach (var both in Sanitize(extra, testModeDefine).Intersect(Sanitize(strip, testModeDefine)))
                issues.Add(new DefineConfigIssue(true, $"'{both}' is in both ExtraDefines and StripDefines — remove it from one."));

            return issues;
        }

        private static void CollectTokenIssues(IEnumerable<string> tokens, string listName,
            string testModeDefine, List<DefineConfigIssue> issues)
        {
            foreach (var raw in tokens ?? Enumerable.Empty<string>())
            {
                var token = raw?.Trim() ?? "";
                if (token.Length == 0) continue; // blank rows are inspector noise, not errors
                if (token == testModeDefine)
                    issues.Add(new DefineConfigIssue(false, $"'{testModeDefine}' is managed by build kind (Test adds it, Release strips it); the {listName} entry is ignored."));
                else if (!BuildSystemSettings.IsValidDefine(token))
                    issues.Add(new DefineConfigIssue(true, $"{listName} entry '{token}' is not a valid define symbol."));
            }
        }

        // Trim, drop blanks / invalid tokens / the kind-ruled define, de-duplicate (order-preserving).
        private static List<string> Sanitize(IEnumerable<string> tokens, string testModeDefine)
        {
            var result = new List<string>();
            foreach (var raw in tokens ?? Enumerable.Empty<string>())
            {
                var token = raw?.Trim() ?? "";
                if (token.Length == 0 || token == testModeDefine) continue;
                if (!BuildSystemSettings.IsValidDefine(token)) continue;
                if (!result.Contains(token)) result.Add(token);
            }
            return result;
        }
    }
}
