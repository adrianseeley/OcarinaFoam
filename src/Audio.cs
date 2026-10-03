using System.Globalization;
using System.Text;

// ocarina audio DIR: one WAV per probe plus CSVs of every intermediate stage. Always overwrites DIR/audio.
public static class Audio
{
    static StreamWriter log;
    static void Log(string line) { Console.WriteLine(line); log.WriteLine(line); }
    static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string Db(double v) => double.IsInfinity(v) ? "-inf" : v.ToString("F2", CultureInfo.InvariantCulture);

    public static void Run(string root)
    {
        Config c = Configuration.Built(root);
        AudioConfig a = c.audio;
        string probeDir = Path.Combine(Paths.Foam(root), "postProcessing", "acousticProbes");
        if (!Directory.Exists(probeDir)) throw new Exception("No probe output yet: " + probeDir);
        string output = Path.Combine(root, "audio");
        if (Directory.Exists(output)) Directory.Delete(output, true);
        Directory.CreateDirectory(output);
        using (log = new StreamWriter(Path.Combine(output, "audio.log"), false, new UTF8Encoding(false)) { AutoFlush = true })
        {
            Log($"ocarina audio  UTC {DateTime.UtcNow:O}");
            Log($"case {root}");
            Log($"settings {Configuration.Serialize(a)}");
            Read(probeDir, c.probes.Length, out double[] time, out double[][] pressure);
            Process(c, a, output, time, pressure);
        }
    }

    // Concatenates restart directories in numeric order; a restart replaces any overlapping later part of earlier data.
    static void Read(string dir, int probes, out double[] timeOut, out double[][] pressureOut)
    {
        var time = new List<double>(); var cols = Enumerable.Range(0, probes).Select(_ => new List<double>()).ToArray();
        var dirs = Directory.GetDirectories(dir).Where(d => File.Exists(Path.Combine(d, "p")) && double.TryParse(Path.GetFileName(d), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            .OrderBy(d => double.Parse(Path.GetFileName(d), CultureInfo.InvariantCulture)).ToArray();
        if (dirs.Length == 0) throw new Exception("No probe pressure files under " + dir);
        foreach (string d in dirs)
        {
            bool first = true; int headers = 0, rows = 0;
            foreach (string line in File.ReadLines(Path.Combine(d, "p")))
            {
                if (line.StartsWith('#')) { if (line.StartsWith("# Probe")) headers++; continue; }
                string[] tok = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (tok.Length == 0) continue;
                if (first && headers != probes) throw new Exception($"{d}/p holds {headers} probes but config has {probes}.");
                if (tok.Length != probes + 1) continue; // partial line from a running solver
                double t = double.Parse(tok[0], CultureInfo.InvariantCulture);
                if (first)
                {
                    first = false;
                    int keep = time.Count; while (keep > 0 && time[keep - 1] >= t) keep--;
                    if (keep < time.Count) { time.RemoveRange(keep, time.Count - keep); foreach (var col in cols) col.RemoveRange(keep, col.Count - keep); }
                }
                time.Add(t);
                for (int i = 0; i < probes; i++) cols[i].Add(double.Parse(tok[i + 1], CultureInfo.InvariantCulture));
                rows++;
            }
            Log($"read {d}/p: {rows} rows");
        }
        timeOut = time.ToArray(); pressureOut = cols.Select(x => x.ToArray()).ToArray();
        if (timeOut.Length < 16) throw new Exception("Too few probe samples to make audio.");
    }

    static void Process(Config c, AudioConfig a, string output, double[] time, double[][] pressure)
    {
        int n = time.Length;
        double dt = (time[n - 1] - time[0]) / (n - 1), fsIn = 1 / dt, fsOut = a.sampleRateHz;
        double drift = 0; for (int i = 0; i < n; i++) drift = Math.Max(drift, Math.Abs(time[i] - (time[0] + i * dt)));
        if (drift > 0.1 * dt) throw new Exception($"Probe times are not uniform (deviation {drift:G3} s vs step {dt:G3} s); a missing or rewritten stretch cannot be resampled.");
        Log($"probe samples {n}; t {F(time[0])}..{F(time[n - 1])} s; step {dt:G6} s ({fsIn:G6} Hz); nominal step {c.deltaTSeconds * c.probeWriteIntervalTimeSteps:G6} s; max time deviation {drift:G3} s");
        Log(time[n - 1] < c.endTimeSeconds - dt ? $"WARNING simulation incomplete: {time[n - 1]:G6} of {c.endTimeSeconds:G6} s" : "simulation complete");
        if (fsIn < fsOut) Log($"WARNING probe rate {fsIn:G6} Hz is below the audio rate; the audio is upsampled and band-limited by the probes.");

        var stages = new AudioStages[pressure.Length];
        for (int p = 0; p < pressure.Length; p++)
        {
            string name = c.probes[p].name;
            Directory.CreateDirectory(Path.Combine(output, name));
            double[] raw = pressure[p]; pressure[p] = null;
            double mean = raw.Average(), min = raw.Min(), max = raw.Max();
            double[] fluct = new double[n]; for (int i = 0; i < n; i++) fluct[i] = raw[i] - mean;
            Log($"\n== probe {name} ({string.Join(", ", c.probes[p].point.Select(F))}) m");
            Log($"native p: mean {mean:F4} Pa  min {min:F4}  max {max:F4}  fluctuation rms {AudioDsp.Rms(fluct):G6} Pa  peak {AudioDsp.Peak(fluct):G6} Pa");
            using (var w = new StreamWriter(Path.Combine(output, name, "native.csv"), false, new UTF8Encoding(false), 1 << 20))
            {
                w.WriteLine("time_s,pressure_pa,fluctuation_pa");
                for (int i = 0; i < n; i++) w.WriteLine(F(time[i]) + "," + F(raw[i]) + "," + F(fluct[i]));
            }
            var s = new AudioStages();
            s.Resampled = AudioDsp.Resample(fluct, fsIn, fsOut, a);
            s.HighPassed = AudioDsp.HighPass(s.Resampled, fsOut, a.highPassHz);
            s.Faded = AudioDsp.Fade(s.HighPassed, fsOut, a.fadeMilliseconds);
            stages[p] = s;
            Log($"resampled {n} -> {s.Resampled.Length} samples at {fsOut:G6} Hz; high-pass {a.highPassHz:G} Hz zero-phase; fade {a.fadeMilliseconds:G} ms");
        }

        double target = Math.Pow(10, a.peakTargetDbfs / 20);
        double sharedPeak = stages.Max(s => AudioDsp.Peak(s.Faded));
        if (a.sharedGain) Log($"\nshared gain from loudest probe peak {sharedPeak:G6} Pa");
        for (int p = 0; p < stages.Length; p++)
        {
            string name = c.probes[p].name; AudioStages s = stages[p];
            double peak = a.sharedGain ? sharedPeak : AudioDsp.Peak(s.Faded);
            s.Gain = peak > 0 ? target / peak : 1;
            if (peak <= 0) Log($"WARNING {name} is silent after filtering.");
            s.Normalised = s.Faded.Select(v => v * s.Gain).ToArray();
            s.Pcm = AudioDsp.Quantise24(s.Normalised, out int clipped);
            string wav = Path.Combine(output, name + ".wav");
            AudioDsp.WriteWav24(wav, s.Pcm, a.sampleRateHz);
            using (var w = new StreamWriter(Path.Combine(output, name, "audio.csv"), false, new UTF8Encoding(false), 1 << 20))
            {
                w.WriteLine("time_s,resampled_pa,highpassed_pa,faded_pa,normalised_fullscale,pcm24");
                for (int i = 0; i < s.Pcm.Length; i++)
                    w.WriteLine(F(i / fsOut) + "," + F(s.Resampled[i]) + "," + F(s.HighPassed[i]) + "," + F(s.Faded[i]) + "," + F(s.Normalised[i]) + "," + s.Pcm[i]);
            }
            double[] db = AudioDsp.Spectrum(s.Normalised, fsOut, out double binHz);
            int top = 1; double centroidNum = 0, centroidDen = 0;
            using (var w = new StreamWriter(Path.Combine(output, name, "spectrum.csv"), false, new UTF8Encoding(false), 1 << 20))
            {
                w.WriteLine("frequency_hz,magnitude_dbfs");
                for (int k = 0; k < db.Length; k++)
                {
                    w.WriteLine(F(k * binHz) + "," + (double.IsInfinity(db[k]) ? "-300" : F(db[k])));
                    if (k > 0 && db[k] > db[top]) top = k;
                    if (k > 0) { double m = Math.Pow(10, db[k] / 20); centroidNum += k * binHz * m * m; centroidDen += m * m; }
                }
            }
            double outPeak = AudioDsp.Peak(s.Normalised), outRms = AudioDsp.Rms(s.Normalised);
            Log($"\n-- {name}: {wav}");
            Log($"duration {s.Pcm.Length / fsOut:F4} s, {s.Pcm.Length} samples, 24-bit mono {a.sampleRateHz} Hz");
            Log($"gain {Db(AudioDsp.Dbfs(s.Gain))} dB ({F(s.Gain)} fs/Pa); pre-gain peak {AudioDsp.Peak(s.Faded):G6} Pa, rms {AudioDsp.Rms(s.Faded):G6} Pa");
            Log($"output peak {Db(AudioDsp.Dbfs(outPeak))} dBFS, rms {Db(AudioDsp.Dbfs(outRms))} dBFS, crest {Db(AudioDsp.Dbfs(outPeak / Math.Max(outRms, 1e-300)))} dB, clipped {clipped}");
            Log($"spectral peak {top * binHz:F2} Hz at {Db(db[top])} dBFS, centroid {(centroidDen > 0 ? centroidNum / centroidDen : 0):F1} Hz, bin {binHz:G4} Hz");
            Log($"csv: {name}/native.csv, {name}/audio.csv, {name}/spectrum.csv");
            if (a.plots.enabled)
            {
                AudioPlotResult plots = AudioPlots.Make(name, s.Normalised, fsOut, fsIn, db, binHz, a, Path.Combine(output, name));
                NoteSpectrum ns = plots.Notes;
                Log($"plots: {name}/plots/spectrum.png, {name}/plots/punch.png");
                Log($"notes: {ns.ValidCount} sampled of {ns.Notes.Length} (A4 {a.plots.concertAHz:G} Hz), mode {ns.Mode}, invalid {ns.InvalidCount}, T {ns.Duration:G6} s, fft bin {binHz:G4} Hz, 1/T {1 / ns.Duration:G4} Hz");
                if (ns.ValidCount > 0 && ns.Mode != NoteMode.TooShort) Log($"note colour range {ns.Low:F2}..{ns.High:F2} dBFS (floor {ns.Floor:G} dBFS); strongest {ns.Notes[ns.StrongestIndex].Name} {Db(ns.Strongest)} dBFS");
                if (plots.Warning != null) Log("WARNING " + plots.Warning);
                if (ns.Mode is NoteMode.BelowFloor or NoteMode.NoNotes or NoteMode.TooShort) Log($"WARNING note plot condition: {ns.Mode}");
            }
        }
        Log("\ndone");
    }
}
