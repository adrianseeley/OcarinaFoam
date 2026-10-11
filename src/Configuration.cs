using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;

public static class Configuration
{
    public static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new PlainDoubleConverter() }
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
        // The built copy is read for its physics only; points always come from the live config.
        if (!strict)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(text)!.AsObject();
            node.Remove("watcher");
            text = node.ToJsonString();
        }
        Config c = JsonSerializer.Deserialize<Config>(text, Json) ?? throw new Exception("Empty config.");
        if (c.worldPaddingMillimeters == null || c.refinement.bodyDistanceLevelsMillimeters == null || c.probes == null)
            throw new Exception("Config requires worldPaddingMillimeters, points, bodyDistanceRefinementLevelsMillimeters and probes.");
        Positive(c.processorCount, "processorCount"); Positive(c.refinement.backgroundCellSizeMillimeters, "refinement.backgroundCellSizeMillimeters");
        long delta = c.DeltaNs(), last = c.LastNs();
        if (delta <= 0) throw new Exception("deltaT must be positive.");
        if (c.points == null) throw new Exception("Config requires points.");
        Positive(c.KeepEvery(), "points.keepEvery");
        if (last % delta != 0) throw new Exception($"lastT ({c.lastT}) is not a whole number of deltaT ({c.deltaT}) steps, so the solver would never land on it.");
        if (c.Steps() % c.KeepEvery() != 0) throw new Exception($"lastT is {c.Steps()} steps, not a multiple of points.keepEvery ({c.KeepEvery()}): the last frame would never be written.");
        if (c.EndNs() >= 999_000_000_000) throw new Exception("lastT plus three guard write intervals must stay below 999 s: point frame names hold three digits of seconds.");
        Positive(c.physics.ambientPressureHectopascals, "physics.ambientPressureHectopascals"); Positive(c.physics.initialTemperatureCelsius + 273.15, "absolute temperature");
        Positive(c.physics.inletVelocityMetersPerSecond, "physics.inletVelocityMetersPerSecond");
        Positive(c.physics.farFieldRelaxationLengthMeters, "physics.farFieldRelaxationLengthMeters");
        Positive(c.physics.damping.targetFrequencyHz, "physics.damping.targetFrequencyHz");
        Positive(c.physics.damping.thicknessMillimeters, "physics.damping.thicknessMillimeters");
        Positive(c.physics.damping.strengthMultiplier, "physics.damping.strengthMultiplier");
        Nonnegative(c.physics.damping.clearanceMillimeters, "physics.damping.clearanceMillimeters");
        foreach (var f in typeof(Padding).GetFields()) Nonnegative((double)Convert.ToDouble(f.GetValue(c.worldPaddingMillimeters)), f.Name);
        if (c.worldPaddingMillimeters.yMin != 0) throw new Exception("The inlet lies on y=0; yMin padding must be zero.");
        foreach (int level in new[] { c.refinement.surface.minLevel, c.refinement.surface.maxLevel, c.refinement.feature.level })
            if (level < 0 || level > 12) throw new Exception("Refinement levels must be 0..12.");
        if (c.refinement.surface.minLevel > c.refinement.surface.maxLevel) throw new Exception("Minimum refinement exceeds maximum.");
        Positive(c.refinement.cellsBetweenLevels, "refinement.cellsBetweenLevels");
        if (c.refinement.feature.includedAngleDegrees <= 0 || c.refinement.feature.includedAngleDegrees > 180) throw new Exception("Feature angle must be (0,180].");
        double[] rings = c.refinement.bodyDistanceLevelsMillimeters;
        if (rings.Length > 12) throw new Exception("bodyDistanceRefinementLevelsMillimeters supports at most 12 levels.");
        for (int i = 0; i < rings.Length; i++)
        {
            Positive(rings[i], "bodyDistanceRefinementLevelsMillimeters entry");
            if (i > 0 && rings[i] >= rings[i - 1]) throw new Exception("bodyDistanceRefinementLevelsMillimeters must strictly decrease: entry N is the distance for level N.");
        }
        foreach (Probe probe in c.ProbeList())
            if (probe.point == null || probe.point.Length != 3 || probe.point.Any(v => !double.IsFinite(v)) || string.IsNullOrWhiteSpace(probe.name) || probe.name.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '_'))
                throw new Exception("Each probe requires a simple alphanumeric name and three finite coordinates in metres.");
        if (!strict) return c;
        if (c.watcher is not ("systemd" or "supervisord")) throw new Exception("watcher must be \"systemd\" or \"supervisord\".");
        PointsConfig pconf = c.points;
        Positive(pconf.pollMilliseconds, "points.pollMilliseconds");
        Points.ParseCompression(pconf.compression);
        if (pconf.mantissaBits < 1 || pconf.mantissaBits > 23) throw new Exception("points.mantissaBits must be 1..23 (23 is lossless).");
        if (Points.Selected(pconf).Length == 0) throw new Exception("Enable at least one points field.");
        return c;
    }
    public static void Positive(double value, string name) { if (!double.IsFinite(value) || value <= 0) throw new Exception(name + " must be finite and positive."); }
    public static void Nonnegative(double value, string name) { if (!double.IsFinite(value) || value < 0) throw new Exception(name + " must be finite and nonnegative."); }
    public static string Fingerprint(Config c)
    {
        // Points controls are read live on restart; physics is frozen in foam/config.json.
        string text = JsonSerializer.Serialize(c, Json);
        using var document = JsonDocument.Parse(text);
        string physical = string.Join("\n", document.RootElement.EnumerateObject().Where(x => x.Name is not ("watcher" or "points")).Select(x => x.ToString()).Append("keepEvery=" + c.points.keepEvery));
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(physical)));
    }
    public static Config Built(string root)
    {
        if (!File.Exists(Path.Combine(Paths.Foam(root), ".built"))) throw new Exception("Case is not fully built. Run ocarina build DIR first.");
        Config current = Load(Path.Combine(root, "config.json"));
        Config built = Load(Path.Combine(Paths.Foam(root), "config.json"), false);
        if (Fingerprint(current) != Fingerprint(built)) throw new Exception("Physics config changed since build. Use a fresh case directory and build it.");
        built.watcher = current.watcher; built.points = current.points;
        return built;
    }
}

// Writes doubles in plain decimal form, never exponent notation.
public class PlainDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDouble();
    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) =>
        writer.WriteRawValue(((decimal)value).ToString("0.0#############", System.Globalization.CultureInfo.InvariantCulture));
}
