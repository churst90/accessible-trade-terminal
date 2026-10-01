#!/bin/bash
# A2q phase 4: both full suites on the FINAL tree (this worktree: main + the campaign's tests and
# its four production fixes). The silence guard runs first; nothing else runs if it is not 5/5.
cd "$(dirname "$0")/.."
dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj -p:UseRazorSourceGenerator=false > scratchpad/a2q_final_build.log 2>&1 || { echo BUILD_FAIL; exit 1; }
dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj -p:UseRazorSourceGenerator=false --no-build --filter "FullyQualifiedName~QuietDesktopTests" > scratchpad/a2q_final_quiet.log 2>&1
grep -q "Failed:     0, Passed:     5," scratchpad/a2q_final_quiet.log || { echo QUIET_NOT_5_OF_5; exit 1; }
dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj -p:UseRazorSourceGenerator=false --no-build > scratchpad/a2q_final_csharp.log 2>&1
grep -E "^(Passed|Failed)!" scratchpad/a2q_final_csharp.log
dotnet build AccessibleTrader.BrowserTests/AccessibleTrader.BrowserTests.csproj -p:UseRazorSourceGenerator=false > scratchpad/a2q_final_bbuild.log 2>&1 || { echo BBUILD_FAIL; exit 1; }
dotnet test AccessibleTrader.BrowserTests/AccessibleTrader.BrowserTests.csproj -p:UseRazorSourceGenerator=false --no-build > scratchpad/a2q_final_browser.log 2>&1
grep -E "^(Passed|Failed)!" scratchpad/a2q_final_browser.log
echo FINAL_SUITES_DONE
