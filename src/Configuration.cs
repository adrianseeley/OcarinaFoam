using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;

public static class Configuration
{
    public static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static string Serialize(object value) => JsonSerializer.Serialize(value, new JsonSerializerOptions { IncludeFields = true });

    // Every public field must be present in the file, recursively; no silent defaults.
    static void RequireKeys(JsonElement json, Type type, string path)
    {
        if (json.ValueKind == JsonValueKind.Array && type.IsArray && IsConfigClass(type.GetElementType()))
        {
            int i = 0;
            foreach (JsonElement item in json.EnumerateArray()) RequireKeys(item, type.GetElementType(), path + "[" + i++ + "]");
            return;
        }
        if (!IsConfigClass(type) || json.ValueKind != JsonValueKind.Object) return;
        foreach (var field in type.GetFields())
        {
            string where = path.Length == 0 ? field.Name : path + "." + field.Name;
            if (!json.TryGetProperty(field.Name, out JsonElement value)) throw new Exception("config.json is missing required key '" + where + "'.");
            RequireKeys(value, field.FieldType, where);
        }
    }
    static bool IsConfigClass(Type t) => t.IsClass && t != typeof(string) && !t.IsArray && t.Namespace == null && t != typeof(JsonElement);

    public static Config Load(string path, bool strict = true)
    {
        string text = File.ReadAllText(path);
        if (strict) { using JsonDocument document = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }); RequireKeys(document.RootElement, typeof(Config), ""); }
        // The built copy is read for its physics only; renderer, audio and report always come from the live config.
        if (!strict)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(text)!.AsObject();
            foreach (string live in new[] { "watcher", "renderer", "audio", "report" }) node.Remove(live);
            text = node.ToJsonString();
        }
        Config c = JsonSerializer.Deserialize<Config>(text, Json) ?? throw new Exception("Empty config.");
        if (c.worldPaddingMillimeters == null || (strict && c.renderer == null) || c.bodyDistanceRefinement == null || c.probes == null)
            throw new Exception("Config requires worldPaddingMillimeters, renderer, bodyDistanceRefinement and probes.");
        Positive(c.processorCount, "processorCount"); Positive(c.backgroundCellSizeMillimeters, "backgroundCellSizeMillimeters");
        Positive(c.deltaTSeconds, "deltaTSeconds"); Positive(c.endTimeSeconds, "endTimeSeconds");
        Positive(c.fieldWriteIntervalTimeSteps, "fieldWriteIntervalTimeSteps"); Positive(c.probeWriteIntervalTimeSteps, "probeWriteIntervalTimeSteps");
        Positive(c.ambientPressureHectopascals, "ambientPressureHectopascals"); Positive(c.initialTemperatureCelsius + 273.15, "absolute temperature");
        Positive(c.inletVelocityMetersPerSecond, "inletVelocityMetersPerSecond");
        Positive(c.farFieldRelaxationLengthMeters, "farFieldRelaxationLengthMeters");
        Positive(c.acousticDampingTargetFrequencyHz, "acousticDampingTargetFrequencyHz");
        Positive(c.acousticDampingThicknessMillimeters, "acousticDampingThicknessMillimeters");
        Positive(c.acousticDampingStrengthMultiplier, "acousticDampingStrengthMultiplier");
        Nonnegative(c.acousticDampingClearanceMillimeters, "acousticDampingClearanceMillimeters");
        foreach (var f in typeof(Padding).GetFields()) Nonnegative((double)Convert.ToDouble(f.GetValue(c.worldPaddingMillimeters)), f.Name);
        if (c.worldPaddingMillimeters.yMin != 0) throw new Exception("The inlet lies on y=0; yMin padding must be zero.");
        foreach (int level in new[] { c.surfaceRefinementMinLevel, c.surfaceRefinementMaxLevel, c.featureRefinementLevel })
            if (level < 0 || level > 12) throw new Exception("Refinement levels must be 0..12.");
        if (c.surfaceRefinementMinLevel > c.surfaceRefinementMaxLevel) throw new Exception("Minimum refinement exceeds maximum.");
        Positive(c.nCellsBetweenLevels, "nCellsBetweenLevels");
        if (c.featureIncludedAngleDegrees <= 0 || c.featureIncludedAngleDegrees > 180) throw new Exception("Feature angle must be (0,180].");
        foreach (Refinement r in c.bodyDistanceRefinement)
        { Positive(r.distanceMillimeters, "refinement distance"); if (r.level < 0 || r.level > 12) throw new Exception("Distance refinement level must be 0..12."); }
        foreach (Probe probe in c.probes)
            if (probe.point == null || probe.point.Length != 3 || probe.point.Any(v => !double.IsFinite(v)) || string.IsNullOrWhiteSpace(probe.name) || probe.name.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '_'))
                throw new Exception("Each probe requires a simple alphanumeric name and three finite coordinates in metres.");
        if (!strict) return c;
        if (c.watcher is not ("systemd" or "supervisord")) throw new Exception("watcher must be \"systemd\" or \"supervisord\".");
        AudioConfig au = c.audio ?? throw new Exception("audio must not be null.");
        if (au.sampleRateHz < 8000 || au.sampleRateHz > 384000) throw new Exception("audio sampleRateHz must be 8000..384000.");
        Nonnegative(au.highPassHz, "highPassHz"); Nonnegative(au.fadeMilliseconds, "fadeMilliseconds");
        if (au.highPassHz >= au.sampleRateHz / 4.0) throw new Exception("highPassHz must be below a quarter of the audio rate.");
        if (!double.IsFinite(au.peakTargetDbfs) || au.peakTargetDbfs > 0) throw new Exception("peakTargetDbfs must be finite and at most 0.");
        Positive(au.kernelZeroCrossings, "kernelZeroCrossings"); Positive(au.kaiserBeta, "kaiserBeta");
        AudioPlots.Validate(au.plots);
        if (strict) ValidateReport(c.report ?? throw new Exception("report must not be null."), au);
        RenderConfig rconf = c.renderer;
        Positive(rconf.renderThreads, "renderThreads"); Positive(rconf.pollMilliseconds, "pollMilliseconds");
        if (rconf.plotWidth < 128 || rconf.plotHeight < 128) throw new Exception("Render tiles must be at least 128 pixels.");
        Positive(rconf.labelFontPixels, "labelFontPixels"); Positive(rconf.pointSizePixels, "pointSizePixels");
        Nonnegative(rconf.marginPixels, "marginPixels"); Nonnegative(rconf.cameraPaddingFraction, "cameraPaddingFraction");
        if (!(rconf.minimumAlpha >= 0 && rconf.minimumAlpha <= 1)) throw new Exception("renderer.minimumAlpha must be 0..1.");
        if (rconf.pngCompressionLevel < 0 || rconf.pngCompressionLevel > 9) throw new Exception("PNG compression must be 0..9.");
        if (!rconf.renderPressure && !rconf.renderVelocityMagnitude && !rconf.renderDensity && !rconf.renderTemperature) throw new Exception("Enable at least one render field.");
        if (!SkiaSharp.SKColor.TryParse(rconf.backgroundColor, out _) || !SkiaSharp.SKColor.TryParse(rconf.labelColor, out _)) throw new Exception("Invalid renderer color.");
        Renderer.ParseViews(rconf);
        double lastFrame = Math.Ceiling(c.endTimeSeconds / (c.deltaTSeconds * c.fieldWriteIntervalTimeSteps)) + 3;
        if (!double.IsFinite(lastFrame) || lastFrame > int.MaxValue) throw new Exception("Too many output frames.");
        return c;
    }
    static void ValidateReport(ReportConfig r, AudioConfig au)
    {
        if (r.slowdowns == null || r.slowdowns.Length == 0 || r.slowdowns.Any(n => n < 1) || r.slowdowns.Distinct().Count() != r.slowdowns.Length)
            throw new Exception("report.slowdowns must be a non-empty list of distinct integers >= 1.");
        if (r.framesPerSecond < 1 || r.framesPerSecond > 240) throw new Exception("report.framesPerSecond must be 1..240.");
        if (r.videoLongSidePixels < 64 || r.videoLongSidePixels > 8192) throw new Exception("report.videoLongSidePixels must be 64..8192.");
        if (r.videoCrf < 0 || r.videoCrf > 51) throw new Exception("report.videoCrf must be 0..51.");
        if (r.slowedAudioMinimumSampleRateHz < 1000 || r.slowedAudioMinimumSampleRateHz > 384000) throw new Exception("report.slowedAudioMinimumSampleRateHz must be 1000..384000.");
        foreach (int n in r.slowdowns)
            if (n > 1 && ReportMath.Intermediate(au.sampleRateHz, n) > 50_000_000L) throw new Exception($"report.slowdowns {n} has no practical exact ratio with audio.sampleRateHz {au.sampleRateHz}; use a factor sharing a large divisor with the sample rate.");
    }
    public static void Positive(double value, string name) { if (!double.IsFinite(value) || value <= 0) throw new Exception(name + " must be finite and positive."); }
    public static void Nonnegative(double value, string name) { if (!double.IsFinite(value) || value < 0) throw new Exception(name + " must be finite and nonnegative."); }
    public static string Fingerprint(Config c)
    {
        // Renderer controls are read live on restart; physics is frozen in foam/config.json.
        string text = JsonSerializer.Serialize(c, Json);
        using var document = JsonDocument.Parse(text);
        string physical = string.Join("\n", document.RootElement.EnumerateObject().Where(x => x.Name is not ("watcher" or "renderer" or "audio" or "report")).Select(x => x.ToString()));
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(physical)));
    }
    public static Config Built(string root)
    {
        if (!File.Exists(Path.Combine(Paths.Foam(root), ".built"))) throw new Exception("Case is not fully built. Run ocarina build DIR first.");
        Config current = Load(Path.Combine(root, "config.json"));
        Config built = Load(Path.Combine(Paths.Foam(root), "config.json"), false);
        if (Fingerprint(current) != Fingerprint(built)) throw new Exception("Physics config changed since build. Use a fresh case directory and build it.");
        built.watcher = current.watcher; built.renderer = current.renderer; built.audio = current.audio; built.report = current.report;
        return built;
    }
}
