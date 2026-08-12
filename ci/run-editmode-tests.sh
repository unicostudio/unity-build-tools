#!/usr/bin/env bash
# CI lane: headless Unity EditMode test run with count assertions.
#
# Orchestrator-agnostic by design: no GitHub-specific behavior; callable from any
# CI system or a plain shell. All logic lives here — the CI layer only wires
# parameters (see the orchestrator-agnostic rule in the CI program docs).
#
# Contract notes (verified against buildsystem 0.10.2 sources, 2026-08-08):
#   - -runTests must NEVER be combined with -quit.
#   - The result XML is the primary truth; the Unity exit code is secondary
#     (a run can exit 0 without producing results — we fail on missing XML).
#   - Assert the per-assembly PASSED count, not just totals: NUnit keeps
#     result="Passed" and the total unchanged when a test is [Ignore]'d, so
#     totals alone are a silent count-drift vector. Skipped/inconclusive
#     tests fail the gate — a drift is a finding, never silently absorbed.
#   - Unity runs in its own process group (setsid) so the watchdog and signal
#     traps can kill the whole tree (AssetImportWorker etc.), and a stale
#     UnityLockfile with no live holder is cleaned up, not fatal.
#
# Exit codes: 0 = all assertions green; 1 = any failure (message says which).

set -euo pipefail

PROJECT=""
EXPECTED_ASSEMBLIES=()
EXPECTED_COUNTS=()
EXPECTED_TOTAL=""
RESULTS_DIR=""
TIMEOUT_MINUTES=30
UNITY_BIN=""

usage() {
  cat <<'EOF'
Usage: run-editmode-tests.sh --project <path> [options]
  --project <path>            Unity project root (must contain ProjectSettings/ProjectVersion.txt)
  --expected-assembly <dll>   Test assembly to assert on; repeatable, each paired in order
                              with the matching --expected-count
  --expected-count <n>        Expected PASSED test count for the paired --expected-assembly
  --expected-total <n>        Expected project-wide test count (optional; PackageCache-coupled, prefer per-assembly)
  --results-dir <path>        Where to write editmode-results.xml and editmode.log (default: <project>/CiResults)
  --timeout-minutes <n>       Outer watchdog for the Unity process (default: 30)
  --unity <path>              Unity binary override (default: resolved from ProjectVersion.txt via Unity Hub)
EOF
}

fail() { echo "FAIL: $*" >&2; exit 1; }
need_value() { [[ $# -ge 2 ]] || { usage >&2; fail "missing value for $1"; } }
is_uint() { [[ "$1" =~ ^[0-9]+$ ]]; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --project) need_value "$@"; PROJECT="$2"; shift 2 ;;
    --expected-assembly) need_value "$@"; EXPECTED_ASSEMBLIES+=("$2"); shift 2 ;;
    --expected-count) need_value "$@"; EXPECTED_COUNTS+=("$2"); shift 2 ;;
    --expected-total) need_value "$@"; EXPECTED_TOTAL="$2"; shift 2 ;;
    --results-dir) need_value "$@"; RESULTS_DIR="$2"; shift 2 ;;
    --timeout-minutes) need_value "$@"; TIMEOUT_MINUTES="$2"; shift 2 ;;
    --unity) need_value "$@"; UNITY_BIN="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) usage >&2; fail "unknown argument: $1" ;;
  esac
done

# Validate cheap inputs BEFORE the expensive Unity run.
[[ -n "$PROJECT" ]] || { usage >&2; fail "--project is required"; }
[[ ${#EXPECTED_ASSEMBLIES[@]} -eq ${#EXPECTED_COUNTS[@]} ]] \
  || fail "each --expected-assembly needs exactly one paired --expected-count (got ${#EXPECTED_ASSEMBLIES[@]} vs ${#EXPECTED_COUNTS[@]})"
for c in ${EXPECTED_COUNTS[@]+"${EXPECTED_COUNTS[@]}"}; do
  is_uint "$c" || fail "--expected-count must be a non-negative integer, got: $c"
done
[[ -z "$EXPECTED_TOTAL" ]] || is_uint "$EXPECTED_TOTAL" || fail "--expected-total must be a non-negative integer, got: $EXPECTED_TOTAL"
is_uint "$TIMEOUT_MINUTES" && [[ "$TIMEOUT_MINUTES" -gt 0 ]] || fail "--timeout-minutes must be a positive integer, got: $TIMEOUT_MINUTES"
PROJECT="$(cd "$PROJECT" && pwd)" || fail "project path does not exist: $PROJECT"

VERSION_FILE="$PROJECT/ProjectSettings/ProjectVersion.txt"
[[ -f "$VERSION_FILE" ]] || fail "not a Unity project (missing $VERSION_FILE)"

# A UnityLockfile is only meaningful while a live process holds it: Unity
# removes it on clean exit, so after a crash/kill (watchdog, CI cancel) a stale
# file remains and must not poison every later run.
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

RESULTS_DIR="${RESULTS_DIR:-$PROJECT/CiResults}"
mkdir -p "$RESULTS_DIR"
RESULTS_DIR="$(cd "$RESULTS_DIR" && pwd)"
RESULTS_XML="$RESULTS_DIR/editmode-results.xml"
LOG_FILE="$RESULTS_DIR/editmode.log"
rm -f "$RESULTS_XML"

echo "== EditMode tests: $PROJECT"
echo "   unity:   $UNITY_BIN"
echo "   results: $RESULTS_XML"
echo "   timeout: ${TIMEOUT_MINUTES}m"

# Unity gets its own session/process group (setsid) so kill -- -PID reaches the
# helper processes it spawns (AssetImportWorker, shader compiler, upm) — killing
# only the main PID leaves orphans holding Library's artifact DB.
# No -quit: forbidden with -runTests (the test runner owns process exit).
python3 -c 'import os, sys; os.setsid(); os.execv(sys.argv[1], sys.argv[1:])' \
  "$UNITY_BIN" \
  -batchmode \
  -projectPath "$PROJECT" \
  -runTests -testPlatform EditMode \
  -testResults "$RESULTS_XML" \
  -logFile "$LOG_FILE" &
UNITY_PID=$!

kill_unity_group() {
  kill -TERM -- "-$UNITY_PID" 2>/dev/null || true
  sleep 10
  kill -KILL -- "-$UNITY_PID" 2>/dev/null || true
}
# If this script is interrupted/terminated, take the Unity tree down with us —
# an orphaned headless Unity would hold the project for hours.
trap 'kill_unity_group' INT TERM

DEADLINE=$((SECONDS + TIMEOUT_MINUTES * 60))
while kill -0 "$UNITY_PID" 2>/dev/null; do
  if (( SECONDS >= DEADLINE )); then
    echo "Watchdog: deadline reached, terminating the Unity process group (pgid $UNITY_PID)" >&2
    kill_unity_group
    wait "$UNITY_PID" 2>/dev/null || true
    fail "Unity did not finish within ${TIMEOUT_MINUTES} minutes (log: $LOG_FILE)"
  fi
  sleep 5
done
UNITY_EXIT=0
wait "$UNITY_PID" || UNITY_EXIT=$?
trap - INT TERM
echo "   unity exit code: $UNITY_EXIT"
if [[ "$UNITY_EXIT" -ne 0 ]]; then
  # The XML stays primary truth, but a nonzero exit alongside a green XML (e.g.
  # a teardown crash) is a signal worth keeping loud in the log.
  echo "WARN: Unity exited nonzero ($UNITY_EXIT); trusting the result XML but check $LOG_FILE if it is green" >&2
fi

# Self-check: the XML is the primary outcome; a missing/unparseable file is a
# failure even if Unity exited 0.
[[ -f "$RESULTS_XML" ]] || fail "no result XML was produced (unity exit $UNITY_EXIT, log: $LOG_FILE)"

# Pairs are passed as one "name=count;name=count" spec (assembly names carry no ';'/'=').
EXPECT_SPEC=""
for ((i = 0; i < ${#EXPECTED_ASSEMBLIES[@]}; i++)); do
  EXPECT_SPEC="$EXPECT_SPEC${EXPECT_SPEC:+;}${EXPECTED_ASSEMBLIES[$i]}=${EXPECTED_COUNTS[$i]}"
done

python3 - "$RESULTS_XML" "$EXPECT_SPEC" "$EXPECTED_TOTAL" <<'PYEOF'
import sys
import xml.etree.ElementTree as ET

xml_path, expect_spec, expected_total = sys.argv[1:4]
expectations = [
    (name, int(count))
    for name, count in (pair.split("=", 1) for pair in expect_spec.split(";") if pair)
]
try:
    root = ET.parse(xml_path).getroot()
except ET.ParseError as e:
    sys.exit(f"FAIL: result XML did not parse: {e}")

total = int(root.get("total", -1))
passed = int(root.get("passed", -1))
failed = int(root.get("failed", -1))
skipped = int(root.get("skipped", 0))
inconclusive = int(root.get("inconclusive", 0))
result = root.get("result", "")

assemblies = {}
for ts in root.iter("test-suite"):
    if ts.get("type") == "Assembly":
        assemblies[ts.get("name")] = {
            "total": int(ts.get("total", 0)),
            "passed": int(ts.get("passed", 0)),
            "failed": int(ts.get("failed", 0)),
            "skipped": int(ts.get("skipped", 0)),
            "inconclusive": int(ts.get("inconclusive", 0)),
        }

print(f"   overall: result={result} total={total} passed={passed} failed={failed} "
      f"skipped={skipped} inconclusive={inconclusive}")
for name, a in sorted(assemblies.items()):
    print(f"   assembly: {name} total={a['total']} passed={a['passed']} failed={a['failed']} "
          f"skipped={a['skipped']} inconclusive={a['inconclusive']}")

errors = []
# passed == total: an [Ignore]'d test keeps result="Passed" and the total
# unchanged — only the passed count exposes it. Silent skips are drift.
if failed != 0 or result != "Passed" or passed != total:
    for tc in root.iter("test-case"):
        if tc.get("result") != "Passed":
            print(f"   NON-PASS: {tc.get('fullname')} -> {tc.get('result')}")
    errors.append(
        f"suite not fully green: result={result}, failed={failed}, "
        f"passed={passed}/{total}, skipped={skipped}, inconclusive={inconclusive}"
    )

for expected_assembly, expected_count in expectations:
    a = assemblies.get(expected_assembly)
    if a is None:
        errors.append(
            f"expected assembly '{expected_assembly}' not found in results "
            f"(present: {', '.join(sorted(assemblies)) or 'none'})"
        )
    elif a["passed"] != expected_count or a["total"] != expected_count:
        errors.append(
            f"count drift in {expected_assembly}: expected {expected_count} passed, "
            f"got passed={a['passed']} total={a['total']} skipped={a['skipped']} "
            f"(a drift is a finding — name it, do not silently rebaseline)"
        )

if expected_total and total != int(expected_total):
    errors.append(f"project total drift: expected {expected_total}, got {total}")

if errors:
    sys.exit("FAIL: " + "; ".join(errors))
print("PASS: all test assertions green")
PYEOF
