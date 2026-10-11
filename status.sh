#!/usr/bin/env bash
# Recursively finds every config.json and reports whether its case's solver and watcher are running.
root="${1:-.}"
procs="$(ps -eo args=)"

running() { # running <_simulate|_watch> <case dir>
    grep -qxE ".*/ocarina $1 $2/?" <<<"$procs" && echo running || echo stopped
}

find "$root" -type f -name config.json -not -path '*/.git/*' -not -path '*/bin/*' -not -path '*/obj/*' | sort | while read -r cfg; do
    # A case also has STLs next to its config; this skips config snapshots and templates.
    [[ -f "$(dirname "$cfg")/solidBody.stl" ]] || continue
    dir="$(cd "$(dirname "$cfg")" && pwd -P)"
    echo "/$(basename "$dir")/ solver: $(running _simulate "$dir") watcher: $(running _watch "$dir")"
done
