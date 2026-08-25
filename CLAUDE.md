# unity-build-tools — working agreement

UPM monorepo. Repo root is a REAL Unity 6000.0.62f1 dev project (open it to compile/test the
packages in place). Two packages under `Packages/`:

- `com.unicostudio.buildsystem` (current: 0.13.0) — panel + headless build pipeline, full
  EditMode suite under `Tests/Editor/`.
- `com.unicostudio.versiontracker` (current: 1.7.3) — build-info export; EditMode suite (21)
  since 1.7.0, wired into the CI gate.

The user (Tolgahan) communicates in **Turkish**; write code/docs/commits in English.
Approval gates: pushes, merges, and tag publishes happen ONLY on explicit instruction.
On any problem: STOP and consult — do not improvise around a failure.

## Iron rules

1. **Package defects are fixed HERE, never in a consumer.** Hosts consume read-only via pinned
   tags; the fix flow is: fix in this repo → test → version bump + CHANGELOG + tag → push tag →
   bump pins in hosts → verify each host's `packages-lock.json` hash equals the tag commit.
2. **The package never reconstructs a convention it does not own.** Host-specific glue stays in
   host code. The one glue file (`Assets/02_Scripts/Editor/VersionTrackerArtifactStep.cs`) is
   deliberately duplicated in both consumers and MUST stay byte-identical (`cmp`); BTA's copy is
   the origin, BT5 mirrors it (tests differ only by namespace).
3. **Measure, don't assert.** Claims about behavior get a live measurement or a test; new tests
   watch red first (a CS error counts as red) and get a mutation probe on committed ground.
4. Gate/verification builds' side effects are ALWAYS reverted (version stores, content-state
   bins, crashlytics ids, Firebase poms, tracker JSON archives, font SDFs). Committed
   `UnicoVersionTracker/*.json` files in hosts record SHIPPED binaries — never regenerate them
   into a commit.

## Release ritual (per package)

`package.json` version bump + `CHANGELOG.md` entry + commit + tag `<package-id>/<version>`
(e.g. `com.unicostudio.buildsystem/0.13.0`) + push main and the tag. Then per host:
edit the manifest pin, run headless `-runTests -testPlatform EditMode`, verify lock hash ==
tag commit, commit manifest+lock together.

## Verification discipline

- Headless runs need the target project's Unity editor CLOSED (check `pgrep -f MacOS/Unity`).
- `-batchmode -nographics -quit` for compile checks; `-runTests -testPlatform EditMode
  -testResults <xml>` for suites (never combine `-runTests` with `-quit`).
- Suite baselines (2026-08-25, post pin-bump to buildsystem 0.13.0 / versiontracker 1.7.3):
  this repo 327/0 (buildsystem 305 + versiontracker 21 + 1 Unity Addressables doc stub);
  BTA 423/0 (host 118 + buildsystem 305 — host keeps growing with team PRs: +52 PR #7,
  +2 PR #8); BT5 626/0 (host 321 + buildsystem 305 — +3 landed with fb07c00c's level-order
  tests). A count drift is a finding, not noise — name it.

## Consumers (hosts)

- BTA — `/Users/tolgahankurtdere/Documents/GitHub/g-brain_test_legacy` (live game). Its
  `docs/superpowers/` holds the program history (boundary spec, extraction spec, audits).
- BT5 — `/Users/tolgahankurtdere/Documents/GitHub/g-brain_test_5` (adopted 2026-08-07, C4).
- Host-specific adoption docs live in the host repo; package/program docs live here.
- Every panel/CLI build strips `UNITY_MCP_READY` (per-config StripDefines) and restores it
  after; ungated MCP references in host code break at build compile time — gate with
  `#if UNITY_EDITOR && UNITY_MCP_READY` (precedent: BTA `Tool_AudioSetup`, BT5 `Tool_*`).
- Host landmines: `AutoKeystore.cs` committed plaintext passwords are DELIBERATE (fleet
  hand-off); the tracker's red `LogError` line is deliberate; a leftover directory under
  `Packages/` shadows a manifest git entry (watch for `.DS_Store` keeping dirs alive).

## Headless CI contract (proven end-to-end 2026-08-01)

`-batchmode -executeMethod UnicoStudio.BuildSystem.Editor.UnicoBuildCli.Build` — NEVER pass
`-quit` (CiCompletionWatcher exits the process: 0=success, 1=failure, 2=timeout/parse-error).
Result JSON at `-resultFile` (default `Builds/result.json`). Keystore secrets via
`UNICO_KEYSTORE_PASS` / `UNICO_KEYALIAS_PASS` env vars. CI never bumps versions unless
`-bumpBuildCode` / `-bumpAddressables` are passed. Use `-versionName` (not `-version` — Unity
owns that flag).

## CI program backlog (updated 2026-08-18)

Resolved since the 2026-08-07 audit: content-state lineage (bins committed to hosts, CI
invariant 6 guards them; BT5 Android lineage KNOWN-LOST — never Update-Previous it), the iOS
Test build debt (`#if TEST_MODE` compiles clean for iOS), Q1 (BuildPlayer pumps zero update
ticks in batchmode; recorded in `Editor/Core/_Info.md`), the versiontracker suite, the
Unity-MCP define poisoning (0.11.0 DefineGuard + 0.12.0 StripPackages + 0.12.1
DefineReassertWatcher; both hosts adopted, mains merged), and the strict-Release batchmode
run (exercised green 2026-08-25 by the build-lane E2E, `ci/run-player-build.sh`).

Open:
1. Runner bring-up: self-hosted Mac registered with the `unity-mac` label + Unity Pro seat +
   `CI_REPO_READ_TOKEN` secret; the first `workflow_dispatch` validates both lanes.
2. CDN upload as a PostSuccess step — `CdnReminderCheck`'s strict-exemption is premised on CI
   doing the upload; needs the DevOps SFTP details. Ride-along niceties queued for the next
   releases (deliberately not worth their own ritual): UnicoBuildCli wrapping `Start` in a
   try/catch that writes a failure result before Exit(1) (see `Editor/Core/_Info.md` item 6),
   and versiontracker's missing `"unity"` field in package.json.
3. ~~Q2 (InitializeOnLoad ordering) and Q6 (pre-Start throw → timeout)~~ — both MEASURED
   2026-08-25 and recorded in `Editor/Core/_Info.md`: the watcher decides before the runner
   drains (one-tick deadline boundary, accepted by design), and a throw escaping `Start` is
   killed by Unity itself in ~10 s with exit 1 (no hang; the build-lane runner already fails
   loudly on the missing result). No new CI gate needed beyond the existing outer watchdog.
