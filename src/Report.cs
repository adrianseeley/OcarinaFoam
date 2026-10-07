using System.IO.Compression;
using System.Net;
using System.Text;

// ocarina report DIR: stop services, rebuild audio, render videos/wavs, write report.html and zip the work product.
public static class Report
{
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
        BuildWavs(root, probes, config);
        string[] fields = BuildVideos(root, config);
        string html = Path.Combine(root, "report.html");
        File.WriteAllText(html, Html(root, probes, fields, config.report), new UTF8Encoding(false));
        Console.WriteLine("wrote " + html);
        string zip = Zip(root);
        Console.WriteLine("wrote " + zip);
    }

    static void Ffmpeg(string root, string log, params string[] args) =>
        Commands.Run("ffmpeg", root, new[] { "-hide_banner", "-nostdin", "-nostats", "-loglevel", "warning", "-y" }.Concat(args).ToArray(), Path.Combine(Paths.Logs(root), log));

    static void BuildWavs(string root, string[] probes, Config config)
    {
        string dir = Path.Combine(root, "wav");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        if (probes.Length == 0) return;
        Directory.CreateDirectory(dir);
        AudioConfig au = config.audio; ReportConfig rc = config.report;
        string first = Path.Combine(root, "audio", probes[0] + ".wav");
        double seconds = (new FileInfo(first).Length - 44) / 3.0 / au.sampleRateHz;
        double total = rc.slowdowns.Where(n => n > 1).Sum(n => ReportMath.SlowedBytes(seconds, ReportMath.SlowedRate(au, rc, n), n)) * probes.Length;
        Console.WriteLine($"slowed WAVs: about {total / 1e9:F1} GB ; trim report.slowdowns to reduce.");
        var jobs = (from name in probes from factor in rc.slowdowns select (name, factor)).ToList();
        // ffmpeg's resampler and the phase vocoder are single-threaded, so run several at once.
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, job =>
        {
            (string name, int factor) = job;
            string source = Path.Combine(root, "audio", name + ".wav");
            string target = Path.Combine(dir, $"{name}_x{factor}.wav");
            if (factor == 1) { File.Copy(source, target); return; }
            Console.WriteLine($"wav {name} {factor}x slower");
            Ffmpeg(root, $"report-wav-{name}-x{factor}.log", "-i", source, "-af", SlowFilter(au, rc, factor), "-c:a", "pcm_s16le", "-rf64", "auto", target);
        });
    }

    // Slow by N without touching pitch handling: relabel the sample rate, then resample to a rate players accept.
    static string SlowFilter(AudioConfig au, ReportConfig rc, int factor)
    {
        var chain = new List<string>();
        if (ReportMath.Intermediate(au.sampleRateHz, factor) != au.sampleRateHz) chain.Add($"aresample={ReportMath.Intermediate(au.sampleRateHz, factor)}");
        chain.Add($"asetrate={ReportMath.Relabelled(au.sampleRateHz, factor)}");
        chain.Add($"aresample={ReportMath.SlowedRate(au, rc, factor)}");
        return string.Join(",", chain);
    }

    // One browser-friendly silent MP4 per rendered field and view (videos/FIELD/VIEW.mp4): every frame, in order, at framesPerSecond.
    static string[] BuildVideos(string root, Config config)
    {
        string dir = Path.Combine(root, "videos");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        string renders = Path.Combine(root, "renders");
        var fields = new List<(string Name, string[] Files)>();
        if (Directory.Exists(renders))
            foreach (string view in Directory.GetDirectories(renders, "*", SearchOption.AllDirectories).OrderBy(d => d, StringComparer.Ordinal))
            {
                if (Path.GetDirectoryName(Path.GetDirectoryName(view)) != renders) continue;
                var found = new List<(int Index, string File)>();
                foreach (string file in Directory.EnumerateFiles(view, "*.png"))
                    if (int.TryParse(Path.GetFileNameWithoutExtension(file), out int index)) found.Add((index, file));
                if (found.Count == 0) continue;
                found.Sort((x, y) => x.Index.CompareTo(y.Index));
                fields.Add((Path.GetFileName(Path.GetDirectoryName(view)) + "/" + Path.GetFileName(view), found.Select(f => f.File).ToArray()));
            }
        if (fields.Count == 0) return Array.Empty<string>();
        Directory.CreateDirectory(dir);
        string temporary = Path.Combine(root, ".report-tmp");
        if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        Directory.CreateDirectory(temporary);
        try
        {
            ReportConfig rc = config.report;
            foreach (var field in fields)
            {
                // Sequentially numbered links give ffmpeg's image2 demuxer a gapless sequence whatever the frame numbers were.
                string sequence = Path.Combine(temporary, field.Name);
                Directory.CreateDirectory(sequence);
                for (int i = 0; i < field.Files.Length; i++) File.CreateSymbolicLink(Path.Combine(sequence, $"{i:D8}.png"), Path.GetFullPath(field.Files[i]));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, field.Name))!);
                Console.WriteLine($"video {field.Name} ({field.Files.Length} frames)");
                string scale = $"scale='if(gte(iw,ih),{rc.videoLongSidePixels},-2)':'if(gte(iw,ih),-2,{rc.videoLongSidePixels})':flags=lanczos,format=yuv420p";
                Ffmpeg(root, $"report-video-{field.Name.Replace('/', '-')}.log",
                    "-framerate", rc.framesPerSecond.ToString(), "-i", Path.Combine(sequence, "%08d.png"),
                    "-vf", scale, "-an", "-fps_mode", "passthrough",
                    "-c:v", "libx264", "-profile:v", "high", "-preset", "slow", "-crf", rc.videoCrf.ToString(),
                    "-movflags", "+faststart",
                    "-metadata", $"title={Path.GetFileName(root)} {field.Name}",
                    Path.Combine(dir, field.Name + ".mp4"));
            }
        }
        finally { Directory.Delete(temporary, true); }
        return fields.Select(f => f.Name).ToArray();
    }

    static string E(string text) => WebUtility.HtmlEncode(text);
    static string Url(string relative) => string.Join("/", relative.Split('/').Select(Uri.EscapeDataString));

    static string Html(string root, string[] probes, string[] fields, ReportConfig rc)
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
        void Segments(string relative)
        {
            string dir = Path.Combine(root, relative);
            if (!Directory.Exists(dir)) return;
            foreach (string f in Directory.EnumerateFiles(dir, "*_of_*.png").OrderBy(x => int.Parse(Path.GetFileName(x).Split('_')[0])))
                Img(relative + "/" + Path.GetFileName(f));
        }
        string title = Path.GetFileName(root);
        h.Append("<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\"><title>").Append(E(title)).Append(" report</title>\n")
         .Append("<style>body{background:#050505;color:#FAFAFA;font-family:\"Inter\",\"Segoe UI\",system-ui,sans-serif;margin:0;padding:48px 6vw 96px;line-height:1.5;color-scheme:dark}\nh1{font-weight:300;font-size:42px;letter-spacing:.04em;margin:0 0 4px;border-bottom:2px solid #303030;padding-bottom:12px}\nh2{font-weight:300;font-size:28px;letter-spacing:.06em;text-transform:uppercase;margin:64px 0 12px;border-bottom:2px solid #303030;padding-bottom:6px}\nh3{font-weight:400;font-size:18px;color:#969696;letter-spacing:.08em;text-transform:uppercase;margin:32px 0 8px}\np,.meta{color:#969696;margin:6px 0}a{color:#969696}a:hover{color:#FAFAFA}\npre{background:#101010;border:1px solid #303030;color:#FAFAFA;padding:16px;overflow:auto;max-height:36em;font-size:14px}\nimg,video{display:block;width:100%;background:#050505;border:2px solid #303030;box-sizing:border-box}\n.videos{display:grid;grid-template-columns:repeat(auto-fit,minmax(900px,1fr));gap:24px}\ntable{border-collapse:collapse;display:block;overflow:auto;margin-top:12px}th,td{border:1px solid #303030;padding:8px 10px;text-align:center}\nth{background:#101010;color:#FAFAFA;font-weight:400;position:sticky;left:0}\naudio{width:260px;height:36px;display:block;margin:0 auto 2px}\n</style>\n")
         .Append("</head><body>\n<h1>").Append(E(title)).Append("</h1>\n<p>Case: ").Append(E(root)).Append("<br>Generated UTC ")
         .Append(DateTime.UtcNow.ToString("u")).Append("</p>\n");
        Pre("config.json", Path.Combine(root, "config.json"));
        Pre("Preflight", Path.Combine(Paths.Foam(root), "preflight.txt"));
        Pre("Mesh summary", Path.Combine(Paths.Foam(root), "meshSummary.txt"));
        string previews = Path.Combine(root, "previews");
        if (Directory.Exists(previews))
            foreach (string preview in Directory.GetFiles(previews, "layout_*.png").OrderBy(f => f, StringComparer.Ordinal)) Img("previews/" + Path.GetFileName(preview));

        h.Append("<h2>Videos</h2>\n");
        if (fields.Length == 0) h.Append("<p>None.</p>\n");
        else
        {
            h.Append("<p>Every rendered frame at ").Append(rc.framesPerSecond).Append(" frames per second, no audio.</p>\n");
            h.Append("<div class=\"videos\">\n");
            foreach (string field in fields)
                h.Append("<div><h3>").Append(E(field.Replace("/", " / "))).Append("</h3><video controls preload=\"metadata\" src=\"").Append(Url($"videos/{field}.mp4")).Append("\"></video></div>\n");
            h.Append("</div>\n");
        }

        h.Append("<h2>Audio</h2>\n");
        if (probes.Length == 0) h.Append("<p>None.</p>\n");
        else
        {
            h.Append("<p>Probes down the side, slowdown across the top. Slowed versions are the same samples played N times slower, so pitch falls with N, down to infrasound. Click a name to download.</p>\n")
             .Append("<table border=\"1\" cellpadding=\"3\" cellspacing=\"0\"><tr><th>probe</th>");
            foreach (int factor in rc.slowdowns) h.Append("<th>").Append(factor).Append("x</th>");
            h.Append("</tr>\n");
            foreach (string p in probes)
            {
                h.Append("<tr><th>").Append(E(p)).Append("</th>");
                foreach (int factor in rc.slowdowns)
                {
                    string url = Url($"wav/{p}_x{factor}.wav");
                    h.Append("<td><audio controls preload=\"none\" src=\"").Append(url).Append("\"></audio><br><a href=\"").Append(url).Append("\">wav</a></td>");
                }
                h.Append("</tr>\n");
            }
            h.Append("</table>\n");
        }
        if (probes.Length > 1)
        {
            h.Append("<h3>All probes</h3>\n");
            Segments("audio/plots/all_waveform");
            Img("audio/plots/all_spectrum.png");
        }
        foreach (string p in probes)
        {
            h.Append("<h3>").Append(E(p)).Append("</h3>\n");
            Segments($"audio/{p}/plots/waveform");
            Img($"audio/{p}/plots/spectrum.png");
            Img($"audio/{p}/plots/octave.png");
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
