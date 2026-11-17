#!/usr/bin/env bash
# Lays out a Linux or macOS WebHost download from a finished `dotnet publish` of the WebHost.
# Run by release.yml and diagnostic-build.yml on Ubuntu; the Windows download is package.ps1.
#
# The publish is about 470 files in one folder, and the zip used to be that folder's contents
# at its root, so unzipping it anywhere but an empty folder scattered them. Every WebHost
# download now has the desktop download's layout:
#
#     AccessibleTrader-WebHost-<version>/
#         accessible-trader            (Linux)  or  Accessible Trader.command  (macOS)
#         README.txt                   which file to run, where your data is kept
#         app/                         the publish, untouched
#
# The starter is a shell script that runs app/AccessibleTrader.WebHost from inside app/, because
# the server reads wwwroot and appsettings.json from its working directory.
#
# Output: <out-dir>/AccessibleTrader-WebHost-<version>-<rid>.zip, made with Info-ZIP so the
# executable bits survive (macOS Archive Utility and unzip both honour them).
#
# Then, for linux-x64, it TRIES the zip: unzipped somewhere new, the starter run from another
# folder must serve the page and blazor.web.js, and the server must write nothing into its own
# folder (the market-data cache used to land there), and it must have run from app/ (its
# content root, where appsettings.json is read). The macOS zips are cross-built here and
# cannot be run. --skip-trial skips it (never in release.yml).
#
# Usage: package.sh --publish-dir DIR --version VER --rid RID [--out-dir dist] [--skip-trial]

set -euo pipefail

publish_dir='' version='' rid='' out_dir='dist' skip_trial=0
while [ $# -gt 0 ]; do
    case "$1" in
        --publish-dir) publish_dir=$2; shift 2 ;;
        --version)     version=$2; shift 2 ;;
        --rid)         rid=$2; shift 2 ;;
        --out-dir)     out_dir=$2; shift 2 ;;
        --skip-trial)  skip_trial=1; shift ;;
        *) echo "Unknown argument: $1" >&2; exit 2 ;;
    esac
done
[ -n "$publish_dir" ] && [ -n "$version" ] && [ -n "$rid" ] \
    || { echo "Usage: package.sh --publish-dir DIR --version VER --rid RID [--out-dir dist] [--skip-trial]" >&2; exit 2; }

here=$(cd "$(dirname "$0")" && pwd)
publish_dir=$(cd "$publish_dir" && pwd)
mkdir -p "$out_dir"
out_dir=$(cd "$out_dir" && pwd)

case "$rid" in
    linux-x64) starter='accessible-trader';         readme='README-linux.txt' ;;
    osx-x64)   starter='Accessible Trader.command'; readme='README-macos.txt'
               arch='Intel'; arch_long='Macs with an Intel processor'
               other_long='Macs with Apple silicon (M1 and later)' ;;
    osx-arm64) starter='Accessible Trader.command'; readme='README-macos.txt'
               arch='Apple silicon'; arch_long='Macs with Apple silicon (M1 and later)'
               other_long='Macs with an Intel processor' ;;
    *) echo "package.sh makes the Linux and macOS downloads; $rid is not one (win-x64 is package.ps1)." >&2; exit 2 ;;
esac

server="$publish_dir/AccessibleTrader.WebHost"
[ -f "$server" ] || { echo "AccessibleTrader.WebHost is not in $publish_dir. Point --publish-dir at the WebHost's publish output for $rid." >&2; exit 1; }

work="${RUNNER_TEMP:-$(mktemp -d)}/att-webhost-package-$rid"
rm -rf "$work"
mkdir -p "$work/stage"

# ── Stage ────────────────────────────────────────────────────────────────────────────────────
top="AccessibleTrader-WebHost-$version"
stage="$work/stage/$top"
mkdir -p "$stage/app"
cp -a "$publish_dir/." "$stage/app/"
cp "$here/$starter" "$stage/$starter"
chmod 755 "$stage/$starter" "$stage/app/AccessibleTrader.WebHost"
[ -f "$stage/app/AccessibleTrader.ScriptWorker" ] && chmod 755 "$stage/app/AccessibleTrader.ScriptWorker"
sed -e "s/{VERSION}/$version/g" -e "s/{ARCH}/${arch:-}/g" \
    -e "s/{ARCH_LONG}/${arch_long:-}/g" -e "s/{OTHER_LONG}/${other_long:-}/g" \
    "$here/$readme" > "$stage/README.txt"

published=$(find "$publish_dir" -type f | wc -l)
staged=$(find "$stage/app" -type f | wc -l)
[ "$published" -eq "$staged" ] || { echo "Staged $staged files into app/ from a publish of $published." >&2; exit 1; }
if grep -q '{[A-Z_]*}' "$stage/README.txt"; then echo "README.txt still has a placeholder: $(grep -o '{[A-Z_]*}' "$stage/README.txt" | head -1)" >&2; exit 1; fi
echo "Staged '$top': $(cd "$stage" && find . -maxdepth 1 -type f -printf '%f, ' | sed 's/, $//') and app/ ($staged files)"

# ── Zip ──────────────────────────────────────────────────────────────────────────────────────
# From the stage's parent, so the zip's root is that one folder. -y keeps symlinks as links.
zip_path="$out_dir/AccessibleTrader-WebHost-$version-$rid.zip"
rm -f "$zip_path"
(cd "$work/stage" && zip -r -y -q "$zip_path" "$top")
ls -lh "$zip_path"

if [ "$skip_trial" -eq 1 ]; then echo "::warning::Trial skipped: the $rid zip was not run."; exit 0; fi
if [ "$rid" != 'linux-x64' ]; then echo "No trial for $rid: it is cross-built here and cannot run on this machine."; exit 0; fi

# ── Trial: unzip, start from elsewhere, serve, write nothing into its own folder ─────────────
trial="$work/trial"
mkdir -p "$trial/unzipped" "$trial/home" "$trial/elsewhere"
unzip -q "$zip_path" -d "$trial/unzipped"
folder=$(cd "$trial/unzipped/$top" && pwd -P)
[ -x "$folder/$starter" ] || { echo "The unzipped $starter is not executable." >&2; exit 1; }
[ -x "$folder/app/AccessibleTrader.WebHost" ] || { echo "The unzipped server is not executable." >&2; exit 1; }
before=$(cd "$folder" && find . | sort)

port=${TRIAL_PORT:-5197}
log="$trial/server.log"
# Its own HOME and XDG folders, so the trial reads and writes nothing of the runner's, and no
# session bus, so the tray stays out of it.
# No subshell: $! has to be the server itself (the starter execs it) for the kill to reach it.
cd "$trial/elsewhere"
env HOME="$trial/home" XDG_DATA_HOME= XDG_CACHE_HOME= XDG_CONFIG_HOME= \
    DBUS_SESSION_BUS_ADDRESS='disabled:' Kestrel__Endpoints__Http__Url="http://127.0.0.1:$port" \
    "$folder/$starter" --no-launch > "$log" 2>&1 &
pid=$!
stop() { kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; }

page=''
for _ in $(seq 1 120); do
    if ! kill -0 "$pid" 2>/dev/null; then echo "The server exited while starting:" >&2; tail -40 "$log" >&2; exit 1; fi
    page=$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$port/" || true)
    [ "$page" = 200 ] && break
    sleep 0.5
done
script=$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$port/_framework/blazor.web.js" || true)
stop
if [ "$page" != 200 ] || [ "$script" != 200 ]; then
    echo "Started from another folder, the server answered / with '$page' and blazor.web.js with '$script'." >&2
    tail -40 "$log" >&2; exit 1
fi
echo "Trial: $starter, started from another folder, served the page and blazor.web.js"

# The page and its scripts are served from any working directory (the published asset manifest
# finds them), but appsettings.json is read from the content root, and started anywhere else the
# server quietly binds port 5000 instead of 5145. So the starter's one job is checked directly.
if ! grep -qxF "      Content root path: $folder/app" "$log"; then
    echo "The server's content root is not $folder/app:" >&2
    grep -F 'Content root path' "$log" >&2 || echo '(no content root line in its log)' >&2
    exit 1
fi
echo "Trial: the server ran from app/, so it read its appsettings.json"

after=$(cd "$folder" && find . | sort)
if [ "$before" != "$after" ]; then
    echo "The server wrote into its own folder:" >&2
    diff <(echo "$before") <(echo "$after") | grep '^>' | head -20 >&2
    exit 1
fi
echo "Trial: the server wrote nothing into its own folder"
