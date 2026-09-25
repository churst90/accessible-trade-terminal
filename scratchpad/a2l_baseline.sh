#!/bin/bash
cd "$(dirname "$0")/.."
nice -n 10 dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj -p:UseRazorSourceGenerator=false -v q --nologo > /tmp/a2l_baseline_build.log 2>&1
echo "BUILD EXIT $?" >> /tmp/a2l_baseline_build.log
nice -n 10 dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj -p:UseRazorSourceGenerator=false --no-build > /tmp/a2l_baseline_test.log 2>&1
echo "TEST EXIT $?" >> /tmp/a2l_baseline_test.log
