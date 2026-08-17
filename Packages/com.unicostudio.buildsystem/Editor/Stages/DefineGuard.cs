using System.Collections.Generic;
using System.Linq;

namespace UnicoStudio.BuildSystem.Editor
{
    /// <summary>
    /// Last-moment re-verification of the build's define plan, run by PlayerBuildStage
    /// right before BuildPipeline.BuildPlayer.
    ///
    /// Why it exists (measured live, 2026-08-13, BTA iOS Test build): the global define
    /// write in ConfigureDefinesStage queues a domain reload, and that reload wakes
    /// third-party [InitializeOnLoad] code that can silently fight the plan — a package's
    /// dependency resolver re-added the very define the strip had just removed, and its
    /// bridge shipped into the player's IL2CPP output (see CHANGELOG 0.11.0). The stage's
    /// own report ("Defines -<define>") was truthful; the world changed after it.
    ///
    /// The plan is recorded into BuildContext.Data (persisted through BuildJobState, so
    /// it survives the reload) and checked against the platform's ACTUAL globals at
    /// player-build time. A violation fails the build loudly — never ship silently.
    /// </summary>
    internal static class DefineGuard
    {
        internal const string MustBeAbsentKey = "DefineGuard.MustBeAbsent";
        internal const string MustBePresentKey = "DefineGuard.MustBePresent";

        /// <summary>
        /// Records the define plan for the player-build guard. MustBeAbsent uses
        /// ForbidInPlayer — the full intended-absent set — rather than RemoveFromGlobal,
        /// which is empty exactly when the define was already absent at stage time (the
        /// case where a later third-party re-add would otherwise go unguarded).
        /// </summary>
        internal static void RecordPlan(IDictionary<string, string> data,
            IReadOnlyList<string> forbidInPlayer, IReadOnlyList<string> addToGlobal)
        {
            if (forbidInPlayer.Count > 0) data[MustBeAbsentKey] = string.Join(";", forbidInPlayer);
            if (addToGlobal.Count > 0) data[MustBePresentKey] = string.Join(";", addToGlobal);
        }

        internal static List<string> ParsePlanList(IReadOnlyDictionary<string, string> data, string key)
        {
            return data.TryGetValue(key, out var joined) && !string.IsNullOrEmpty(joined)
                ? joined.Split(';').Where(s => s.Length > 0).ToList()
                : new List<string>();
        }

        /// <summary>
        /// Pure decision core (unit-tested): the violation message, or null when the
        /// globals still honor the plan. Absent-side violations report first — a
        /// re-added stripped define is the shipping hazard; a vanished required define
        /// is the broken-build hazard.
        /// </summary>
        internal static string FindViolation(IReadOnlyList<string> mustBeAbsent,
            IReadOnlyList<string> mustBePresent, IReadOnlyList<string> currentGlobals)
        {
            var back = mustBeAbsent.Where(currentGlobals.Contains).ToList();
            if (back.Count > 0)
                return $"stripped define(s) {string.Join(",", back)} are back in the platform globals — " +
                       "something re-added them after this build's strip (third-party [InitializeOnLoad] " +
                       "code reacting to the reload is the measured culprit); refusing to compile the player with them";

            var gone = mustBePresent.Where(d => !currentGlobals.Contains(d)).ToList();
            if (gone.Count > 0)
                return $"required define(s) {string.Join(",", gone)} vanished from the platform globals " +
                       "after this build set them; refusing to compile the player without them";

            return null;
        }
    }
}
