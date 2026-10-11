public class SurfaceRefinement
{
    public int minLevel = 3;
    public int maxLevel = 5;
}

public class FeatureRefinement
{
    public int level = 5;
    public int includedAngleDegrees = 150;
}

public class RefinementConfig
{
    public int backgroundCellSizeMillimeters = 5;
    public int cellsBetweenLevels = 3;
    public SurfaceRefinement surface = null;
    public FeatureRefinement feature = null;
    public double[] bodyDistanceLevelsMillimeters = null;
}

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
    public RefinementConfig refinement = null;
    public PhysicsConfig physics = null;
    public double deltaTSeconds = 1e-07;
    public double endTimeSeconds = 3.0;
    public int fieldWriteIntervalTimeSteps = 10;
    public int probeWriteIntervalTimeSteps = 1;
    public string watcher = "systemd";
    public PointsConfig points = null;
    public Dictionary<string, double[]> probes = null;
    public Probe[] ProbeList() => probes.Select(p => new Probe { name = p.Key, point = p.Value }).ToArray();
}
