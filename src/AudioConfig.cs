// Every key (including plots) is required in config.json; these initial values only back programmatic use and the self-test.
public class AudioConfig
{
    public int sampleRateHz = 96000;
    public double highPassHz = 2;
    public double fadeMilliseconds = 0;
    public double peakTargetDbfs = -1;
    // false: every probe is normalised to the target alone; true: one gain keeps the probes' relative levels.
    public bool sharedGain = false;
    public int kernelZeroCrossings = 48;
    public double kaiserBeta = 10;
    public AudioPlotConfig plots = new AudioPlotConfig();
}
