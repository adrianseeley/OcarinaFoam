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

`ocarina render DIR renderSettings.json` renders a 2048×2048 point-cloud bitmap for each
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
  "fadeUnchanged": true,
  "unchangedAlpha": 0.15,
  "filter": {
    "x": { "greaterThanEqual": -1, "lessThanEqual": 1 }
  }
}
```

`field`, `frames`, `view`, `color`, `pointSize`, `alpha`, and `dataMode` are required.
Unknown settings and comparison names are rejected. `filter` defaults to `{}` (all points),
`fadeUnchanged` defaults to `true`, and `unchangedAlpha` defaults to `0`.

- `frames`: `"*"` for every eligible saved frame, `"-t"` or `"-1"` for the latest,
  a string containing a zero-based frame index (negative indices count from the end),
  or a timestamp such as `"000s001ms000us000ns"`. Indices refer to original saved frames.
- `view`: `fromLeft`, `fromRight`, `fromAbove`, `fromBelow`, `fromTop`, `fromBottom`,
  or `fromAboveRight`.
- `color`: `inferno` (dark purple through orange to pale yellow), `gameboy` (olive greens),
  `vanimo` (diverging pink/purple through dark to green), or `hsv` (the full hue rainbow).
  HSV has red at both ends, so opposite extremes share a colour.
- `pointSize` is the positive integer diameter of each circular point in pixels;
  `alpha` is its maximum opacity from 0 to 1. Points are composited back to front.
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

`fadeUnchanged` works in **all three modes** without changing the colour values. In standard
mode it measures `abs(p[n] - p[n-1])`; in difference modes it measures the absolute value
of the displayed difference. Activity is normalized independently in each rendered frame by the maximum absolute
activity among its selected, finite points. A larger change in another frame does not
affect this frame's opacity; zero activity maps to the floor and this frame's largest
activity maps to full configured alpha. Colour scaling still uses all saved frames. Opacity is
`alpha * (unchangedAlpha + (1 - unchangedAlpha) * normalizedActivity)`.
Thus `unchangedAlpha: 0` hides zero activity, while `0.15` keeps it faintly visible at 15%
of the configured alpha. The first standard frame has no preceding history and uses the
configured alpha. Setting `fadeUnchanged: false` gives every finite selected point the
configured alpha, including points with zero change.

Output is `DIR/renders/DATA_MODE/pressure_TIMESTAMP.bmp`, keeping standard and difference
images separate. The render and video commands in `copy` currently use `standard`; if you
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
