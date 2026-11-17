#!/bin/sh
# Starts the Accessible Trader server from the app folder beside this file; your browser then
# opens on it. Double-clicking a .command file in Finder runs it in a Terminal window, and that
# window is the running server. Staged at the top of the macOS WebHost downloads by
# packaging/webhost/package.sh. The server reads wwwroot and appsettings.json from its working
# directory, so it has to run from inside app/.
here=$(cd "$(dirname "$0")" && pwd)
if [ ! -x "$here/app/AccessibleTrader.WebHost" ]; then
    echo "Accessible Trader could not start: $here/app/AccessibleTrader.WebHost is missing." >&2
    echo "Unzip the whole download, keeping the app folder beside this file, or download it again." >&2
    exit 1
fi
cd "$here/app" || exit 1
exec ./AccessibleTrader.WebHost "$@"
