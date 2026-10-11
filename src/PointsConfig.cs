// Which fields every points frame carries, and how frames are compressed. This file is the only record of the frame layout.
// Pressure is stored as gauge pressure: p minus physics.ambientPressureHectopascals (in Pa).
// Per point, in this fixed order, only the enabled ones: velocity (vX vY vZ), temperature, pressure, density. All single-precision floats.
public class PointsConfig
{
    // Keep one solver write in every keepEvery steps; also the solver write interval in steps.
    public int keepEvery = 1;
    public int pollMilliseconds = 2000;
    // "fastest", "optimal" or "smallest": Brotli effort (quality 4, 9, 11) for every frame; position.bin.br follows the same level.
    // Brotli at "smallest" is slow; the watcher must still keep up with the solver.
    public string compression = "optimal";
    // Mantissa bits kept per float32 value, 1..23. 23 is lossless; each bit dropped halves the precision
    // (relative error about 2^-(bits+1)) and makes the frames compress much harder. Positions are always lossless.
    public int mantissaBits = 23;
    public bool velocity = true;
    public bool temperature = true;
    public bool pressure = true;
    public bool density = true;
}
