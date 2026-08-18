# VersionTracker Glue sample

The host-side coupling between this build system and `com.unicostudio.versiontracker`:
a `PostSuccess` step that, after every successful **player** build, verifies the tracker's
metadata JSON was freshly written for THIS run and attaches it to the build result as a
`Metadata` artifact (visible in the panel's artifact list and in the CI result JSON).

Neither package references the other — that is deliberate (each stays independently
consumable). If your project uses both, this ~90-line file is the one place that knows
they coexist. Without it nothing breaks loudly; you silently lose two things:

- **Freshness verification.** The tracker's own hook still writes JSON during
  `BuildPlayer`, but a silently failed hook goes undetected: the tracker outputs are
  committed to git per shipped release and the version does not auto-increment, so a
  rebuild of the same version finds the PREVIOUS build's file on disk. This step's
  `Stale`/`Missing` verdicts exist precisely because file existence lies here.
- **The artifact record.** The metadata path is never attached via
  `UnicoBuildService.AppendPostArtifact`, so it is absent from the panel's artifact list
  and from `result.json` (`TypedArtifacts`, kind `Metadata`).

## Requirements

- `com.unicostudio.versiontracker` **>= 1.6.0** installed (the step calls its
  `GetBuildInfoPath`, and 1.6.0 made the export synchronous — the file is guaranteed on
  disk by the time `PostSuccess` runs). Importing this sample without the tracker
  installed produces compile errors — that is the missing dependency, not a sample bug.
- The convention that gives `Stale` meaning: hosts **commit** their
  `UnicoVersionTracker/*.json` outputs per shipped release (they record SHIPPED
  binaries — never regenerate them into a commit).

## Import

Package Manager > Unico Build System > Samples > **VersionTracker Glue** > Import.
Keep exactly ONE copy in the project (an imported sample AND a hand-copied duplicate
reach TypeCache twice; the duplicate is logged and ignored, but it is dead weight).

A `Missing`/`Stale` verdict throws: the runner reports "Post step FAILED" on the result
WITHOUT flipping the build's `Success` — a metadata problem is worth surfacing, not
worth failing a shipped build over.
