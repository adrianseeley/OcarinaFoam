// Read triangular facets from ordinary ASCII STL text without an external CAD library.
// Return only vertices: source normals are ignored. Binary STL is unsupported.
// This deliberately small parser checks facet shape and NaN coordinates, not manifold
// closure, winding, self-intersection or every possible invalid number. Use surfaceCheck.

module.exports = function parseAsciiStl(text, sourceName) {
    const facets = [];
    const facetPattern = /facet\s+normal\s+[^\r\n]+\s+outer\s+loop\s+([\s\S]*?)\s+endloop\s+endfacet/gi;
    let facetMatch;

    while ((facetMatch = facetPattern.exec(text)) !== null) {
        const vertexMatches = facetMatch[1].matchAll(/vertex\s+([^\s]+)\s+([^\s]+)\s+([^\s]+)/gi);
        const vertices = [];
        let hasInvalidVertex = false;
        for (const vertexMatch of vertexMatches) {
            const vertex = [];
            for (let coordinateIndex = 1; coordinateIndex <= 3; coordinateIndex += 1) {
                const coordinate = Number(vertexMatch[coordinateIndex]);
                if (Number.isNaN(coordinate)) {
                    hasInvalidVertex = true;
                }
                vertex.push(coordinate);
            }
            vertices.push(vertex);
        }
        if (vertices.length !== 3 || hasInvalidVertex) {
            throw new Error(`${sourceName} contains a malformed STL facet.`);
        }
        facets.push(vertices);
    }

    if (facets.length === 0) {
        throw new Error(`${sourceName} has no ASCII STL facets.`);
    }

    return facets;
};