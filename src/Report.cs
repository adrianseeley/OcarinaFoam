using System.IO.Compression;
using System.Net;
using System.Text;

// ocarina report DIR: stop services, rebuild audio, render videos/wavs, write report.html and zip the work product.
public static class Report
{
    const int PresentedFps = 30;
    const int VideoLongSide = 1024;
    static readonly int[] Slowdowns = { 1, 2, 3, 4, 5, 10, 20, 30, 40, 50, 100, 200, 300, 400, 500, 1000 };
    // Slowing by N moves content down to (rate / 2N); keep the output rate high enough to hold it, but playable.
    static int OutputRate(int sourceRate, int factor) => factor >= 100 ? 8000 : Math.Max(16000, sourceRate / factor);

    public static void Run(string root)
    {
        if (Commands.Run("ffmpeg", root, new[] { "-version" }, check: false).ExitCode != 0) throw new Exception("ffmpeg is required: sudo apt-get install ffmpeg");
        try { Services.StopAll(root); }
        catch (Exception e) { Console.WriteLine("WARNING could not stop services: " + e.Message); }

        Config config = Configuration.Built(root);
        string probeDir = Path.Combine(Paths.Foam(root), "postProcessing", "acousticProbes");
        bool hasAudio = Directory.Exists(probeDir);
        if (hasAudio) Audio.Run(root); else Console.WriteLine("WARNING no probe output; skipping audio.");

        string[] probes = hasAudio ? config.probes.Select(p => p.name).ToArray() : Array.Empty<string>();
        BuildWavs(root, probes, config.audio.sampleRateHz);
        string[] fields = BuildVideos(root, config, probes);
        string html = Path.Combine(root, "report.html");
        File.WriteAllText(html, Html(root, probes, fields), new UTF8Encoding(false));
        Console.WriteLine("wrote " + html);
        string zip = Zip(root);
        Console.WriteLine("wrote " + zip);
    }

    static void Ffmpeg(string root, string log, params string[] args) =>
        Commands.Run("ffmpeg", root, new[] { "-hide_banner", "-nostdin", "-y" }.Concat(args).ToArray(), Path.Combine(Paths.Logs(root), log));

    static void BuildWavs(string root, string[] probes, int sourceRate)
    {
        string dir = Path.Combine(root, "wav");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        if (probes.Length == 0) return;
        Directory.CreateDirectory(dir);
        foreach (string name in probes)
        {
            string source = Path.Combine(root, "audio", name + ".wav");
            foreach (int factor in Slowdowns)
            {
                string target = Path.Combine(dir, $"{name}_x{factor}.wav");
                if (factor == 1) { File.Copy(source, target); continue; }
                int rate = OutputRate(sourceRate, factor);
                Console.WriteLine($"wav {name} {factor}x slower");
                // Relabelling the sample rate slows playback (and drops pitch); resample to a rate players accept.
                Ffmpeg(root, $"report-wav-{name}-x{factor}.log", "-i", source, "-af", $"asetrate={sourceRate / (double)factor:R},aresample={rate}",
                    "-c:a", "pcm_s16le", target);
            }
        }
    }

    // One MKV per slowdown: a titled video track per field and a titled audio track per probe, all on the same slowed timeline.
    // Frame k of a field is simulation time k * interval. Each presented tick shows the nearest rendered frame, so frames are
    // dropped when the slowdown is small and held when it is large.
    static string[] BuildVideos(string root, Config config, string[] probes)
    {
        string dir = Path.Combine(root, "videos");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        string renders = Path.Combine(root, "renders");
        var frames = new List<(string Name, string[] Files, int[] Index)>();
        if (Directory.Exists(renders))
            foreach (string field in Directory.GetDirectories(renders).OrderBy(d => d, StringComparer.Ordinal))
            {
                var found = new List<(int Index, string File)>();
                foreach (string file in Directory.EnumerateFiles(field, "*.png"))
                    if (int.TryParse(Path.GetFileNameWithoutExtension(file), out int index)) found.Add((index, file));
                if (found.Count == 0) continue;
                found.Sort((x, y) => x.Index.CompareTo(y.Index));
                frames.Add((Path.GetFileName(field), found.Select(f => f.File).ToArray(), found.Select(f => f.Index).ToArray()));
            }
        if (frames.Count == 0 && probes.Length == 0) return Array.Empty<string>();
        Directory.CreateDirectory(dir);
        string temporary = Path.Combine(root, ".report-tmp");
        if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        Directory.CreateDirectory(temporary);
        try
        {
            double interval = config.fieldWriteIntervalTimeSteps * config.deltaTSeconds;
            double span = frames.Count == 0 ? 0 : (frames.Max(f => f.Index[^1]) + 1) * interval;
            foreach (int factor in Slowdowns)
            {
                Console.WriteLine($"video {factor}x slower");
                var args = new List<string> { "-hide_banner", "-nostdin", "-y" };
                for (int v = 0; v < frames.Count; v++)
                {
                    string list = Path.Combine(temporary, $"x{factor}-{v}.txt");
                    WriteList(list, frames[v].Files, frames[v].Index, interval, factor, span);
                    args.AddRange(new[] { "-f", "concat", "-safe", "0", "-i", list });
                }
                foreach (string p in probes) args.AddRange(new[] { "-i", Path.Combine(root, "wav", $"{p}_x{factor}.wav") });
                for (int v = 0; v < frames.Count; v++) args.AddRange(new[] { "-map", $"{v}:v" });
                for (int a = 0; a < probes.Length; a++) args.AddRange(new[] { "-map", $"{frames.Count + a}:a" });
                for (int v = 0; v < frames.Count; v++) args.AddRange(new[] { $"-metadata:s:v:{v}", "title=" + frames[v].Name });
                for (int a = 0; a < probes.Length; a++) args.AddRange(new[] { $"-metadata:s:a:{a}", "title=" + probes[a] });
                string scale = $"scale='if(gte(iw,ih),{VideoLongSide},-2)':'if(gte(iw,ih),-2,{VideoLongSide})':flags=lanczos,format=yuv420p";
                if (frames.Count > 0)
                    args.AddRange(new[] { "-vf", scale, "-r", PresentedFps.ToString(), "-fps_mode", "cfr", "-c:v", "libx264", "-preset", "medium", "-crf", "16" });
                if (probes.Length > 0) args.AddRange(new[] { "-c:a", "flac" });
                args.AddRange(new[] { "-metadata", $"title={Path.GetFileName(root)} {factor}x slower", Path.Combine(dir, $"x{factor}.mkv") });
                Ffmpeg(root, $"report-video-x{factor}.log", args.ToArray());
            }
        }
        finally { Directory.Delete(temporary, true); }
        return frames.Select(f => f.Name).ToArray();
    }

    // Concat list of (frame, duration) runs covering factor * span seconds at exactly PresentedFps ticks per second.
    static void WriteList(string path, string[] files, int[] index, double interval, int factor, double span)
    {
        long ticks = (long)Math.Ceiling(span * factor * PresentedFps - 1e-9);
        var text = new StringBuilder();
        long runStart = 0, previousStart = 0; int current = -1;
        void Emit(int file, long from, long to)
        {
            // Rounded cumulative microseconds: no drift however many runs there are.
            long d = (long)Math.Round(to * 1e6 / PresentedFps) - (long)Math.Round(from * 1e6 / PresentedFps);
            text.Append("file '").Append(files[file].Replace("'", "'\\''")).Append("'\nduration ").Append((d / 1e6).ToString("F6", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        }
        int cursor = 0;
        for (long t = 0; t < ticks; t++)
        {
            double wanted = t / (double)PresentedFps / factor / interval;
            while (cursor + 1 < index.Length && Math.Abs(index[cursor + 1] - wanted) <= Math.Abs(index[cursor] - wanted)) cursor++;
            if (cursor != current)
            {
                if (current >= 0) Emit(current, runStart, t);
                current = cursor; runStart = t;
            }
        }
        if (current >= 0)
        {
            Emit(current, runStart, ticks);
            // The concat demuxer ignores the last duration, so repeat the final file.
            text.Append("file '").Append(files[current].Replace("'", "'\\''")).Append("'\n");
        }
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    static string E(string text) => WebUtility.HtmlEncode(text);
    static string Url(string relative) => string.Join("/", relative.Split('/').Select(Uri.EscapeDataString));

    static string Html(string root, string[] probes, string[] fields)
    {
        var h = new StringBuilder();
        void Pre(string title, string path)
        {
            if (!File.Exists(path)) return;
            h.Append("<h3>").Append(E(title)).Append("</h3><pre>").Append(E(File.ReadAllText(path))).Append("</pre>\n");
        }
        void Img(string relative)
        {
            if (File.Exists(Path.Combine(root, relative))) h.Append("<p><img src=\"").Append(Url(relative)).Append("\" alt=\"").Append(E(relative)).Append("\"></p>\n");
        }
        string title = Path.GetFileName(root);
        h.Append("<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\"><title>").Append(E(title)).Append(" report</title>\n")
         .Append("<style>body{font-family:sans-serif;margin:2em;}pre{background:#eee;padding:.5em;overflow:auto;max-height:30em;max-width:1100px}</style>\n")
         .Append("</head><body>\n<h1>").Append(E(title)).Append("</h1>\n<p>Case: ").Append(E(root)).Append("<br>Generated UTC ")
         .Append(DateTime.UtcNow.ToString("u")).Append("</p>\n");
        Pre("config.json", Path.Combine(root, "config.json"));
        Pre("Preflight", Path.Combine(Paths.Foam(root), "preflight.txt"));
        Pre("Mesh summary", Path.Combine(Paths.Foam(root), "meshSummary.txt"));
        Img("previews/layout.png");

        bool anyVideo = Directory.Exists(Path.Combine(root, "videos"));
        h.Append("<h2>Videos</h2>\n");
        if (!anyVideo) h.Append("<p>None.</p>\n");
        else
        {
            h.Append("<p>One file per slowdown. Video tracks: ").Append(E(fields.Length == 0 ? "none" : string.Join(", ", fields)))
             .Append(". Audio tracks: ").Append(E(probes.Length == 0 ? "none" : string.Join(", ", probes)))
             .Append(". Choose tracks in the player (VLC: Video / Audio menus). Nominal ").Append(PresentedFps).Append(" frames per second presented.</p>\n<p>");
            foreach (int factor in Slowdowns)
                h.Append("<a href=\"").Append(Url($"videos/x{factor}.mkv")).Append("\">").Append(factor == 1 ? "1x" : factor + "x slower").Append("</a> ");
            h.Append("</p>\n");
        }

        h.Append("<h2>Audio</h2>\n");
        if (probes.Length == 0) h.Append("<p>None.</p>\n");
        else
        {
            h.Append("<p>WAV files; slowed versions drop pitch on purpose.</p>\n<table border=\"1\" cellpadding=\"3\" cellspacing=\"0\"><tr><th>probe</th>");
            foreach (int factor in Slowdowns) h.Append("<th>").Append(factor).Append("x</th>");
            h.Append("</tr>\n");
            foreach (string p in probes)
            {
                h.Append("<tr><td>").Append(E(p)).Append("</td>");
                foreach (int factor in Slowdowns)
                    h.Append("<td><a href=\"").Append(Url($"wav/{p}_x{factor}.wav")).Append("\">wav</a></td>");
                h.Append("</tr>\n");
            }
            h.Append("</table>\n");
        }
        foreach (string p in probes)
        {
            h.Append("<h3>").Append(E(p)).Append("</h3>\n");
            Img($"audio/{p}/plots/waveform.png");
            Img($"audio/{p}/plots/spectrum.png");
            Img($"audio/{p}/plots/punch.png");
        }
        Pre("Audio log", Path.Combine(root, "audio", "audio.log"));
        h.Append("</body></html>\n");
        return h.ToString();
    }

    // Field data (processor directories, numeric time directories) and per-frame render PNGs (replaced by videos) are left out.
    static bool Excluded(string relative)
    {
        string[] parts = relative.Split('/');
        string file = parts[^1];
        if (relative == "report.zip" || file.EndsWith(".lock") || file.EndsWith(".tmp")) return true;
        if (parts[0].StartsWith(".foam-build-") || parts[0] == ".report-tmp") return true;
        if (parts[0] == "foam" && parts.Length > 2)
        {
            string d = parts[1];
            if (d.StartsWith("processor")) return true;
            if (d != "0" && double.TryParse(d, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)) return true;
        }
        if (parts[0] == "renders" && parts.Length > 2 && file.EndsWith(".png")) return true;
        return false;
    }

    static string Zip(string root)
    {
        string zip = Path.Combine(root, "report.zip"), temporary = zip + ".tmp";
        File.Delete(temporary);
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            string top = Path.GetFileName(root);
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (string path in Directory.EnumerateFiles(root, "*", options))
            {
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (Excluded(relative)) continue;
                // Already-compressed media gains nothing from deflate.
                var level = relative.EndsWith(".mp4") || relative.EndsWith(".png") ? CompressionLevel.NoCompression : CompressionLevel.Fastest;
                try { archive.CreateEntryFromFile(path, top + "/" + relative, level); }
                catch (FileNotFoundException) { }
            }
        }
        File.Move(temporary, zip, true);
        return zip;
    }
}
