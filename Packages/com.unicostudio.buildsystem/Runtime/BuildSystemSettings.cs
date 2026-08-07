using System.Text.RegularExpressions;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnicoStudio.BuildSystem
{
    // Optional project-level build-system settings. Each game commits at most one instance under
    // Assets/ (convention: Assets/Settings/Build/). Every knob degrades to a safe default when the
    // asset — or an individual value — is absent, so games without the asset behave exactly as before.
    [CreateAssetMenu(fileName = "BuildSystemSettings", menuName = "Unico/Build/Build System Settings")]
    public sealed class BuildSystemSettings : ScriptableObject
    {
        public const string DEFAULT_TEST_MODE_DEFINE = "TEST_MODE";

        [Tooltip("Scripting define managed by build kind: ensured present on Test builds, removed " +
                 "on Release builds. Change it only if this game names its test define differently. " +
                 "Blank or invalid values fall back to TEST_MODE.")]
        [SerializeField] private string testModeDefine = DEFAULT_TEST_MODE_DEFINE;

        public string TestModeDefine
        {
            get => testModeDefine;
            set => testModeDefine = value;
        }

        // Fail-safe: a blank or malformed name degrades to the default instead of weakening the
        // kind rule — the rail keeping test features out of Release must survive misconfiguration.
        public string EffectiveTestModeDefine => IsValidDefine(testModeDefine) ? testModeDefine.Trim() : DEFAULT_TEST_MODE_DEFINE;

        // Valid C# preprocessor symbol; anything else would corrupt a ';'-joined defines string.
        public static bool IsValidDefine(string token) => !string.IsNullOrEmpty(token) && Regex.IsMatch(token.Trim(), "^[A-Za-z_][A-Za-z0-9_]*$");

        [Tooltip("This game's remote content root, e.g. https://cdn.example.com/mygame/Content/v — " +
                 "the version number is appended directly to it. Required for hosts that build " +
                 "Addressables content; the build fails loudly rather than guessing.")]
        [SerializeField] private string remoteLoadRoot = "";

        public string RemoteLoadRoot
        {
            get => remoteLoadRoot;
            set => remoteLoadRoot = value;
        }

        // Mirrors EffectiveTestModeDefine, with the one difference the comment below spells out: an
        // invalid remote root has no safe default to degrade to, so this reports "not usable" (null)
        // rather than substituting something, and RequireRemoteLoadRoot turns that null into a throw.
        public string EffectiveRemoteLoadRoot => IsValidRemoteLoadRoot(remoteLoadRoot) ? remoteLoadRoot.Trim() : null;

        // No fail-safe here, deliberately. The test-mode define degrades to TEST_MODE because a
        // wrong-but-present define is safer than none; a CDN root has no such default — guessing
        // one would publish content to, or load it from, the wrong host. The ENDING is not
        // validated: both shipped games end in "v" because Compose appends the version directly,
        // but that is convention, not a requirement. The scheme comparison is ORDINAL and
        // case-INSENSITIVE: URL schemes are case-insensitive per RFC 3986, while culture-sensitive
        // matching treats some zero-width characters as ignorable and would accept a root that no
        // CDN can serve.
        public static bool IsValidRemoteLoadRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return false;

            var trimmed = root.Trim();
            return trimmed.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase);
        }

#if UNITY_EDITOR
        // Same discovery contract as AddressablesVersionStore: the asset's location is the game's
        // choice, scoped to Assets/ so a stray copy inside a package is never picked up.
        public static BuildSystemSettings LoadOrNull()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(BuildSystemSettings), new[] { "Assets" });
            if (guids.Length == 0) return null;

            var paths = System.Array.ConvertAll(guids, AssetDatabase.GUIDToAssetPath);
            System.Array.Sort(paths, System.StringComparer.Ordinal);
            if (paths.Length > 1)
                Debug.LogError($"[Build] Multiple BuildSystemSettings assets found ({string.Join(", ", paths)}) — using the first; delete the extras.");

            return AssetDatabase.LoadAssetAtPath<BuildSystemSettings>(paths[0]);
        }

        // The project's kind-ruled test define; DEFAULT_TEST_MODE_DEFINE when no asset exists.
        public static string ResolveTestModeDefine()
        {
            var settings = LoadOrNull();
            return settings ? settings.EffectiveTestModeDefine : DEFAULT_TEST_MODE_DEFINE;
        }

        // Symmetric with UnicoBuildPaths.RequireVersion: fails loud. The Addressables profile
        // evaluator SWALLOWS exceptions thrown by a property getter and substitutes the token's own
        // text, so this throw does not surface as an exception — it surfaces as an unresolved
        // binding, which is exactly what ProfileBindingRule and RemotePathCheck exist to catch.
        public static string RequireRemoteLoadRoot()
        {
            var settings = LoadOrNull();
            if (!settings)
                throw new System.InvalidOperationException(
                    "No BuildSystemSettings asset exists, so RemoteLoadRoot cannot be resolved. Create " +
                    "the asset via Assets > Create > Unico > Build > Build System Settings (convention: " +
                    "Assets/Settings/Build/) and set this game's remote content root.");

            // The two cases need different instructions: creating the asset is useless advice to
            // someone who already has one, and naming the path and the rejected value is the whole
            // difference between "fix this field" and "hunt for the field".
            var root = settings.EffectiveRemoteLoadRoot;
            if (root == null)
                throw new System.InvalidOperationException(
                    $"BuildSystemSettings.RemoteLoadRoot is not an http(s) URL: \"{settings.RemoteLoadRoot}\" " +
                    $"in {AssetDatabase.GetAssetPath(settings)}. Set this game's remote content root, " +
                    "e.g. https://cdn.example.com/mygame/Content/v");

            return root;
        }
#endif
    }
}
