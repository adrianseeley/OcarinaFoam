// Settings for ocarina report. Every key is required in config.json; none of this affects the physics fingerprint.
public class ReportConfig
{
    // Each factor N gives wav/NAME_xN.wav and videos/xN.mkv. Factor 1 is the untouched original audio.
    public int[] slowdowns = null;
    // Presented frames per second in the videos (nearest rendered frame is shown at each tick).
    public int framesPerSecond = 30;
    public int videoLongSidePixels = 1024;
    public int videoCrf = 16;
    // Slowed tracks (factor > 1): every frequency f becomes rescale * (f / factor + shiftHz).
    public double slowedAudioShiftHz = 200;
    public double slowedAudioRescale = 2;
    // Slowed tracks are written at the lowest rate that holds their content, but not below this.
    public int slowedAudioMinimumSampleRateHz = 8000;
}
