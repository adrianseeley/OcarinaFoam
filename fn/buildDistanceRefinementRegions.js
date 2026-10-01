// Translate distance/level pairs into snappyHexMesh distance refinement around solidWalls.
// Sort a copied array from shortest to longest distance without reordering the config.
// Levels halve nominal cell width at each increment; distances become metres.

const formatNumber = require('./formatNumber');

module.exports = function buildDistanceRefinementRegions(config) {
    const refinements = [];
    for (const refinement of config.bodyDistanceRefinement) {
        refinements.push(refinement);
    }
    for (let index = 0; index < refinements.length - 1; index += 1) {
        for (let nextIndex = index + 1; nextIndex < refinements.length; nextIndex += 1) {
            if (refinements[index].distanceMillimeters > refinements[nextIndex].distanceMillimeters) {
                const temporary = refinements[index];
                refinements[index] = refinements[nextIndex];
                refinements[nextIndex] = temporary;
            }
        }
    }

    const lines = [];
    for (const entry of refinements) {
        lines.push(`                (${formatNumber(entry.distanceMillimeters * config.stlMillimetersToMeters)} ${entry.level})`);
    }
    return `        solidWalls\n        {\n            mode    distance;\n            levels\n            (\n${lines.join('\n')}\n            );\n        }`;
};