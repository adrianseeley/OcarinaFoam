using System.IO.Compression;
using System.Net;
using System.Text;

// ocarina report DIR: stop services, rebuild audio, render videos/wavs, write report.html and zip the work product.
public static class Report
{
    const int VideoFps = 60;
    const int VideoLongSide = 1024;
    static readonly (int Factor, int Rate)[] Slowdowns = { (10, 16000), (100, 16000), (1000, 8000) };

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
        string[] videos = BuildVideos(root);
        string html = Path.Combine(root, "report.html");
        File.WriteAllText(html, Html(root, probes, videos), new UTF8Encoding(false));
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
            File.Copy(source, Path.Combine(dir, name + ".wav"));
            foreach (var (factor, rate) in Slowdowns)
            {
                Console.WriteLine($"wav {name} {factor}x slower");
                // Relabelling the sample rate slows playback (and drops pitch); resample to a rate players accept.
                Ffmpeg(root, $"report-wav-{name}-x{factor}.log", "-i", source, "-af", $"asetrate={sourceRate / (double)factor:R},aresample={rate}",
                    "-c:a", "pcm_s16le", Path.Combine(dir, $"{name}_x{factor}.wav"));
            }
        }
    }

    static string[] BuildVideos(string root)
    {
        string renders = Path.Combine(root, "renders");
        if (!Directory.Exists(renders)) return Array.Empty<string>();
        string dir = Path.Combine(root, "videos");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        var made = new List<string>();
        foreach (string field in Directory.GetDirectories(renders).OrderBy(d => d, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(field);
            if (!Directory.EnumerateFiles(field, "*.png").Any()) continue;
            Directory.CreateDirectory(dir);
            Console.WriteLine("video " + name);
            string scale = $"scale='if(gte(iw,ih),{VideoLongSide},-2)':'if(gte(iw,ih),-2,{VideoLongSide})':flags=lanczos";
            Ffmpeg(root, $"report-video-{name}.log", "-framerate", VideoFps.ToString(), "-pattern_type", "glob", "-i", Path.Combine(field, "*.png"),
                "-vf", scale, "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p", "-movflags", "+faststart", Path.Combine(dir, name + ".mp4"));
            made.Add(name);
        }
        return made.ToArray();
    }

    static string E(string text) => WebUtility.HtmlEncode(text);
    static string Url(string relative) => string.Join("/", relative.Split('/').Select(Uri.EscapeDataString));

    static string Html(string root, string[] probes, string[] videos)
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
         .Append("<style>body{font-family:sans-serif;max-width:1100px;margin:2em auto;padding:0 1em}img,video{max-width:100%}pre{background:#eee;padding:.5em;overflow:auto;max-height:30em}audio{display:block;margin:.3em 0}</style>\n")
         .Append("</head><body>\n<h1>").Append(E(title)).Append("</h1>\n<p>Case: ").Append(E(root)).Append("<br>Generated UTC ")
         .Append(DateTime.UtcNow.ToString("u")).Append("</p>\n");
        Pre("config.json", Path.Combine(root, "config.json"));
        Pre("Preflight", Path.Combine(Paths.Foam(root), "preflight.txt"));
        Pre("Mesh summary", Path.Combine(Paths.Foam(root), "meshSummary.txt"));
        Img("previews/layout.png");

        h.Append("<h2>Videos</h2>\n");
        if (videos.Length == 0) h.Append("<p>None.</p>\n");
        foreach (string v in videos)
            h.Append("<h3>").Append(E(v)).Append("</h3><video controls loop preload=\"metadata\" src=\"").Append(Url("videos/" + v + ".mp4")).Append("\"></video>\n");

        h.Append("<h2>Audio</h2>\n");
        if (probes.Length == 0) h.Append("<p>None.</p>\n");
        foreach (string p in probes)
        {
            h.Append("<h3>").Append(E(p)).Append("</h3>\n<p>Original</p><audio controls preload=\"none\" src=\"").Append(Url("wav/" + p + ".wav")).Append("\"></audio>\n");
            foreach (var (factor, _) in Slowdowns)
                h.Append("<p>").Append(factor).Append("x slower (pitch drops; distorted on purpose)</p><audio controls preload=\"none\" src=\"")
                 .Append(Url($"wav/{p}_x{factor}.wav")).Append("\"></audio>\n");
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
        if (parts[0].StartsWith(".foam-build-")) return true;
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
