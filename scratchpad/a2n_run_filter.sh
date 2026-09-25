#!/bin/bash
# Run AccessibleTrader.Tests (no build) with a --filter; print the summary and failing lines.
cd "$(dirname "$0")/.."
nice -n 10 dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj -p:UseRazorSourceGenerator=false --no-build --filter "$1" > /tmp/a2n_filter.log 2>&1
grep -E "Failed |Passed!|Failed!|No test matches|Error Message|Assert\.|Expected|Actual" /tmp/a2n_filter.log | head -${2:-60}
