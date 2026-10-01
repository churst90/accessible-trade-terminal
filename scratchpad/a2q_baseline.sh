#!/bin/bash
# A2q baseline on the worktree: build and run both suites, record totals.
cd "$(dirname "$0")/.."
dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj -p:UseRazorSourceGenerator=false > scratchpad/a2q_baseline_build.log 2>&1 || { echo BUILD_FAIL; exit 1; }
dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj -p:UseRazorSourceGenerator=false --no-build > scratchpad/a2q_baseline_csharp.log 2>&1
grep -E "^(Passed|Failed)!" scratchpad/a2q_baseline_csharp.log
dotnet build AccessibleTrader.BrowserTests/AccessibleTrader.BrowserTests.csproj -p:UseRazorSourceGenerator=false > scratchpad/a2q_baseline_bbuild.log 2>&1 || { echo BBUILD_FAIL; exit 1; }
dotnet test AccessibleTrader.BrowserTests/AccessibleTrader.BrowserTests.csproj -p:UseRazorSourceGenerator=false --no-build > scratchpad/a2q_baseline_browser.log 2>&1
grep -E "^(Passed|Failed)!" scratchpad/a2q_baseline_browser.log
