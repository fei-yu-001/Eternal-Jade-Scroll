#!/usr/bin/env bash
# 只监听指定员工：以 completion_seq 变化判定交付。用法: bash Tools/herdr-watch.sh fe-map fe-rig fe-perf
get() { herdr agent get "$1" 2>/dev/null | python -c "import json,sys; d=json.load(sys.stdin)['result']['agent']; print(d.get('completion_seq',0), d['agent_status'])" 2>/dev/null; }
declare -A last
for a in "$@"; do read -r s _ < <(get "$a"); last[$a]=${s:-0}; done
while true; do
  for a in "$@"; do
    read -r s st < <(get "$a")
    if [ "${s:-0}" != "${last[$a]}" ]; then echo "DONE $a seq=$s status=$st"; last[$a]=$s; fi
  done
  sleep 20
done
