using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnicoStudio.BuildSystem
{
    // The writable remote-content version (the {N} in ServerData/v{N} and the CDN v{N} folder).
    // Each game commits exactly one instance under Assets/ (convention: Assets/Settings/Build/).
    [CreateAssetMenu(fileName = "AddressablesVersionStore",
        menuName = "Unico/Build/Addressables Version Store")]
    public sealed class AddressablesVersionStore : ScriptableObject
    {
        // Default is for FRESH assets only — existing assets keep their serialized value.
        [SerializeField] private int version = 1;

        public int Version
        {
            get => version;
            set => version = value;
        }

#if UNITY_EDITOR
        // Discovered rather than hardcoded so the asset's location is the game's choice. Scoped to
        // Assets/ — a stray copy inside a package must never be picked up (and could not be saved).
        // Zero assets => null (callers degrade: panel shows "unavailable", bump silently skips,
        // snapshot records -1; builds fail loud via UnicoBuildPaths.RequireVersion).
        public static AddressablesVersionStore LoadOrNull()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(AddressablesVersionStore), new[] { "Assets" });
            if (guids.Length == 0) return null;

            // Deterministic pick + loud complaint: duplicate stores mean two sources of truth.
            var paths = System.Array.ConvertAll(guids, AssetDatabase.GUIDToAssetPath);
            System.Array.Sort(paths, System.StringComparer.Ordinal);
            if (paths.Length > 1)
                Debug.LogError("[Build] Multiple AddressablesVersionStore assets found (" +
                               string.Join(", ", paths) + ") — using the first; delete the extras.");

            return AssetDatabase.LoadAssetAtPath<AddressablesVersionStore>(paths[0]);
        }
#endif
    }
}
