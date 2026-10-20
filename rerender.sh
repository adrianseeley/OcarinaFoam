#!/usr/bin/env bash
# Rebuild all six cases' renders and pressure videos using renderSettings.json.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"

for tool in dotnet ffmpeg python3; do
    command -v "$tool" >/dev/null || { echo "Missing required command: $tool" >&2; exit 1; }
done

mkdir -p temp
dotnet publish -c Release -o "$PWD/temp/render-bin"
renderer=(dotnet "$PWD/temp/render-bin/ocarina.dll")
settings=renderSettings.json
mode=$(python3 -c 'import json, sys; v=json.load(open(sys.argv[1])); panels=v if isinstance(v,list) else [v]; modes={p["dataMode"] for p in panels}; assert len(modes)==1, "All panels must share dataMode"; print(next(iter(modes)))' "$settings")
case "$mode" in
    standard|firstDifferences|secondDifferences) ;;
    *) echo "Unsupported dataMode: $mode" >&2; exit 1 ;;
esac
cases=(ocarinaZeroSlim ocarinaZeroSlimC ocarinaZeroSlimD ocarinaZeroSlimO ocarinaZeroSlimN ocarinaZeroSlimU)
for simulation in "${cases[@]}"; do
    [[ -d "$simulation" ]] || { echo "Missing case: $simulation" >&2; exit 1; }
done

for simulation in "${cases[@]}"; do
    echo "Cleaning renders: $simulation"
    "${renderer[@]}" render clean "$simulation"
done
# Render and encode one case at a time to bound disk use for wide frames.
for simulation in "${cases[@]}"; do
    echo "Rendering: $simulation ($mode)"
    "${renderer[@]}" render "$simulation" "$settings"
    echo "Encoding video: $simulation-pressure.mp4"
    ffmpeg -framerate 60 -pattern_type glob -i "$simulation/renders/$mode/pressure_*.png" \
        -c:v libx264 -threads 4 -crf 18 -pix_fmt yuv420p -movflags +faststart "$simulation-pressure.pending.mp4" -y
    mv -- "$simulation-pressure.pending.mp4" "$simulation-pressure.mp4"

done
echo "Finished all six renders and videos."
