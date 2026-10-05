using System.Numerics;
using System.Text.Json;
using SkiaSharp;

public static partial class Renderer
{
    public static RenderTileDefinition[] ParseViews(RenderConfig config)
    {
        if (config.views == null || config.views.Length == 0) throw new Exception("renderer.views must contain at least one view.");
        var views = new RenderTileDefinition[config.views.Length];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < views.Length; i++)
        {
            string path = $"renderer.views[{i}]";
            if (config.views[i].ValueKind != JsonValueKind.Object) throw new Exception(path + " must be a camera object.");
            views[i] = ParseCameraTile(config.views[i], path);
            if (!views[i].Name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-' || ch == '_')) throw new Exception(path + ".name may only contain letters, digits, '-' and '_' (it names a folder and a video).");
            if (!names.Add(views[i].Name)) throw new Exception(path + ".name duplicates another view.");
        }
        ValidateViewGeometry(config.marginPixels, config.labelFontPixels, config.plotWidth, config.plotHeight);
        return views;
    }


    public static RenderTileDefinition ParseCameraTile(JsonElement tile, string path)
    {
        string name = null;
        double[] from = null;
        double[] up = null;
        double[] target = null;
        double zoom = 1;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in tile.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw new Exception(path + " has duplicate key \"" + property.Name + "\".");
            switch (property.Name)
            {
                case "name":
                    if (property.Value.ValueKind != JsonValueKind.String) throw new Exception(path + ".name must be a nonempty string.");
                    name = property.Value.GetString();
                    if (string.IsNullOrWhiteSpace(name)) throw new Exception(path + ".name must be a nonempty string.");
                    break;
                case "from":
                    from = ParseVector(property.Value, path + ".from");
                    if (VectorLengthSquared(from) <= 0) throw new Exception(path + ".from must be nonzero.");
                    break;
                case "up":
                    if (property.Value.ValueKind == JsonValueKind.Null) throw new Exception(path + ".up cannot be null.");
                    up = ParseVector(property.Value, path + ".up");
                    if (VectorLengthSquared(up) <= 0) throw new Exception(path + ".up must be nonzero.");
                    break;
                case "targetMillimeters":
                    if (property.Value.ValueKind == JsonValueKind.Null) throw new Exception(path + ".targetMillimeters cannot be null.");
                    target = ParseVector(property.Value, path + ".targetMillimeters");
                    break;
                case "zoom":
                    if (property.Value.ValueKind == JsonValueKind.Null) throw new Exception(path + ".zoom cannot be null.");
                    if (property.Value.ValueKind != JsonValueKind.Number) throw new Exception(path + ".zoom must be a positive finite number.");
                    zoom = property.Value.GetDouble();
                    if (!double.IsFinite(zoom) || zoom <= 0) throw new Exception(path + ".zoom must be a positive finite number.");
                    break;
                default:
                    throw new Exception(path + " has unknown key \"" + property.Name + "\".");
            }
        }
        if (name == null) throw new Exception(path + ".name is required.");
        if (from == null) throw new Exception(path + ".from is required.");
        return new RenderTileDefinition { Name = name, From = from, Up = up, TargetMillimeters = target, Zoom = zoom };
    }

    public static double[] ParseVector(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Array) throw new Exception(path + " must be an array of three finite numbers.");
        var values = element.EnumerateArray().ToArray();
        if (values.Length != 3) throw new Exception(path + " must be an array of three finite numbers.");
        double[] vector = new double[3];
        for (int i = 0; i < 3; i++)
        {
            if (values[i].ValueKind != JsonValueKind.Number) throw new Exception(path + " must be an array of three finite numbers.");
            double value = values[i].GetDouble();
            if (!double.IsFinite(value)) throw new Exception(path + " must be an array of three finite numbers.");
            vector[i] = value;
        }
        return vector;
    }

    public static double VectorLengthSquared(double[] value) => value[0] * value[0] + value[1] * value[1] + value[2] * value[2];

    public static SKRect TileContentRect()
    {
        return new SKRect(MarginPixels, HeaderBottom(MarginPixels, LabelFontPixels) + MarginPixels, PlotWidth - MarginPixels, PlotHeight - MarginPixels);
    }

    // Title line, subtitle line, then the colour bar; returns the bar's bottom edge.
    public static float HeaderBottom(float margin, float font) => margin + 3 * font + 28;

    public static void ValidateViewGeometry(int marginPixels, int labelFontPixels, int plotWidth, int plotHeight)
    {
        float contentWidth = plotWidth - 2 * marginPixels;
        float contentHeight = plotHeight - HeaderBottom(marginPixels, labelFontPixels) - 2 * marginPixels;
        if (contentWidth < 2 || contentHeight <= 0) throw new Exception("renderer margin/font settings leave no render area.");
    }

    public static RenderTileDefinition[] ResolveViewDefaults(RenderTileDefinition[] input, double[] defaultTargetMillimeters)
    {
        return input.Select(view =>
        {
            Vector3 from = Vector3.Normalize(new Vector3((float)view.From[0], (float)view.From[1], (float)view.From[2]));
            Vector3 up = view.Up == null ? DefaultUp(from) : Vector3.Normalize(new Vector3((float)view.Up[0], (float)view.Up[1], (float)view.Up[2]));
            return new RenderTileDefinition
            {
                Name = view.Name,
                From = (double[])view.From.Clone(),
                Up = new[] { (double)up.X, (double)up.Y, (double)up.Z },
                TargetMillimeters = view.TargetMillimeters == null ? (double[])defaultTargetMillimeters.Clone() : (double[])view.TargetMillimeters.Clone(),
                Zoom = view.Zoom
            };
        }).ToArray();
    }

    public static void PrintLayoutSummary()
    {
        Console.WriteLine($"Frames: {PlotWidth} x {PlotHeight} px, {Views.Length} views per field");
        Console.WriteLine("Views: " + string.Join(", ", Views.Select(v => v.Name)));
    }
}
