#!/usr/bin/env bash
# CI lane: headless Unico player/content build via UnicoBuildCli.
#
# Orchestrator-agnostic by design: no GitHub-specific behavior; callable from any
# CI system or a plain shell. All logic lives here — the CI layer only wires
# parameters (see the orchestrator-agnostic rule in the CI program docs).
#
# Contract notes (verified against buildsystem 0.12.4 sources, live E2E in a host clone):
#   - NEVER pass -quit: the reload chain needs the editor alive across the job's
#     domain reloads; CiCompletionWatcher exits the process (0=success, 1=build
#     failure incl. preflight Block, 2=timeout/parse-error/conclusion trouble —
#     on exit 2 the result's Error prefix distinguishes which).
#   - The result JSON is the primary truth; this script fails on a missing or
#     inconsistent result even when the exit code looks right, verifies every
#     recorded artifact actually exists on disk, and prints the steps so CI logs
#     tell the story without opening the editor log.
#   - -buildTarget is derived from --platform and passed to Unity: launching on
#     the wrong target costs a second full reimport, and under --strict the
#     PlatformMatchCheck Warn is a Block.
#   - -versionName is required for CI runs: BuildRequest.VersionName defaults to
#     "" and VersionFormatCheck Blocks a non-semver value by design.
#   - Keystore secrets travel via UNICO_KEYSTORE_PASS / UNICO_KEYALIAS_PASS in
#     the environment; this script never sees or logs their values.
#   - Nothing bumps unless explicitly flagged (CI default of the CLI itself).
#
# Exit codes: 0 = build succeeded and all assertions green; 1 = any failure.

set -euo pipefail

PROJECT=""
PLATFORM=""
KIND=""
VERSION_NAME=""
BUILD_CODE=""
BUMP_BUILD_CODE=0
BUMP_ADDRESSABLES=0
BUILD_PLAYER="true"
BUILD_ADDRESSABLES="false"
ADDRESSABLES_MODE=""
OUTPUTS=""
LABEL=""
OUTPUT_FOLDER=""
STRICT=0
RESULT_FILE=""
TIMEOUT_MINUTES=90
UNITY_BIN=""

usage() {
  cat <<'EOF'
Usage: run-player-build.sh --project <path> --platform <Android|iOS> --kind <Test|Release> --version-name <semver> [options]
  --project <path>            Unity project root (must contain ProjectSettings/ProjectVersion.txt)
  --platform <p>              Android | iOS (also sets Unity's -buildTarget to match)
  --kind <k>                  Test | Release
  --version-name <v>          Version name to build (strict semver MAJOR.MINOR.PATCH)
  --build-code <n>            Pin the build code / build number (disables bumping)
  --bump-build-code           Bump the build code (CI default is NO bump)
  --bump-addressables         Bump the Addressables content version (CI default is NO bump)
  --build-player <bool>       Build the player (default: true)
  --build-addressables <bool> Build Addressables content (default: false)
  --addressables-mode <m>     UpdatePrevious | NewBuild (only with --build-addressables true)
  --outputs <list>            Comma list of Apk,Aab,XcodeProject (required when building the player)
  --label <s>                 Artifact label
  --output-folder <path>      Build output folder (default: the pipeline's Builds/<Platform>/<Kind>/)
  --strict                    -strictWarnings: non-exempt preflight Warns block
  --result-file <path>        Result JSON path (default: <project>/Builds/result.json)
  --timeout-minutes <n>       In-editor deadline for CiCompletionWatcher (default: 90);
                              the script's outer watchdog adds 10 minutes on top
  --unity <path>              Unity binary override (default: resolved from ProjectVersion.txt via Unity Hub)
EOF
}

fail() { echo "FAIL: $*" >&2; exit 1; }
need_value() { [[ $# -ge 2 ]] || { usage >&2; fail "missing value for $1"; } }
is_uint() { [[ "$1" =~ ^[0-9]+$ ]]; }
is_bool() { [[ "$1" == "true" || "$1" == "false" ]]; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --project) need_value "$@"; PROJECT="$2"; shift 2 ;;
    --platform) need_value "$@"; PLATFORM="$2"; shift 2 ;;
    --kind) need_value "$@"; KIND="$2"; shift 2 ;;
    --version-name) need_value "$@"; VERSION_NAME="$2"; shift 2 ;;
    --build-code) need_value "$@"; BUILD_CODE="$2"; shift 2 ;;
    --bump-build-code) BUMP_BUILD_CODE=1; shift ;;
    --bump-addressables) BUMP_ADDRESSABLES=1; shift ;;
    --build-player) need_value "$@"; BUILD_PLAYER="$2"; shift 2 ;;
    --build-addressables) need_value "$@"; BUILD_ADDRESSABLES="$2"; shift 2 ;;
    --addressables-mode) need_value "$@"; ADDRESSABLES_MODE="$2"; shift 2 ;;
    --outputs) need_value "$@"; OUTPUTS="$2"; shift 2 ;;
    --label) need_value "$@"; LABEL="$2"; shift 2 ;;
    --output-folder) need_value "$@"; OUTPUT_FOLDER="$2"; shift 2 ;;
    --strict) STRICT=1; shift ;;
    --result-file) need_value "$@"; RESULT_FILE="$2"; shift 2 ;;
    --timeout-minutes) need_value "$@"; TIMEOUT_MINUTES="$2"; shift 2 ;;
    --unity) need_value "$@"; UNITY_BIN="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) usage >&2; fail "unknown argument: $1" ;;
  esac
done

# Validate cheap inputs BEFORE the expensive Unity run.
[[ -n "$PROJECT" ]] || { usage >&2; fail "--project is required"; }
case "$PLATFORM" in
  Android) BUILD_TARGET="Android" ;;
  iOS) BUILD_TARGET="iOS" ;;
  "") usage >&2; fail "--platform is required" ;;
  *) fail "--platform must be Android or iOS, got: $PLATFORM" ;;
esac
[[ "$KIND" == "Test" || "$KIND" == "Release" ]] || fail "--kind must be Test or Release, got: '${KIND}'"
[[ -n "$VERSION_NAME" ]] || fail "--version-name is required (BuildRequest defaults to '' and VersionFormatCheck blocks it)"
[[ "$VERSION_NAME" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || fail "--version-name must be strict semver MAJOR.MINOR.PATCH, got: $VERSION_NAME"
[[ -z "$BUILD_CODE" ]] || { is_uint "$BUILD_CODE" && [[ "$BUILD_CODE" -gt 0 ]]; } || fail "--build-code must be a positive integer, got: $BUILD_CODE"
is_bool "$BUILD_PLAYER" || fail "--build-player expects true|false, got: $BUILD_PLAYER"
is_bool "$BUILD_ADDRESSABLES" || fail "--build-addressables expects true|false, got: $BUILD_ADDRESSABLES"
[[ "$BUILD_PLAYER" == "false" || -n "$OUTPUTS" ]] || fail "--outputs is required when building the player (OutputSelectionCheck blocks OutputKind.None)"
is_uint "$TIMEOUT_MINUTES" && [[ "$TIMEOUT_MINUTES" -gt 0 ]] || fail "--timeout-minutes must be a positive integer, got: $TIMEOUT_MINUTES"
PROJECT="$(cd "$PROJECT" && pwd)" || fail "project path does not exist: $PROJECT"

VERSION_FILE="$PROJECT/ProjectSettings/ProjectVersion.txt"
[[ -f "$VERSION_FILE" ]] || fail "not a Unity project (missing $VERSION_FILE)"

# A UnityLockfile is only meaningful while a live process holds it (see the
# test runner for the rationale).
LOCKFILE="$PROJECT/Temp/UnityLockfile"
if [[ -f "$LOCKFILE" ]]; then
  if lsof -- "$LOCKFILE" >/dev/null 2>&1; then
    fail "project is open in a live Unity process (Temp/UnityLockfile is held). Close it or run against a dedicated CI workdir."
  fi
  echo "   note: removing stale UnityLockfile (no live holder — a previous run was killed)"
  rm -f "$LOCKFILE"
fi

if [[ -z "$UNITY_BIN" ]]; then
  EDITOR_VERSION="$(awk '/^m_EditorVersion:/{gsub(/\r/,""); print $2}' "$VERSION_FILE")"
  [[ -n "$EDITOR_VERSION" ]] || fail "could not read m_EditorVersion from $VERSION_FILE"
  UNITY_BIN="/Applications/Unity/Hub/Editor/$EDITOR_VERSION/Unity.app/Contents/MacOS/Unity"
fi
[[ -x "$UNITY_BIN" ]] || fail "Unity binary not found: $UNITY_BIN (install this version via Unity Hub, or pass --unity)"

RESULT_FILE="${RESULT_FILE:-$PROJECT/Builds/result.json}"
mkdir -p "$(dirname "$RESULT_FILE")"
LOG_FILE="$(dirname "$RESULT_FILE")/player-build.log"
rm -f "$RESULT_FILE"

CLI_ARGS=(
  -executeMethod UnicoStudio.BuildSystem.Editor.UnicoBuildCli.Build
  -platform "$PLATFORM" -kind "$KIND" -versionName "$VERSION_NAME"
  -buildPlayer "$BUILD_PLAYER" -buildAddressables "$BUILD_ADDRESSABLES"
  -resultFile "$RESULT_FILE" -timeoutMinutes "$TIMEOUT_MINUTES"
)
[[ -z "$OUTPUTS" ]] || CLI_ARGS+=(-outputs "$OUTPUTS")
[[ -z "$BUILD_CODE" ]] || CLI_ARGS+=(-buildCode "$BUILD_CODE")
[[ "$BUMP_BUILD_CODE" -eq 0 ]] || CLI_ARGS+=(-bumpBuildCode)
[[ "$BUMP_ADDRESSABLES" -eq 0 ]] || CLI_ARGS+=(-bumpAddressables)
[[ -z "$ADDRESSABLES_MODE" ]] || CLI_ARGS+=(-addressablesMode "$ADDRESSABLES_MODE")
[[ -z "$LABEL" ]] || CLI_ARGS+=(-label "$LABEL")
[[ -z "$OUTPUT_FOLDER" ]] || CLI_ARGS+=(-outputFolder "$OUTPUT_FOLDER")
[[ "$STRICT" -eq 0 ]] || CLI_ARGS+=(-strictWarnings)

echo "== Player build: $PROJECT"
echo "   unity:    $UNITY_BIN"
echo "   request:  $PLATFORM/$KIND v$VERSION_NAME player=$BUILD_PLAYER addressables=$BUILD_ADDRESSABLES outputs=${OUTPUTS:-none} strict=$STRICT"
echo "   result:   $RESULT_FILE"
echo "   timeout:  ${TIMEOUT_MINUTES}m (+10m outer watchdog)"

# Unity gets its own session/process group (setsid) so kill -- -PID reaches the
# helper tree. No -quit — CiCompletionWatcher owns process exit.
python3 -c 'import os, sys; os.setsid(); os.execv(sys.argv[1], sys.argv[1:])' \
  "$UNITY_BIN" \
  -batchmode \
  -projectPath "$PROJECT" \
  -buildTarget "$BUILD_TARGET" \
  -logFile "$LOG_FILE" \
  "${CLI_ARGS[@]}" &
UNITY_PID=$!

kill_unity_group() {
  kill -TERM -- "-$UNITY_PID" 2>/dev/null || true
  sleep 10
  kill -KILL -- "-$UNITY_PID" 2>/dev/null || true
}
trap 'kill_unity_group' INT TERM

# Outer watchdog is a fallback ABOVE the in-editor CiCompletionWatcher deadline:
# the watcher exits 2 gracefully with a result file; this only fires if the
# editor wedged so hard the watcher itself never ran.
DEADLINE=$((SECONDS + (TIMEOUT_MINUTES + 10) * 60))
while kill -0 "$UNITY_PID" 2>/dev/null; do
  if (( SECONDS >= DEADLINE )); then
    echo "Watchdog: deadline reached, terminating the Unity process group (pgid $UNITY_PID)" >&2
    kill_unity_group
    wait "$UNITY_PID" 2>/dev/null || true
    fail "Unity did not finish within $((TIMEOUT_MINUTES + 10)) minutes and the in-editor watcher never concluded (log: $LOG_FILE)"
  fi
  sleep 10
done
UNITY_EXIT=0
wait "$UNITY_PID" || UNITY_EXIT=$?
trap - INT TERM
echo "   unity exit code: $UNITY_EXIT"

# The result JSON is the primary truth — verify it exists, is consistent with
# the exit code, and that every recorded artifact is really on disk.
[[ -f "$RESULT_FILE" ]] || fail "no result JSON was produced (unity exit $UNITY_EXIT, log: $LOG_FILE)"

python3 - "$RESULT_FILE" "$UNITY_EXIT" "$PROJECT" <<'PYEOF'
import json
import os
import sys

result_path, unity_exit, project = sys.argv[1], int(sys.argv[2]), sys.argv[3]
try:
    with open(result_path) as f:
        r = json.load(f)
except (OSError, json.JSONDecodeError) as e:
    sys.exit(f"FAIL: result JSON did not parse: {e}")

success = bool(r.get("Success"))
error = r.get("Error", "")
print(f"   result: Success={success} DurationSeconds={r.get('DurationSeconds', 0):.0f} "
      f"VersionName={r.get('VersionName', '')!r} BuildCode={r.get('BuildCode', '')!r} "
      f"AddressablesVersion={r.get('AddressablesVersion', -1)}")
for step in r.get("Steps", []):
    print(f"   step: {step}")
for warning in r.get("Warnings", []):
    print(f"   warning: {warning}")

errors = []
artifacts = r.get("TypedArtifacts", [])
for artifact in artifacts:
    path = artifact.get("Path", "")
    resolved = path if os.path.isabs(path) else os.path.normpath(os.path.join(project, path))
    exists = os.path.exists(resolved)
    print(f"   artifact: kind={artifact.get('Kind')} path={path} exists={exists}")
    if not exists:
        errors.append(f"artifact recorded but missing on disk: {path}")

if unity_exit == 0 and not success:
    errors.append("contract violation: exit 0 but Success=false in the result")
if unity_exit != 0 and success:
    errors.append(f"contract violation: exit {unity_exit} but Success=true in the result")
if unity_exit == 2:
    errors.append(f"watcher concluded abnormally (exit 2): {error or '(no Error recorded)'}")
elif not success:
    errors.append(f"build failed: {error or '(no Error recorded)'}")
elif not artifacts and os.environ.get("UNICO_EXPECT_ARTIFACTS", "1") != "0":
    errors.append("build succeeded but recorded zero artifacts (set UNICO_EXPECT_ARTIFACTS=0 for content-only runs)")

if errors:
    sys.exit("FAIL: " + "; ".join(errors))
print("PASS: build succeeded, result consistent, all artifacts on disk")
PYEOF
