public static class ReportMath
{
    static long Gcd(long a, long b) { while (b != 0) (a, b) = (b, a % b); return a; }

    // Playing the audio N times slower means relabelling fs as fs/N. ffmpeg needs an integer rate, so first upsample by
    // K = N/gcd(fs, N) to fs*K, which makes fs*K/N = fs/gcd(fs, N) an integer.
    public static long Intermediate(int fs, int factor) => (long)fs * (factor / Gcd(fs, factor));
    public static long Relabelled(int fs, int factor) => fs / Gcd(fs, factor);

    // Highest frequency present after slowing, shifting and rescaling, then the lowest rate that holds it with 10% headroom.
    public static int SlowedRate(AudioConfig a, ReportConfig r, int factor)
    {
        double slowed = a.sampleRateHz / 2.0 / factor + r.slowedAudioShiftHz;
        double highest = slowed * Math.Max(1, r.slowedAudioRescale);
        return (int)Math.Max(r.slowedAudioMinimumSampleRateHz, Math.Ceiling(2 * highest * 1.1));
    }

    // 16-bit mono bytes of one slowed probe track.
    public static double SlowedBytes(double seconds, int rate, int factor) => seconds * factor * rate * 2;
}
