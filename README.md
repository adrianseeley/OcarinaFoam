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
| `ocarina DIR` | If `DIR/foam/.built` is missing, build first: hydrate `DIR/foam`, check surfaces, extract features, mesh, check topology/geometry, decompose for MPI and write `points/position.bin.gz`, one logged step at a time, stopping at the first failure. The `.built` marker is written last, so a failed build is simply retried by running the command again. Then start the solver and the watcher as detached services (already-running ones are left alone) and report every 30 s: service status, simulation time, frames simulated and written of the total, and percent complete. Ctrl+C stops the monitor only. Exits by itself when both services stop. Run it again after a stop to resume from the last complete solver write. |
| `ocarina stop DIR` | Stop the watcher, then the solver, removing both units. Keeps logs/results. |
| `ocarina clean DIR` | Stop both services, then delete everything generated: `foam/`, `points/`, `logs/`, failed build stages, lock files. Inputs (STLs, `config.json`) stay. |

Services continue after Ctrl+C and SSH logout. They do **not** restart on failure or
after a reboot. A failed job stays stopped with its error visible; invoking its
command again is an explicit retry. Unit names use the canonical case path, so cases
with the same folder name do not collide. Always use the same account, and stop both
services before moving a case. Multiple cases can run independently.

A build is published as `foam/` only after all steps pass. A failed build leaves its
partial `.foam-build-*` directory and numbered logs for inspection. A mesh-only case
can be rebuilt; a case containing simulation/points results is never erased by build.
Create a new directory for a new experiment. Config physics changes after a build
are rejected rather than silently disagreeing with the generated dictionaries.

## Outputs and restart behaviour

- `foam/` — generated dictionaries, metre-scale STLs, static mesh, config snapshot,
  `processorN/` fields when parallel, and `postProcessing/` probe time series.
- `points/` — the field history in compact binary, all little-endian float32, gzip-compressed:
  - `position.bin.gz`, written by `ocarina build`: the cell centre of every point as `XYZXYZXYZ...`.
    Processor 0's cells come first, then processor 1's, and so on. The mesh never changes,
    so points are never added, removed or moved and no ids are stored.
  - `SSSsMMMmsUUUusNNNns.bin.gz` (e.g. `000s000ms100us000ns.bin.gz`), one per written time: for each
    point, in the same order as `position`, the enabled fields back to back. Order
    is velocity (`vX vY vZ`), temperature, pressure, density, skipping any disabled in the `points`
    block of `config.json`, e.g. `vXvYvZ T p` for point 0, then point 1, and so on. That config
    block is the only record of which fields a frame holds, so keep it fixed for a run and
    delete `points/` frames if you change it. Fields are the raw SI values, not magnitudes.
  - `points` config keys: `pollMilliseconds`, `compression` (`fastest`, `optimal` or `smallest`),
    `velocity`, `temperature`, `pressure`, `density`.
- `foam/postProcessing/acousticProbes/` — probe time series, written by the solver as before (no audio processing).
- `logs/` — timestamped build steps and solver runs, plus service logs for attaching.

The watcher keeps the three newest positive times on every processor. Once a fourth exists it
writes the oldest as a `points/` frame and deletes that raw time from every processor (an
existing frame is never recomputed). Probe histories remain. This is a convert-and-consume
pipeline: copy raw output separately if an experiment needs it for other post-processing.
The watcher always uses the case's `config.json`. Detailed lifetime/recovery rules are in
the template guide.

On solver restart the CLI verifies latest-time agreement and mandatory fields on
every rank. It restores the fixed sponge reference from each rank's initial state.
It does not silently roll back a partially written solver step. The error and
retained files let you diagnose what failed before explicitly retrying.

## Source layout and verification

One `ocarina.csproj` at the root, sources in `src/`, no application namespaces. Data types have public fields only.
The `.cs` files contain static operations; points functions are split into
`Points.*.cs` files sharing one static class. `foamTemplate/` owns simulation policy;
`ocarinaZero/` owns the example's inputs. No legacy generators or generated case is
shipped. Shell is used only for machine provisioning and sourcing OpenFOAM's own
environment before executing its tools.

```bash
dotnet build -c Release --no-restore
ocarina self-test
```

Self-tests use tiny synthetic meshes/fields, exercise frame naming,
field parsing, config contracts and
case protection. They do not run a physical simulation or replace deployment testing.
See [VALIDATION.md](VALIDATION.md) for the checks run on this revision and the remaining
Ubuntu/OpenFOAM/watcher acceptance steps.

Original project code, example geometry, documentation and stroke glyphs are offered
under [CC0](LICENSE). Third-party software retains its own licences; see
[THIRD_PARTY.md](THIRD_PARTY.md).
