using System;
using System.Globalization;
using UnityEngine;

namespace UnicoStudio.BuildSystem.Editor
{
    internal readonly struct VersionRowGating
    {
        public readonly bool CodeRowEnabled;
        public readonly bool AddressablesRowEnabled;
        public readonly bool EffectiveBumpCode;
        public readonly bool EffectiveBumpAddressables;

        public VersionRowGating(bool codeRowEnabled, bool addressablesRowEnabled,
            bool effectiveBumpCode, bool effectiveBumpAddressables)
        {
            CodeRowEnabled = codeRowEnabled;
            AddressablesRowEnabled = addressablesRowEnabled;
            EffectiveBumpCode = effectiveBumpCode;
            EffectiveBumpAddressables = effectiveBumpAddressables;
        }
    }

    // The panel's pure/testable surface. The window stays IMGUI plumbing; every decision a
    // test can pin lives here instead.
    internal static class PanelDecisions
    {
        // Every editor icon the panel renders — the window consumes THESE constants, so the
        // pinning test covers exactly what is on screen (a literal typed straight into the
        // window would escape the test; a constant cannot). Base names only, no explicit
        // "d_" prefix: IconContent auto-selects the skin variant, and a hardcoded dark-skin
        // glyph is near-invisible on the light skin. IconContent degrades to an EMPTY
        // content for an unknown name, so a typo would ship as an invisible hole — the test
        // resolves each name on the running Unity version.
        internal const string IconAndroid = "BuildSettings.Android.Small";
        internal const string IconIos = "BuildSettings.iPhone.Small";
        internal const string IconVersion = "editicon.sml";
        internal const string IconStages = "PlayButton";
        internal const string IconOutput = "Folder Icon";
        internal const string IconResultOk = "TestPassed";
        internal const string IconResultFailed = "TestFailed";

        internal static readonly string[] IconNames =
        {
            IconAndroid, IconIos, IconVersion, IconStages, IconOutput, IconResultOk, IconResultFailed,
        };

        // Version-row gating. VersionName is deliberately NOT gated: ApplyVersionStage runs
        // unconditionally in every job (measured — content-only jobs stamp the version too).
        // The bump toggles ARE gated by the stage that gives them meaning: bumping the player
        // build code with Build Player off burns a code no binary carries, and bumping the
        // content version with Build Addressables off is exactly the state
        // BumpConsistencyCheck warns about (the check stays for the CLI, which has no UI).
        // Effective values are returned so a greyed row can never smuggle a hidden true into
        // Start() — same force-false rule the window already applies when the Addressables
        // package is absent.
        internal static VersionRowGating GateVersionRows(bool buildPlayer, bool buildAddressables,
            bool requestedBumpCode, bool requestedBumpAddressables)
        {
            return new VersionRowGating(
                codeRowEnabled: buildPlayer,
                addressablesRowEnabled: buildAddressables,
                effectiveBumpCode: requestedBumpCode && buildPlayer,
                effectiveBumpAddressables: requestedBumpAddressables && buildAddressables);
        }

        // Short local timestamp for the result header, dd.MM.yyyy to match the artifact
        // filename convention. Legacy results carry "" and anything unparseable degrades to
        // "" — the header simply omits the stamp rather than rendering a lie.
        internal static string FormatStartedLocal(string startedUtcIso)
        {
            if (string.IsNullOrEmpty(startedUtcIso)) return "";
            if (!DateTime.TryParse(startedUtcIso, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var utc)) return "";
            return utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        }

        // Muted background tints for the kind accent (GUI.backgroundColor multiplier).
        // Purely presentational — values are human-verified, only the off-switch is contract:
        // with accents disabled the panel must render stock. Color is never the sole carrier;
        // the kind name stays in the popup and the section header text.
        internal static Color AccentFor(BuildKind kind, bool accentsEnabled)
        {
            if (!accentsEnabled) return Color.white;
            return kind == BuildKind.Release
                ? new Color(1f, 0.80f, 0.62f)
                : new Color(0.74f, 1f, 0.86f);
        }
    }
}
