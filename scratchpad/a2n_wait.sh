#!/bin/bash
# wait until the campaign log has at least N result lines (or the control line), max ~9.5 min
LOG="$(dirname "$0")/a2n_sabotage_run.log"
N=${1:-1}
for i in $(seq 1 57); do
  c=$(grep -cE '^[TS][0-9]+:' "$LOG")
  if [ "$c" -ge "$N" ] || grep -q "CONTROL" "$LOG"; then break; fi
  sleep 10
done
grep -E '^[TS][0-9]+:|^      |CONTROL|restored|catch|trading:|scripting:|NOT' "$LOG"
