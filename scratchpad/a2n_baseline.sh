#!/bin/bash
# A2n baseline: full build + full AccessibleTrader.Tests run, recorded.
cd "$(dirname "$0")/.."
nice -n 10 dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj -p:UseRazorSourceGenerator=false > scratchpad/a2n_baseline_build.log 2>&1
echo "build exit $?"
nice -n 10 dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj -p:UseRazorSourceGenerator=false --no-build > scratchpad/a2n_baseline_test.log 2>&1
echo "test exit $?"
grep -E "Failed:|Passed!|Failed!" scratchpad/a2n_baseline_test.log | tail -3
