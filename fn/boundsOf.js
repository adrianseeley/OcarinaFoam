// Find axis-aligned bounds over all triangle vertices, in the input STL's units.
// No unit conversion or geometry repair occurs here. Bounds drive domain construction,
// inlet extent, camera framing and damping placement; outlying facets affect all four.

module.exports = function boundsOf(facets) {
    const min = [Infinity, Infinity, Infinity];
    const max = [-Infinity, -Infinity, -Infinity];
    for (const facet of facets) {
        for (const vertex of facet) {
            for (let axis = 0; axis < 3; axis += 1) {
                min[axis] = Math.min(min[axis], vertex[axis]);
                max[axis] = Math.max(max[axis], vertex[axis]);
            }
        }
    }
    return { min, max };
};