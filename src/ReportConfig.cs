// Settings for ocarina report. Every key is required in config.json; none of this affects the physics fingerprint.
public class ReportConfig
{
    // Each factor N gives wav/NAME_xN.wav: the same samples played N times slower (pitch drops, possibly to infrasound). Factor 1 is the untouched original audio.
    public int[] slowdowns = null;
    // Every rendered frame of every field is presented at this rate, in order, none dropped (videos/FIELD/VIEW.mp4, no audio).
    public int framesPerSecond = 30;
    public int videoLongSidePixels = 1920;
    public int videoCrf = 12;
    // Slowed tracks are written at the original rate divided by N, but not below this.
    public int slowedAudioMinimumSampleRateHz = 8000;
}
