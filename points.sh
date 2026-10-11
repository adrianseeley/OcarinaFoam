#!/usr/bin/env bash
# Recursively finds every points/ directory and prints its parent name and file count.
root="${1:-.}"

find "$root" -type d -name points -not -path '*/.git/*' | sort | while read -r dir; do
    parent="$(basename "$(dirname "$(cd "$dir" && pwd)")")"
    count="$(find "$dir" -type f | wc -l)"
    echo "/$parent/points/ $count"
done
