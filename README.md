# OcarinaFoam — beta

One C# command-line application turns an ocarina's ASCII STLs into an OpenFOAM
case, builds its mesh, runs the simulation, and distils the fields into compact point-cloud frames.
Everything runs on the deployment machine. No JavaScript, generated C#, bundled
font, GUI, or manual OpenFOAM command sequence.

The baseline uses transient compressible dry-air flow, WALE LES, a prescribed inlet
jet, rigid walls, and an approximate open exterior. It is a repeatable research
starting point awaiting physical validation. The model choices, alternatives,
configuration reference and limitations live in **[foamTemplate](foamTemplate/README.md)**
and the commented dictionaries themselves.

## Prepare an Ubuntu server

Use **Ubuntu 24.04 or 26.04 LTS, amd64**, an ordinary SSH account with sudo, and
sufficient RAM/disk for your chosen mesh and output rate. Solver ranks and
watcher share those resources; the sample's six of each are not a
hardware recommendation.

```bash
git clone https://github.com/adrianseeley/OcarinaFoam.git
cd OcarinaFoam
sudo bash prepare-machine.sh
```

For a downloaded source archive, extract it and run the script from that directory.
The fully commented script installs OpenCFD **v2606**, .NET 10, MPI, supervisord and
systemd user session support. It runs on bare-metal/VM Ubuntu and inside Docker
containers (e.g. Vast.ai) as root or via sudo. On a systemd host it also enables
lingering for your SSH account; in a container (no systemd) it skips that. Reconnect
over SSH after it finishes.

### Job watcher

Long-running solver and watch jobs are supervised by the `watcher` key in each case's
`config.json` (required):

- `"systemd"`: per-user systemd units (`systemctl --user`). Use on normal Ubuntu hosts.
- `"supervisord"`: a private per-user supervisord instance (config in `~/.config/ocarina`,
  state in `~/.local/state/ocarina`), started automatically on first use. Use in Docker
  or any host without systemd.

The commands below behave the same with either watcher; "unit" means a systemd unit or
a supervisord program. Run the following commands as that account, **without sudo**:

```bash
./compile.sh
```

It restores, publishes to `~/.local/share/ocarina` and links `~/.local/bin/ocarina`;
re-run it for every rebuild.

Keep `~/.local/bin` on your shell's PATH. Keep the entire publish directory: it
contains `foamTemplate`, not just the executable.
Stop services before updating that installation. The app has no package dependencies.

## Create and run a case

Copy the example to a writable working location:

```bash
cp -r ocarinaZero "$HOME/myCase"
```

A case needs `config.json`, `solidBody.stl`, and `spawnPlane.stl`. `model.scad` is
included as editable CAD source; building uses the exported STLs. Inputs are ASCII
STL in **millimetres**. The inlet is a rectangular face at **y=0**, directed along
**+Y**; the solid also starts at y=0. A thin inlet box extending below y=0 is accepted:
only its planar y=0 face is used. Export changed CAD before building.

Edit the case's config before building. The example preserves the research settings:
**3 seconds at 1e-7 s steps, with full fields every 10 steps means roughly 3 million
point-cloud frames.** Start with a short duration and suitable field output interval
for a first deployment check; probe sampling is independent.

```bash
ocarina "$HOME/myCase"        # build if needed, start solver and watcher, monitor
# Ctrl+C leaves monitoring; both services keep running. Run it again to re-attach.
ocarina stop "$HOME/myCase"
ocarina clean "$HOME/myCase"
```

| Command | Behaviour |
|---|---|
| `ocarina DIR` | If `DIR/foam/.built` is missing, build first: hydrate `DIR/foam`, check surfaces, extract features, mesh, check topology/geometry, decompose for MPI and write `points/position.bin.br`, one logged step at a time, stopping at the first failure. The `.built` marker is written last, so a failed build is simply retried by running the command again. Then start the solver and the watcher as detached services (already-running ones are left alone) and report every 30 s: service status, simulation time, frames simulated and written of the total, and percent complete. Ctrl+C stops the monitor only. Exits by itself when both services stop. Run it again after a stop to resume from the last complete solver write. |
| `ocarina all` | From the current directory, launch every discovered case in a detached worker. Each worker builds if needed and starts its services; the command returns immediately. Worker output is written to each case's `logs/all.log`. |
| `ocarina stop DIR` | Stop the watcher, then the solver, removing both units. Keeps logs/results. |
| `ocarina clean DIR` | Stop both services, then delete everything generated: `foam/`, `points/`, `logs/`, failed build stages, lock files. Inputs (STLs, `config.json`) stay. |
| `ocarina stop all` | From the current directory, stop every case found recursively in its subdirectories. |
| `ocarina clean all` | From the current directory, stop and clean every case found recursively in its subdirectories. |

Services continue after Ctrl+C and SSH logout. If a service exits unexpectedly, its
watcher automatically retries it (systemd waits 10 seconds and limits repeated starts;
supervisord also retries unexpected exits). Normal completion and an explicit
`ocarina stop` are not restarted. After the restart limit is reached, inspect the
service log and invoke `ocarina DIR` again for an explicit retry. Services do not
start automatically after a reboot; run `ocarina DIR` once after reboot. Unit names
use the canonical case path, so cases with the same folder name do not collide.
Always use the same account, and stop both services before moving a case. Multiple
cases can run independently.

A build is published as `foam/` only after all steps pass. A failed build leaves its
partial `.foam-build-*` directory and numbered logs for inspection. A mesh-only case
can be rebuilt; a case containing simulation/points results is never erased by build.
Create a new directory for a new experiment. Config physics changes after a build
are rejected rather than silently disagreeing with the generated dictionaries.

## Outputs and restart behaviour

- `foam/` — generated dictionaries, metre-scale STLs, static mesh, config snapshot,
  `processorN/` fields when parallel, and `postProcessing/` probe time series.
- `points/` — the field history in compact binary, float32 values, Brotli-compressed (see encoding below):
  - `position.bin.br`, written by `ocarina build`: the cell centre of every point as `XYZXYZXYZ...`.
    Processor 0's cells come first, then processor 1's, and so on. The mesh never changes,
    so points are never added, removed or moved and no ids are stored.
  - `SSSsMMMmsUUUusNNNns.bin.br` (e.g. `000s000ms100us000ns.bin.br`), one per written time: for each
    point, in the same order as `position`, the enabled fields back to back. Order
    is velocity (`vX vY vZ`), temperature, pressure, density, skipping any disabled in the `points`
    block of `config.json`, e.g. `vXvYvZ T p` for point 0, then point 1, and so on. That config
    block is the only record of which fields a frame holds, so keep it fixed for a run and
    delete `points/` frames if you change it. Fields are SI values, not magnitudes, except
    pressure, which is stored as gauge pressure: `p` minus `physics.ambientPressureHectopascals` (in Pa),
    so acoustic fluctuations keep their precision. Add the ambient back for absolute pressure.
  - Encoding of every `.bin.br` file: decompress with Brotli to get float32 byte planes, one per
    (channel, byte index) in channel-major order, each plane holding that byte for every point in point
    order. Channel c of point i has byte k at `plane[c*4+k][i]` (little-endian), where the channel count is
    3 for `position` and the enabled field components for a frame. Point order is never changed.
  - `points` config keys: `keepEvery`, `pollMilliseconds`, `compression` (`fastest`, `optimal` or
    `smallest`: Brotli quality 4, 9 or 11), `mantissaBits` (23 = lossless; lower rounds each value to that
    many mantissa bits for much smaller frames), `velocity`, `temperature`, `pressure`, `density`.
- `foam/postProcessing/acousticProbes/` — probe time series, written by the solver as before (no audio processing).
- `logs/` — timestamped build steps and solver runs, plus service logs for attaching.

The watcher keeps the three newest positive times on every processor. Once a fourth exists it
writes the oldest as a `points/` frame and deletes that raw time from every processor (an
existing frame is never recomputed). Probe histories remain. This is a convert-and-consume
pipeline: copy raw output separately if an experiment needs it for other post-processing.
The watcher always uses the case's `config.json`. Detailed lifetime/recovery rules are in
the template guide.

## Rendering selected points

`ocarina render clean DIR` deletes only `DIR/renders`, including every data-mode subfolder.
It succeeds when the folder is already absent and leaves simulation data, settings, and videos elsewhere untouched.

`ocarina render DIR renderSettings.json` renders a 1024×1024 point-cloud area with a header and compact legends for each
selected frame. The settings file path is relative to the current working directory,
independently of the case directory. The root `renderSettings.json` is ready to use:

```json
{
  "field": "pressure",
  "frames": "*",
  "view": "fromRight",
  "color": "hsv",
  "pointSize": 17,
  "alpha": 1.0,
  "dataMode": "standard",
  "removeRankBandMiddle": 0.05,
  "removeRankBandLow": null,
  "removeRankBandHigh": null,
  "removeDeltaRankBandLow": null,
  "removeDeltaRankBandMiddle": null,
  "removeDeltaRankBandHigh": null,
  "filter": {
    "x": { "greaterThanEqual": -1, "lessThanEqual": 1 }
  }
}
```

`field`, `frames`, `view`, `color`, `pointSize`, `alpha`, and `dataMode` are required.
Settings may also be a nonempty array of these objects. Array entries render left to right
in one image, each with its own legend and metadata. All entries must share
`frames` and `dataMode`; view, palette, filtering, opacity and removal controls
can differ. Each panel is 1024 pixels wide.

Unknown settings and comparison names are rejected. `filter` defaults to `{}` (all points),
`removeRankBandMiddle` defaults to `0.05`; all other rank-removal controls default to `null`.

- `frames`: `"*"` for every eligible saved frame, `"-t"` or `"-1"` for the latest,
  a string containing a zero-based frame index (negative indices count from the end),
  or a timestamp such as `"000s001ms000us000ns"`. Indices refer to original saved frames.
- `view`: `fromLeft`, `fromRight`, `fromAbove`, `fromBelow`, `fromTop`, `fromBottom`,
  or `fromAboveRight`.
- `color`: `inferno` (dark purple through orange to pale yellow), `gameboy` (olive greens),
  `vanimo` (diverging pink/purple through dark to green), or `hsv` (the full hue rainbow).
  HSV has red at both ends, so opposite extremes share a colour.
  `monochrome` runs from black through gray to white.
  `monochrome-contrast` applies a symmetric S-curve for darker shadows and brighter
  highlights, preserving the midpoint and endpoints without clipping the range.
  `monochrome-polarize` uses a much steeper S-curve for nearly black/white output,
  with a narrow transition around mid-gray.
- `pointSize` is the positive integer diameter of each circular point in pixels;
  `alpha` is the shared opacity from 0 to 1. Points are composited back to front.
- `filter` contains any of `x`, `y`, and `z`, in **millimetres**. Each axis accepts
  `greaterThan`, `greaterThanEqual`, `lessThan`, `lessThanEqual`, `equal`, and `notEqual`.
  All comparisons must match. For example, `"x": {"greaterThan": 6, "lessThanEqual": 10}`
  selects `6 < x <= 10`. Rendering uses the selected original points without interpolation.

`dataMode` controls the value used for colour, with `p[n]` denoting a point's pressure
in the nth saved frame:

| Mode | Value | History required |
| --- | --- | --- |
| `standard` | `p[n]` | None |
| `firstDifferences` | `p[n] - p[n-1]` | One preceding frame |
| `secondDifferences` | `p[n] - 2*p[n-1] + p[n-2]` | Two preceding frames |

Differences are signed changes between consecutive **saved** frames, without division by
the time interval; they are not time derivatives. With `frames: "*"`, frames without enough
history are skipped. Selecting such a frame explicitly gives an error. Single-frame
renders still read their preceding saved frames. Nonfinite values or history are skipped.

Every render scans all eligible saved frames available at startup for the selected points'
transformed range. Standard mode uses the observed minimum and maximum. Difference modes
use a symmetric range around zero, so zero stays at the palette midpoint. This scale is
shared by all rendered frames, including when selecting just one. It is per case and can
grow as new frames arrive.

Rank removal controls accept fractions from `0` to `1`, or `null` to disable:

- `removeRankBandMiddle: 0.05` hides the middle 5% (47.5th through 52.5th percentile).
- `removeRankBandLow: 0.05` hides the lowest 5%.
- `removeRankBandHigh: 0.05` hides the highest 5%.

Any point in any enabled band is hidden. `0` also disables a band; `1` hides all points.
Each band is calculated per frame by sorting selected finite points in all three data
modes and selecting a fraction of the point count, rounded to the nearest whole point
(half rounds up). Low takes the first N points, high takes the last N, and middle takes
N consecutive points centered in the sorted list (an odd spare point goes to the high tail).
Equal values are ordered by original point index so ties cannot increase the number hidden.
Long-tail magnitudes do not determine how many points are removed. Visible points share `alpha`.
`removeDeltaRankBandLow`, `removeDeltaRankBandMiddle`, and `removeDeltaRankBandHigh`
apply the same rank rules to the **absolute change of the displayed value** since the
preceding saved frame. For standard mode this is `abs(p[n] - p[n-1])`; for difference
modes it is the change between consecutive displayed differences. Low removes the
least-changing points and high removes the most-changing points. The earliest eligible
frame has no delta history and is unaffected by delta removal; a single-frame selection
still reads its preceding history. Nonfinite deltas are not ranked or drawn when delta
removal is enabled and history exists.

All six removal controls affect drawing only. Hidden points still contribute to both
histograms, colour limits, and both sets of rank cutoffs. Value and delta ranks are
calculated independently before any removal. Spatial `filter` exclusions are omitted
from these calculations entirely.

The old `centerBandPercent` setting has been replaced by these fraction controls.

Each lossless PNG panel is 1024×1580: a 100-pixel header, 1024×1024 plot, and 456-pixel footer.
The left-aligned header separates the title, filter, RANK MUTE and DELTA MUTE into four lines.
A right-aligned count column shows total points beside the title, filtered points
beside the filter, points remaining after rank muting beside RANK MUTE, and
final visible points after both mute stages beside DELTA MUTE. OFF labels are red
and specified fractions green, formatted with two decimal places in fixed columns. Palette, point diameter and alpha sit below the pressure
color bar, above PA. Pressure, delta rank and time occupy equal-height footer sections with white outlines and inner padding.
The color bar has its own white outline; delta rank has a curve with no bar beneath it.
Current time aligns left, a right-facing arrow is centered, and the latest saved time aligns right.
The footer contains the pressure histogram/palette, white DELTA RANK curve
(0.00–1.00), and frame progress with current time → latest saved time. Timestamp triplets use gold (seconds), cyan (milliseconds),
purple (microseconds), and pink (nanoseconds), with white units. Green
legend intervals and MUTE ACTIVE identify muted values/ranks. All panels share a saved-frame
snapshot taken when rendering starts, so running simulations do not change the time endpoint.
Its colour bar uses the same fixed range as the points, with low/high labels in Pa
(including signed differences). The white curve above the bar is a 128-bin histogram
of this frame's selected finite values, with its peak normalized to 1. Counts include
hidden points and are not weighted by opacity. The histogram uses the colour bar's range,
so its horizontal position corresponds directly to a colour. Green shading and
boundary lines identify muted numeric intervals. The pressure histogram remains a value
axis: muting the bottom 5% by rank can occupy less than 5% of its width.
At tied cutoff values, only the selected ranks are hidden.

The white delta curve plots absolute change ordered from smallest to largest, with
rank from 0.00 to 1.00 horizontally and height normalized to maximum change. Green
shading marks muted delta ranks. All selected finite values contribute to the legends,
including muted points. With no preceding frame the delta panel says so.
TOTAL is every recorded point, AFTER FILTER counts spatial matches, and AFTER MUTE
counts finite, unmuted points with positive opacity.

Output is `DIR/renders/DATA_MODE/pressure_TIMESTAMP.png`, keeping standard and difference
images separate. Run `./rerender.sh` to clean and regenerate renders for all six
`ocarinaZeroSlim` cases, then encode their pressure videos at 60 fps. It reads the video
input mode from `renderSettings.json` and overwrites the matching pressure MP4 files.
The script builds an isolated renderer in `temp/render-bin` and processes cases sequentially,
retaining lossless PNG frames after video encoding.
The render and video commands in `copy` currently use `standard`; if you
change `dataMode`, update the video input folder to match.

On solver restart the CLI verifies latest-time agreement and mandatory fields on
every rank. It restores the fixed sponge reference from each rank's initial state.
It does not silently roll back a partially written solver step. The error and
retained files let you diagnose what failed before explicitly retrying.

## Source layout and verification

One `ocarina.csproj` at the root, sources in `src/`, no application namespaces. Simulation config data types use public fields; render settings use required JSON properties.
The `.cs` files contain static operations; points functions are split into
`Points.*.cs` files sharing one static class. `foamTemplate/` owns simulation policy.
No legacy generators or generated case is shipped. Shell is used only for machine
provisioning and sourcing OpenFOAM's own environment before executing its tools.

```bash
dotnet build -c Release --no-restore
dotnet run --project tests/RenderChecks -c Release
```
See [VALIDATION.md](VALIDATION.md) for the checks run on this revision and the remaining
Ubuntu/OpenFOAM/watcher acceptance steps.

Original project code, example geometry, documentation and stroke glyphs are offered
under [CC0](LICENSE). Third-party software retains its own licences; see
[THIRD_PARTY.md](THIRD_PARTY.md).
