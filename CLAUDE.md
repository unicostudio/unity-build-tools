# unity-build-tools — working agreement

UPM monorepo. Repo root is a REAL Unity 6000.0.62f1 dev project (open it to compile/test the
packages in place). Two packages under `Packages/`:

- `com.unicostudio.buildsystem` (current: 0.10.2) — panel + headless build pipeline, full
  EditMode suite under `Tests/Editor/`.
- `com.unicostudio.versiontracker` (current: 1.6.0) — build-info export; **has no tests yet**
  (known gap, CI matrix has an empty cell until fixed).

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
(e.g. `com.unicostudio.buildsystem/0.10.2`) + push main and the tag. Then per host:
edit the manifest pin, run headless `-runTests -testPlatform EditMode`, verify lock hash ==
tag commit, commit manifest+lock together.

## Verification discipline

- Headless runs need the target project's Unity editor CLOSED (check `pgrep -f MacOS/Unity`).
- `-batchmode -nographics -quit` for compile checks; `-runTests -testPlatform EditMode
  -testResults <xml>` for suites (never combine `-runTests` with `-quit`).
- Suite baselines (2026-08-07): this repo 247/0 (buildsystem 246 + 1 Unity Addressables doc
  stub); BTA 310/0; BT5 564/0. A count drift is a finding, not noise — name it.

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

## CI program backlog (pre-CI audit, 2026-08-07)

1. Content-state lineage decision FIRST: `addressables_content_state.bin` is gitignored in both
   hosts (machine-local) — CI content builds need a storage/retrieval strategy.
2. CDN upload as a PostSuccess step — `CdnReminderCheck`'s strict-exemption is premised on CI
   doing the upload; building CI without it falsifies that premise.
3. First real gate should be an iOS Test build — `#if TEST_MODE` blocks have never compiled for
   iOS (v0.7.0 verification debt); a strict-Release batchmode run is also still unexercised.
4. The permanent open questions in `Editor/Core/_Info.md` (Q1 update-pump re-entrancy, Q2
   InitializeOnLoad ordering, Q6 pre-Start throw → timeout) want live experiments — design CI
   gates to answer them.
5. versiontracker test suite (empty CI cell); BT5 `feature/economy-rebalance-migration` is
   READY-TO-MERGE but unmerged (host-side, user's call).
