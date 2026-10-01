// Planning estimates only: ideal-gas sound speed, nominal finest surface cell width,
// acoustic Courant number and sponge thickness measured in target wavelengths.
// This does not inspect the actual mesh, enforce stability or modify deltaT.
// The estimate ignores smaller source-aligned bands, snap distortion and feature or
// region levels above surfaceRefinementMaxLevel. AIR.gamma is a diagnostic/boundary
// constant; the solver's eConst/perfectGas thermodynamics determine its own properties.

module.exports = function calculateAcousticDiagnostics(config, air) {
    const temperatureKelvin = config.initialTemperatureCelsius + 273.15;
    const specificGasConstant = 8314.46261815324 / air.molWeightKilogramsPerKmol;
    const speedOfSoundMetersPerSecond = Math.sqrt(air.gamma * specificGasConstant * temperatureKelvin);
    const minimumCellSizeMeters = (
        config.backgroundCellSizeMillimeters * config.stlMillimetersToMeters
    ) / (2 ** config.surfaceRefinementMaxLevel);
    const acousticCourant = (
        (speedOfSoundMetersPerSecond + config.inletVelocityMetersPerSecond)
        * config.deltaTSeconds
    ) / minimumCellSizeMeters;
    const dampingWavelengthMeters = speedOfSoundMetersPerSecond / config.acousticDampingTargetFrequencyHz;

    return {
        minimumCellSizeMeters,
        acousticCourant,
        dampingWavelengthMeters,
        dampingThicknessWavelengths: (
            config.acousticDampingThicknessMillimeters * config.stlMillimetersToMeters
        ) / dampingWavelengthMeters,
    };
};