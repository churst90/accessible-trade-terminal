import json, os, re, subprocess, time
REPO = "/home/cody/external-rescue/Github/accessible-trade-terminal"
A = "AccessibleTrader.Core/Services/Audio/"
SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+)")

# Is `&& _stopAllFaded` load-bearing at all, or is the protection carried entirely by
# re-arming to _userMasterGain rather than to a literal? Kill 2 is the HISTORICAL defect
# the fix was written for; kill 1 is the A2g mutant.
KILLS = [
    ("G33-flag  (drop && _stopAllFaded)", A + "AudioEngine.cs",
     "                    if (cmd.IsActive && _stopAllFaded)", "                    if (cmd.IsActive)"),
    ("G33-literal (re-arm to 1.0f)", A + "AudioEngine.cs",
     "                        _targetMasterGain = _userMasterGain;", "                        _targetMasterGain = 1.0f;"),
    ("G33-both", A + "AudioEngine.cs",
     "                    if (cmd.IsActive && _stopAllFaded)\n                    {\n                        _targetMasterGain = _userMasterGain;",
     "                    if (cmd.IsActive)\n                    {\n                        _targetMasterGain = 1.0f;"),
]

def run(cmd, timeout=1800):
    p = subprocess.run(cmd, cwd=REPO, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, (p.stdout or "") + (p.stderr or "")

for name, rel, find, repl in KILLS:
    path = os.path.join(REPO, rel)
    original = open(path, encoding='utf-8-sig').read()
    n = original.count(find)
    if n != 1:
        print(f"{name}: BAD ANCHOR ({n})"); continue
    try:
        open(path, 'w', encoding='utf-8').write(original.replace(find, repl))
        code, log = run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj -p:UseRazorSourceGenerator=false -v q --nologo")
        if code != 0:
            print(f"{name}: NO_COMPILE\n{log[-500:]}"); continue
        _, out = run("dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj -p:UseRazorSourceGenerator=false --no-build --filter FullyQualifiedName~AudioSafetyTests")
        m = SUMMARY_RE.search(out)
        names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
        print(f"{name}: failed={m.group(1) if m else '?'} -> {'RED' if m and int(m.group(1))>0 else 'STILL_GREEN'}")
        for t in names[:6]: print("      ", t)
    finally:
        open(path, 'w', encoding='utf-8').write(original)
        os.utime(path, None)
code, diff = run("git diff --stat -- AccessibleTrader.Core/Services/Audio/")
print("restored byte-identical" if not diff.strip() else f"TREE NOT RESTORED:\n{diff}")
