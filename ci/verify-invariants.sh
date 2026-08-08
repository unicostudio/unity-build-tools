#!/usr/bin/env bash
# CI lane: cross-repo invariant checks for the unity-build-tools program.
# Requires no Unity — pure git/file checks, cheap enough to run on every push.
#
# Orchestrator-agnostic by design: no GitHub-specific behavior; callable from any
# CI system or a plain shell.
#
# Invariants enforced (numbered in the output):
#   1. Tag/version consistency in this repo: each package's package.json version
#      has a matching tag <package-id>/<version>, and that tag's package.json
#      carries the same version string.
#   2. Host pin integrity: each host's manifest pins the package to a tag that
#      exists here, and the packages-lock.json hash equals that tag's commit.
#      (A pin to an older tag is a WARN — bumping hosts is a deliberate act —
#      but a lock hash that does not match its pinned tag is a FAIL.)
#   3. Glue byte-parity: VersionTrackerArtifactStep.cs is byte-identical across
#      all hosts (iron rule 2).
#   4. StripDefines convention: every BuildTargetConfig asset in every host
#      lists the required strip define (UNITY_MCP_READY). The fleet convention
#      is enforced nowhere else — this check is the enforcement.
#   5. Host manifests list com.unicostudio.buildsystem under "testables"
#      (without it the package's EditMode tests silently do not run in hosts).
#   6. Content-state sanity: every bundle URL referenced by a host's
#      addressables_content_state.bin must use the Production profile and
#      point at PUBLISHED content (one sample URL per referenced version
#      folder is probed on the CDN; 404 = FAIL, network trouble = WARN).
#      Guards against the v14/Test-style poisoning that destroyed BT5's
#      Android lineage on 2026-07-17.
#
# Usage:
#   verify-invariants.sh --host BTA=/path/to/host [--host BT5=/path/to/host ...]
#     [--tools <path>]          unity-build-tools repo (default: this script's repo)
#     [--glue <relative-path>]  glue file path inside hosts
#     [--strip-define <name>]   required StripDefines entry
#     [--require-profile <p>]   profile the content-state bins must reference (default: Production)
#     [--skip-cdn]              skip the live CDN probes of invariant 6 (offline runs)
#
# Exit codes: 0 = all checks pass (warnings allowed); 1 = at least one FAIL.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TOOLS="$(cd "$SCRIPT_DIR/.." && pwd)"
GLUE_REL="Assets/02_Scripts/Editor/VersionTrackerArtifactStep.cs"
STRIP_DEFINE="UNITY_MCP_READY"
REQUIRE_PROFILE="Production"
SKIP_CDN=0
PACKAGES=(com.unicostudio.buildsystem com.unicostudio.versiontracker)
HOST_NAMES=()
HOST_PATHS=()

# Print the header comment block as usage (robust to header edits: stops at
# the first non-comment line).
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }
need_value() { [[ $# -ge 2 ]] || { usage >&2; echo "FAIL: missing value for $1" >&2; exit 1; } }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --host)
      need_value "$@"
      [[ "$2" == *"="* ]] || { echo "FAIL: --host expects NAME=PATH, got: $2" >&2; exit 1; }
      host_path="${2#*=}"
      # Validate now: a typo'd path would otherwise surface as a pile of
      # bogus convention FAILs instead of one clear error.
      host_path="$(cd "$host_path" 2>/dev/null && pwd)" || { echo "FAIL: --host path does not exist: ${2#*=}" >&2; exit 1; }
      HOST_NAMES+=("${2%%=*}"); HOST_PATHS+=("$host_path"); shift 2 ;;
    --tools) need_value "$@"; TOOLS="$(cd "$2" && pwd)"; shift 2 ;;
    --glue) need_value "$@"; GLUE_REL="$2"; shift 2 ;;
    --strip-define) need_value "$@"; STRIP_DEFINE="$2"; shift 2 ;;
    --require-profile) need_value "$@"; REQUIRE_PROFILE="$2"; shift 2 ;;
    --skip-cdn) SKIP_CDN=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) usage >&2; echo "FAIL: unknown argument: $1" >&2; exit 1 ;;
  esac
done

[[ ${#HOST_NAMES[@]} -ge 1 ]] || { usage >&2; echo "FAIL: at least one --host NAME=PATH is required" >&2; exit 1; }
git -C "$TOOLS" rev-parse --git-dir >/dev/null 2>&1 || { echo "FAIL: --tools is not a git repo: $TOOLS" >&2; exit 1; }

FAILS=0
WARNS=0
pass() { echo "[PASS] $*"; }
warn() { echo "[WARN] $*"; WARNS=$((WARNS + 1)); }
failed() { echo "[FAIL] $*"; FAILS=$((FAILS + 1)); }

json_get() { # json_get <file> <key> [<key> ...] -> value or empty (keys may contain dots)
  python3 - "$@" <<'PYEOF'
import json, sys
try:
    node = json.load(open(sys.argv[1]))
    for key in sys.argv[2:]:
        node = node[key]
    print(node)
except Exception:
    pass
PYEOF
}

echo "== Invariant checks (tools: $TOOLS)"

# --- 1. tag/version consistency in the tools repo -----------------------------
for pkg in "${PACKAGES[@]}"; do
  version="$(json_get "$TOOLS/Packages/$pkg/package.json" version)"
  if [[ -z "$version" ]]; then
    failed "1. $pkg: could not read version from package.json"
    continue
  fi
  tag="$pkg/$version"
  if ! git -C "$TOOLS" rev-parse -q --verify "refs/tags/$tag" >/dev/null; then
    failed "1. $pkg: package.json is $version but tag '$tag' does not exist"
    continue
  fi
  # Guarded read: a tag whose tree lacks/breaks the package.json must produce a
  # numbered FAIL, not a set -e abort that skips the remaining invariants.
  tagged_version="$( (git -C "$TOOLS" show "$tag:Packages/$pkg/package.json" 2>/dev/null || true) | python3 -c '
import json, sys
try:
    print(json.load(sys.stdin)["version"])
except Exception:
    pass')"
  if [[ -z "$tagged_version" ]]; then
    failed "1. $pkg: tag $tag exists but Packages/$pkg/package.json at that tag is missing or unparseable"
  elif [[ "$tagged_version" == "$version" ]]; then
    pass "1. $pkg: version $version <-> tag $tag consistent"
  else
    failed "1. $pkg: tag $tag carries package.json version $tagged_version (expected $version)"
  fi
done

# --- 2. host pin integrity ----------------------------------------------------
for i in "${!HOST_NAMES[@]}"; do
  host="${HOST_NAMES[$i]}"; host_path="${HOST_PATHS[$i]}"
  manifest="$host_path/Packages/manifest.json"
  lock="$host_path/Packages/packages-lock.json"
  [[ -f "$manifest" && -f "$lock" ]] || { failed "2. $host: missing manifest or packages-lock under $host_path/Packages"; continue; }
  for pkg in "${PACKAGES[@]}"; do
    pin_url="$(json_get "$manifest" dependencies "$pkg")"
    if [[ "$pin_url" != *"#$pkg/"* ]]; then
      failed "2. $host/$pkg: manifest is not pinned to a '$pkg/<version>' tag (value: ${pin_url:-<absent>})"
      continue
    fi
    pinned_tag="${pin_url#*#}"
    pinned_version="${pinned_tag#"$pkg/"}"
    current_version="$(json_get "$TOOLS/Packages/$pkg/package.json" version)"
    tag_commit="$(git -C "$TOOLS" rev-parse -q --verify "refs/tags/$pinned_tag^{commit}" || true)"
    if [[ -z "$tag_commit" ]]; then
      failed "2. $host/$pkg: pinned tag '$pinned_tag' does not exist in the tools repo"
      continue
    fi
    lock_hash="$(json_get "$lock" dependencies "$pkg" hash)"
    if [[ "$lock_hash" == "$tag_commit" ]]; then
      pass "2. $host/$pkg: lock hash == tag commit ($pinned_tag @ ${tag_commit:0:12})"
    else
      failed "2. $host/$pkg: lock hash ${lock_hash:-<absent>} != commit of pinned tag $pinned_tag ($tag_commit) — resolve by re-resolving the package in the host, never by hand-editing the lock"
    fi
    if [[ "$pinned_version" != "$current_version" ]]; then
      warn "2. $host/$pkg: pinned to $pinned_version while tools repo is at $current_version (host bump pending — deliberate act, not auto-fixed)"
    fi
  done
done

# --- 3. glue byte-parity ------------------------------------------------------
if [[ ${#HOST_NAMES[@]} -lt 2 ]]; then
  warn "3. glue parity needs >=2 hosts; got ${#HOST_NAMES[@]} — skipped"
else
  first_host="${HOST_NAMES[0]}"; first_glue="${HOST_PATHS[0]}/$GLUE_REL"
  if [[ ! -f "$first_glue" ]]; then
    failed "3. $first_host: glue file missing: $first_glue"
  else
    for i in "${!HOST_NAMES[@]}"; do
      [[ $i -eq 0 ]] && continue
      host="${HOST_NAMES[$i]}"; glue="${HOST_PATHS[$i]}/$GLUE_REL"
      if [[ ! -f "$glue" ]]; then
        failed "3. $host: glue file missing: $glue"
      elif cmp -s "$first_glue" "$glue"; then
        pass "3. glue byte-identical: $first_host == $host ($GLUE_REL)"
      else
        failed "3. glue differs between $first_host and $host ($GLUE_REL) — iron rule 2: BTA's copy is the origin, mirror it byte-for-byte"
      fi
    done
  fi
fi

# --- 4. StripDefines convention on BuildTargetConfig assets -------------------
# Config assets are identified by the m_Script GUID of BuildTargetConfig.cs so
# renamed/moved assets are still found and unrelated assets never match.
CONFIG_GUID="$(awk '/^guid:/{gsub(/\r/, ""); print $2}' "$TOOLS/Packages/com.unicostudio.buildsystem/Editor/Config/BuildTargetConfig.cs.meta" 2>/dev/null || true)"
if [[ -z "$CONFIG_GUID" ]]; then
  failed "4. could not read BuildTargetConfig.cs.meta GUID from the tools repo"
else
  for i in "${!HOST_NAMES[@]}"; do
    host="${HOST_NAMES[$i]}"; host_path="${HOST_PATHS[$i]}"
    found=0; missing=0
    while IFS= read -r asset; do
      found=$((found + 1))
      # StripDefines serializes as a YAML block: "  StripDefines:" then "  - NAME"
      # lines. \r is stripped first so a CRLF-rewritten asset cannot false-red.
      if ! awk -v define="$STRIP_DEFINE" '
            { sub(/\r$/, "") }
            /^  StripDefines:/ { inblock=1; next }
            inblock && /^  - / { if ($2 == define) found=1; next }
            inblock { inblock=0 }
            END { exit found ? 0 : 1 }' "$asset"; then
        failed "4. $host: $(basename "$asset") lacks '$STRIP_DEFINE' in StripDefines"
        missing=$((missing + 1))
      fi
    done < <(grep -rl "guid: $CONFIG_GUID" "$host_path/Assets" --include="*.asset" 2>/dev/null)
    if [[ $found -eq 0 ]]; then
      failed "4. $host: no BuildTargetConfig assets found under Assets/ (convention requires them)"
    elif [[ $missing -eq 0 ]]; then
      pass "4. $host: all $found BuildTargetConfig assets strip '$STRIP_DEFINE'"
    fi
  done
fi

# --- 5. testables -------------------------------------------------------------
for i in "${!HOST_NAMES[@]}"; do
  host="${HOST_NAMES[$i]}"; manifest="${HOST_PATHS[$i]}/Packages/manifest.json"
  if python3 -c '
import json, sys
m = json.load(open(sys.argv[1]))
sys.exit(0 if "com.unicostudio.buildsystem" in m.get("testables", []) else 1)
' "$manifest" 2>/dev/null; then
    pass "5. $host: manifest testables includes com.unicostudio.buildsystem"
  else
    failed "5. $host: manifest testables does NOT include com.unicostudio.buildsystem — its EditMode tests silently do not run in this host"
  fi
done

# --- 6. content-state sanity (poison guard) -----------------------------------
# The committed bin records the SHIPPED state, so every reference must use the
# required (Production) profile and point at content that is actually live.
# A bin written by a gate/test build references a Test-profile or unpublished
# version folder — exactly how BT5's Android lineage was destroyed 2026-07-17.
for i in "${!HOST_NAMES[@]}"; do
  host="${HOST_NAMES[$i]}"; host_path="${HOST_PATHS[$i]}"
  found_bins=0
  for bin in "$host_path"/Assets/AddressableAssetsData/*/addressables_content_state.bin; do
    [[ -f "$bin" ]] || continue
    found_bins=$((found_bins + 1))
    platform="$(basename "$(dirname "$bin")")"
    urls="$(strings "$bin" | grep -oE 'https?://[^"[:space:]]+\.bundle' | sort -u || true)"
    if [[ -z "$urls" ]]; then
      failed "6. $host/$platform: no bundle URLs extractable from content-state bin (unreadable or unexpected format)"
      continue
    fi
    bad_profile="$(echo "$urls" | grep -m1 -vE "/$REQUIRE_PROFILE/" || true)"
    if [[ -n "$bad_profile" ]]; then
      failed "6. $host/$platform: bin references non-$REQUIRE_PROFILE content (poison pattern): $bad_profile"
      continue
    fi
    prefixes="$(echo "$urls" | sed -E 's|/[^/]+$||' | sort -u)"
    if [[ "$SKIP_CDN" -eq 1 ]]; then
      warn "6. $host/$platform: profile OK; CDN probe skipped (--skip-cdn)"
      continue
    fi
    bad=0; inconclusive=0; labels=""
    while IFS= read -r p; do
      sample="$(echo "$urls" | grep -F -m1 "$p/" || true)"
      short="$(echo "$p" | awk -F/ '{print $(NF-2)"/"$(NF-1)"/"$NF}')"
      # 1-byte range GET: cheap liveness probe that works where HEAD may not.
      code="$(curl -sL --max-time 20 -r 0-0 -o /dev/null -w '%{http_code}' "$sample" 2>/dev/null || echo 000)"
      case "$code" in
        200|206) labels="$labels${labels:+, }$short" ;;
        404|410) failed "6. $host/$platform: bin references UNPUBLISHED content ($short -> HTTP $code): $sample"; bad=$((bad + 1)) ;;
        *) warn "6. $host/$platform: CDN probe inconclusive for $short (HTTP $code) — not treated as a failure"; inconclusive=$((inconclusive + 1)) ;;
      esac
    done <<< "$prefixes"
    if [[ $bad -eq 0 && $inconclusive -eq 0 ]]; then
      pass "6. $host/$platform: all referenced version folders published ($labels)"
    fi
  done
  if [[ $found_bins -eq 0 ]]; then
    warn "6. $host: no content-state bin present for any platform (lineage not adopted, or known-lost — see the host's docs/content-state-lineage.md)"
  fi
done

echo "== Summary: FAILS=$FAILS WARNS=$WARNS"
[[ $FAILS -eq 0 ]] || exit 1
