using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    public static class BuildConfigCatalog
    {
        // Discovery lives here — not in the panel — so every caller (panel today, a CLI entry
        // tomorrow) resolves the same config assets. Same discovery contract as the sibling
        // loaders (BuildSystemSettings, AddressablesVersionStore): scoped to Assets/ so a stray
        // copy inside a package is never picked up, and ordinal-sorted by path so "first match"
        // means the same asset on every machine and every session.
        public static BuildTargetConfig[] LoadAll()
        {
            var paths = AssetDatabase.FindAssets("t:BuildTargetConfig", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            Array.Sort(paths, StringComparer.Ordinal);
            return paths.Select(AssetDatabase.LoadAssetAtPath<BuildTargetConfig>)
                .Where(c => c).ToArray();
        }

        public static BuildTargetConfig Find(IEnumerable<BuildTargetConfig> all,
            BuildPlatform platform, BuildKind kind)
        {
            // A duplicate (an editing accident, or a merge resurrecting an old path) must not
            // silently redirect a build's profile, bundle id, or StripDefines. Loud, like the
            // sibling loaders — and deterministic, because LoadAll's ordering makes "the first"
            // the same asset everywhere.
            var matches = (all ?? Enumerable.Empty<BuildTargetConfig>())
                .Where(c => c && c.Platform == platform && c.Kind == kind).ToList();
            if (matches.Count > 1)
                Debug.LogError($"[Build] Multiple BuildTargetConfig assets match {platform}/{kind} " +
                               $"({string.Join(", ", matches.Select(m => m.name))}) — using the first; delete the extras.");
            return matches.FirstOrDefault();
        }

        // The single implementation of "which Addressables profile does this build use". Start used
        // to inline it AFTER preflight, so checks saw BuildContext.Profile's initialiser instead —
        // Test, whatever the request. Callers must resolve it BEFORE running checks.
        public static AddressablesProfile ResolveProfile(IEnumerable<BuildTargetConfig> all,
            BuildPlatform platform, BuildKind kind)
        {
            var cfg = Find(all, platform, kind);
            return cfg
                ? cfg.Profile
                : kind == BuildKind.Test
                    ? AddressablesProfile.Test
                    : AddressablesProfile.Production;
        }
    }
}
