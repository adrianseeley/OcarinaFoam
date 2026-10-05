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
The fully commented script installs OpenCFD **v2606**, .NET 10, MPI, supervisord and
systemd user session support. It runs on bare-metal/VM Ubuntu and inside Docker
containers (e.g. Vast.ai) as root or via sudo. On a systemd host it also enables
lingering for your SSH account; in a container (no systemd) it skips that. Reconnect
over SSH after it finishes.

### Job watcher

Long-running solver and render jobs are supervised by the `watcher` key in each case's
`config.json` (required):

- `"systemd"`: per-user systemd units (`systemctl --user`). Use on normal Ubuntu hosts.
- `"supervisord"`: a private per-user supervisord instance (config in `~/.config/ocarina`,
  state in `~/.local/state/ocarina`), started automatically on first use. Use in Docker
  or any host without systemd.

The commands below behave the same with either watcher; "unit" means a systemd unit or
a supervisord program. Run the following commands as that account, **without sudo**:

```bash
dotnet restore --locked-mode
dotnet publish -c Release --no-restore -o "$HOME/.local/share/ocarina"
mkdir -p "$HOME/.local/bin"
ln -sfn "$HOME/.local/share/ocarina/ocarina" "$HOME/.local/bin/ocarina"
export PATH="$HOME/.local/bin:$PATH"
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
| `ocarina simulate DIR` | Create its job (unit/program) if needed, start if stopped, or attach if running. Follow its logs. |
| `ocarina simulate stop DIR` | Stop solver and MPI children, clear failure state, remove the unit, reload the watcher. Keep logs/results. |
| `ocarina render DIR` | The same lifecycle for the runtime-configured renderer. |
| `ocarina render preview DIR` | Foreground layout preview at `DIR/previews/layout_VIEW.png`, one per view, using the built solid wireframe; consumes no field history and writes nothing under `renders/`. |
| `ocarina render stop DIR` | Stop renderer workers and remove its unit. Keep logs/results. |
| `ocarina start DIR` | Operator shortcut: start the solver and renderer as detached services, then stay in the foreground reporting every 30 s: frames simulated and remaining, simulation rate, estimated time left, frames fully rendered and remaining. Ctrl+C detaches; the services keep running. Exits by itself when both stop. Already-running services are left alone. |
| `ocarina stop DIR` | Stop the renderer, then the solver, removing both units. Keeps logs/results. |
| `ocarina reset DIR` | Stop both services, then return a built case to t=0 without rebuilding: keeps the mesh, decomposition and initial fields (`0/`), deletes solver time directories, probe histories, `renders/`, `audio/`, `videos/`, `wav/`, `previews/`, the report and the solver/render logs (build logs are kept). Then `ocarina start DIR` begins again. |
| `ocarina clean DIR` | Stop both services, then delete everything generated: `foam/`, `renders/`, `previews/`, `audio/`, `logs/`, `videos/`, `wav/`, `report.html`, `report.zip`, lock files and leftover build stages. Keeps your inputs (STLs, `config.json`, model files). |
| `ocarina audio DIR` | Turn every probe's pressure history into `DIR/audio/NAME.wav` (24-bit mono, 96 kHz) with CSVs of each stage (`NAME/native.csv`, `audio.csv`, `spectrum.csv`), three plots per probe (`NAME/plots/waveform/K_of_N.png` (one plot per `plots.waveformPointsPerPlot` samples, so the scale is the same on every plot and the last may be partly filled), `spectrum.png`, `punch.png`) plus, with more than one probe, joint plots with every probe as a line (`plots/all_waveform/K_of_N.png`, `plots/all_spectrum.png`) and details in `audio/audio.log`. Always deletes and rebuilds `audio/`; safe to run while the solver runs (uses what is written so far). |
| `ocarina report DIR` | Stop the solver and renderer if running (never restarts them), rebuild `audio/`, then write `DIR/wav/`, `DIR/videos/`, `DIR/report.html` and `DIR/report.zip`. Driven by the required `report` block of `config.json`: `slowdowns` (default list 1 to 100000; N = 1 is the untouched original), `framesPerSecond`, `videoLongSidePixels`, `videoCrf`, `slowedAudioMinimumSampleRateHz`. `wav/NAME_xN.wav` is the same samples played N times slower, with no pitch shifting (pitch simply falls, down to infrasound), 16-bit at the original rate divided by N but not below the minimum rate. `videos/FIELD.mp4` is one silent, browser-friendly H.264 video per rendered field containing every rendered frame in order at `framesPerSecond` (none dropped or repeated). `report.html` has the config, preflight, layout, each video in a `<video>` player, a grid of `<audio>` players (probes down, slowdowns across) and the joint and per-probe waveform/spectrum/punch plots, all linked by relative path, not embedded. The zip holds everything except field data (`foam/processor*`, numeric time directories other than `0`), the per-frame PNGs in `renders/`, lock files and build stages. Needs `ffmpeg`. **Disk and time grow with the slowdown**: N times the simulated time per probe, e.g. 3 s at 100000x is 83 hours of audio; the report prints an estimate of the WAV size first, so trim `slowdowns` to suit. |
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
- `renders/` — `FIELD/VIEW/NNNNNNNNN.png` for each enabled field (`pressure`,
  `velocityMagnitude`, `density`, `temperature`) and each entry of `renderer.views`,
  plus completion records. Every frame is `plotWidth` x `plotHeight` (1920x1080 in the
  example): a title line (view, field, point count, time), a subtitle with the colour
  scale, a colour bar, then the render area. `renderer.views` is a flat list of camera
  objects (`name`, `from`, `up`, `targetMillimeters`, `zoom`); names use letters, digits,
  `-` and `_` only. `ocarina report` makes one silent video per field and view at
  `videos/FIELD/VIEW.mp4`.
- `audio/` — from `ocarina audio`: per-probe WAV, stage CSVs and `audio.log`. Native-rate CSVs have one row per probe sample (3 s at 1e-7 s is 30 million rows, several GB per probe, so check disk).
  Pipeline: mean removal, Kaiser-windowed-sinc resample (anti-aliased), 2 Hz zero-phase high-pass (drift removal only; `highPassHz` 0 disables), peak normalisation to -1 dBFS, 24-bit quantisation. The `audio` block in `config.json` is required with every key (not part of the build fingerprint; `ocarinaZero/config.json` shows them all): `sampleRateHz`, `highPassHz`, `fadeMilliseconds`, `peakTargetDbfs`, `sharedGain`, `kernelZeroCrossings`, `kaiserBeta`, and a nested `plots` block (`enabled`, `concertAHz`, `minimumOctave`, `maximumOctave`, `maximumFrequencyHz`, `spectrumWidth`/`Height`, `punchWidth`/`Height`, `labelFontPixels`, `displayFloorDbfs`).
  Plots describe the processed (pre-quantisation) audio. `spectrum.png` folds the FFT (dBFS) onto one C-to-C axis with one flat-coloured line per octave (blue low octave to red high octave, linearly interpolated between FFT bins), so harmonics line up vertically; the joint `all_spectrum.png` instead uses a log frequency axis with one fixed flat colour per probe (`#B39AF5`, `#79D7E8`, `#F291BE`, `#E8C878`, `#8DD3A8`, cycling). Every plot is 1280x720 by default (`plots.*Width/Height`; `labelFontPixels` for spectrum and waveform text, `punchLabelFontPixels` for the punch grid). Every render tile is 1280x720, so a 4x2 field PNG is 5120x1440; videos default to that width. `punch.png` is an octave-by-pitch-class grid (12 columns, high octaves at the top) whose cells are the Hann-windowed Fourier magnitude evaluated *exactly* at each note frequency, not rounded to an FFT bin. Everything (renders, plots, report) uses the ultraviolet palette (black, `#180C38`, `#392075`, `#6248B3`, `#A08CE4`, `#E7DFFF`; magnitude only, no sign) in `src/Theme.cs`. Note colours are min/max-scaled in dB across that probe's valid notes only (floor-clamped), so colours are not comparable between probes; cell text gives dB relative to the strongest sampled note. Notes above the supported band (below Nyquist of both the probe and audio rates, and `maximumFrequencyHz`) show N/A. Enharmonic aliases (up to double sharps/flats) share their cell and are listed in the key, not duplicated. These are spectrum samples, not detected notes: harmonics and transients light cells too, and a short record cannot resolve adjacent notes (the plots warn when neighbouring notes are closer than 2/T).
- `previews/` — optional non-consuming layout previews (`layout_VIEW.png`).
- `logs/` — timestamped build steps and solver runs, plus service logs for attaching.

Rendering consumes old raw field directories after their own frame and the next
frame finish. It keeps the three newest positive times on every processor and one
predecessor needed for change-based opacity (point alpha runs from `renderer.minimumAlpha` for unchanged points to 1 for the largest change). Probe histories remain. This is a
render-and-consume pipeline: copy raw output separately if an experiment needs it
for other post-processing. The renderer always uses the case's `config.json`;
it keeps no copy of it. Frames already written keep the layout they were rendered with,
so delete `renders/` when you change the layout and want uniform output. Detailed
lifetime/recovery rules are in the template guide.

On solver restart the CLI verifies latest-time agreement and mandatory fields on
every rank. It restores the fixed sponge reference from each rank's initial state.
It does not silently roll back a partially written solver step. The error and
retained files let you diagnose what failed before explicitly retrying.

## Source layout and verification

One `ocarina.csproj` at the root, sources in `src/`, no application namespaces. Data types have public fields only.
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
Ubuntu/OpenFOAM/watcher acceptance steps.

Original project code, example geometry, documentation and stroke glyphs are offered
under [CC0](LICENSE). Third-party software retains its own licences; see
[THIRD_PARTY.md](THIRD_PARTY.md).
