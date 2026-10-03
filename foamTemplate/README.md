# Ocarina baseline â€” beta 1

This folder is the simulation specification. The C# builder copies every file,
substitutes uppercase template placeholders, and runs the OpenCFD OpenFOAM **v2606** utilities on the
same machine. Read the comments in each dictionary for choices, alternatives,
and their consequences. Only `0/`, `constant/`, and `system/` contain solver input.

The purpose is a reproducible reference experiment against which geometry and
numerical changes can be compared. It is **not a physically validated â€œcorrect
ocarina model.â€** Physical validation remains future work. Preserve the baseline
when testing geometry; change one numerical/physical assumption at a time when
studying that assumption, and record the resulting case inputs and code revision.

## What is fixed

| Decision | Baseline |
|---|---|
| Solver / release | OpenCFD v2606, `rhoPimpleFoam` |
| Gas | Single dry perfect gas, constant Cv and transport |
| Turbulence | WALE LES with `cubeRootVol` filter |
| Driving | Abrupt, constant +Y inlet velocity; initially still air |
| Body | Rigid, stationary, no-slip, adiabatic fluid wall |
| Radiation | `waveTransmissive` pressure plus spherical velocity sponge |
| Clock | Fixed timestep, fixed step-based field and probe output |
| Mesh | Static, feature-refined, snapped, no prism layers |
| Partitioning | Serial at one rank; scotch/uncollated at multiple ranks |
| Geometry units | Input ASCII STL in mm; converted once to metres |
| Disk fields | ASCII, gzip compressed; no OpenFOAM purge |
| Renderer delay | Exactly three newer positive times on every rank |
| Runtime changes | No live physics dictionary changes |

The comments cover the significant alternative model families, not every model
implemented anywhere in OpenFOAM. Selecting arbitrary alternatives is a deliberate
code/template experiment; there is no compatibility layer or universal solver UI.

## Config reference

Each case owns `config.json`. Unknown keys are errors. Keep a full config with the
case so its intent is inspectable. The builder snapshots it to `foam/config.json`;
physics changes subsequently require a fresh case/build. Runtime rendering reads
this snapshot and the case's current renderer section; it never regenerates code.

| Key(s) | Meaning and baseline intent |
|---|---|
| `processorCount` | MPI ranks; 6 in the sample. Match available CPU/RAM. Rendering also needs CPU and memory. |
| `worldPaddingMillimeters` | Six per-side paddings around body bounds; yMin must be 0. **0 means exactly the solid's bound** (not rounded to the grid); positive values round outward to the grid. The class default is 100 mm per side; the sample config uses 0 everywhere except zMax = 100 (reduced-domain experiment). A positive zMax of at least two cells is required: locationInMesh lives in that air. |
| `backgroundCellSizeMillimeters` | 5 mm coarse grid before refinement. Inlet-aligned remainder bands can be smaller. |
| `surfaceRefinementMinLevel`, `surfaceRefinementMaxLevel` | 3 / 5; approximate coarse-cell halvings at the surface. |
| `featureRefinementLevel` | 5 around extracted features; scale matches the body surface. |
| `nCellsBetweenLevels` | 3; refinement-transition spacing. |
| `bodyDistanceRefinement` | Distance in mm and level pairs, sorted nearest first; default 2 mm/3, 5 mm/2, 10 mm/1. |
| `featureIncludedAngleDegrees` | 150; threshold for extracted sharp edges, separate from snapping feature angle. |
| `inletVelocityMetersPerSecond` | 12; prescribed flow speed, not supply pressure or microphone particle velocity. |
| `ambientPressureHectopascals` | 1013.25 hPa = 101325 Pa absolute. |
| `initialTemperatureCelsius` | 26.85 Â°C = 300 K. |
| `deltaTSeconds` | 1e-7 s; nominal acoustic Courant estimate printed at build. Actual cell sizes matter. |
| `endTimeSeconds` | Requested duration, 3 s; rounded to a write boundary plus three guard writes. |
| `fieldWriteIntervalTimeSteps` | 10; at the default dt, full fields every 1 microsecond. This is very large output: about 3 million frame sets for 3 seconds. |
| `probeWriteIntervalTimeSteps` | 1; independent compact p/U/T sampling every solver step. |
| `farFieldRelaxationLengthMeters` | 0.1; waveTransmissive relaxation length, not sponge thickness. |
| `acousticDampingEnabled` | true; compare a disabled sponge only as an explicit experiment. |
| `acousticDampingTargetFrequencyHz` | 3000; source-strength scale, not a tuned band-pass filter. |
| `acousticDampingThicknessMillimeters` | 250; configured spherical ramp width. The mesh clips this shell. |
| `acousticDampingStrengthMultiplier` | 20; OpenFOAM coefficient `w`, a dimensionless source-strength multiplier. |
| `acousticDampingClearanceMillimeters` | 20 beyond the body bounding sphere before damping starts. |
| `probes` | Named three-coordinate points **in metres**. They do not automatically follow CAD changes. The build rejects any probe outside the domain, inside the solid, or in fluid disconnected from locationInMesh; it never relocates one. |

For a practical first run, copy the sample into a new directory and choose a short
duration and a larger field-write interval. Probe sampling can remain dense while
field output is sparse. That trades visual time resolution, not solver time resolution.

## Renderer controls

| Key(s) inside `renderer` | Meaning |
|---|---|
| `renderThreads` | Concurrent frame workers; each owns a full point/field working set and a composite bitmap. Start small if RAM is limited. |
| `pollMilliseconds` | Discovery/idle interval, 2000 ms by default. |
| `pngCompressionLevel` | 0â€“9, default 6. Compression does not change PNG pixel values. |
| `plotWidth`, `plotHeight` | Per-tile pixels; 1024Â² gives a 5120Ã—3072 composite with the default 14 views plus legend. |
| `marginPixels`, `labelFontPixels` | Legend margin and stroke-letter size. Long legend labels shrink to fit. |
| `backgroundColor`, `labelColor` | Hex colours accepted by Skia. |
| `pointSizePixels` | Cell-centre marker size in pixels, not cell volume. |
| `axisTiltDegrees` | Optional camera tilt so aligned rows of cells do not hide each other. |
| `cameraPaddingFraction` | Padding around the solid's bounds for camera framing. |
| `renderPressure`, `renderVelocityMagnitude`, `renderDensity`, `renderTemperature` | Enabled field images. At least one is required. |

Concurrency and polling can change on restart. Once rendering has committed frames,
other renderer changes are rejected to prevent mixing visual recipes after source
fields have been consumed. Use a new case for a different recipe.

The renderer merges the partitions into a static cell-centre cloud. Hue is normalised
to each field's current minimum/maximum. Opacity is absolute change from the previous
available frame, normalised by the largest change. The first frame is opaque. Speed
is the magnitude of U; direction-only changes at constant speed are invisible.
These are qualitative field visualisations, not calibrated sound-pressure-level maps.
Pressure is absolute; derive acoustic pressure fluctuations from numerical probes.

Each frame's PNGs are published through temporary files. A completion record is
written only after every enabled image succeeds. Raw time t is deleted only after
t's images and its successor's images finish, preserving t for the successor's
change calculation. Recovery finishes interrupted deletions and skips committed
frames. `postProcessing/` probe output is retained. Do not delete PNGs, completion
records or raw times manually while a renderer owns the case.

The three-write holdback is a lag rule, not atomic filesystem completion. All required
fields must parse through their closing boundary block before a frame can commit.
A parse/render error stops the worker service; it never silently skips a bad frame.

## Reproducibility and validation

Keep original geometry/config, the generated foam dictionaries and bounds, build
logs, runtime logs, probes, and the code revision. Meshing logs and `foamVersion`
record the deployed tools; `packages.lock.json` pins the C# dependency graph.
OpenCFD package updates within v2606 can still change build details: save installed
package versions with the experiment when comparing machines.

Before claiming physical accuracy, investigate:

1. Mesh and timestep convergence of frequency, growth rate, waveform and spectrum.
2. Sensitivity to domain size, sponge strength/extent and the outer boundary.
3. Resolution/closure effects in the windway, shear layer, lip and wall boundary layers.
4. Probe placement, pressure reference, sampling and startup-window selection.
5. A physical instrument with measured geometry, drive conditions and calibrated
   recordings; compare more than a plausible-looking pressure video.

A clean `checkMesh` and a converged linear residual are necessary numerical checks,
not substitutes for these comparisons. The supplied default sponge is truncated by
the exterior domain, wall thermal losses are approximate, and the abrupt inlet is
not a human breath model. These assumptions are explicit parts of beta 1.

## Upstream references

- [OpenCFD v2606 release](https://www.openfoam.com/news/main-news/openfoam-v2606)
- [rhoPimpleFoam](https://doc.openfoam.com/2312/tools/processing/solvers/rtm/compressible/rhoPimpleFoam/)
- [waveTransmissive](https://doc.openfoam.com/2312/tools/processing/boundary-conditions/rtm/derived/outlet/waveTransmissive/)
- [OpenFOAM source/API](https://api.openfoam.com/)
- [snappyHexMesh](https://doc.openfoam.com/2312/tools/pre-processing/mesh/generation/snappyhexmesh/)

The public guide pages are version-labelled; the installed v2606 code/dictionaries
and saved run logs are authoritative for a deployed experiment.

## Reduced-domain experiment (zero padding except +Z)

The sample config sets `worldPaddingMillimeters` to 0 on X, Y and -Z and 100 on +Z.
This is an experiment setting, not a restriction: all six values stay configurable.

- **Bounds.** Zero padding is the solid's exact bounding coordinate (X ±30, Y 0..105, Z from -52.5 mm).
  Spans that are not a multiple of the cell size get ceil'd cell counts (cells never larger than
  nominal in Y; X/Z remainder bands are narrower, slivers under a quarter cell are merged or rejected).
  Padded sides still round outward, so +Z ends at 110 mm, i.e. 102.5 mm above the body top at 7.5 mm.
- **Geometry is untouched.** The STL, throat, chamber, voicing opening and the y=0 inlet patch are
  unchanged; the domain is not cropped to the cavity. Body surfaces lie exactly on the domain faces.
  A zero bounding-box padding does **not** remove every exterior pocket: air beside the throat
  (|x| 7.5..30, y 0..45, z -52.5..7.5) is still meshed and is open to the sides and to y=0.
- **Patches.** irSource is only the inlet rectangle. Fluid exposed on any outer face, including the
  lateral edges of the air above the ocarina and the pocket faces, is tmosphere. The solid is
  solidWalls; no cropped side is made a wall or symmetry plane. Cells outside the body that would
  touch coincident domain/STL faces lie inside the solid and are removed by snappyHexMesh, so those
  faces should not survive as boundaries; confirm in the final oundary file and checkMesh.
- **locationInMesh** is in the +Z air, centred over the body and away from all cell faces at every
  refinement level. A voxel flood fill at build confirms it reaches the throat, voicing, chamber,
  the whole inlet face, and every probe (also verified afterwards via Number of regions: 1).
- **Probes.** arMic (Y = 0.15 m) lies beyond the 0.105 m body bound and was **removed** from this
  experiment; no replacement position was chosen. Any probe out of bounds is a build error.
- **Damping sponge.** Coefficients are unchanged (radius1 87.5 mm, radius2 337.5 mm). With this mesh
  the farthest retained fluid is about 145 mm from the sphere centre, so only about 57 of the
  configured 250 mm ramp (23 %) exists, and the ramp strength reached is the early part of the ramp.
  The build prints this in preflight.txt. Do not read the configured thickness as the meshed thickness.
- **Counts.** Background cells: 52x41x54 = 115128 in the 100 mm-padded domain versus 13x21x33 = 9009
  here. Final snappy cell counts are reported at build (meshSummary.txt); compare them by building
  both configs. Solver settings, near-voicing refinement and deltaT must stay identical.
- **Equivalence is unproven.** Compare probe waveforms (throat, mouth, chamberCenter, chamberBack,
  nearMic) against the larger-domain case before treating the reduced domain as equivalent;
  nearby open boundaries and a truncated sponge can reflect waves and change pressure/frequency.

ocarina self-test checks exact zero-padding bounds, non-grid-aligned spans, block validity,
inlet geometry, locationInMesh clearance, probe bounds and flood-fill connectivity.

