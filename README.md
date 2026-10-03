# OcarinaFoam — beta

One C# command-line application turns an ocarina's ASCII STLs into an OpenFOAM
case, builds its mesh, runs the simulation, and renders the resulting fields.
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
renderer workers share those resources; the sample's six of each are not a
hardware recommendation.

```bash
git clone https://github.com/adrianseeley/OcarinaFoam.git
cd OcarinaFoam
sudo bash prepare-machine.sh
```

For a downloaded source archive, extract it and run the script from that directory.
The fully commented script installs OpenCFD **v2606**, .NET 10, MPI and systemd user
session support, then enables lingering for your SSH account. Reconnect over SSH
after it finishes. Run the following commands as that account, **without sudo**:

```bash
dotnet restore --locked-mode
dotnet publish -c Release --no-restore -o "$HOME/.local/share/ocarina"
mkdir -p "$HOME/.local/bin"
ln -sfn "$HOME/.local/share/ocarina/ocarina" "$HOME/.local/bin/ocarina"
export PATH="$HOME/.local/bin:$PATH"
ocarina self-test
```

Keep `~/.local/bin` on your shell's PATH. Keep the entire publish directory: it
contains native renderer dependencies and `foamTemplate`, not just the executable.
Stop services before updating that installation. Package versions are pinned in
`packages.lock.json`; the app uses SkiaSharp for raster drawing/PNG encoding and
its own small line font for labels.

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
rendered frame sets.** Start with a short duration and suitable field output interval
for a first deployment check; probe sampling is independent.

```bash
ocarina build "$HOME/myCase"
ocarina simulate "$HOME/myCase"
# Ctrl+C detaches; the solver continues.
ocarina render "$HOME/myCase"
# Ctrl+C detaches; the renderer continues.
ocarina render preview "$HOME/myCase"
ocarina check "$HOME/myCase"
```

| Command | Behaviour |
|---|---|
| `ocarina build DIR` | Hydrate `DIR/foam`, check surfaces, extract features, mesh, check topology/geometry, and decompose for MPI. One logged step at a time; stop at the first failure. |
| `ocarina simulate DIR` | Create its user service if needed, start if stopped, or attach if running. Follow its logs. |
| `ocarina simulate stop DIR` | Stop solver and MPI children, clear failure state, remove the unit, reload systemd. Keep logs/results. |
| `ocarina render DIR` | The same lifecycle for the runtime-configured renderer. |
| `ocarina render preview DIR` | Foreground layout preview at `DIR/previews/layout.png` using the built solid wireframe; consumes no field history and does not write `renders/config.json`. |
| `ocarina render stop DIR` | Stop renderer workers and remove its unit. Keep logs/results. |
| `ocarina start DIR` | Operator shortcut: start the solver and renderer services detached (no log following). Already-running services are left alone. |
| `ocarina stop DIR` | Stop the renderer, then the solver, removing both units. Keeps logs/results. |
| `ocarina check DIR` | Show both service states, complete rendered frame count, eligible backlog, held writes and latest time per rank. |

Services continue after Ctrl+C and SSH logout. They do **not** restart on failure or
after a reboot. A failed job stays stopped with its error visible; invoking its
command again is an explicit retry. Unit names use the canonical case path, so cases
with the same folder name do not collide. Always use the same account, and stop both
services before moving a case. Multiple cases can run independently.

A build is published as `foam/` only after all steps pass. A failed build leaves its
partial `.foam-build-*` directory and numbered logs for inspection. A mesh-only case
can be rebuilt; a case containing simulation/render results is never erased by build.
Create a new directory for a new experiment. Config physics changes after a build
are rejected rather than silently disagreeing with the generated dictionaries.

## Outputs and restart behaviour

- `foam/` — generated dictionaries, metre-scale STLs, static mesh, config snapshot,
  `processorN/` fields when parallel, and `postProcessing/` probe time series.
- `renders/` — separate `pressure/`, `velocityMagnitude/`, `density/`, and
  `temperature/` directories containing nine-digit PNG names such as
  `pressure/000000000.png`, plus completion records. One countable frame includes
  all enabled fields.
- `previews/` — optional non-consuming layout previews (`layout.png`).
- `logs/` — timestamped build steps and solver runs, plus service logs for attaching.

Rendering consumes old raw field directories after their own frame and the next
frame finish. It keeps the three newest positive times on every processor and one
predecessor needed for change-based opacity. Probe histories remain. This is a
render-and-consume pipeline: copy raw output separately if an experiment needs it
for other post-processing. The saved recipe in `renders/config.json` freezes as soon
as normal renderer startup accepts it (before first frame completion), because consumed
history cannot be recreated with a later layout change. Detailed lifetime/recovery
rules are in the template guide.

On solver restart the CLI verifies latest-time agreement and mandatory fields on
every rank. It restores the fixed sponge reference from each rank's initial state.
It does not silently roll back a partially written solver step. The error and
retained files let you diagnose what failed before explicitly retrying.

## Source layout and verification

One `ocarina.csproj`, no application namespaces. Data types have public fields only.
The `.cs` files contain static operations; renderer functions are split into
`Render.*.cs` files sharing one static class. `foamTemplate/` owns simulation policy;
`ocarinaZero/` owns the example's inputs. No legacy generators or generated case is
shipped. Shell is used only for machine provisioning and sourcing OpenFOAM's own
environment before executing its tools.

```bash
dotnet build -c Release --no-restore
ocarina self-test
```

Self-tests use tiny synthetic meshes/fields, render real PNGs, and exercise scale,
parsing, output lag, failure handling, completion/recovery, config contracts and
case protection. They do not run a physical simulation or replace deployment testing.
See [VALIDATION.md](VALIDATION.md) for the checks run on this revision and the remaining
Ubuntu/OpenFOAM/systemd acceptance steps.

Original project code, example geometry, documentation and stroke glyphs are offered
under [CC0](LICENSE). Third-party software retains its own licences; see
[THIRD_PARTY.md](THIRD_PARTY.md).
