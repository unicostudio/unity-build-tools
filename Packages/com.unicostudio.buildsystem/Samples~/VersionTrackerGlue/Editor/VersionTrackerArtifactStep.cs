// HOST GLUE — deliberately the only place in this project that knows both the build system and
// UnicoVersionTracker exist. The build system exposes AppendPostArtifact and knows nothing about
// the tracker; the tracker publishes GetBuildInfoPath and knows nothing about the build system;
// keeping their coupling in ~30 lines of host code is what keeps both packages independently
// consumable (a host without the tracker simply does not have this file).
//
// Requires tracker >= 1.6.0: GetBuildInfoPath is the tracker's own path composition (no
// reconstruction, no sanitizer drift), and the 1.6.0 hook writes the file SYNCHRONOUSLY during
// BuildPlayer, so by the time this post-success step runs the file is guaranteed on disk — if the
// hook ran at all. That last "if" is why existence alone is not the check.
using System;
using System.Globalization;
using System.IO;
using UnicoStudio.BuildSystem.Editor;
using UnicoStudio.UnicoLibs.VersionTracker;
using UnityEditor;

[UnicoBuildStep(BuildStepAnchor.PostSuccess)]
public sealed class VersionTrackerArtifactStep : IPostSuccessStep
{
    public string Name => "VersionTracker metadata";

    public void Execute(BuildResult result)
    {
        // Content-only jobs (Build Player off — the routine CDN content-update flow) drain the
        // PostSuccess queue too, but the tracker's hook only writes during BuildPlayer, so
        // without a player artifact every verdict below is a false alarm: Missing on a
        // never-shipped version, Stale against the committed archive of a shipped one. No
        // player, no metadata to attach; the runner's "OK" line then means "correctly did
        // nothing" (AppendPostStep is internal, so a distinct skip note is not available).
        if (!BuiltPlayer(result)) return;

        // Correct after a build by the package's own contract: DevStateSnapshot.Restore never
        // switches the active platform back, so the editor is still on the build target.
        var path = UnicoVersionExporter.GetBuildInfoPath(EditorUserBuildSettings.activeBuildTarget);

        switch (Judge(File.Exists(path), File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default,
                    result.StartedUtc))
        {
            case MetadataVerdict.Missing:
                // Throwing is the API: the runner reports "Post step FAILED: ..." on this run's
                // result without flipping the build's Success — a metadata problem is worth
                // surfacing, not worth failing a shipped build over.
                throw new FileNotFoundException($"tracker metadata was not written: {path}");
            case MetadataVerdict.Stale:
                // Existence lies here: the outputs are committed to git per release and the
                // version does not auto-increment, so a rebuild of the same version finds the
                // PREVIOUS build's file on disk even when this run's hook silently failed. Same
                // freshness pattern PlayerBuildStage applies to the symbols archive.
                throw new InvalidOperationException(
                    $"tracker metadata is stale — left by an earlier build of the same version: {path}");
            default:
                UnicoBuildService.AppendPostArtifact(result.StartedUtc, path, ArtifactKind.Metadata);
                break;
        }
    }

    internal enum MetadataVerdict { Missing, Stale, Fresh }

    // Pure decision core (host-tested): a job built a player iff its result records a player
    // artifact — Apk, Aab or XcodeProject; SymbolsZip/Metadata/AddressablesContent are sidecars.
    // Deliberately TypedArtifacts-only: a post queue is armed and drained by the same package
    // version, so a padded-Unknown legacy payload never reaches this step.
    internal static bool BuiltPlayer(BuildResult result)
    {
        foreach (var artifact in result.TypedArtifacts)
        {
            switch (artifact.Kind)
            {
                case ArtifactKind.Apk:
                case ArtifactKind.Aab:
                case ArtifactKind.XcodeProject:
                    return true;
            }
        }
        return false;
    }

    // Pure decision core (host-tested). An unstamped result throws FormatException by design:
    // it identifies no run, and attributing an artifact to a guess is worse than a loud FAILED.
    internal static MetadataVerdict Judge(bool exists, DateTime lastWriteUtc, string startedUtcIso)
    {
        var startedUtc = DateTime.Parse(startedUtcIso, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        if (!exists) return MetadataVerdict.Missing;
        return lastWriteUtc < startedUtc ? MetadataVerdict.Stale : MetadataVerdict.Fresh;
    }
}
