// Serialise retained inlet triangles as ASCII STL in their original coordinate units.
// Zero normals are placeholders; vertices define geometry. This filtered surface records
// the inlet selection, while blockMesh uses its bounding rectangle to create airSource.

const formatNumber = require('./formatNumber');

module.exports = function toAsciiStl(name, facets) {
    const lines = [`solid ${name}`];
    for (const vertices of facets) {
        lines.push('  facet normal 0 0 0', '    outer loop');
        for (const vertex of vertices) {
            const coordinates = [];
            for (const coordinate of vertex) {
                coordinates.push(formatNumber(coordinate));
            }
            lines.push(`      vertex ${coordinates.join(' ')}`);
        }
        lines.push('    endloop', '  endfacet');
    }
    lines.push(`endsolid ${name}`, '');
    return lines.join('\n');
};