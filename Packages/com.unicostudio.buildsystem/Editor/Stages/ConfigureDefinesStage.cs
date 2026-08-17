using System.Collections.Generic;
using System.IO;
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

            // The plan is re-verified by PlayerBuildStage against the ACTUAL globals right
            // before BuildPlayer: the reload this write queues wakes third-party
            // [InitializeOnLoad] code that can fight the plan (measured 2026-08-13: a
            // third-party dependency resolver re-added the very define this build had just
            // stripped — see CHANGELOG 0.11.0). ctx.Data survives the reload.
            DefineGuard.RecordPlan(ctx.Data, delta.ForbidInPlayer, delta.AddToGlobal);

            // StripPackages and the define write must land ATOMICALLY, before the single
            // combined reload: measured (spec T-probe, 2026-08-17), removing the package
            // and the define in separate steps breaks compilation — the surviving half
            // sees an impossible world. The auto-refresh bracket makes Unity plan ONE
            // compile instead of racing the define write against the PackageCache removal.
            var strippedPackages = new List<string>();
            var globalsWritten = false;
            AssetDatabase.DisallowAutoRefresh();
            try
            {
                var stripPackages = cfg ? cfg.StripPackages : null;
                if (stripPackages is { Length: > 0 })
                    strippedPackages = StripManifestPackages(ctx, stripPackages);

                // The kind rule's add and every removal share ONE global write, so a build pays at
                // most one reload here (the orchestrator detects the change and resumes at the next
                // stage). Both directions are build-scoped: the snapshot restore in Finish puts the
                // developer's exact define string back, on success, on failure, and via crash
                // recovery; stripped packages come back through PackageStripGuard the same way.
                var next = NextGlobalDefines(current, delta.AddToGlobal, delta.RemoveFromGlobal);
                if (next != string.Join(";", current))
                {
                    globalsWritten = true;
                    PlayerSettings.SetScriptingDefineSymbols(nbt, next);
                    if (delta.AddToGlobal.Length > 0)
                        ctx.AddStep($"Defines +{string.Join(",", delta.AddToGlobal)} (global, restored after the build)");
                    if (delta.RemoveFromGlobal.Length > 0)
                        ctx.AddStep($"Defines -{string.Join(",", delta.RemoveFromGlobal)} (global)");
                }

                if (strippedPackages.Count > 0)
                    UnityEditor.PackageManager.Client.Resolve();
            }
            finally
            {
                AssetDatabase.AllowAutoRefresh();
            }

            // Explicit pump, outside the bracket: AllowAutoRefresh does not replay the
            // refresh it suppressed, and batchmode has no editor ticks that would — the
            // E2E without this line wedged until the CI deadline with the recompile never
            // starting (defines-hash changed, reload never landed), for plain StripDefines
            // builds too, not just package strips.
            if (globalsWritten || strippedPackages.Count > 0)
                AssetDatabase.Refresh();

            // The resolve's recompile carries no define delta, so the defines-hash in Advance
            // cannot see it — this is the Q5 signal path the reload-request flag exists for.
            if (strippedPackages.Count > 0)
                ctx.RequestReload();
        }

        // Backs up manifest+lock (job-stamped, ContentStateGuard pattern), edits the manifest
        // through the unit-tested remover, and reports the strip as a step. Returns the ids
        // actually removed — absent ids are per-package no-ops the preflight already named.
        private static List<string> StripManifestPackages(BuildContext ctx, IReadOnlyList<string> stripPackages)
        {
            const string manifestPath = "Packages/manifest.json";
            const string lockPath = "Packages/packages-lock.json";

            var manifestText = File.ReadAllText(manifestPath);
            var (nextText, removed) = PackageStripGuard.RemoveDependencies(manifestText, stripPackages);
            if (removed.Count == 0) return removed;

            Directory.CreateDirectory("Library/UnicoBuild");
            var manifestBackup = "Library/UnicoBuild/manifest_backup.json";
            var lockBackup = "Library/UnicoBuild/packages_lock_backup.json";
            File.Copy(manifestPath, manifestBackup, overwrite: true);
            if (File.Exists(lockPath)) File.Copy(lockPath, lockBackup, overwrite: true);
            else lockBackup = "";
            PackageStripGuard.Arm(manifestBackup, lockBackup, BuildJobState.Load().StartedTicksUtc);

            File.WriteAllText(manifestPath, nextText);
            ctx.AddStep($"Packages -{string.Join(",", removed)} (build-scoped, restored after the build)");
            return removed;
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
