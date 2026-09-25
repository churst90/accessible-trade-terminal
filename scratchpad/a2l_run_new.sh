#!/bin/bash
# Runs only the tests A2l added or extended. Usage: a2l_run_new.sh <logfile>
cd "$(dirname "$0")/.."
nice -n 10 dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj -p:UseRazorSourceGenerator=false --no-build \
  --filter "FullyQualifiedName~StrategyLifecycleTests|FullyQualifiedName~EditableStrategySpecRoundTripTests|FullyQualifiedName~StrategySpecNarratorTests|FullyQualifiedName~SetupSonifierSpeechTests|FullyQualifiedName~MultiTimeframeDataServiceTests|FullyQualifiedName~ScriptStrategyCausalityFingerprintTests|FullyQualifiedName~LabRunnerTests" \
  > "$1" 2>&1
echo "EXIT $?" >> "$1"
