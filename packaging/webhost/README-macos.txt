Accessible Trader {VERSION}, WebHost for macOS ({ARCH})

To start it, open "Accessible Trader.command" in this folder. A Terminal window opens: that
window is the Accessible Trader server. Your web browser then opens on it, at
http://localhost:5145. To stop the server, press Control-C in that window, or close it.

The app folder holds the server and everything it needs, including its own copy of .NET, so
nothing else has to be installed. Keep it beside "Accessible Trader.command", and do not move
files out of it.

Your settings, workspaces and logs are kept in ~/Library/Application Support/AccessibleTrader, and
downloaded market data in ~/Library/Caches/AccessibleTrader, not in this folder, so you can
replace this folder with a newer version and keep them.

If something else is already using port 5145, the server says so and stops. Close the other
program, or start the server on another port from Terminal, in this folder:
    Kestrel__Endpoints__Http__Url=http://localhost:5146 "./Accessible Trader.command"

The program is not signed, so macOS may say it "cannot be opened because it is from an
unidentified developer". Control-click "Accessible Trader.command", choose Open, then Open again.
If macOS still refuses, run this once in Terminal, in this folder, and try again:
    xattr -dr com.apple.quarantine .

This download is for {ARCH_LONG}. The other one is for {OTHER_LONG}.

Downloads: https://github.com/churst90/accessible-trade-terminal/releases
Website and help: https://accessibletrader.com
