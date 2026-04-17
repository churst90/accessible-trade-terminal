#!/usr/bin/env bash
# Generates plugins_trusted.manifest — the SHA-256 allow-list consumed by
# PluginTrustPolicy at startup.
#
# Scans the Release plugin output directories, hashes every
# AccessibleTrader.Plugins.*.dll it finds, and writes a manifest with one
# SHA-256 hex digest per line and a trailing `# filename.dll` comment.
#
# Usage:
#   ./tools/generate-plugin-trust-manifest.sh                 # default: writes ./plugins_trusted.manifest
#   ./tools/generate-plugin-trust-manifest.sh -o /tmp/foo.m   # custom output path
#
# Run after a clean Release build. Re-run whenever a first-party plugin DLL
# changes — any code change recompiles and invalidates the previous hash.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${REPO_ROOT}/plugins_trusted.manifest"

while getopts "o:h" opt; do
  case "$opt" in
    o) OUT="$OPTARG" ;;
    h) echo "Usage: $0 [-o output_file]"; exit 0 ;;
    *) echo "Usage: $0 [-o output_file]"; exit 1 ;;
  esac
done

PLUGIN_ROOTS=(
  "${REPO_ROOT}/Plugins/Providers"
  "${REPO_ROOT}/Plugins/Analytics"
  "${REPO_ROOT}/Plugins/Indicators"
)

echo "Generating plugin trust manifest..."
echo "  Repo root:  ${REPO_ROOT}"
echo "  Output:     ${OUT}"
echo ""

# Pick the sha256 command flavour available on this platform.
if command -v sha256sum >/dev/null 2>&1; then
  SHA_CMD="sha256sum"
elif command -v shasum >/dev/null 2>&1; then
  SHA_CMD="shasum -a 256"
else
  echo "error: neither sha256sum nor shasum found" >&2
  exit 1
fi

tmp="$(mktemp)"
trap "rm -f '${tmp}'" EXIT

{
  echo "# AccessibleTrader plugin trust manifest"
  echo "# Generated: $(date -u +'%Y-%m-%d %H:%M:%S UTC')"
  echo "# One SHA-256 hex digest per line. '#' starts a comment."
  echo "# Re-generate via tools/generate-plugin-trust-manifest.sh after each Release build."
  echo ""
} > "${tmp}"

count=0
for root in "${PLUGIN_ROOTS[@]}"; do
  [ -d "${root}" ] || continue
  # Only hash Release-build DLLs with the AccessibleTrader.Plugins.* naming
  # convention that PluginLoaderService scans for.
  while IFS= read -r -d '' dll; do
    if [[ "${dll}" != *"/bin/Release/"* ]]; then
      continue
    fi
    hash=$(${SHA_CMD} "${dll}" | awk '{print toupper($1)}')
    name="$(basename "${dll}")"
    echo "${hash}  # ${name}" >> "${tmp}"
    echo "  ${hash}  ${name}"
    count=$((count + 1))
  done < <(find "${root}" -type f -name "AccessibleTrader.Plugins.*.dll" -print0)
done

if [ "${count}" -eq 0 ]; then
  echo "warning: no plugin DLLs found — did you run a Release build first?" >&2
  exit 1
fi

# Sort + unique by hash+filename so multi-TFM outputs don't double up.
{
  head -n 5 "${tmp}"
  tail -n +6 "${tmp}" | sort -u
} > "${OUT}"

echo ""
echo "Wrote ${count} trusted plugin hashes to ${OUT}"
