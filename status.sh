#!/usr/bin/env bash
# Recursively finds every config.json and reports its solver/watcher status and points file count.
root="${1:-.}"
procs="$(ps -eo args=)"

running() { # running <_simulate|_watch> <case dir>
    grep -qxE ".*/ocarina $1 $2/?" <<<"$procs" && echo running || echo stopped
}

{
    printf 'CASE\tSOLVER\tWATCHER\tPOINTS\tLATEST\tTARGET\n'
    find "$root" -type f -name config.json -not -path '*/.git/*' -not -path '*/bin/*' -not -path '*/obj/*' | sort | while read -r cfg; do
        # A case also has STLs next to its config; this skips config snapshots and templates.
        [[ -f "$(dirname "$cfg")/solidBody.stl" ]] || continue
        dir="$(cd "$(dirname "$cfg")" && pwd -P)"
        points=0
        [[ -d "$dir/points" ]] && points="$(find "$dir/points" -type f | wc -l)"
        # Frame names are fixed-width times, so the last name in sort order is the latest.
        latest="$(ls "$dir/points" 2>/dev/null | grep -E '^[0-9]{3}s[0-9]{3}ms[0-9]{3}us[0-9]{3}ns\.bin\.br$' | sort | tail -n 1)"
        latest="${latest%.bin.br}"
        target="$(sed -n 's/.*"lastT"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$cfg")"
        printf '%s\t%s\t%s\t%s\t%s\t%s\n' "/$(basename "$dir")/" "$(running _simulate "$dir")" "$(running _watch "$dir")" "$points" "${latest:--}" "${target:--}"
    done
} | column -t -s $'\t'
