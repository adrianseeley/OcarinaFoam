using System.Globalization;
using SkiaSharp;

public class AudioPlotResult
{
    public string SpectrumPath, PunchPath;
    public NoteSpectrum Notes;
    public double BinHz;
    public string Warning;
}

// spectrum.png (log-frequency FFT with note lines) and punch.png (octave by pitch-class grid) for one probe.
public static class AudioPlots
{
    const int PngLevel = 6;
    const double MaxPixels = 64e6;
    static readonly SKColor Background = new SKColor(5, 5, 5), Ink = new SKColor(250, 250, 250), Dim = new SKColor(150, 150, 150),
        Grid = new SKColor(48, 48, 48), NoteLine = new SKColor(70, 70, 70), OctaveLine = new SKColor(130, 130, 130), Line = new SKColor(255, 210, 60),
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
    static float PunchBottom(float f) => 17 * f;

    public static void Validate(AudioPlotConfig p)
    {
        if (p == null) throw new Exception("audio.plots must not be null.");
        Configuration.Positive(p.concertAHz, "plots.concertAHz"); Configuration.Positive(p.maximumFrequencyHz, "plots.maximumFrequencyHz");
        if (p.minimumOctave < 0 || p.maximumOctave > 9 || p.minimumOctave > p.maximumOctave) throw new Exception("plots octaves must satisfy 0 <= minimumOctave <= maximumOctave <= 9.");
        if (!double.IsFinite(p.displayFloorDbfs) || p.displayFloorDbfs >= 0) throw new Exception("plots.displayFloorDbfs must be finite and negative.");
        if (p.labelFontPixels < 8 || p.labelFontPixels > 80) throw new Exception("plots.labelFontPixels must be 8..80.");
        foreach (int d in new[] { p.spectrumWidth, p.spectrumHeight, p.punchWidth, p.punchHeight })
            if (d < 400 || d > 16384) throw new Exception("plot dimensions must be 400..16384 pixels.");
        if ((double)p.spectrumWidth * p.spectrumHeight > MaxPixels || (double)p.punchWidth * p.punchHeight > MaxPixels) throw new Exception("plot images are too large.");
        Note[] notes = MusicalNotes.Generate(p.concertAHz, p.minimumOctave, p.maximumOctave);
        if (p.maximumFrequencyHz <= notes[0].Hz) throw new Exception("plots.maximumFrequencyHz must exceed the lowest note.");
        float f = p.labelFontPixels;
        float plotW = p.spectrumWidth - SpectrumLeft(f) - SpectrumRight(f), plotH = p.spectrumHeight - SpectrumTop(f) - SpectrumBottom(f);
        double semitones = 12 * Math.Log2(Math.Min(p.maximumFrequencyHz, notes[^1].Hz) / notes[0].Hz);
        // Note labels alternate between two rows, so same-row labels sit two semitones apart.
        if (plotH < 10 * f || plotW <= 0 || 2 * plotW / Math.Max(semitones, 1) < LineFont.Width("C" + MusicalNotes.Sharp + "9", f) + 0.4f * f)
            throw new Exception("plots spectrum size is too small for the label font; enlarge spectrumWidth/Height or reduce labelFontPixels.");
        int rows = p.maximumOctave - p.minimumOctave + 1;
        float cellW = (p.punchWidth - PunchLeft(f) - PunchRight(f)) / 12, cellH = (p.punchHeight - PunchTop(f) - PunchBottom(f)) / rows;
        if (cellW < LineFont.Width("BELOW FLOOR", f) + 0.4f * f || cellH < 4.8f * f)
            throw new Exception("plots punch size is too small for the label font; enlarge punchWidth/Height or reduce labelFontPixels.");
    }

    public static AudioPlotResult Make(string name, double[] samples, double fsOut, double fsIn, double[] fftDb, double binHz, AudioConfig a, string folder)
    {
        AudioPlotConfig p = a.plots;
        NoteSpectrum notes = NoteAnalysis.Analyze(samples, fsOut, fsIn, p);
        var result = new AudioPlotResult { Notes = notes, BinHz = binHz };
        string dir = Directory.CreateDirectory(Path.Combine(folder, "plots")).FullName;
        result.SpectrumPath = Path.Combine(dir, "spectrum.png"); result.PunchPath = Path.Combine(dir, "punch.png");
        string context = $"PROCESSED AUDIO: RESAMPLED TO {N(fsOut, "F0")} HZ, HIGH-PASS {N(a.highPassHz)} HZ, FADE {N(a.fadeMilliseconds)} MS, GAIN APPLIED" +
            (fsIn / 2 < fsOut / 2 ? $", NATIVE NYQUIST {N(fsIn / 2, "F0")} HZ" : "");
        string meta = $"N={notes.SampleCount} SAMPLES, T={N(notes.Duration)} S, 1/T={N(1 / Math.Max(notes.Duration, 1e-300))} HZ, FFT BIN {N(binHz)} HZ, HANN, A4 = {N(p.concertAHz)} HZ";
        if (notes.Unresolved > 0) result.Warning = $"SHORT RECORD: ADJACENT NOTES MAY NOT BE RESOLVED ({notes.Unresolved} OF {notes.ValidCount} NOTES CLOSER THAN 2/T)";
        Render(result.SpectrumPath, name, p.spectrumWidth, p.spectrumHeight, c => DrawSpectrum(c, p, name, notes, fftDb, binHz, meta, context, result.Warning));
        Render(result.PunchPath, name, p.punchWidth, p.punchHeight, c => DrawPunch(c, p, name, notes, meta, context, result.Warning));
        return result;
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

    static void DrawSpectrum(SKCanvas canvas, AudioPlotConfig p, string name, NoteSpectrum r, double[] db, double binHz, string meta, string context, string warning)
    {
        float f = p.labelFontPixels, W = p.spectrumWidth, H = p.spectrumHeight;
        float left = SpectrumLeft(f), right = W - SpectrumRight(f), top = SpectrumTop(f), bottom = H - SpectrumBottom(f);
        Text(canvas, name + " - SPECTRUM", left, 2.4f * f, SKTextAlign.Left, 2 * f, Ink, W - left - 2 * f);
        Text(canvas, meta, left, 4.2f * f, SKTextAlign.Left, f, Dim, W - left - 2 * f);
        double yMin = p.displayFloorDbfs, yMax = 0;
        foreach (double v in db) if (double.IsFinite(v)) yMax = Math.Max(yMax, v);
        yMax = Math.Ceiling(yMax / 10) * 10;
        float Y(double v) => bottom - (bottom - top) * (float)((Math.Clamp(double.IsNaN(v) ? yMin : v, yMin, yMax) - yMin) / (yMax - yMin));
        double step = (yMax - yMin) > 150 ? 20 : 10;
        using var gridPaint = new SKPaint { Color = Grid, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        for (double v = Math.Ceiling(yMin / step) * step; v <= yMax; v += step)
        {
            canvas.DrawLine(left, Y(v), right, Y(v), gridPaint);
            Text(canvas, N(v, "F0"), left - 0.6f * f, Y(v) + 0.5f * f, SKTextAlign.Right, f, Dim);
        }
        Text(canvas, "DBFS", left - 0.6f * f, top - 0.5f * f, SKTextAlign.Right, f, Dim);
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
            float labelBase = (row++ % 2 == 0 ? 6.2f : 7.8f) * f;
            canvas.DrawLine(x, labelBase + 0.15f * f, x, top, n.PitchClass == 0 ? octavePaint : notePaint);
            Text(canvas, n.Name, x, labelBase, SKTextAlign.Center, f, n.PitchClass == 0 ? Ink : Dim);
        }
        foreach (double tick in new double[] { 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 })
        {
            if (tick < r.FMin || tick > r.FMax) continue;
            canvas.DrawLine(X(tick), bottom, X(tick), bottom + 0.4f * f, frame);
            Text(canvas, N(tick, "F0"), X(tick), bottom + 1.7f * f, SKTextAlign.Center, f, Dim);
        }

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
        canvas.Save(); canvas.ClipRect(new SKRect(left, top, right, bottom));
        using var linePaint = new SKPaint { Color = Line, StrokeWidth = 2.5f, Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeJoin = SKStrokeJoin.Round };
        canvas.DrawPath(path, linePaint);
        canvas.Restore();
        if (db.All(v => double.IsNegativeInfinity(v) || double.IsNaN(v)))
            Text(canvas, "SILENT: NO SPECTRAL ENERGY", (left + right) / 2, (top + bottom) / 2, SKTextAlign.Center, 1.5f * f, Warn, right - left);
    }

    static SKColor Contrast(SKColor c) => 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue > 140 ? new SKColor(0, 0, 0) : new SKColor(255, 255, 255);

    static void DrawPunch(SKCanvas canvas, AudioPlotConfig p, string name, NoteSpectrum r, string meta, string context, string warning)
    {
        float f = p.labelFontPixels, W = p.punchWidth, H = p.punchHeight;
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
                using (var border = new SKPaint { Color = Background, Style = SKPaintStyle.Stroke, StrokeWidth = 2 }) canvas.DrawRect(rect, border);
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
        float ay = gridBottom + 10.9f * f;
        for (int line = 0; line < 3; line++)
        {
            for (int col = 0; col < 4; col++)
            {
                int pc = line * 4 + col;
                Text(canvas, MusicalNotes.Header(pc) + " = " + (MusicalNotes.Aliases(pc) is { Length: > 0 } al ? string.Join(" ", al) : "NONE"), left + (W - left - 2 * f) * col / 4, ay + 1.4f * f * line, SKTextAlign.Left, f, Dim, (W - left - 2 * f) / 4 - f);
            }
        }
        Text(canvas, "ALIASES MAY CROSS OCTAVE BOUNDARIES: B" + MusicalNotes.Sharp + "3 = C4, C" + MusicalNotes.Flat + "4 = B3", left, ay + 1.4f * f * 3, SKTextAlign.Left, f, Dim, W - left - 2 * f);
    }
}
