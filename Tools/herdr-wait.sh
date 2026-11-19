#!/usr/bin/env bash
# 阻塞等待任一员工新的交付（completion_seq 变化即返回）。用法: bash Tools/herdr-wait.sh <秒> fe-map fe-rig fe-perf
STATE=Tools/logs/.herdr-seq
touch "$STATE"
declare -A cur
get() { herdr agent get "$1" 2>/dev/null | python -c "import json,sys; d=json.load(sys.stdin)['result']['agent']; print(d.get('completion_seq') or 0)" 2>/dev/null; }
LIMIT=${1:-280}; shift
while [ $SECONDS -lt $LIMIT ]; do
  for a in "$@"; do
    now=$(get "$a")
    prev=$(grep "^$a=" "$STATE" 2>/dev/null | cut -d= -f2)
    if [ -n "$prev" ] && [ "$now" != "$prev" ]; then
      sed -i "/^$a=/d" "$STATE"; echo "$a=$now" >> "$STATE"
      echo "DONE $a seq=$prev->$now"
      exit 0
    fi
    sed -i "/^$a=/d" "$STATE"; echo "$a=$now" >> "$STATE"
  done
  sleep 15
done
echo "TIMEOUT"
