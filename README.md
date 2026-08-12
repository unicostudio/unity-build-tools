# unity-build-tools

Unity build & release tooling packages (UPM monorepo). The repo root is a real Unity project
(6000.0.62f1) so every package here can be compiled, tested and exercised **in place** — including
the real player builds that post-build hooks need to fire at all.

Packages live under `Packages/` as embedded packages:

| Package | What it is |
|---|---|
| `com.unicostudio.versiontracker` | Tracks third-party SDK versions and build details, exported to JSON after every successful build. Moved verbatim from `unicostudio/UnicoVersionTracker` (its history stays there). |
| `com.unicostudio.buildsystem` | Single-panel build automation for Unico mobile games: reload-safe build jobs, preflight checks, versioned Addressables content updates, artifact naming, crash recovery, headless CLI. Moved verbatim from `g-brain_test_legacy` (its history stays there). |

## The golden rules

- **Install by git URL, pin to a tag.** `main` moves as packages evolve — always append
  `#<package-id>/<version>` for anything past local experimentation.
- **Tags are per package:** `com.unicostudio.versiontracker/1.7.0`, `com.unicostudio.buildsystem/0.10.2`. A release = version bump in
  the package's `package.json` + a CHANGELOG entry + the matching tag.
- **Packages must stay independently consumable.** A package may not reference another package in
  this repo unless it declares the dependency in its own `package.json`.

## Install

**com.unicostudio.versiontracker**

```
https://github.com/unicostudio/unity-build-tools.git?path=Packages/com.unicostudio.versiontracker
```

Pinned to a version (recommended):

```
https://github.com/unicostudio/unity-build-tools.git?path=Packages/com.unicostudio.versiontracker#com.unicostudio.versiontracker/1.7.0
```

> Migrating from the old repo? The package id, namespaces and layout are unchanged — replace the
> manifest value `https://github.com/unicostudio/UnicoVersionTracker.git` with the pinned URL
> above. One line, no code changes.

**com.unicostudio.buildsystem**

```
https://github.com/unicostudio/unity-build-tools.git?path=Packages/com.unicostudio.buildsystem
```

Pinned to a version (recommended):

```
https://github.com/unicostudio/unity-build-tools.git?path=Packages/com.unicostudio.buildsystem#com.unicostudio.buildsystem/0.10.2
```

> Consumers should also add `"testables": ["com.unicostudio.buildsystem"]` to their manifest —
> a git-URL package's EditMode tests run only when listed there (246 in buildsystem; versiontracker ships its own 21 under the same rule).

## Working in this repo

Open the repo root in Unity 6000.0.62f1. The dev project is deliberately minimal; each package's
own README/CHANGELOG documents its behavior. `Assets/Editor/DevBuild.cs` provides the headless
smoke build (`-executeMethod DevBuild.BuildAndroid`) used to verify that player-build hooks fire.
The `UnicoVersionTracker/` folder at the repo root is that tool's test output and is gitignored.
