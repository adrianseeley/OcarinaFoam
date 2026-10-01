// Construct blockMesh text with a rectangular inlet on the minimum-Y domain face.
// The inlet is the retained STL's X/Z bounding rectangle, not its triangulated outline.
// Domain bounds round outward to the configured coarse grid. X/Z bands preserve the
// inlet edges exactly, so remainder bands can have cells narrower than the nominal
// background spacing. This is why the acoustic cell-size estimate is only an estimate.
// Coordinates are converted to metres once; blockMesh subsequently uses scale 1.

const formatNumber = require('./formatNumber');

function wholeCellCount(start, end, cellSize, label) {
    const count = Math.round((end - start) / cellSize);
    if (count <= 0 || Math.abs((count * cellSize) - (end - start)) > 1e-9) {
        throw new Error(`${label} is not an exact multiple of backgroundCellSizeMillimeters.`);
    }
    return count;
}

function quantizeDown(value, cellSize) {
    return Math.floor((value / cellSize) + 1e-10) * cellSize;
}

function quantizeUp(value, cellSize) {
    return Math.ceil((value / cellSize) - 1e-10) * cellSize;
}

function buildUniformAxisBands(minimum, maximum, cellSize, label) {
    return [{
        start: minimum,
        end: maximum,
        cells: wholeCellCount(minimum, maximum, cellSize, label),
        isSource: false,
    }];
}

function buildSourceAlignedAxisBands(minimum, maximum, sourceMinimum, sourceMaximum, cellSize, label) {
    const epsilon = 1e-9;
    if (sourceMinimum < minimum - epsilon || sourceMaximum > maximum + epsilon || sourceMaximum <= sourceMinimum) {
        throw new Error(`${label} bounds must lie within the generated world.`);
    }

    const bands = [];
    const addSideBands = (sideMinimum, sideMaximum, alignAt, fromMinimum) => {
        const span = sideMaximum - sideMinimum;
        if (span <= epsilon) {
            return;
        }

        const fullCells = Math.floor((span / cellSize) + epsilon);
        const aligned = fromMinimum
            ? sideMinimum + (fullCells * cellSize)
            : sideMaximum - (fullCells * cellSize);
        const remainder = Math.abs(span - (fullCells * cellSize));

        if (fromMinimum) {
            if (fullCells > 0) {
                bands.push({ start: sideMinimum, end: aligned, cells: fullCells, isSource: false });
            }
            if (remainder > epsilon) {
                bands.push({ start: aligned, end: alignAt, cells: 1, isSource: false });
            }
        } else {
            if (remainder > epsilon) {
                bands.push({ start: alignAt, end: aligned, cells: 1, isSource: false });
            }
            if (fullCells > 0) {
                bands.push({ start: aligned, end: sideMaximum, cells: fullCells, isSource: false });
            }
        }
    };

    addSideBands(minimum, sourceMinimum, sourceMinimum, true);
    bands.push({
        start: sourceMinimum,
        end: sourceMaximum,
        cells: Math.max(1, Math.ceil(((sourceMaximum - sourceMinimum) / cellSize) - epsilon)),
        isSource: true,
    });
    addSideBands(sourceMaximum, maximum, sourceMaximum, false);
    return bands;
}

function axisStations(bands) {
    const stations = [bands[0].start];
    for (const band of bands) {
        stations.push(band.end);
    }
    return stations;
}

module.exports = function buildBackgroundMesh(solidBounds, sourceBounds, config) {
    const scale = config.stlMillimetersToMeters;
    const backgroundCellSizeMeters = config.backgroundCellSizeMillimeters * scale;
    const padding = config.worldPaddingMillimeters;
    const world = {
        min: [
            quantizeDown(solidBounds.min[0] - padding.xMin, config.backgroundCellSizeMillimeters),
            quantizeDown(solidBounds.min[1] - padding.yMin, config.backgroundCellSizeMillimeters),
            quantizeDown(solidBounds.min[2] - padding.zMin, config.backgroundCellSizeMillimeters),
        ],
        max: [
            quantizeUp(solidBounds.max[0] + padding.xMax, config.backgroundCellSizeMillimeters),
            quantizeUp(solidBounds.max[1] + padding.yMax, config.backgroundCellSizeMillimeters),
            quantizeUp(solidBounds.max[2] + padding.zMax, config.backgroundCellSizeMillimeters),
        ],
    };
    const sourceY = sourceBounds.min[1];

    if (Math.abs(sourceBounds.max[1] - sourceY) > 1e-9 || Math.abs(sourceY - world.min[1]) > 1e-9) {
        throw new Error('The filtered spawn plane must lie on the world minimum-y boundary.');
    }

    const xBands = buildSourceAlignedAxisBands(
        world.min[0], world.max[0], sourceBounds.min[0], sourceBounds.max[0],
        config.backgroundCellSizeMillimeters, 'airSource x',
    );
    const yBands = buildUniformAxisBands(
        world.min[1], world.max[1], config.backgroundCellSizeMillimeters, 'world y extent',
    );
    const zBands = buildSourceAlignedAxisBands(
        world.min[2], world.max[2], sourceBounds.min[2], sourceBounds.max[2],
        config.backgroundCellSizeMillimeters, 'airSource z',
    );
    const x = axisStations(xBands);
    const y = axisStations(yBands);
    const z = axisStations(zBands);
    for (let index = 0; index < x.length; index += 1) x[index] *= scale;
    for (let index = 0; index < y.length; index += 1) y[index] *= scale;
    for (let index = 0; index < z.length; index += 1) z[index] *= scale;

    const vertexIndex = (xIndex, yIndex, zIndex) => xIndex + (x.length * (yIndex + (y.length * zIndex)));
    const vertices = [];
    for (let zIndex = 0; zIndex < z.length; zIndex += 1) {
        for (let yIndex = 0; yIndex < y.length; yIndex += 1) {
            for (let xIndex = 0; xIndex < x.length; xIndex += 1) {
                const index = vertexIndex(xIndex, yIndex, zIndex);
                vertices.push(`    (${formatNumber(x[xIndex])} ${formatNumber(y[yIndex])} ${formatNumber(z[zIndex])}) // ${index}`);
            }
        }
    }

    const blocks = [];
    const atmosphereFaces = [];
    const sourceFaces = [];
    for (let zIndex = 0; zIndex < zBands.length; zIndex += 1) {
        for (let xIndex = 0; xIndex < xBands.length; xIndex += 1) {
            const xCells = xBands[xIndex].cells;
            const yCells = yBands[0].cells;
            const zCells = zBands[zIndex].cells;
            const v0 = vertexIndex(xIndex, 0, zIndex);
            const v1 = vertexIndex(xIndex + 1, 0, zIndex);
            const v2 = vertexIndex(xIndex + 1, 1, zIndex);
            const v3 = vertexIndex(xIndex, 1, zIndex);
            const v4 = vertexIndex(xIndex, 0, zIndex + 1);
            const v5 = vertexIndex(xIndex + 1, 0, zIndex + 1);
            const v6 = vertexIndex(xIndex + 1, 1, zIndex + 1);
            const v7 = vertexIndex(xIndex, 1, zIndex + 1);
            blocks.push(`    hex (${v0} ${v1} ${v2} ${v3} ${v4} ${v5} ${v6} ${v7}) (${xCells} ${yCells} ${zCells}) simpleGrading (1 1 1)`);

            const frontFace = `            (${v0} ${v1} ${v5} ${v4})`;
            if (xBands[xIndex].isSource && zBands[zIndex].isSource) sourceFaces.push(frontFace);
            else atmosphereFaces.push(frontFace);
            atmosphereFaces.push(`            (${v3} ${v7} ${v6} ${v2})`);
            if (xIndex === 0) atmosphereFaces.push(`            (${v0} ${v4} ${v7} ${v3})`);
            if (xIndex === xBands.length - 1) atmosphereFaces.push(`            (${v1} ${v2} ${v6} ${v5})`);
            if (zIndex === 0) atmosphereFaces.push(`            (${v0} ${v3} ${v2} ${v1})`);
            if (zIndex === zBands.length - 1) atmosphereFaces.push(`            (${v4} ${v5} ${v6} ${v7})`);
        }
    }

    return {
        vertices: vertices.join('\n'),
        blocks: blocks.join('\n'),
        sourceFaces: sourceFaces.join('\n'),
        atmosphereFaces: atmosphereFaces.join('\n'),
        locationInMesh: [
            x[x.length - 1] - ((x[x.length - 1] - x[x.length - 2]) / (2 * xBands[xBands.length - 1].cells)),
            y[y.length - 1] - ((y[y.length - 1] - y[y.length - 2]) / (2 * yBands[yBands.length - 1].cells)),
            z[z.length - 1] - ((z[z.length - 1] - z[z.length - 2]) / (2 * zBands[zBands.length - 1].cells)),
        ],
        worldBoundsMillimeters: world,
        backgroundCellSizeMeters,
    };
};