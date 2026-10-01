// Emit fixed decimals with trailing zeroes removed and normalise negative zero.
// Ten decimal places are sufficient for the current 1e-7 s step, but very small future
// values can round to zero. This formatting precision differs from field writePrecision.

module.exports = function formatNumber(value) {
    const decimal = value.toFixed(10).replace(/\.?0+$/, '');
    return decimal === '-0' ? '0' : decimal;
};