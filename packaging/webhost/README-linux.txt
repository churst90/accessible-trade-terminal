Accessible Trader {VERSION}, WebHost for Linux (x64)

To start it, run accessible-trader in this folder, from a terminal:
    ./accessible-trader
The terminal it runs in is the Accessible Trader server. Your web browser then opens on it, at
http://localhost:5145. To stop the server, press Ctrl+C in that terminal.

Add --no-launch to start the server without opening a browser.

The app folder holds the server and everything it needs, including its own copy of .NET, so
nothing else has to be installed. Keep it beside accessible-trader, and do not move files out of
it. A link to accessible-trader (in ~/bin, say) works.

Your settings, workspaces and logs are kept in ~/.local/share/AccessibleTrader (or
$XDG_DATA_HOME/AccessibleTrader), and downloaded market data in ~/.cache/AccessibleTrader, not in
this folder, so you can replace this folder with a newer version and keep them.

If something else is already using port 5145, the server says so and stops. Close the other
program, or start the server on another port:
    Kestrel__Endpoints__Http__Url=http://localhost:5146 ./accessible-trader

Downloads: https://github.com/churst90/accessible-trade-terminal/releases
Website and help: https://accessibletrader.com
