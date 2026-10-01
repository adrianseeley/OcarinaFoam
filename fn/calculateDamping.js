// Place a spherical damping ramp around the solid's axis-aligned bounding box.
// The inner radius encloses the box's corners plus clearance; outer radius adds thickness.
// This calculation does NOT enlarge the rectangular mesh to fit the shell. Compare the
// radii with the generated world bounds before interpreting outgoing-wave absorption.

module.exports = function calculateDamping(solidBounds, config) {
    const scale = config.stlMillimetersToMeters;
    const centerMillimeters = [];
    const halfExtentsMillimeters = [];
    for (let axis = 0; axis < solidBounds.min.length; axis += 1) {
        const minimum = solidBounds.min[axis];
        centerMillimeters.push((minimum + solidBounds.max[axis]) / 2);
        halfExtentsMillimeters.push((solidBounds.max[axis] - minimum) / 2);
    }
    const originMeters = [];
    for (const value of centerMillimeters) {
        originMeters.push(value * scale);
    }
    let squaredHalfExtents = 0;
    for (const halfExtent of halfExtentsMillimeters) {
        squaredHalfExtents += halfExtent ** 2;
    }
    const bodyBoundingRadiusMeters = Math.sqrt(squaredHalfExtents) * scale;
    const clearanceMeters = config.acousticDampingClearanceMillimeters * scale;
    const thicknessMeters = config.acousticDampingThicknessMillimeters * scale;
    const innerRadiusMeters = bodyBoundingRadiusMeters + clearanceMeters;

    return {
        originMeters,
        innerRadiusMeters,
        outerRadiusMeters: innerRadiusMeters + thicknessMeters,
        thicknessMeters,
    };
};