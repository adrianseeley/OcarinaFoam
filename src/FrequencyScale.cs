using System.Text;

// Multiplies every frequency by a ratio while keeping duration and timing (STFT phase vocoder, bin remapping).
public static class FrequencyScale
{
    public static double[] Scale(double[] x, double ratio)
    {
        if (ratio == 1 || x.Length < 8) return (double[])x.Clone();
        int m = 64; while (m * 2 <= Math.Min(4096, x.Length / 2)) m *= 2;
        int hop = m / 8, half = m / 2, length = x.Length;
        var window = new double[m];
        for (int i = 0; i < m; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / m);
        int padded = length + 2 * m;
        var input = new double[padded]; Array.Copy(x, 0, input, m, length);
        var output = new double[padded]; var norm = new double[padded];
        var re = new double[m]; var im = new double[m];
        var lastPhase = new double[half + 1]; var sumPhase = new double[half + 1];
        var magnitude = new double[half + 1]; var binFrequency = new double[half + 1];
        double step = 2 * Math.PI * hop / m;
        for (int start = 0; start + m <= padded; start += hop)
        {
            for (int i = 0; i < m; i++) { re[i] = input[start + i] * window[i]; im[i] = 0; }
            AudioDsp.Fft(re, im);
            Array.Clear(magnitude); for (int k = 0; k <= half; k++) binFrequency[k] = k;
            for (int k = 0; k <= half; k++)
            {
                double phase = Math.Atan2(im[k], re[k]), delta = phase - lastPhase[k] - k * step;
                lastPhase[k] = phase;
                delta -= 2 * Math.PI * Math.Round(delta / (2 * Math.PI));
                double trueBin = k + delta / step;
                int target = (int)Math.Round(k * ratio);
                if (target > half) continue;
                magnitude[target] += Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                binFrequency[target] = trueBin * ratio;
            }
            for (int k = 0; k <= half; k++)
            {
                sumPhase[k] += binFrequency[k] * step;
                re[k] = magnitude[k] * Math.Cos(sumPhase[k]); im[k] = magnitude[k] * Math.Sin(sumPhase[k]);
            }
            im[0] = 0; im[half] = 0;
            for (int k = 1; k < half; k++) { re[m - k] = re[k]; im[m - k] = -im[k]; }
            // Inverse FFT by swapping real and imaginary parts around a forward FFT.
            AudioDsp.Fft(im, re);
            for (int i = 0; i < m; i++) { output[start + i] += re[i] / m * window[i]; norm[start + i] += window[i] * window[i]; }
        }
        var result = new double[length];
        for (int i = 0; i < length; i++) { double n = norm[m + i]; result[i] = n > 1e-9 ? output[m + i] / n : 0; }
        return result;
    }

    // Reads 16/24-bit PCM or 32-bit float mono WAV/RF64 (data chunk runs to the end of the file if its size is unset).
    public static double[] ReadWav(string path, out int rate)
    {
        using var r = new BinaryReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
        string id = Encoding.ASCII.GetString(r.ReadBytes(4));
        if (id is not ("RIFF" or "RF64")) throw new Exception("Not a WAV file: " + path);
        r.ReadUInt32(); if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "WAVE") throw new Exception("Not a WAV file: " + path);
        int format = 0, bits = 0, channels = 1; rate = 0;
        while (r.BaseStream.Position + 8 <= r.BaseStream.Length)
        {
            string chunk = Encoding.ASCII.GetString(r.ReadBytes(4)); long size = r.ReadUInt32();
            long body = r.BaseStream.Position;
            if (chunk == "fmt ")
            {
                format = r.ReadUInt16(); channels = r.ReadUInt16(); rate = r.ReadInt32(); r.ReadInt32(); r.ReadUInt16(); bits = r.ReadUInt16();
                if (format == 0xFFFE) format = bits == 32 ? 3 : 1;
            }
            else if (chunk == "data")
            {
                if (channels != 1) throw new Exception("Expected mono WAV: " + path);
                int bytes = bits / 8;
                if (size == 0xFFFFFFFF || body + size > r.BaseStream.Length) size = r.BaseStream.Length - body;
                var samples = new double[size / bytes];
                for (long i = 0; i < samples.Length; i++)
                    samples[i] = format == 3 ? r.ReadSingle() : bits == 16 ? r.ReadInt16() / 32768.0 : (r.ReadByte() | r.ReadByte() << 8 | (sbyte)r.ReadByte() << 16) / 8388608.0;
                return samples;
            }
            r.BaseStream.Position = body + size + (size & 1);
        }
        throw new Exception("No audio data in " + path);
    }

    // Slowed track from ffmpeg's float WAV: scale frequencies, normalise to the peak target, write 24-bit.
    public static void Finish(string slowed, string target, double ratio, double peakTargetDbfs)
    {
        double[] x = Scale(ReadWav(slowed, out int rate), ratio);
        if (x.Length * 3L > int.MaxValue - 64) throw new Exception("Slowed track exceeds the 2 GB WAV limit; remove very large factors from report.slowdowns: " + target);
        double peak = AudioDsp.Peak(x), gain = peak > 0 ? Math.Pow(10, peakTargetDbfs / 20) / peak : 1;
        AudioDsp.WriteWav24(target, AudioDsp.Quantise24(x.Select(v => v * gain).ToArray(), out _), rate);
    }
}
