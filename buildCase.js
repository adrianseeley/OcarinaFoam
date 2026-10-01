// Case generator: ASCII geometry + root JSON + foamTemplate -> disposable OpenFOAM case.
// This script prepares files only; it does not run CAD, mesh, deploy, solve or render.
// Edit these sources, never the generated <input-name>Case directory: main() removes
// that entire output tree before writing replacements, including any results inside it.
// Geometry inputs use millimetres; solver coordinates and probe positions use metres.
// The README is the configuration reference, including frozen implementation limits.

const fs = require('fs');
const path = require('path');
const CONFIG = require('./buildCase.config.json');
const hydrate = require('./fn/hydrate');
const formatNumber = require('./fn/formatNumber');
const parseAsciiStl = require('./fn/parseAsciiStl');
const boundsOf = require('./fn/boundsOf');
const toAsciiStl = require('./fn/toAsciiStl');
const buildBackgroundMesh = require('./fn/buildBackgroundMesh');
const calculateDamping = require('./fn/calculateDamping');
const calculateAcousticDiagnostics = require('./fn/calculateAcousticDiagnostics');
const buildDistanceRefinementRegions = require('./fn/buildDistanceRefinementRegions');
const writeCaseFile = require('./fn/writeCaseFile');
const printCaseSummary = require('./fn/printCaseSummary');
const formatCoordinates = require('./fn/formatCoordinates');
const buildFfmpegCommands = require('./fn/buildFfmpegCommands');

// Constant idealised dry-air inputs. Cv and molWeight enter the solver template;
// gamma enters the pressure boundary and the planning estimate. No humidity model.
const AIR = {
    molWeightKilogramsPerKmol: 28.9,
    specificHeatCvJoulesPerKilogramKelvin: 712,
    gamma: 1.4,
};

// Coordinates are metres and can be changed without changing the CAD model.
const PROBE_LOCATIONS = [
    { name: 'throat', point: [0, 0.025, 0] },
    { name: 'mouth', point: [0, 0.0525, 0.004] },
    { name: 'chamberCenter', point: [0, 0.075, -0.0225] },
    { name: 'chamberBack', point: [0, 0.095, -0.04] },
    { name: 'nearMic', point: [0, 0.075, 0.025] },
    { name: 'farMic', point: [0, 0.15, 0.05] },
];

// This explicit manifest controls generation. Adding a file to foamTemplate alone
// does not copy it; it must also be included in the generator write path.
const TEMPLATE_FILES = {
    blockMeshDict: 'system/blockMeshDict',
    snappyHexMeshDict: 'system/snappyHexMeshDict',
    surfaceFeatureExtractDict: 'system/surfaceFeatureExtractDict',
    controlDict: 'system/controlDict',
    decomposeParDict: 'system/decomposeParDict',
    fvSchemes: 'system/fvSchemes',
    fvSolution: 'system/fvSolution',
    thermophysicalProperties: 'constant/thermophysicalProperties',
    turbulenceProperties: 'constant/turbulenceProperties',
    fvOptions: 'system/fvOptions',
    p: '0/p',
    U: '0/U',
    T: '0/T',
    nut: '0/nut',
    alphat: '0/alphat',
    UMean: '0/UMean',
    rendererSource: 'render/FoamRenderer.cs',
    solverService: 'systemd/solver.service',
    rendererService: 'systemd/renderer.service',
    instructions: 'instructions.md',
};

const TEMPLATE_DIRECTORY = path.join(__dirname, 'foamTemplate');
const TEMPLATES = {};
for (const name in TEMPLATE_FILES) {
    if (Object.hasOwn(TEMPLATE_FILES, name)) {
        const relativePath = TEMPLATE_FILES[name];
        TEMPLATES[name] = fs.readFileSync(path.join(TEMPLATE_DIRECTORY, relativePath), 'utf8');
    }
}

function main() {
    const argumentsAfterNode = process.argv.slice(2);
    if (argumentsAfterNode.length !== 1) {
        throw new Error('Usage: node buildCase.js <directory>');
    }

    const inputDirectory = path.resolve(argumentsAfterNode[0]);
    const caseName = `${path.basename(inputDirectory)}Case`;
    const caseDirectory = path.join(inputDirectory, caseName);
    const requiredFiles = {
        model: path.join(inputDirectory, 'model.scad'),
        solid: path.join(inputDirectory, 'solidBody.stl'),
        spawn: path.join(inputDirectory, 'spawnPlane.stl'),
    };

    for (const label in requiredFiles) {
        if (!Object.hasOwn(requiredFiles, label)) {
            continue;
        }
        const filePath = requiredFiles[label];
        if (!fs.existsSync(filePath)) {
            throw new Error(`Missing required ${label} file: ${filePath}`);
        }
    }

    const solidFacets = parseAsciiStl(fs.readFileSync(requiredFiles.solid, 'utf8'), requiredFiles.solid);
    const spawnFacets = parseAsciiStl(fs.readFileSync(requiredFiles.spawn, 'utf8'), requiredFiles.spawn);
    // Keep whole facets only when every vertex is at Y >= 0; do not clip crossing triangles.
    // For the supplied thin box, only the two triangles on its Y=0 face survive.
    const retainedSpawnFacets = [];
    for (const facet of spawnFacets) {
        let retainFacet = true;
        for (const vertex of facet) {
            if (vertex[1] < 0) {
                retainFacet = false;
                break;
            }
        }
        if (retainFacet) {
            retainedSpawnFacets.push(facet);
        }
    }
    if (retainedSpawnFacets.length === 0) {
        throw new Error('Filtering spawnPlane.stl removed every facet.');
    }

    const solidBounds = boundsOf(solidFacets);
    const spawnBounds = boundsOf(retainedSpawnFacets);
    const mesh = buildBackgroundMesh(solidBounds, spawnBounds, CONFIG);
    const damping = calculateDamping(solidBounds, CONFIG);
    const diagnostics = calculateAcousticDiagnostics(CONFIG, AIR);
    const ambientPressurePascals = CONFIG.ambientPressureHectopascals * 100;
    const initialTemperatureKelvin = CONFIG.initialTemperatureCelsius + 273.15;
    // The renderer always leaves the newest `untouchedTimes` writes on disk to confirm
    // newer times exist; this is a write-lag heuristic, not proof that writes are complete.
    // Extend endTime so the requested ending can become eligible for rendering.
    const paddedEndTimeSeconds = CONFIG.endTimeSeconds
        + CONFIG.renderer.untouchedTimes * CONFIG.fieldWriteIntervalTimeSteps * CONFIG.deltaTSeconds;
    const ffmpegCommands = buildFfmpegCommands(CONFIG);
    const probeLinesParts = [];
    for (let probeIndex = 0; probeIndex < PROBE_LOCATIONS.length; probeIndex += 1) {
        const probe = PROBE_LOCATIONS[probeIndex];
        const coordinates = [];
        for (const coordinate of probe.point) {
            coordinates.push(formatNumber(coordinate));
        }
        probeLinesParts.push(`            (${coordinates.join(' ')}) // ${probeIndex}: ${probe.name}`);
    }
    const probeLines = probeLinesParts.join('\n');
    // Service ownership is captured from the BUILD host environment, not discovered on
    // the server. Build under the intended runtime username or provide USER explicitly.
    const caseOwner = process.env.SUDO_USER || process.env.USER || process.env.USERNAME || 'root';
    // The build machine and the OpenFOAM server are different hosts, so the runtime
    // path baked into generated files must come from config, not the local build path.
    const casePosixDirectory = path.posix.join(CONFIG.caseRuntimeDirectory, caseName);
    const values = {
        ACOUSTIC_DAMPING_ENABLED: CONFIG.acousticDampingEnabled ? 'yes' : 'no',
        AIR_CV: formatNumber(AIR.specificHeatCvJoulesPerKilogramKelvin),
        AIR_GAMMA: formatNumber(AIR.gamma),
        AIR_MOL_WEIGHT: formatNumber(AIR.molWeightKilogramsPerKmol),
        BLOCKS: mesh.blocks,
        DAMPING_INNER_RADIUS: formatNumber(damping.innerRadiusMeters),
        DAMPING_ORIGIN: formatCoordinates(damping.originMeters),
        DAMPING_OUTER_RADIUS: formatNumber(damping.outerRadiusMeters),
        DAMPING_STENCIL_WIDTH: CONFIG.acousticDampingStencilWidth,
        DAMPING_TARGET_FREQUENCY: formatNumber(CONFIG.acousticDampingTargetFrequencyHz),
        DISTANCE_REFINEMENT_REGIONS: buildDistanceRefinementRegions(CONFIG),
        DELTA_T: formatNumber(CONFIG.deltaTSeconds),
        END_TIME: formatNumber(paddedEndTimeSeconds),
        FAR_FIELD_RELAXATION_LENGTH: formatNumber(CONFIG.farFieldRelaxationLengthMeters),
        FIELD_WRITE_COMPRESSION: CONFIG.fieldWriteCompression ? 'on' : 'off',
        FIELD_WRITE_FORMAT: CONFIG.fieldWriteFormat,
        FIELD_WRITE_INTERVAL: CONFIG.fieldWriteIntervalTimeSteps,
        FEATURE_INCLUDED_ANGLE_DEGREES: formatNumber(CONFIG.featureIncludedAngleDegrees),
        FEATURE_REFINEMENT_LEVEL: CONFIG.featureRefinementLevel,
        N_CELLS_BETWEEN_LEVELS: CONFIG.nCellsBetweenLevels,
        INITIAL_PRESSURE: formatNumber(ambientPressurePascals),
        INITIAL_TEMPERATURE: formatNumber(initialTemperatureKelvin),
        INLET_VELOCITY: formatNumber(CONFIG.inletVelocityMetersPerSecond),
        LOCATION_IN_MESH: formatCoordinates(mesh.locationInMesh),
        PROCESSOR_COUNT: CONFIG.processorCount,
        PROBE_LOCATIONS: probeLines,
        PROBE_WRITE_INTERVAL: CONFIG.probeWriteIntervalTimeSteps,
        SOURCE_FACES: mesh.sourceFaces,
        ATMOSPHERE_FACES: mesh.atmosphereFaces,
        STL_SCALE: formatNumber(CONFIG.stlMillimetersToMeters),
        SURFACE_REFINEMENT_MAX_LEVEL: CONFIG.surfaceRefinementMaxLevel,
        SURFACE_REFINEMENT_MIN_LEVEL: CONFIG.surfaceRefinementMinLevel,
        VERTICES: mesh.vertices,
        CASE_NAME: caseName,
        CASE_DIRECTORY: casePosixDirectory,
        SYSTEMD_CASE_DIRECTORY: casePosixDirectory,
        RENDER_CASE_DIRECTORY: casePosixDirectory,
        RENDER_OUTPUT_DIRECTORY: path.posix.join(casePosixDirectory, 'renders'),
        RENDER_FONT_FILE: 'render/JuliaMono-Regular.ttf',
        // Frame numbers are derived from this, not from counting existing PNGs, so
        // frame names remain tied to physical time. This does not coordinate processes;
        // use only one renderer process per case because queues and deletion state are local.
        RENDER_WRITE_TIME_STEP: formatNumber(CONFIG.deltaTSeconds * CONFIG.fieldWriteIntervalTimeSteps),
        FFMPEG_COMMANDS: ffmpegCommands,
        RENDER_UNTOUCHED_TIMES: CONFIG.renderer.untouchedTimes,
        RENDER_THREAD_COUNT: CONFIG.renderer.renderThreads,
        RENDER_POLL_MILLISECONDS: CONFIG.renderer.pollMilliseconds,
        RENDER_PNG_COMPRESSION_LEVEL: CONFIG.renderer.pngCompressionLevel,
        RENDER_PLOT_WIDTH: CONFIG.renderer.plotWidth,
        RENDER_PLOT_HEIGHT: CONFIG.renderer.plotHeight,
        RENDER_MARGIN_PIXELS: CONFIG.renderer.marginPixels,
        RENDER_LABEL_FONT_PIXELS: CONFIG.renderer.labelFontPixels,
        RENDER_BACKGROUND_COLOR: CONFIG.renderer.backgroundColor,
        RENDER_LABEL_COLOR: CONFIG.renderer.labelColor,
        RENDER_POINT_SIZE_PIXELS: CONFIG.renderer.pointSizePixels,
        RENDER_AXIS_TILT_DEGREES: formatNumber(CONFIG.renderer.axisTiltDegrees),
        RENDER_OBJECT_MIN_X: formatNumber(solidBounds.min[0] * CONFIG.stlMillimetersToMeters),
        RENDER_OBJECT_MIN_Y: formatNumber(solidBounds.min[1] * CONFIG.stlMillimetersToMeters),
        RENDER_OBJECT_MIN_Z: formatNumber(solidBounds.min[2] * CONFIG.stlMillimetersToMeters),
        RENDER_OBJECT_MAX_X: formatNumber(solidBounds.max[0] * CONFIG.stlMillimetersToMeters),
        RENDER_OBJECT_MAX_Y: formatNumber(solidBounds.max[1] * CONFIG.stlMillimetersToMeters),
        RENDER_OBJECT_MAX_Z: formatNumber(solidBounds.max[2] * CONFIG.stlMillimetersToMeters),
        RENDER_CAMERA_PADDING_FRACTION: formatNumber(CONFIG.renderer.cameraPaddingFraction),
        RENDER_PROCESSOR_COUNT: CONFIG.processorCount,
        RENDER_PRESSURE: CONFIG.renderer.renderPressure,
        RENDER_VELOCITY_MAGNITUDE: CONFIG.renderer.renderVelocityMagnitude,
        RENDER_DENSITY: CONFIG.renderer.renderDensity,
        RENDER_TEMPERATURE: CONFIG.renderer.renderTemperature,
        CASE_OWNER: caseOwner,
        OPENFOAM_BASHRC: '/usr/lib/openfoam/openfoam2606/etc/bashrc',
    };

    // Destructive output boundary: this removes an existing case recursively. Never run
    // the generator against a live case or the only copy of research results.
    fs.rmSync(caseDirectory, { recursive: true, force: true });
    fs.mkdirSync(caseDirectory, { recursive: true });
    writeCaseFile(caseDirectory, 'constant/triSurface/solidBody.stl', fs.readFileSync(requiredFiles.solid, 'utf8'));
    writeCaseFile(caseDirectory, 'constant/triSurface/spawnPlane.stl', toAsciiStl('spawnPlane', retainedSpawnFacets));
    writeCaseFile(caseDirectory, 'geometry/model.scad', fs.readFileSync(requiredFiles.model, 'utf8'));
    writeCaseFile(caseDirectory, `${caseName}.foam`, '');
    writeCaseFile(caseDirectory, 'render/FoamRenderer.cs', hydrate(TEMPLATES.rendererSource, values));
    fs.copyFileSync(
        path.join(TEMPLATE_DIRECTORY, 'render/JuliaMono-Regular.ttf'),
        path.join(caseDirectory, 'render/JuliaMono-Regular.ttf'),
    );
    writeCaseFile(caseDirectory, 'renders/static/.gitkeep', '');
    writeCaseFile(caseDirectory, 'renders/.tmp/.gitkeep', '');
    writeCaseFile(caseDirectory, 'videos/.gitkeep', '');

    // fvOptions is deliberately emitted to both system and constant in this frozen
    // snapshot. They receive identical content; they are not two different damping models.
    const fileTemplates = {
        'system/blockMeshDict': TEMPLATES.blockMeshDict,
        'system/snappyHexMeshDict': TEMPLATES.snappyHexMeshDict,
        'system/surfaceFeatureExtractDict': TEMPLATES.surfaceFeatureExtractDict,
        'system/fvOptions': TEMPLATES.fvOptions,
        'system/controlDict': TEMPLATES.controlDict,
        'system/decomposeParDict': TEMPLATES.decomposeParDict,
        'system/fvSchemes': TEMPLATES.fvSchemes,
        'system/fvSolution': TEMPLATES.fvSolution,
        'constant/thermophysicalProperties': TEMPLATES.thermophysicalProperties,
        'constant/turbulenceProperties': TEMPLATES.turbulenceProperties,
        'constant/fvOptions': TEMPLATES.fvOptions,
        '0/p': TEMPLATES.p,
        '0/U': TEMPLATES.U,
        '0/T': TEMPLATES.T,
        '0/nut': TEMPLATES.nut,
        '0/alphat': TEMPLATES.alphat,
        '0/UMean': TEMPLATES.UMean,
        [`systemd/${caseName}-solver.service`]: TEMPLATES.solverService,
        [`systemd/${caseName}-renderer.service`]: TEMPLATES.rendererService,
        'instructions.md': TEMPLATES.instructions,
    };

    for (const relativePath in fileTemplates) {
        if (!Object.hasOwn(fileTemplates, relativePath)) {
            continue;
        }
        writeCaseFile(caseDirectory, relativePath, hydrate(fileTemplates[relativePath], values));
    }

    console.log(`Built ${caseDirectory}`);
    console.log(`Retained ${retainedSpawnFacets.length} spawn-plane facets.`);
    printCaseSummary(solidBounds, mesh, diagnostics, damping, CONFIG);
}

main();