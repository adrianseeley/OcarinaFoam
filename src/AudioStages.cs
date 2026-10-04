public class AudioStages
{
    public double[] Resampled, HighPassed, Faded, Normalised;
    public int[] Pcm;
    public double Gain;
}

// Pure signal operations for the audio command.
public static class AudioDsp
{
    const double PassbandFraction = 0.95;

    static double I0(double x)
    {
        double sum = 1, term = 1;
        for (int k = 1; k < 60; k++) { term *= x * x / (4.0 * k * k); sum += term; if (term < 1e-17 * sum) break; }
        return sum;
    }

    // Windowed-sinc resampler evaluated directly at each output time; the low-pass doubles as the anti-alias filter.
    public static double[] Resample(double[] x, double fsIn, double fsOut, AudioConfig a)
    {
        int count = (int)Math.Floor((x.Length - 1) * fsOut / fsIn) + 1;
        double cutoff = 0.5 * Math.Min(fsIn, fsOut) * PassbandFraction / fsIn;
        double halfWidth = a.kernelZeroCrossings / (2 * cutoff);
        const int res = 8;
        var table = new double[(int)Math.Ceiling(halfWidth * res) + 3];
        double i0 = I0(a.kaiserBeta);
        for (int j = 0; j < table.Length; j++)
        {
            double d = (double)j / res, r = d / halfWidth;
            double window = r >= 1 ? 0 : I0(a.kaiserBeta * Math.Sqrt(1 - r * r)) / i0;
            double s = Math.PI * 2 * cutoff * d;
            table[j] = 2 * cutoff * (d == 0 ? 1 : Math.Sin(s) / s) * window;
        }
        var y = new double[count];
        Parallel.For(0, count, n =>
        {
            double pos = n * fsIn / fsOut;
            int lo = Math.Max(0, (int)Math.Ceiling(pos - halfWidth)), hi = Math.Min(x.Length - 1, (int)Math.Floor(pos + halfWidth));
            double sum = 0, weights = 0;
            for (int k = lo; k <= hi; k++)
            {
                double d = Math.Abs(k - pos) * res; int j = (int)d; double f = d - j;
                double w = table[j] * (1 - f) + table[j + 1] * f;
                sum += w * x[k]; weights += w;
            }
            y[n] = weights == 0 ? 0 : sum / weights;
        });
        return y;
    }

    // Zero-phase 4th-order Butterworth high-pass (two RBJ biquads run forward then backward).
    public static double[] HighPass(double[] x, double fs, double hz)
    {
        var y = (double[])x.Clone();
        if (hz <= 0) return y;
        foreach (double q in new[] { 0.5411961, 1.3065630 })
        {
            double w = 2 * Math.PI * hz / fs, c = Math.Cos(w), alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha;
            double b0 = (1 + c) / 2 / a0, b1 = -(1 + c) / a0, a1 = -2 * c / a0, a2 = (1 - alpha) / a0;
            for (int pass = 0; pass < 2; pass++)
            {
                double z1 = 0, z2 = 0;
                for (int i = 0; i < y.Length; i++)
                {
                    int k = pass == 0 ? i : y.Length - 1 - i;
                    double v = y[k], o = b0 * v + z1;
                    z1 = b1 * v - a1 * o + z2; z2 = b0 * v - a2 * o;
                    y[k] = o;
                }
            }
        }
        return y;
    }

    public static double[] Fade(double[] x, double fs, double milliseconds)
    {
        var y = (double[])x.Clone();
        int n = Math.Min((int)(fs * milliseconds / 1000), y.Length / 2);
        for (int i = 0; i < n; i++)
        {
            double g = 0.5 - 0.5 * Math.Cos(Math.PI * i / n);
            y[i] *= g; y[y.Length - 1 - i] *= g;
        }
        return y;
    }

    public static double Peak(double[] x) { double p = 0; foreach (double v in x) p = Math.Max(p, Math.Abs(v)); return p; }
    public static double Rms(double[] x) => x.Length == 0 ? 0 : Math.Sqrt(x.Sum(v => v * v) / x.Length);
    public static double Dbfs(double v) => v <= 0 ? double.NegativeInfinity : 20 * Math.Log10(v);

    public static int[] Quantise24(double[] x, out int clipped)
    {
        const int max = 8388607; clipped = 0;
        var q = new int[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            double v = Math.Round(x[i] * max);
            if (v > max) { v = max; clipped++; } else if (v < -max) { v = -max; clipped++; }
            q[i] = (int)v;
        }
        return q;
    }

    // Hann-windowed single-FFT magnitude in dBFS (a full-scale sine reads 0 dB). Returns bin spacing in Hz.
    public static double[] Spectrum(double[] x, double fs, out double binHz)
    {
        int n = 1; while (n < x.Length) n <<= 1;
        var re = new double[n]; var im = new double[n];
        double wsum = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double w = x.Length < 2 ? 1 : 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (x.Length - 1));
            re[i] = x[i] * w; wsum += w;
        }
        Fft(re, im);
        binHz = fs / n;
        var db = new double[n / 2 + 1];
        for (int k = 0; k < db.Length; k++) db[k] = Dbfs(2 * Math.Sqrt(re[k] * re[k] + im[k] * im[k]) / Math.Max(wsum, 1e-300));
        return db;
    }

    public static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int u = i + k, v = i + k + len / 2;
                    double tr = re[v] * cr - im[v] * ci, ti = re[v] * ci + im[v] * cr;
                    re[v] = re[u] - tr; im[v] = im[u] - ti; re[u] += tr; im[u] += ti;
                    double nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                }
            }
        }
    }

    public static void WriteWav24(string path, int[] samples, int rate)
    {
        using var w = new BinaryWriter(new FileStream(path, FileMode.Create, FileAccess.Write));
        int bytes = samples.Length * 3;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + bytes); w.Write("WAVEfmt "u8.ToArray());
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 3); w.Write((short)3); w.Write((short)24);
        w.Write("data"u8.ToArray()); w.Write(bytes);
        foreach (int s in samples) { w.Write((byte)s); w.Write((byte)(s >> 8)); w.Write((byte)(s >> 16)); }
    }
}
