using System.Globalization;
using SkiaSharp;

public class AudioPlotResult
{
    public string SpectrumPath, OctavePath, PunchPath;
    public string[] WaveformPaths;
    public NoteSpectrum Notes;
    public double BinHz;
    public string Warning;
}

// spectrum.png (log-frequency FFT), octave.png (folded by octave), and punch.png for one probe.
public static class AudioPlots
{
    const int PngLevel = 6;
    const double MaxPixels = 64e6;
    static readonly SKColor Background = Theme.Background, Ink = Theme.Ink, Dim = Theme.Secondary,
        Grid = Theme.Grid, NoteLine = new SKColor(70, 70, 70), OctaveLine = Theme.StrongGrid,
        Warn = new SKColor(255, 170, 60), NotAvailable = new SKColor(64, 64, 64);

    static string N(double v, string f = "G4") => v.ToString(f, CultureInfo.InvariantCulture);
    static string Hz(double v) => N(v, "F1") + " HZ";

    static void Text(SKCanvas canvas, string text, float x, float baseline, SKTextAlign align, float size, SKColor color, float maxWidth = float.PositiveInfinity)
    {
        string missing = LineFont.Missing(text);
        if (missing.Length > 0) throw new InvalidOperationException($"Plot label '{text}' uses glyphs missing from LineFont: {missing}");
        using var paint = new SKPaint { Color = color };
        LineFont.Draw(canvas, text, x, baseline, align, size, paint, maxWidth);
    }

    // Layout, shared by drawing and by configuration validation.
    static float SpectrumLeft(float f) => 7 * f;
    static float SpectrumRight(float f) => 3 * f;
    static float SpectrumTop(float f) => 8.2f * f;
    static float SpectrumBottom(float f) => 7 * f;
    static float PunchLeft(float f) => 5 * f;
    static float PunchRight(float f) => 2 * f;
    static float PunchTop(float f) => 9 * f;
    static float PunchBottom(float f) => 11 * f;

    public static void Validate(AudioPlotConfig p)
    {
        if (p == null) throw new Exception("audio.plots must not be null.");
        Configuration.Positive(p.concertAHz, "plots.concertAHz"); Configuration.Positive(p.maximumFrequencyHz, "plots.maximumFrequencyHz");
        if (p.minimumOctave < 0 || p.maximumOctave > 9 || p.minimumOctave > p.maximumOctave) throw new Exception("plots octaves must satisfy 0 <= minimumOctave <= maximumOctave <= 9.");
        if (!double.IsFinite(p.displayFloorDbfs) || p.displayFloorDbfs >= 0) throw new Exception("plots.displayFloorDbfs must be finite and negative.");
        if (p.waveformPointsPerPlot < 1000) throw new Exception("plots.waveformPointsPerPlot must be at least 1000.");
        if (p.labelFontPixels < 8 || p.labelFontPixels > 80) throw new Exception("plots.labelFontPixels must be 8..80.");
        foreach (int d in new[] { p.spectrumWidth, p.waveformWidth, p.waveformHeight, p.spectrumHeight, p.punchWidth, p.punchHeight })
            if (d < 400 || d > 16384) throw new Exception("plot dimensions must be 400..16384 pixels.");
        if ((double)p.spectrumWidth * p.spectrumHeight > MaxPixels || (double)p.waveformWidth * p.waveformHeight > MaxPixels || (double)p.punchWidth * p.punchHeight > MaxPixels) throw new Exception("plot images are too large.");
        Note[] notes = MusicalNotes.Generate(p.concertAHz, p.minimumOctave, p.maximumOctave);
        if (p.maximumFrequencyHz <= notes[0].Hz) throw new Exception("plots.maximumFrequencyHz must exceed the lowest note.");
        float f = p.labelFontPixels;
        float plotW = p.spectrumWidth - SpectrumLeft(f) - SpectrumRight(f), plotH = p.spectrumHeight - SpectrumTop(f) - SpectrumBottom(f);
        double octaves = Math.Log2(Math.Min(p.maximumFrequencyHz, notes[^1].Hz) / notes[0].Hz);
        // Only C labels are drawn on the joint spectrum, one per octave.
        if (plotH < 10 * f || plotW <= 0 || plotW / Math.Max(octaves, 1) < LineFont.Width("C9", f) + 0.4f * f)
            throw new Exception("plots spectrum size is too small for the label font; enlarge spectrumWidth/Height or reduce labelFontPixels.");
        if (p.waveformWidth - 10 * f <= 0 || p.waveformHeight - 8.5f * f < 6 * f)
            throw new Exception("plots waveform size is too small for the label font; enlarge waveformWidth/Height or reduce labelFontPixels.");
        if (p.punchLabelFontPixels < 6 || p.punchLabelFontPixels > 40) throw new Exception("plots.punchLabelFontPixels must be 6..40.");
        f = p.punchLabelFontPixels;
        int rows = p.maximumOctave - p.minimumOctave + 1;
        float cellW = (p.punchWidth - PunchLeft(f) - PunchRight(f)) / 12, cellH = (p.punchHeight - PunchTop(f) - PunchBottom(f)) / rows;
        if (cellW < LineFont.Width("BELOW FLOOR", f) + 0.4f * f || cellH < 4.8f * f)
            throw new Exception("plots punch size is too small for the label font; enlarge punchWidth/Height or reduce punchLabelFontPixels.");
    }

    public static AudioPlotResult Make(string name, double[] samples, double fsOut, double fsIn, double[] fftDb, double binHz, AudioConfig a, string folder, int probe = 0)
    {
        AudioPlotConfig p = a.plots;
        NoteSpectrum notes = NoteAnalysis.Analyze(samples, fsOut, fsIn, p);
        var result = new AudioPlotResult { Notes = notes, BinHz = binHz };
        string dir = Directory.CreateDirectory(Path.Combine(folder, "plots")).FullName;
        result.SpectrumPath = Path.Combine(dir, "spectrum.png"); result.OctavePath = Path.Combine(dir, "octave.png"); result.PunchPath = Path.Combine(dir, "punch.png");
        string context = $"PROCESSED AUDIO: RESAMPLED TO {N(fsOut, "F0")} HZ, HIGH-PASS {N(a.highPassHz)} HZ, FADE {N(a.fadeMilliseconds)} MS, GAIN APPLIED" +
            (fsIn / 2 < fsOut / 2 ? $", NATIVE NYQUIST {N(fsIn / 2, "F0")} HZ" : "");
        string meta = $"N={notes.SampleCount} SAMPLES, T={N(notes.Duration)} S, 1/T={N(1 / Math.Max(notes.Duration, 1e-300))} HZ, FFT BIN {N(binHz)} HZ, HANN, A4 = {N(p.concertAHz)} HZ";
        result.WaveformPaths = WaveformSegments(Path.Combine(dir, "waveform"), name, new[] { samples }, new[] { Theme.Probe(probe) }, null, fsOut, p, context);
        if (notes.Unresolved > 0) result.Warning = $"SHORT RECORD: ADJACENT NOTES MAY NOT BE RESOLVED ({notes.Unresolved} OF {notes.ValidCount} NOTES CLOSER THAN 2/T)";
        Render(result.SpectrumPath, name, p.spectrumWidth, p.spectrumHeight, c => DrawSpectrum(c, p, name, notes, new[] { (fftDb, binHz) }, new[] { Theme.Probe(probe) }, new[] { name }, meta, context, result.Warning));
        Render(result.OctavePath, name, p.spectrumWidth, p.spectrumHeight, c => DrawOctaveSpectrum(c, p, name, notes, fftDb, binHz, meta, context, result.Warning));
        Render(result.PunchPath, name, p.punchWidth, p.punchHeight, c => DrawPunch(c, p, name, notes, meta, context, result.Warning));
        return result;
    }

    // Splits the record into windows of exactly waveformPointsPerPlot samples, one PNG each (waveform/K_of_N.png).
    // The last window may be partly filled; every window has the same time scale.
    static string[] WaveformSegments(string dir, string title, double[][] series, SKColor[] colors, string[] labels, double fs, AudioPlotConfig p, string context)
    {
        Directory.CreateDirectory(dir);
        foreach (string old in Directory.EnumerateFiles(dir, "*_of_*.png")) File.Delete(old);
        long total = series.Max(x => (long)x.Length);
        long points = p.waveformPointsPerPlot;
        int n = (int)Math.Max(1, (total + points - 1) / points);
        var paths = new string[n];
        for (int k = 0; k < n; k++)
        {
            long a = points * k, b = points * (k + 1);
            double[][] part = series.Select(x => x.Skip((int)Math.Min(a, x.Length)).Take((int)Math.Max(0, Math.Min(b, x.Length) - a)).ToArray()).ToArray();
            double t0 = a / fs, window = points / fs;
            string label = $"{title} WAVEFORM {k + 1} OF {n}";
            paths[k] = Path.Combine(dir, $"{k + 1}_of_{n}.png");
            Render(paths[k], title, p.waveformWidth, p.waveformHeight, c => DrawWaveform(c, p, label, part, colors, labels, fs, p.waveformWidth, p.waveformHeight, context, t0, window));
        }
        return paths;
    }

    static void Render(string path, string probe, int width, int height, Action<SKCanvas> draw)
    {
        try
        {
            using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bitmap)) { canvas.Clear(Background); draw(canvas); }
            Renderer.SavePng(bitmap, path, PngLevel);
        }
        catch (Exception e) { throw new Exception($"Could not write plot for probe {probe}: {path}", e); }
    }

    // Per-pixel-column min/max envelope of the processed audio against time, full scale = +/-1.
    static void DrawWaveform(SKCanvas canvas, AudioPlotConfig p, string title, double[][] series, SKColor[] colors, string[] labels, double fs, int width, int height, string context, double t0, double duration)
    {
        float f = p.labelFontPixels, left = 7 * f, right = 3 * f, top = 4.5f * f, bottom = 4 * f;
        float plotW = width - left - right, plotH = height - top - bottom;
        using var paint = new SKPaint { Color = Grid, StrokeWidth = 1, IsAntialias = false };
        foreach (double level in new[] { -1, -0.5, 0, 0.5, 1 })
        {
            float y = top + plotH * (float)(1 - level) / 2;
            paint.Color = Grid;
            canvas.DrawLine(left, y, left + plotW, y, paint);
            Text(canvas, N(level, "0.0"), left - 0.5f * f, y + 0.35f * f, SKTextAlign.Right, f, Dim);
        }
        int ticks = 10;
        for (int t = 0; t <= ticks; t++)
        {
            float px = left + plotW * t / ticks;
            paint.Color = Grid; canvas.DrawLine(px, top, px, top + plotH, paint);
            Text(canvas, N(t0 + duration * t / ticks, "0.000"), px, top + plotH + 1.6f * f, SKTextAlign.Center, f, Dim);
        }
        paint.StrokeWidth = Theme.Stroke;
        for (int si = 0; si < series.Length; si++)
        {
        double[] x = series[si];
        paint.Color = colors[si];
        int columns = Math.Max(1, (int)plotW);
        if (x.Length > 1 && Math.Round(duration * fs) < 8 * columns)
        {
            // Fewer samples than pixels: join them rather than leave isolated dots.
            using var path = new SKPath();
            for (int i = 0; i < x.Length; i++)
            {
                float px = left + plotW * i / (float)Math.Max(1, Math.Round(duration * fs) - 1), py = top + plotH * (float)(1 - Math.Clamp(x[i], -1, 1)) / 2;
                if (i == 0) path.MoveTo(px, py); else path.LineTo(px, py);
            }
            paint.Style = SKPaintStyle.Stroke; paint.IsAntialias = true;
            canvas.DrawPath(path, paint);
            columns = 0;
        }
        for (int c = 0; c < columns && x.Length > 0; c++)
        {
            long total = (long)Math.Round(duration * fs); int a = (int)(total * c / columns), b = Math.Max(a + 1, (int)(total * (c + 1) / columns));
            if (a >= x.Length) break;
            double lo = double.MaxValue, hi = double.MinValue;
            for (int i = a; i < b && i < x.Length; i++) { if (x[i] < lo) lo = x[i]; if (x[i] > hi) hi = x[i]; }
            if (lo > hi) continue;
            float y1 = top + plotH * (float)(1 - Math.Clamp(hi, -1, 1)) / 2, y2 = top + plotH * (float)(1 - Math.Clamp(lo, -1, 1)) / 2;
            canvas.DrawLine(left + c + 0.5f, y1, left + c + 0.5f, Math.Max(y2, y1 + 1), paint);
        }
        }
        if (labels != null) Legend(canvas, labels, colors, left + plotW - f, top + 1.5f * f, f);
        Text(canvas, title.ToUpperInvariant(), left, 1.8f * f, SKTextAlign.Left, f * 1.2f, Ink);
        Text(canvas, "TIME (S)", left + plotW / 2, height - 0.5f * f, SKTextAlign.Center, f, Dim);
        Text(canvas, context, left, 3.5f * f, SKTextAlign.Left, f * 0.9f, Dim, plotW);
    }

    // One line per octave, all folded onto C..B: a harmonic series lines up vertically across the octave lines.
    static void DrawOctaveSpectrum(SKCanvas canvas, AudioPlotConfig p, string name, NoteSpectrum r, double[] db, double binHz, string meta, string context, string warning)
    {
        float f = p.labelFontPixels, W = p.spectrumWidth, H = p.spectrumHeight;
        float left = 5.5f * f, right = W - 9 * f, top = 6.5f * f, bottom = H - 7 * f;
        Text(canvas, name + " - SPECTRUM BY OCTAVE", left, 2.4f * f, SKTextAlign.Left, 2 * f, Ink, W - left - 2 * f);
        Text(canvas, meta, left, 4.2f * f, SKTextAlign.Left, f * 0.8f, Dim, W - left - 2 * f);
        double yMin = p.displayFloorDbfs, yMax = 0;
        foreach (double v in db) if (double.IsFinite(v)) yMax = Math.Max(yMax, v);
        yMax = Math.Ceiling(yMax / 10) * 10;
        float Y(double v) => bottom - (bottom - top) * (float)((Math.Clamp(double.IsNaN(v) ? yMin : v, yMin, yMax) - yMin) / (yMax - yMin));
        float X(double semitone) => left + (right - left) * (float)(semitone / 12);
        double step = (yMax - yMin) > 150 ? 20 : 10;
        using var gridPaint = new SKPaint { Color = Grid, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        using var strongPaint = new SKPaint { Color = OctaveLine, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        for (double v = Math.Ceiling(yMin / step) * step; v <= yMax; v += step)
        {
            canvas.DrawLine(left, Y(v), right, Y(v), gridPaint);
            Text(canvas, N(v, "F0"), left - 0.6f * f, Y(v) + 0.5f * f, SKTextAlign.Right, f, Dim);
        }
        Text(canvas, "DBFS", left - 0.6f * f, top - 0.6f * f, SKTextAlign.Right, f, Dim);
        string[] names = { "C", "C" + MusicalNotes.Sharp, "D", "D" + MusicalNotes.Sharp, "E", "F", "F" + MusicalNotes.Sharp, "G", "G" + MusicalNotes.Sharp, "A", "A" + MusicalNotes.Sharp, "B", "C" };
        for (int s = 0; s <= 12; s++)
        {
            canvas.DrawLine(X(s), top, X(s), bottom, s % 12 == 0 ? strongPaint : gridPaint);
            Text(canvas, names[s], X(s), bottom + 1.6f * f, SKTextAlign.Center, f, Ink);
        }
        using var frame = new SKPaint { Color = Dim, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        canvas.DrawRect(left, top, right - left, bottom - top, frame);
        Text(canvas, "NOTE WITHIN OCTAVE (EACH LINE = ONE OCTAVE, C TO NEXT C)", (left + right) / 2, bottom + 3.1f * f, SKTextAlign.Center, f, Ink);
        Text(canvas, context, left, bottom + 4.5f * f, SKTextAlign.Left, f * 0.7f, Dim, W - left - 2 * f);
        Text(canvas, warning ?? "LINEAR INTERPOLATION BETWEEN FFT BINS; BIN SPACING IS ZERO-PADDED SAMPLING, NOT RESOLUTION", left, bottom + 5.8f * f, SKTextAlign.Left, f * 0.7f, warning == null ? Dim : Warn, W - left - 2 * f);
        if (!(r.FMax > r.FMin)) { Text(canvas, "NO VALID FREQUENCY RANGE FOR THIS SAMPLE RATE", (left + right) / 2, (top + bottom) / 2, SKTextAlign.Center, 1.5f * f, Warn, right - left); return; }

        double c0 = p.concertAHz * Math.Pow(2, -57.0 / 12);
        int count = p.maximumOctave - p.minimumOctave + 1, points = Math.Max(2, (int)(right - left));
        bool silent = db.All(v => double.IsNegativeInfinity(v) || double.IsNaN(v));
        canvas.Save(); canvas.ClipRect(new SKRect(left, top, right, bottom));
        var colors = new SKColor[count]; var labels = new string[count];
        for (int i = 0; i < count; i++)
        {
            int octave = p.minimumOctave + i;
            colors[i] = Theme.Map(count == 1 ? 1 : (double)i / (count - 1));
            labels[i] = "C" + octave + "-B" + octave;
            using var path = new SKPath();
            bool pen = false;
            for (int k = 0; k < points; k++)
            {
                double semitone = 12.0 * k / (points - 1), hz = c0 * Math.Pow(2, octave + semitone / 12);
                double bin = hz / binHz; int b0 = (int)Math.Floor(bin);
                if (b0 < 1 || b0 + 1 >= db.Length || hz > r.FMax || hz < r.FMin) { pen = false; continue; }
                double d0 = db[b0], d1 = db[b0 + 1];
                if (double.IsNaN(d0) || double.IsNaN(d1)) { pen = false; continue; }
                double v = double.IsFinite(d0) && double.IsFinite(d1) ? d0 + (d1 - d0) * (bin - b0) : double.NegativeInfinity;
                if (pen) path.LineTo(X(semitone), Y(v)); else { path.MoveTo(X(semitone), Y(v)); pen = true; }
            }
            using var line = new SKPaint { Color = colors[i], StrokeWidth = Theme.Stroke, Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeJoin = SKStrokeJoin.Round };
            canvas.DrawPath(path, line);
        }
        canvas.Restore();
        Legend(canvas, labels.Reverse().ToArray(), colors.Reverse().ToArray(), W - f, top + 1.5f * f, f);
        if (silent) Text(canvas, "SILENT: NO SPECTRAL ENERGY", (left + right) / 2, (top + bottom) / 2, SKTextAlign.Center, 1.5f * f, Warn, right - left);
    }

    static void DrawSpectrum(SKCanvas canvas, AudioPlotConfig p, string name, NoteSpectrum r, (double[] Db, double BinHz)[] series, SKColor[] colors, string[] labels, string meta, string context, string warning)
    {
        float f = p.labelFontPixels, W = p.spectrumWidth, H = p.spectrumHeight;
        float left = SpectrumLeft(f), right = W - SpectrumRight(f), top = SpectrumTop(f), bottom = H - SpectrumBottom(f);
        Text(canvas, name + " - SPECTRUM", left, 2.4f * f, SKTextAlign.Left, 2 * f, Ink, W - left - 2 * f);
        Text(canvas, meta, left, 4.2f * f, SKTextAlign.Left, f, Dim, W - left - 2 * f);
        double yMin = p.displayFloorDbfs, yMax = 0;
        foreach (var sr in series) foreach (double v in sr.Db) if (double.IsFinite(v)) yMax = Math.Max(yMax, v);
        yMax = Math.Ceiling(yMax / 10) * 10;
        float Y(double v) => bottom - (bottom - top) * (float)((Math.Clamp(double.IsNaN(v) ? yMin : v, yMin, yMax) - yMin) / (yMax - yMin));
        double step = (yMax - yMin) > 150 ? 20 : 10;
        using var gridPaint = new SKPaint { Color = Grid, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        for (double v = Math.Ceiling(yMin / step) * step; v <= yMax; v += step)
        {
            canvas.DrawLine(left, Y(v), right, Y(v), gridPaint);
            Text(canvas, N(v, "F0"), left - 0.6f * f, Y(v) + 0.5f * f, SKTextAlign.Right, f, Dim);
        }
        Text(canvas, "DBFS", left - 1.8f * f, top - 0.5f * f, SKTextAlign.Right, f, Dim);
        using var frame = new SKPaint { Color = Dim, StrokeWidth = 1.5f, Style = SKPaintStyle.Stroke };
        canvas.DrawRect(left, top, right - left, bottom - top, frame);
        Text(canvas, "FREQUENCY (HZ, LOG SCALE)", (left + right) / 2, bottom + 3 * f, SKTextAlign.Center, f, Dim);
        Text(canvas, context, left, bottom + 4.6f * f, SKTextAlign.Left, f, Dim, W - left - 2 * f);
        string footer = warning ?? "FFT BIN SPACING IS ZERO-PADDED SAMPLING OF THE SPECTRUM, NOT FREQUENCY RESOLUTION";
        Text(canvas, footer, left, bottom + 6.1f * f, SKTextAlign.Left, f, warning == null ? Dim : Warn, W - left - 2 * f);
        if (!(r.FMax > r.FMin))
        {
            Text(canvas, "NO VALID FREQUENCY RANGE FOR THIS SAMPLE RATE", (left + right) / 2, (top + bottom) / 2, SKTextAlign.Center, 1.5f * f, Warn, right - left);
            return;
        }
        double span = Math.Log2(r.FMax / r.FMin);
        float X(double hz) => left + (right - left) * (float)(Math.Log2(hz / r.FMin) / span);

        // Reference lines first so the spectrum stays on top.
        using var notePaint = new SKPaint { Color = NoteLine, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        using var octavePaint = new SKPaint { Color = OctaveLine, StrokeWidth = 1.5f, Style = SKPaintStyle.Stroke };
        int row = 0;
        for (int i = 0; i < r.Notes.Length; i++)
        {
            Note n = r.Notes[i];
            if (n.Hz > r.FMax) break;
            float x = X(n.Hz);
            canvas.DrawLine(x, top, x, bottom, n.PitchClass == 0 ? octavePaint : notePaint);
            float labelBase = 7f * f;
            if (n.PitchClass != 0) continue;
            canvas.DrawLine(x, labelBase + 0.15f * f, x, top, octavePaint);
            Text(canvas, n.Name, x, labelBase, SKTextAlign.Center, f, Ink);
        }
        foreach (double tick in new double[] { 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 })
        {
            if (tick < r.FMin || tick > r.FMax) continue;
            canvas.DrawLine(X(tick), bottom, X(tick), bottom + 0.4f * f, frame);
            Text(canvas, N(tick, "F0"), X(tick), bottom + 1.7f * f, SKTextAlign.Center, f, Dim);
        }

        canvas.Save(); canvas.ClipRect(new SKRect(left, top, right, bottom));
        bool silent = true;
        for (int si = 0; si < series.Length; si++)
        {
            double[] db = series[si].Db; double binHz = series[si].BinHz;
            if (!db.All(v => double.IsNegativeInfinity(v) || double.IsNaN(v))) silent = false;
            // Straight segments between FFT bins; points just outside the axis keep the edge segments correct.
            using var path = new SKPath();
            bool pen = false;
            int first = Math.Max(1, (int)Math.Floor(r.FMin / binHz) - 1);
            for (int k = first; k < db.Length; k++)
            {
                double hz = k * binHz;
                if (double.IsNaN(db[k])) { pen = false; continue; }
                float x = X(hz), y = Y(db[k]);
                if (pen) path.LineTo(x, y); else { path.MoveTo(x, y); pen = true; }
                if (hz > r.FMax) break;
            }
            using var linePaint = new SKPaint { Color = colors[si], StrokeWidth = Theme.Stroke, Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeJoin = SKStrokeJoin.Round };
            canvas.DrawPath(path, linePaint);
        }
        canvas.Restore();
        if (labels != null) Legend(canvas, labels, colors, right - f, top + 1.5f * f, f);
        if (silent)
            Text(canvas, "SILENT: NO SPECTRAL ENERGY", (left + right) / 2, (top + bottom) / 2, SKTextAlign.Center, 1.5f * f, Warn, right - left);
    }

    static void Legend(SKCanvas canvas, string[] labels, SKColor[] colors, float right, float top, float f)
    {
        float width = labels.Max(l => LineFont.Width(l.ToUpperInvariant(), f)) + 3 * f;
        using var back = new SKPaint { Color = new SKColor(5, 5, 5, 220) };
        canvas.DrawRect(right - width - 0.5f * f, top - 1.2f * f, width + f, 1.4f * f * labels.Length + 0.6f * f, back);
        for (int i = 0; i < labels.Length; i++)
        {
            using var swatch = new SKPaint { Color = colors[i], StrokeWidth = Theme.Stroke, Style = SKPaintStyle.Stroke, IsAntialias = true };
            float y = top + 1.4f * f * i;
            canvas.DrawLine(right - width, y - 0.35f * f, right - width + 1.8f * f, y - 0.35f * f, swatch);
            Text(canvas, labels[i].ToUpperInvariant(), right - width + 2.4f * f, y, SKTextAlign.Left, f, Ink);
        }
    }

    // all_spectrum.png and all_waveform.png: every probe as one line on a shared plot.
    public static void MakeJoint(string[] names, double[][] samples, double[][] db, double[] binHz, double fsOut, double fsIn, AudioConfig a, string folder)
    {
        AudioPlotConfig p = a.plots;
        NoteSpectrum notes = NoteAnalysis.Analyze(samples[0], fsOut, fsIn, p);
        string dir = Directory.CreateDirectory(Path.Combine(folder, "plots")).FullName;
        SKColor[] colors = Enumerable.Range(0, names.Length).Select(Theme.Probe).ToArray();
        string context = $"ALL PROBES, PROCESSED AUDIO AT {N(fsOut, "F0")} HZ, HIGH-PASS {N(a.highPassHz)} HZ, FADE {N(a.fadeMilliseconds)} MS, GAIN APPLIED";
        string meta = $"A4 = {N(p.concertAHz)} HZ, HANN, PER-PROBE FFT BIN SPACING";
        var series = db.Select((d, i) => (d, binHz[i])).ToArray();
        WaveformSegments(Path.Combine(dir, "all_waveform"), "ALL PROBES", samples, colors, names, fsOut, p, context);
        Render(Path.Combine(dir, "all_spectrum.png"), "all", p.spectrumWidth, p.spectrumHeight, c => DrawSpectrum(c, p, "ALL PROBES", notes, series, colors, names, meta, context, null));
    }

    static SKColor Contrast(SKColor c) => 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue > 110 ? Theme.Background : Theme.Ink;

    static void DrawPunch(SKCanvas canvas, AudioPlotConfig p, string name, NoteSpectrum r, string meta, string context, string warning)
    {
        float f = p.punchLabelFontPixels, W = p.punchWidth, H = p.punchHeight;
        float left = PunchLeft(f), top = PunchTop(f);
        int rows = p.maximumOctave - p.minimumOctave + 1;
        float cellW = (W - left - PunchRight(f)) / 12, cellH = (H - top - PunchBottom(f)) / rows;
        SKColor[] palette = Renderer.Palette();
        Text(canvas, name + " - NOTE SPECTRUM", left, 2.4f * f, SKTextAlign.Left, 2 * f, Ink, W - left - 2 * f);
        Text(canvas, $"EXACT NOTE-FREQUENCY SAMPLES / A4 = {N(p.concertAHz)} HZ", left, 4 * f, SKTextAlign.Left, f, Dim, W - left - 2 * f);
        string status = r.Mode switch
        {
            NoteMode.NoNotes => "NO NOTES IN RANGE",
            NoteMode.TooShort => "RECORD TOO SHORT TO ANALYZE",
            NoteMode.BelowFloor => "NO SIGNAL ABOVE DISPLAY FLOOR",
            NoteMode.Flat => "ALL SAMPLED NOTES EQUAL",
            _ => null
        };
        if (r.InvalidCount > 0 && r.Mode != NoteMode.TooShort) status = (status == null ? "" : status + " / ") + r.InvalidCount + " NOTES INVALID (EXCLUDED)";
        if (status != null) Text(canvas, status, left, 5.5f * f, SKTextAlign.Left, f, Warn, W - left - 2 * f);
        for (int pc = 0; pc < 12; pc++)
        {
            string[] parts = MusicalNotes.Header(pc).Split('/');
            for (int k = 0; k < parts.Length; k++)
                Text(canvas, parts[k], left + cellW * (pc + .5f), (parts.Length == 1 ? 8.3f : 6.9f + 1.4f * k) * f, SKTextAlign.Center, f, Ink, cellW);
        }
        double rel(double v) => v - r.Strongest;
        for (int rowIndex = 0; rowIndex < rows; rowIndex++)
        {
            int octave = p.maximumOctave - rowIndex;
            float y = top + cellH * rowIndex;
            Text(canvas, octave.ToString(CultureInfo.InvariantCulture), left - 1.2f * f, y + cellH / 2 + 0.5f * f, SKTextAlign.Right, f, Ink);
            for (int pc = 0; pc < 12; pc++)
            {
                int i = (octave - p.minimumOctave) * 12 + pc;
                Note n = r.Notes[i];
                var rect = new SKRect(left + cellW * pc + 2, y + 2, left + cellW * (pc + 1) - 2, y + cellH - 2);
                bool valid = r.Status[i] == NoteStatus.Valid;
                SKColor fill = valid ? palette[(int)Math.Round(NoteAnalysis.Position(r, i) * (palette.Length - 1))] : NotAvailable;
                using (var paint = new SKPaint { Color = fill, Style = SKPaintStyle.Fill }) canvas.DrawRect(rect, paint);
                using (var border = new SKPaint { Color = Grid, Style = SKPaintStyle.Stroke, StrokeWidth = 2 }) canvas.DrawRect(rect, border);
                SKColor ink = valid ? Contrast(fill) : Dim;
                string third = r.Status[i] == NoteStatus.OutOfRange ? "N/A" : r.Status[i] == NoteStatus.Invalid ? "INVALID"
                    : r.Mode == NoteMode.BelowFloor || r.Db[i] <= r.Floor ? "BELOW FLOOR" : N(rel(r.Db[i]), "F1") + " DB";
                float cx = rect.MidX, line = 1.4f * f, block = valid || r.Status[i] == NoteStatus.Invalid ? 3 : 2, y0 = rect.MidY - (block * line - 0.4f * f) / 2 + f;
                Text(canvas, n.Name, cx, y0, SKTextAlign.Center, 1.15f * f, ink, cellW - 8);
                if (r.Status[i] == NoteStatus.OutOfRange) Text(canvas, third, cx, y0 + line, SKTextAlign.Center, f, ink, cellW - 8);
                else { Text(canvas, Hz(n.Hz), cx, y0 + line, SKTextAlign.Center, f, ink, cellW - 8); Text(canvas, third, cx, y0 + 2 * line, SKTextAlign.Center, f, ink, cellW - 8); }
            }
        }

        float gridBottom = top + cellH * rows;
        float barX = left, barY = gridBottom + f, barW = Math.Min(W * 0.5f, 700 * f / 20), barH = 1.2f * f;
        using (var bar = new SKPaint())
            for (int x = 0; x < (int)barW; x++) { bar.Color = palette[(int)Math.Round((double)x / Math.Max(1, (int)barW - 1) * (palette.Length - 1))]; canvas.DrawRect(barX + x, barY, 1, barH, bar); }
        using (var edge = new SKPaint { Color = Dim, Style = SKPaintStyle.Stroke, StrokeWidth = 1 }) canvas.DrawRect(barX, barY, barW, barH, edge);
        if (r.Mode == NoteMode.Normal)
            for (int t = 0; t <= 4; t++)
                Text(canvas, N(rel(r.Low + (r.High - r.Low) * t / 4) + 0.0, "F1"), barX + barW * t / 4, barY + barH + 1.5f * f, SKTextAlign.Center, f, Dim);
        else if (r.Mode == NoteMode.Flat) Text(canvas, "0.0", barX + barW / 2, barY + barH + 1.5f * f, SKTextAlign.Center, f, Dim);
        else Text(canvas, "FLOOR " + N(r.Floor, "F0") + " DBFS", barX, barY + barH + 1.5f * f, SKTextAlign.Left, f, Dim);
        Text(canvas, "RELATIVE TO STRONGEST SAMPLED NOTE (DB)", barX + barW + 3 * f, barY + barH * .85f, SKTextAlign.Left, f, Ink, W - barX - barW - 5 * f);
        Text(canvas, "COLORS SCALED WITHIN THIS PROBE", barX + barW + 3 * f, barY + barH * .85f + 1.4f * f, SKTextAlign.Left, f, Dim, W - barX - barW - 5 * f);
        float my = gridBottom + 6.5f * f;
        Text(canvas, meta, left, my, SKTextAlign.Left, f, Dim, W - left - 2 * f);
        string strongest = r.StrongestIndex >= 0 && double.IsFinite(r.Strongest) ? $"STRONGEST SAMPLED NOTE {r.Notes[r.StrongestIndex].Name} AT {N(r.Strongest, "F1")} DBFS, " : "";
        Text(canvas, strongest + $"MIN/MAX IN DB, FLOOR {N(r.Floor, "F0")} DBFS. " + context, left, my + 1.4f * f, SKTextAlign.Left, f, Dim, W - left - 2 * f);
        if (warning != null) Text(canvas, warning, left, my + 2.8f * f, SKTextAlign.Left, f, Warn, W - left - 2 * f);
    }
}
