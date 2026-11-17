Accessible Trader {VERSION}, WebHost for Windows

To start it, run "Accessible Trader.exe" in this folder. A console window opens: that window is
the Accessible Trader server. Your web browser then opens on it, at http://localhost:5145. To
stop the server, close that window, or press Ctrl+C in it.

The app folder holds the server and everything it needs, including its own copy of .NET, so
nothing else has to be installed. Keep it beside Accessible Trader.exe, and do not move files out
of it.

Your settings, workspaces and logs are kept in %LocalAppData%\AccessibleTrader, not in this folder,
so you can replace this folder with a newer version and keep them.

If the server stops as it starts, Accessible Trader.exe says so in a message box. Most often that
is port 5145 already in use, by a copy that is already running (use its browser tab, or go to
http://localhost:5145) or by another program. Close the other program, or start the server on
another port from a command prompt in the app folder:
    set Kestrel__Endpoints__Http__Url=http://localhost:5146
    AccessibleTrader.WebHost.exe

The program is not signed, so the first time you run it Windows may say "Windows protected your
PC". Choose "More info", then "Run anyway".

Downloads: https://github.com/churst90/accessible-trade-terminal/releases
Website and help: https://accessibletrader.com
