public static class ReportMath
{
    static long Gcd(long a, long b) { while (b != 0) (a, b) = (b, a % b); return a; }

    // Playing the audio N times slower means relabelling fs as fs/N. ffmpeg needs an integer rate, so first upsample by
    // K = N/gcd(fs, N) to fs*K, which makes fs*K/N = fs/gcd(fs, N) an integer.
    public static long Intermediate(int fs, int factor) => (long)fs * (factor / Gcd(fs, factor));
    public static long Relabelled(int fs, int factor) => fs / Gcd(fs, factor);

    // The slowed content tops out at fs/2/N, so fs/N holds it; very slow tracks are held at the minimum rate so players accept them.
    public static int SlowedRate(AudioConfig a, ReportConfig r, int factor) => Math.Max(r.slowedAudioMinimumSampleRateHz, (int)Math.Ceiling((double)a.sampleRateHz / factor));

    // 16-bit mono bytes of one slowed probe track.
    public static double SlowedBytes(double seconds, int rate, int factor) => seconds * factor * rate * 2;
}
