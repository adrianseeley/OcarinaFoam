public class DampingConfig
{
    public bool enabled = true;
    public int targetFrequencyHz = 3000;
    public int thicknessMillimeters = 250;
    public int strengthMultiplier = 20;
    public int clearanceMillimeters = 20;
}

public class PhysicsConfig
{
    public int inletVelocityMetersPerSecond = 12;
    public double ambientPressureHectopascals = 1013.25;
    public double initialTemperatureCelsius = 26.85;
    public double farFieldRelaxationLengthMeters = 0.1;
    public DampingConfig damping = null;
}

public class Config
{
    public int processorCount = 6;
    public Padding worldPaddingMillimeters = null;
    public double cellSizeMillimeters = 0.5;
    public PhysicsConfig physics = null;
    public string deltaT = "000s000ms000us100ns";
    public string lastT = "003s000ms000us000ns";
    public string watcher = "systemd";
    public PointsConfig points = null;
    public Dictionary<string, double[]> probes = null;
    public long DeltaNs() => Points.ParseTime(deltaT, "deltaT");
    public long LastNs() => Points.ParseTime(lastT, "lastT");
    public int KeepEvery() => points.keepEvery;
    public long Steps() => LastNs() / DeltaNs();
    public int Frames() => (int)(Steps() / KeepEvery());
    // Three extra write intervals so the watcher, which holds back the newest three, still converts lastT.
    public long EndNs() => LastNs() + 3L * KeepEvery() * DeltaNs();
    public Probe[] ProbeList() => probes.Select(p => new Probe { name = p.Key, point = p.Value }).ToArray();
}
