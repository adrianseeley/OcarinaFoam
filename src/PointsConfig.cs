// Which fields every points frame carries, and how frames are compressed. This file is the only record of the frame layout.
// Per point, in this fixed order, only the enabled ones: velocity (vX vY vZ), temperature, pressure, density. All single-precision floats.
public class PointsConfig
{
    public int pollMilliseconds = 2000;
    // "fastest", "optimal" or "smallest": gzip effort for position.bin.gz and every frame.
    public string compression = "optimal";
    public bool velocity = true;
    public bool temperature = true;
    public bool pressure = true;
    public bool density = true;
}
