public enum NoteStatus { Valid, OutOfRange, Invalid }
public enum NoteMode { Normal, NoNotes, TooShort, BelowFloor, Flat }

public class NoteSpectrum
{
    public Note[] Notes;
    public double[] Db;            // raw dBFS; -inf for exact zeros, NaN when not evaluated
    public NoteStatus[] Status;
    public NoteMode Mode;
    public int SampleCount, ValidCount, InvalidCount, Unresolved;
    public double Duration, FMin, FMax, Nyquist;
    public double Strongest = double.NegativeInfinity, Low, High;
    public int StrongestIndex = -1;
    public double Floor;
    public const int MinimumSamples = 8;
}

// Exact-frequency DTFT samples of a finite record, with the same Hann window and coherent gain as AudioDsp.Spectrum.
public static class NoteAnalysis
{
    public static double[] Window(int n)
    {
        var w = new double[n];
        for (int i = 0; i < n; i++) w[i] = n < 2 ? 1 : 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1));
        return w;
    }

    // Oscillator recurrence, re-seeded from the analytic phase every block to bound drift.
    public static double[] Evaluate(double[] x, double fs, double[] frequencies)
    {
        int n = x.Length; double[] w = Window(n); double wsum = w.Sum();
        var xw = new double[n]; for (int i = 0; i < n; i++) xw[i] = x[i] * w[i];
        var db = new double[frequencies.Length];
        Parallel.For(0, frequencies.Length, j =>
        {
            double th = 2 * Math.PI * frequencies[j] / fs, c = Math.Cos(th), s = Math.Sin(th), re = 0, im = 0;
            for (int b = 0; b < n; b += 2048)
            {
                double cr = Math.Cos(th * b), ci = -Math.Sin(th * b);
                for (int i = b, end = Math.Min(n, b + 2048); i < end; i++)
                {
                    re += xw[i] * cr; im += xw[i] * ci;
                    double next = cr * c + ci * s; ci = ci * c - cr * s; cr = next;
                }
            }
            db[j] = AudioDsp.Dbfs(2 * Math.Sqrt(re * re + im * im) / wsum);
        });
        return db;
    }

    // Reference implementation for verification only.
    public static double[] EvaluateDirect(double[] x, double fs, double[] frequencies)
    {
        int n = x.Length; double[] w = Window(n); double wsum = w.Sum();
        return frequencies.Select(f =>
        {
            double re = 0, im = 0;
            for (int i = 0; i < n; i++) { double a = 2 * Math.PI * f * i / fs; re += x[i] * w[i] * Math.Cos(a); im -= x[i] * w[i] * Math.Sin(a); }
            return AudioDsp.Dbfs(2 * Math.Sqrt(re * re + im * im) / wsum);
        }).ToArray();
    }

    public static NoteSpectrum Analyze(double[] x, double fsOut, double fsIn, AudioPlotConfig p)
    {
        Note[] notes = MusicalNotes.Generate(p.concertAHz, p.minimumOctave, p.maximumOctave);
        var r = new NoteSpectrum
        {
            Notes = notes, Db = Enumerable.Repeat(double.NaN, notes.Length).ToArray(), Status = new NoteStatus[notes.Length],
            SampleCount = x.Length, Duration = x.Length / fsOut, Nyquist = Math.Min(fsOut, fsIn) / 2, Floor = p.displayFloorDbfs, FMin = notes[0].Hz
        };
        double top = Math.Min(p.maximumFrequencyHz, notes[^1].Hz);
        r.FMax = Math.Min(top, r.Nyquist);
        var evaluate = new List<int>();
        for (int i = 0; i < notes.Length; i++)
        {
            bool inside = notes[i].Hz <= top && notes[i].Hz < r.Nyquist;
            r.Status[i] = inside ? NoteStatus.Valid : NoteStatus.OutOfRange;
            if (inside) evaluate.Add(i);
        }
        if (x.Length < NoteSpectrum.MinimumSamples && evaluate.Count > 0)
        {
            foreach (int i in evaluate) r.Status[i] = NoteStatus.Invalid;
            r.InvalidCount = evaluate.Count; r.Mode = NoteMode.TooShort; return r;
        }
        double[] result = Evaluate(x, fsOut, evaluate.Select(i => notes[i].Hz).ToArray());
        for (int k = 0; k < evaluate.Count; k++)
        {
            int i = evaluate[k]; r.Db[i] = result[k];
            if (double.IsNaN(result[k])) { r.Status[i] = NoteStatus.Invalid; r.InvalidCount++; }
        }
        double gap = 2 / r.Duration;
        for (int i = 0; i < notes.Length; i++)
        {
            if (r.Status[i] != NoteStatus.Valid) continue;
            r.ValidCount++;
            double near = Math.Min(i > 0 ? notes[i].Hz - notes[i - 1].Hz : double.MaxValue, i + 1 < notes.Length ? notes[i + 1].Hz - notes[i].Hz : double.MaxValue);
            if (near < gap) r.Unresolved++;
            if (r.Db[i] > r.Strongest || r.StrongestIndex < 0) { r.Strongest = r.Db[i]; r.StrongestIndex = i; }
        }
        if (r.ValidCount == 0) { r.Mode = NoteMode.NoNotes; return r; }
        // Colours are scaled by min/max in dB within this probe only, after clamping at the display floor.
        if (r.Strongest <= p.displayFloorDbfs) { r.Mode = NoteMode.BelowFloor; r.Low = r.High = p.displayFloorDbfs; return r; }
        r.Low = double.MaxValue; r.High = double.MinValue;
        for (int i = 0; i < notes.Length; i++)
            if (r.Status[i] == NoteStatus.Valid) { double l = Level(r, i); r.Low = Math.Min(r.Low, l); r.High = Math.Max(r.High, l); }
        r.Mode = r.High - r.Low <= 1e-9 ? NoteMode.Flat : NoteMode.Normal;
        return r;
    }

    public static double Level(NoteSpectrum r, int i) => Math.Max(r.Floor, r.Db[i]);

    // Position 0..1 along the palette.
    public static double Position(NoteSpectrum r, int i) => r.Mode switch
    {
        NoteMode.Normal => Math.Clamp((Level(r, i) - r.Low) / (r.High - r.Low), 0, 1),
        NoteMode.Flat => 0.5,
        _ => 0
    };
}
