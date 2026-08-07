using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    public sealed class ConfigureDefinesStage : IBuildStage
    {
        public string Name => "Configure defines";

        public void Execute(BuildContext ctx)
        {
            var platform = ctx.Request.Platform;
            var nbt = platform.ToNamedBuildTarget();

            var current = PlayerSettings.GetScriptingDefineSymbols(nbt)
                .Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

            // Same self-load contract as the checks (BundleIdCheck): the config is committed data,
            // read exactly once — this stage never re-runs after the reload it may trigger.
            var cfg = BuildConfigCatalog.Find(BuildConfigCatalog.LoadAll(), platform, ctx.Request.Kind);
            var delta = BuildDefineResolver.Resolve(ctx.Request.Kind, current,
                cfg ? cfg.ExtraDefines : null, cfg ? cfg.StripDefines : null,
                BuildSystemSettings.ResolveTestModeDefine());

            // Config ExtraDefines are deferred to the player build (no reload). MERGE, never
            // replace: a PreBuild hook may already have contributed defines here (the documented
            // way for hooks to add build-only defines), and clearing the list would silently drop
            // them. Eviction uses ForbidInPlayer, NOT RemoveFromGlobal: the latter is strip ∩
            // current globals, which goes empty exactly when the define is not committed for this
            // platform — i.e. when the hook's copy would be the ONE copy shipping to the player.
            var evicted = ApplyExtraDefines(ctx, delta.AddViaExtra, delta.ForbidInPlayer);
            if (evicted.Count > 0)
            {
                // Loud: a hook tried to re-add exactly what this build removes.
                Debug.LogError($"[Build] Dropped build-only define(s) {string.Join(",", evicted)} added by an " +
                               "earlier step — this build strips them; the kind rule and StripDefines win.");
                ctx.AddStep($"Dropped conflicting build-only defines: {string.Join(",", evicted)}");
            }
            if (delta.AddViaExtra.Length > 0)
                ctx.AddStep($"Defines +{string.Join(",", delta.AddViaExtra)} (build-only)");

            // The kind rule's add and every removal share ONE global write, so a build pays at most
            // one reload here (the orchestrator detects the change and resumes at the next stage).
            // Both directions are build-scoped: the snapshot restore in Finish puts the developer's
            // exact define string back, on success, on failure, and via crash recovery.
            var next = NextGlobalDefines(current, delta.AddToGlobal, delta.RemoveFromGlobal);
            if (next != string.Join(";", current))
            {
                PlayerSettings.SetScriptingDefineSymbols(nbt, next);
                if (delta.AddToGlobal.Length > 0)
                    ctx.AddStep($"Defines +{string.Join(",", delta.AddToGlobal)} (global, restored after the build)");
                if (delta.RemoveFromGlobal.Length > 0)
                    ctx.AddStep($"Defines -{string.Join(",", delta.RemoveFromGlobal)} (global)");
            }
        }

        // Pure global-define core (unit-tested): removals first, then additions appended in order,
        // never duplicating. Returned in the ';'-joined form PlayerSettings expects, so the caller
        // can compare it against the current set and skip a write that would change nothing (an
        // empty-delta write would still queue a recompile the pipeline has no reason to pay for).
        internal static string NextGlobalDefines(IReadOnlyList<string> current,
            IReadOnlyList<string> add, IReadOnlyList<string> remove)
        {
            var kept = current.Where(d => !remove.Contains(d)).ToList();
            foreach (var define in add)
            {
                if (!kept.Contains(define)) kept.Add(define);
            }
            return string.Join(";", kept);
        }

        // Pure merge core (unit-tested): keeps what earlier stages and PreBuild hooks put in the
        // list, appends the resolved additions, never duplicates — and evicts every forbidden
        // define. A hook that added the test-mode define (or a StripDefines entry) would otherwise
        // hand it straight to the player via extraScriptingDefines, shipping a Release player with
        // the test features compiled in — including on a platform whose globals never carried the
        // define at all, which is why the caller passes ForbidInPlayer and not RemoveFromGlobal.
        internal static List<string> ApplyExtraDefines(BuildContext ctx, IReadOnlyList<string> add,
            IReadOnlyList<string> forbidden)
        {
            var evicted = new List<string>();
            foreach (var define in forbidden)
            {
                if (ctx.ExtraScriptingDefines.Remove(define)) evicted.Add(define);
            }
            foreach (var define in add)
            {
                if (!ctx.ExtraScriptingDefines.Contains(define))
                    ctx.ExtraScriptingDefines.Add(define);
            }
            return evicted;
        }
    }
}
