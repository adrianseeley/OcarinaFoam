// Report intended geometry, refinement and acoustic planning estimates after generation.
// Warnings are advisory and do not stop generation. A quiet summary is not a mesh-quality,
// boundary-reflection or physical-validation result. All detailed assumptions are in README.

const formatNumber = require('./formatNumber');

function formatBounds(bounds, unit) {
    const minimum = [];
    const maximum = [];
    for (let axis = 0; axis < bounds.min.length; axis += 1) {
        minimum.push(formatNumber(bounds.min[axis]));
        maximum.push(formatNumber(bounds.max[axis]));
    }
    return `min (${minimum.join(' ')}) max (${maximum.join(' ')}) ${unit}`;
}

module.exports = function printCaseSummary(solidBounds, mesh, diagnostics, damping, config) {
    const distanceRefinementParts = [];
    for (const entry of config.bodyDistanceRefinement) {
        distanceRefinementParts.push(`${entry.distanceMillimeters} mm -> level ${entry.level} (${formatNumber(config.backgroundCellSizeMillimeters / (2 ** entry.level))} mm)`);
    }
    const distanceRefinement = distanceRefinementParts.join(', ');
    const surfaceMinimumMillimeters = config.backgroundCellSizeMillimeters / (2 ** config.surfaceRefinementMinLevel);
    const surfaceMaximumMillimeters = config.backgroundCellSizeMillimeters / (2 ** config.surfaceRefinementMaxLevel);

    console.log([
        'Case summary:',
        `  solid-body bounds: ${formatBounds(solidBounds, 'mm')}`,
        `  world bounds: ${formatBounds(mesh.worldBoundsMillimeters, 'mm')}`,
        `  world padding (mm): x ${config.worldPaddingMillimeters.xMin}/${config.worldPaddingMillimeters.xMax}, y ${config.worldPaddingMillimeters.yMin}/${config.worldPaddingMillimeters.yMax}, z ${config.worldPaddingMillimeters.zMin}/${config.worldPaddingMillimeters.zMax}`,
        `  background cell size: ${formatNumber(config.backgroundCellSizeMillimeters)} mm`,
        `  distance refinement: ${distanceRefinement}`,
        `  surface cell size: ${formatNumber(surfaceMinimumMillimeters)} mm to ${formatNumber(surfaceMaximumMillimeters)} mm`,
        `  maximum refinement level: ${config.surfaceRefinementMaxLevel}`,
        `  estimated minimum cell size: ${formatNumber(diagnostics.minimumCellSizeMeters * 1000)} mm`,
        `  fixed timestep: ${formatNumber(config.deltaTSeconds)} s`,
        `  estimated acoustic Courant number: ${formatNumber(diagnostics.acousticCourant)} (> 0.5: reduce deltaTSeconds)`,
        '  LES model: WALE',
        '  temporal scheme: CrankNicolson 0.9',
        `  acoustic damping: ${config.acousticDampingTargetFrequencyHz} Hz, ${formatNumber(damping.thicknessMeters * 1000)} mm (${formatNumber(diagnostics.dampingThicknessWavelengths)} wavelengths)`,
        `  damping radii: ${formatNumber(damping.innerRadiusMeters)} m to ${formatNumber(damping.outerRadiusMeters)} m`,
    ].join('\n'));

    if (diagnostics.acousticCourant > 0.5) {
        console.warn(`WARNING: estimated acoustic Courant number ${formatNumber(diagnostics.acousticCourant)} exceeds 0.5; reduce deltaTSeconds manually if this is not intentional.`);
    }
    if (diagnostics.dampingThicknessWavelengths < 2) {
        console.warn(`WARNING: acoustic damping thickness is only ${formatNumber(diagnostics.dampingThicknessWavelengths)} wavelengths at ${config.acousticDampingTargetFrequencyHz} Hz; use roughly two wavelengths or more for a more gradual sponge.`);
    }
};