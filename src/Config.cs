public class Config
{
    public int processorCount = 6;
    public Padding worldPaddingMillimeters = null;
    public int backgroundCellSizeMillimeters = 5;
    public int surfaceRefinementMinLevel = 3;
    public int surfaceRefinementMaxLevel = 5;
    public int featureRefinementLevel = 5;
    public int nCellsBetweenLevels = 3;
    public Refinement[] bodyDistanceRefinement = null;
    public int featureIncludedAngleDegrees = 150;
    public int inletVelocityMetersPerSecond = 12;
    public double ambientPressureHectopascals = 1013.25;
    public double initialTemperatureCelsius = 26.85;
    public double deltaTSeconds = 1e-07;
    public double endTimeSeconds = 3.0;
    public int fieldWriteIntervalTimeSteps = 10;
    public int probeWriteIntervalTimeSteps = 1;
    public double farFieldRelaxationLengthMeters = 0.1;
    public bool acousticDampingEnabled = true;
    public int acousticDampingTargetFrequencyHz = 3000;
    public int acousticDampingThicknessMillimeters = 250;
    public int acousticDampingStrengthMultiplier = 20;
    public int acousticDampingClearanceMillimeters = 20;
    public RenderConfig renderer = null;
    public AudioConfig audio = new AudioConfig();
    public Probe[] probes = null;
}
