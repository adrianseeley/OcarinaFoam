using System.Globalization;
using System.Text.Json;

// ocarina render DIR SETTINGS_FILE: splats selected points onto 1024x1024 plots with headers and legends.
public static partial class Render
{
    public const int Size = 1024;

    static readonly (double T, double R, double G, double B)[] Inferno =
    {
        (0.0, 0, 0, 4), (0.13, 31, 12, 72), (0.25, 85, 15, 109), (0.38, 136, 34, 106),
        (0.5, 186, 54, 85), (0.63, 227, 89, 51), (0.75, 249, 140, 10), (0.88, 249, 201, 50), (1.0, 252, 255, 164)
    };

    static readonly (double T, double R, double G, double B)[] GameBoy =
    {
        (0.0, 15, 56, 15), (0.2, 33, 82, 26), (0.4, 57, 112, 31),
        (0.6, 88, 143, 22), (0.8, 139, 172, 15), (1.0, 180, 198, 42)
    };

    static readonly (double T, double R, double G, double B)[] Vanimo =
    {
        (0.0, 255, 205, 253), (0.125, 205, 120, 189), (0.25, 155, 68, 136),
        (0.375, 84, 33, 72), (0.5, 31, 20, 25), (0.625, 47, 64, 24),
        (0.75, 89, 123, 42), (0.875, 140, 190, 85), (1.0, 190, 253, 165)
    };

    static readonly (double T, double R, double G, double B)[] Hsv =
    {
        (0.0, 255, 0, 0), (1.0 / 6, 255, 255, 0), (2.0 / 6, 0, 255, 0),
        (0.5, 0, 255, 255), (4.0 / 6, 0, 0, 255), (5.0 / 6, 255, 0, 255), (1.0, 255, 0, 0)
    };

    static readonly (double T, double R, double G, double B)[] Monochrome =
    {
        (0.0, 0, 0, 0), (1.0, 255, 255, 255)
    };

    readonly record struct Condition(int Axis, string Operator, double Value)
    {
        public bool Matches(float[] positions, int point)
        {
            double coordinate = positions[point * 3 + Axis];
            return Operator switch
            {
                "<" => coordinate < Value,
                ">" => coordinate > Value,
                "<=" => coordinate <= Value,
                ">=" => coordinate >= Value,
                "==" => coordinate == Value,
                "!=" => coordinate != Value,
                _ => throw new InvalidOperationException("Unknown condition operator.")
            };
        }
    }

    readonly record struct Presentation(int Diameter, double Alpha);

    static (byte R, byte G, byte B) Color(string name, double t)
    {
        (double T, double R, double G, double B)[] palette = name switch
        {
            "inferno" => Inferno,
            "gameboy" => GameBoy,
            "vanimo" => Vanimo,
            "hsv" => Hsv,
            "monochrome" or "monochrome-contrast" or "monochrome-polarize" => Monochrome,
            _ => throw new ArgumentException($"Unknown color '{name}'. Supported: inferno, gameboy, vanimo, hsv, monochrome, monochrome-contrast, monochrome-polarize.")
        };
        t = Math.Clamp(t, 0, 1);
        if (name is "monochrome-contrast" or "monochrome-polarize")
        {
            // A symmetric S-curve deepens shadows and lifts highlights without clipping values.
            double dark = t * t, light = (1 - t) * (1 - t);
            if (name == "monochrome-polarize")
            {
                // Power 16 makes the transition around mid-gray much steeper than power 2.
                for (int power = 0; power < 3; power++) { dark *= dark; light *= light; }
            }
            t = dark / (dark + light);
        }
        for (int i = 1; i < palette.Length; i++)
        {
            if (t > palette[i].T) continue;
            var a = palette[i - 1]; var b = palette[i];
            double f = (t - a.T) / (b.T - a.T);
            return ((byte)Math.Round(a.R + (b.R - a.R) * f), (byte)Math.Round(a.G + (b.G - a.G) * f), (byte)Math.Round(a.B + (b.B - a.B) * f));
        }
        return ((byte)palette[^1].R, (byte)palette[^1].G, (byte)palette[^1].B);
    }

    public sealed class Settings
    {
        public required string field { get; init; }
        public required string frames { get; init; }
        public required string view { get; init; }
        public required string color { get; init; }
        public required int pointSize { get; init; }
        public required double alpha { get; init; }
        public required string dataMode { get; init; }
        public double? removeRankBandMiddle { get; init; } = 0.05;
        public double? removeRankBandLow { get; init; }
        public double? removeRankBandHigh { get; init; }
        public double? removeDeltaRankBandMiddle { get; init; }
        public double? removeDeltaRankBandLow { get; init; }
        public double? removeDeltaRankBandHigh { get; init; }
        public Dictionary<string, Dictionary<string, double>> filter { get; init; } = new();
    }

    static Settings LoadSettings(JsonElement element)
    {
        Settings settings = element.Deserialize<Settings>(Configuration.Json)
            ?? throw new ArgumentException("Render settings must be a JSON object.");
        if (settings.field != "pressure") throw new ArgumentException("Only the pressure field is supported for now.");
        if (string.IsNullOrWhiteSpace(settings.frames)) throw new ArgumentException("frames must be *, -t, a frame index, or a timestamp.");
        View(settings.view);
        Color(settings.color, 0);
        if (settings.pointSize < 1) throw new ArgumentException("pointSize must be a positive whole-pixel diameter.");
        if (!double.IsFinite(settings.alpha) || settings.alpha < 0 || settings.alpha > 1)
            throw new ArgumentException("alpha must be between 0 and 1.");
        foreach (var (name, value) in new[] { (nameof(settings.removeRankBandMiddle), settings.removeRankBandMiddle),
            (nameof(settings.removeRankBandLow), settings.removeRankBandLow), (nameof(settings.removeRankBandHigh), settings.removeRankBandHigh),
            (nameof(settings.removeDeltaRankBandMiddle), settings.removeDeltaRankBandMiddle),
            (nameof(settings.removeDeltaRankBandLow), settings.removeDeltaRankBandLow),
            (nameof(settings.removeDeltaRankBandHigh), settings.removeDeltaRankBandHigh) })
            if (value is double band && (!double.IsFinite(band) || band < 0 || band > 1))
                throw new ArgumentException($"{name} must be null or between 0 and 1.");
        DifferenceOrder(settings.dataMode);
        ParseFilter(settings.filter);
        return settings;
    }

    static int DifferenceOrder(string mode) => mode switch
    {
        "standard" => 0,
        "firstDifferences" => 1,
        "secondDifferences" => 2,
        _ => throw new ArgumentException("dataMode must be standard, firstDifferences, or secondDifferences.")
    };

    static Condition[] ParseFilter(Dictionary<string, Dictionary<string, double>> filter)
    {
        if (filter == null) throw new ArgumentException("filter must be an object; use {} for no filtering.");
        var conditions = new List<Condition>();
        foreach (var (axisName, comparisons) in filter)
        {
            int axis = axisName.Length == 1 ? "xyz".IndexOf(axisName[0]) : -1;
            if (axis < 0 || comparisons == null) throw new ArgumentException("filter axes must be x, y, or z, each containing a comparison object.");
            foreach (var (name, value) in comparisons)
            {
                string op = name switch
                {
                    "greaterThan" => ">", "greaterThanEqual" => ">=",
                    "lessThan" => "<", "lessThanEqual" => "<=",
                    "equal" => "==", "notEqual" => "!=",
                    _ => throw new ArgumentException($"Unknown filter comparison '{name}'. Use greaterThan, greaterThanEqual, lessThan, lessThanEqual, equal, or notEqual.")
                };
                if (!double.IsFinite(value)) throw new ArgumentException($"filter.{axisName}.{name} must be finite.");
                conditions.Add(new Condition(axis, op, value));
            }
        }
        return conditions.ToArray();
    }

    // Read one extra saved frame for delta ranking, with bounded memory.
    static IEnumerable<(int Index, double[] Values, double[] Delta)> DataFrames(string directory, string[] frames, int stride, int count, int offset, int differenceOrder,
        int first = 0, int last = int.MaxValue)
    {
        double[] previous = null, previousPrevious = null, previousValues = null;
        for (int index = Math.Max(0, first - differenceOrder - 1); index < frames.Length && index <= last; index++)
        {
            float[] raw = ReadFrame(directory, frames[index], stride, count);
            var current = new double[count];
            for (int i = 0; i < count; i++) current[i] = raw[i * stride + offset];
            if ((differenceOrder == 0 || previous != null) && (differenceOrder < 2 || previousPrevious != null))
            {
                var values = new double[count];
                for (int i = 0; i < count; i++)
                    values[i] = differenceOrder switch
                    {
                        0 => current[i],
                        1 => current[i] - previous[i],
                        _ => (current[i] - previous[i]) - (previous[i] - previousPrevious[i])
                    };
                double[] delta = previousValues == null ? null : new double[count];
                if (delta != null)
                    for (int i = 0; i < count; i++) delta[i] = values[i] - previousValues[i];
                previousValues = values;
                if (index >= first) yield return (index, values, delta);
            }
            previousPrevious = previous;
            previous = current;
        }
    }

    // Select exact counts by sorted rank. Point index breaks ties deterministically.
    static (bool[] Hidden, (double Low, double High)[] Bands, (double Low, double High)[] Ranks, int Count)
        RankBands(double[] values, bool[] selected, double? middle, double? lowFraction, double? highFraction)
    {
        int[] sorted = Enumerable.Range(0, values.Length)
            .Where(i => selected[i] && double.IsFinite(values[i]))
            .OrderBy(i => values[i]).ThenBy(i => i).ToArray();
        var hidden = new bool[values.Length];
        int n = sorted.Length;
        int Count(double? fraction) => (int)Math.Round(n * (fraction ?? 0), MidpointRounding.AwayFromZero);
        void Hide(int start, int count)
        {
            for (int rank = start; rank < start + count; rank++) hidden[sorted[rank]] = true;
        }
        int middleCount = Count(middle);
        Hide((n - middleCount) / 2, middleCount);
        Hide(0, Count(lowFraction));
        int highCount = Count(highFraction);
        Hide(n - highCount, highCount);
        var bands = new List<(double Low, double High)>();
        var ranks = new List<(double Low, double High)>();
        for (int rank = 0; rank < n; rank++)
        {
            if (!hidden[sorted[rank]]) continue;
            int first = rank;
            while (rank + 1 < n && hidden[sorted[rank + 1]]) rank++;
            bands.Add((values[sorted[first]], values[sorted[rank]]));
            ranks.Add((first / (double)n, (rank + 1) / (double)n));
        }
        return (hidden, bands.ToArray(), ranks.ToArray(), n);
    }

    static string[] Frames(string directory) => Directory.EnumerateFiles(directory, "*s*ms*us*ns.bin.br").Select(f => Path.GetFileName(f)[..^".bin.br".Length]).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    static string ResolveFrame(string[] frames, string t)
    {
        if (frames.Length == 0) throw new FileNotFoundException("No frames found.");
        if (t == "-t") t = "-1";
        if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            int i = index < 0 ? frames.Length + index : index;
            if (i < 0 || i >= frames.Length) throw new ArgumentException($"Frame index {t} is out of range; {frames.Length} frames exist.");
            return frames[i];
        }
        Points.ParseTime(t, "t");
        if (!frames.Contains(t)) throw new FileNotFoundException($"No frame named {t}.");
        return t;
    }

    static float[] ReadFrame(string directory, string frame, int stride, int count)
    {
        float[] values = Points.ReadFloats(Path.Combine(directory, frame + ".bin.br"), stride);
        if (values.Length / stride != count) throw new InvalidDataException($"Frame {frame} has {values.Length / stride} points but position file has {count}.");
        return values;
    }

    static ((double X, double Y, double Z) Direction, (double X, double Y, double Z) Up) View(string name) => name switch
    {
        "fromLeft" => ((-1, 0, 0), (0, 0, 1)),
        "fromRight" => ((1, 0, 0), (0, 0, 1)),
        "fromAbove" => ((0, 0, 1), (0, 1, 0)),
        "fromBelow" => ((0, 0, -1), (0, 1, 0)),
        "fromTop" => ((0, 1, 0), (0, 0, 1)),
        "fromBottom" => ((0, -1, 0), (0, 0, 1)),
        "fromAboveRight" => (Unit((1, 0, 1), "named view"), (0, 1, 0)),
        _ => throw new ArgumentException($"Unknown view '{name}'. Supported: fromLeft, fromRight, fromAbove, fromBelow, fromTop, fromBottom, fromAboveRight.")
    };

    static (double X, double Y, double Z) Cross((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    static (double X, double Y, double Z) Unit((double X, double Y, double Z) a, string what)
    {
        double n = Math.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z);
        if (n == 0) throw new ArgumentException($"Camera {what} is degenerate.");
        return (a.X / n, a.Y / n, a.Z / n);
    }

    public static int Run(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Use: ocarina render DIR SETTINGS_FILE or ocarina render clean DIR");
        if (args[1] == "clean")
        {
            string directory = Path.Combine(Paths.Canonical(args[2]), "renders");
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            Console.WriteLine($"Cleaned renders: {directory}");
            return 0;
        }
        using var document = JsonDocument.Parse(File.ReadAllText(args[2]));
        var element = document.RootElement;
        Settings[] panels = element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().Select(LoadSettings).ToArray()
            : new[] { LoadSettings(element) };
        if (panels.Length == 0) throw new ArgumentException("Render settings array cannot be empty.");
        if (panels.Any(p => p.frames != panels[0].frames || p.dataMode != panels[0].dataMode))
            throw new ArgumentException("Side-by-side panels must use the same frames and dataMode.");
        string root = Paths.Canonical(args[1]);
        string[] snapshot = Frames(Points.PointsDirectory(root));
        var streams = panels.Select(p => RenderFrames(root, p, snapshot).GetEnumerator()).ToArray();
        try
        {
            while (true)
            {
                bool[] ready = streams.Select(s => s.MoveNext()).ToArray();
                if (ready.All(r => !r)) break;
                if (ready.Any(r => !r) || streams.Any(s => s.Current.Frame != streams[0].Current.Frame))
                    throw new InvalidDataException("Panel frames do not match.");
                int width = checked(Size * panels.Length);
                int height = streams.Max(s => s.Current.Image.Length / (Size * 3));
                byte[] combined = new byte[checked(width * height * 3)];
                for (int panel = 0; panel < streams.Length; panel++)
                {
                    byte[] image = streams[panel].Current.Image;
                    for (int y = 0; y < image.Length / (Size * 3); y++)
                        Buffer.BlockCopy(image, y * Size * 3, combined, (y * width + panel * Size) * 3, Size * 3);
                }
                string output = Path.Combine(root, "renders", panels[0].dataMode, $"pressure_{streams[0].Current.Frame}.png");
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                WritePng(output, combined, width);
                Console.WriteLine($"{output} ({panels.Length} panel(s), {width}x{height})");
            }
        }
        finally { foreach (var stream in streams) stream.Dispose(); }
        return 0;
    }

    static IEnumerable<(string Frame, byte[] Image)> RenderFrames(string root, Settings settings, string[] availableFrames)
    {
        string field = settings.field, pointsDirectory = Points.PointsDirectory(root);
        int differenceOrder = DifferenceOrder(settings.dataMode);
        if (availableFrames.Length <= differenceOrder)
            throw new ArgumentException($"{settings.dataMode} requires at least {differenceOrder + 1} saved frames; found {availableFrames.Length}.");
        int[] frameIndices = settings.frames == "*"
            ? Enumerable.Range(differenceOrder, availableFrames.Length - differenceOrder).ToArray()
            : new[] { Array.IndexOf(availableFrames, ResolveFrame(availableFrames, settings.frames)) };
        if (frameIndices[0] < differenceOrder)
            throw new ArgumentException($"Frame {frameIndices[0]} does not have the {differenceOrder} preceding saved frame(s) required for {settings.dataMode}.");
        var view = View(settings.view);
        string color = settings.color;
        Presentation presentation = new(settings.pointSize, settings.alpha);
        Condition[] conditions = ParseFilter(settings.filter);

        Config config = Configuration.Built(root);
        var layouts = Points.Selected(config.points);
        int stride = layouts.Sum(l => l.Vector ? 3 : 1), offset = 0;
        bool found = false;
        foreach (var l in layouts) { if (l.Name == field) { found = true; break; } offset += l.Vector ? 3 : 1; }
        if (!found) throw new ArgumentException($"{field} is not enabled in points config, so it was not recorded.");

        float[] positions = Points.ReadFloats(Points.PositionFile(root), 3);
        int count = positions.Length / 3;
        for (int i = 0; i < positions.Length; i++) positions[i] *= 1000f;
        if (count == 0) throw new InvalidDataException("Cannot render an empty point cloud.");

        bool[] selected = new bool[count];
        int selectedCount = 0;
        for (int i = 0; i < count; i++)
        {
            selected[i] = conditions.All(condition => condition.Matches(positions, i));
            if (selected[i]) selectedCount++;
        }

        double minX = positions[0], maxX = positions[0], minY = positions[1], maxY = positions[1], minZ = positions[2], maxZ = positions[2];
        for (int i = 3; i < positions.Length; i += 3)
        {
            minX = Math.Min(minX, positions[i]); maxX = Math.Max(maxX, positions[i]);
            minY = Math.Min(minY, positions[i + 1]); maxY = Math.Max(maxY, positions[i + 1]);
            minZ = Math.Min(minZ, positions[i + 2]); maxZ = Math.Max(maxZ, positions[i + 2]);
        }
        var at = ((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
        var forward = (-view.Direction.X, -view.Direction.Y, -view.Direction.Z);
        var right = Unit(Cross(forward, view.Up), "named view");
        var up = Cross(right, forward);
        double horizontal = 0, vertical = 0;
        for (int i = 0; i < count; i++)
        {
            double x = positions[i * 3] - at.Item1, y = positions[i * 3 + 1] - at.Item2, z = positions[i * 3 + 2] - at.Item3;
            horizontal = Math.Max(horizontal, Math.Abs(x * right.X + y * right.Y + z * right.Z));
            vertical = Math.Max(vertical, Math.Abs(x * up.X + y * up.Y + z * up.Z));
        }
        double scale = Math.Max(horizontal, vertical) * 2.1;
        if (scale <= 0) scale = 1;
        double pixels = Size / scale;

        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        // Scale the transformed data across all eligible saved frames, including for a single-frame render.
        foreach (var (_, values, _) in DataFrames(pointsDirectory, availableFrames, stride, count, offset, differenceOrder))
        {
            for (int i = 0; i < count; i++)
            {
                double v = values[i];
                if (!selected[i] || !double.IsFinite(v)) continue;
                min = Math.Min(min, v); max = Math.Max(max, v);
            }
        }
        if (min > max) { min = 0; max = 0; }
        double magnitude = Math.Max(Math.Abs(min), Math.Abs(max));
        // Keep zero at the palette midpoint and positive/negative changes on consistent sides.
        if (differenceOrder > 0) { min = -magnitude; max = magnitude; }
        double range = max > min ? max - min : 1;

        var depth = new float[count]; var px = new double[count]; var py = new double[count]; var order = new int[count];
        for (int i = 0; i < count; i++)
        {
            double x = positions[i * 3] - at.Item1, y = positions[i * 3 + 1] - at.Item2, z = positions[i * 3 + 2] - at.Item3;
            double sx = x * right.Item1 + y * right.Item2 + z * right.Item3, sy = x * up.Item1 + y * up.Item2 + z * up.Item3;
            depth[i] = (float)-(x * forward.Item1 + y * forward.Item2 + z * forward.Item3);
            px[i] = Size / 2.0 + sx * pixels; py[i] = Size / 2.0 - sy * pixels;
            order[i] = i;
        }
        Array.Sort(depth, order);

        foreach (var (frameIndex, values, delta) in DataFrames(pointsDirectory, availableFrames, stride, count, offset, differenceOrder, frameIndices[0], frameIndices[^1]))
        {
            string frame = availableFrames[frameIndex];
            double[] activity = delta?.Select(Math.Abs).ToArray();
            bool deltaRemoval = settings.removeDeltaRankBandLow > 0 || settings.removeDeltaRankBandMiddle > 0 || settings.removeDeltaRankBandHigh > 0;
            var deltaBands = activity == null ? (Hidden: new bool[count], Bands: Array.Empty<(double Low, double High)>(), Ranks: Array.Empty<(double Low, double High)>(), Count: 0)
                : RankBands(activity, selected, settings.removeDeltaRankBandMiddle, settings.removeDeltaRankBandLow, settings.removeDeltaRankBandHigh);
            var bands = RankBands(values, selected, settings.removeRankBandMiddle, settings.removeRankBandLow, settings.removeRankBandHigh);
            double PointAlpha(int i)
            {
                if (!selected[i] || !double.IsFinite(values[i])) return 0;
                if (bands.Hidden[i]) return 0;
                if (activity != null && deltaRemoval &&
                    (!double.IsFinite(activity[i]) || deltaBands.Hidden[i])) return 0;
                return presentation.Alpha;
            }
            int shown = Enumerable.Range(0, count).Count(i => PointAlpha(i) > 0);
            string[] metadata = Metadata(root, frame, settings, conditions, shown, selectedCount, count);
            int imageHeight = ImageHeight;
            byte[] image = new byte[Size * imageHeight * 3];
            foreach (int i in order)
            {
                double v = values[i];
                if (!selected[i] || !double.IsFinite(v)) continue;
                double alpha = PointAlpha(i);
                if (alpha == 0) continue;
                var (r, g, b) = Color(color, (v - min) / range);
                double radius = presentation.Diameter / 2.0, radiusSquared = radius * radius;
                int x0 = Math.Max(0, (int)Math.Floor(px[i] - radius)), x1 = Math.Min(Size - 1, (int)Math.Ceiling(px[i] + radius) - 1);
                int y0 = Math.Max(0, (int)Math.Floor(py[i] - radius)), y1 = Math.Min(Size - 1, (int)Math.Ceiling(py[i] + radius) - 1);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (presentation.Diameter == 1 && (x != (int)Math.Floor(px[i]) || y != (int)Math.Floor(py[i]))) continue;
                    double dx = x + .5 - px[i], dy = y + .5 - py[i];
                    if (presentation.Diameter != 1 && dx * dx + dy * dy > radiusSquared) continue;
                    int p = ((y + HeaderHeight) * Size + x) * 3;
                    image[p] = (byte)Math.Round(b * alpha + image[p] * (1 - alpha));
                    image[p + 1] = (byte)Math.Round(g * alpha + image[p + 1] * (1 - alpha));
                    image[p + 2] = (byte)Math.Round(r * alpha + image[p + 2] * (1 - alpha));
                }
            }

            DrawLegend(image, color, min, max, values, selected, metadata, bands, activity, deltaBands,
                settings, frame, availableFrames[^1], (frameIndex - frameIndices[0] + 1.0) / frameIndices.Length);
            yield return (frame, image);
        }
    }

}
