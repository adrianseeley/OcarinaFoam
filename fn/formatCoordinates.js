// Format a coordinate tuple with OpenFOAM-compatible whitespace and decimal numbers.
// This helper performs no unit conversion; callers supply coordinates in their target units.

const formatNumber = require('./formatNumber');

module.exports = function formatCoordinates(coordinates) {
    const formatted = [];
    for (const coordinate of coordinates) {
        formatted.push(formatNumber(coordinate));
    }
    return formatted.join(' ');
};