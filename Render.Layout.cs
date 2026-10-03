using System.Numerics;
using System.Text.Json;
using SkiaSharp;

public static partial class Renderer
{
    public static RenderTileDefinition[][] ParseTiles(RenderConfig config)
    {
        if (config.tiles == null) throw new Exception("renderer.tiles is required.");
        if (config.tiles.Length == 0) throw new Exception("renderer.tiles must contain at least one row.");
        int columns = -1;
        int cameras = 0;
        int legends = 0;
        var rows = new RenderTileDefinition[config.tiles.Length][];
        for (int row = 0; row < config.tiles.Length; row++)
        {
            JsonElement[] sourceRow = config.tiles[row] ?? throw new Exception($"renderer.tiles[{row}] must be an array row.");
            if (sourceRow.Length == 0) throw new Exception($"renderer.tiles[{row}] must contain at least one tile.");
            if (columns < 0) columns = sourceRow.Length;
            if (sourceRow.Length != columns) throw new Exception($"renderer.tiles[{row}] has {sourceRow.Length} columns; expected {columns}.");
            rows[row] = new RenderTileDefinition[columns];
            for (int column = 0; column < columns; column++)
            {
                JsonElement cell = sourceRow[column];
                string path = $"renderer.tiles[{row}][{column}]";
                if (cell.ValueKind == JsonValueKind.String)
                {
                    string text = cell.GetString() ?? "";
                    if (text != "legend") throw new Exception(path + " must be \"legend\" or a camera object.");
                    rows[row][column] = new RenderTileDefinition { Legend = true };
                    legends++;
                    continue;
                }
                if (cell.ValueKind != JsonValueKind.Object) throw new Exception(path + " must be \"legend\" or a camera object.");
                rows[row][column] = ParseCameraTile(cell, path);
                cameras++;
            }
        }
        if (cameras == 0) throw new Exception("renderer.tiles must include at least one camera tile.");
        GridRows = rows.Length;
        GridColumns = columns;
        CameraTileCount = cameras;
        LegendTileCount = legends;
        ValidateTileGeometry(rows.Length, columns, config.marginPixels, config.labelFontPixels, config.plotWidth, config.plotHeight);
        return rows;
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
        return new RenderTileDefinition { Legend = false, Name = name, From = from, Up = up, TargetMillimeters = target, Zoom = zoom };
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

    public static SKRect TileContentRect(float tileX, float tileY)
    {
        float left = tileX + MarginPixels;
        float top = tileY + 2 * MarginPixels + LabelFontPixels;
        float right = tileX + PlotWidth - MarginPixels;
        float bottom = tileY + PlotHeight - MarginPixels;
        return new SKRect(left, top, right, bottom);
    }

    public static void ValidateTileGeometry(int rows, int columns, int marginPixels, int labelFontPixels, int plotWidth, int plotHeight)
    {
        float contentWidth = plotWidth - 2 * marginPixels;
        float contentHeight = plotHeight - 3 * marginPixels - labelFontPixels;
        if (contentWidth <= 0 || contentHeight <= 0) throw new Exception("renderer margin/title settings leave no camera content area.");
        if (plotWidth - 2 * marginPixels < 2) throw new Exception("renderer legend bar width must be at least 2 pixels.");
        float finalLegendBaseline = marginPixels + 8 * labelFontPixels + 56;
        if (finalLegendBaseline > plotHeight - marginPixels) throw new Exception("renderer plotHeight is too small for legend labels with current margins and font.");
        if (rows <= 0 || columns <= 0) throw new Exception("renderer.tiles must be a nonempty rectangular matrix.");
    }

    public static RenderTileDefinition[][] ResolveTileDefaults(RenderTileDefinition[][] input, double[] defaultTargetMillimeters)
    {
        var resolved = new RenderTileDefinition[input.Length][];
        for (int row = 0; row < input.Length; row++)
        {
            resolved[row] = new RenderTileDefinition[input[row].Length];
            for (int column = 0; column < input[row].Length; column++)
            {
                RenderTileDefinition tile = input[row][column];
                if (tile.Legend)
                {
                    resolved[row][column] = new RenderTileDefinition { Legend = true };
                    continue;
                }
                Vector3 from = Vector3.Normalize(new Vector3((float)tile.From[0], (float)tile.From[1], (float)tile.From[2]));
                Vector3 up = tile.Up == null ? DefaultUp(from) : Vector3.Normalize(new Vector3((float)tile.Up[0], (float)tile.Up[1], (float)tile.Up[2]));
                resolved[row][column] = new RenderTileDefinition
                {
                    Legend = false,
                    Name = tile.Name,
                    From = (double[])tile.From.Clone(),
                    Up = new[] { (double)up.X, (double)up.Y, (double)up.Z },
                    TargetMillimeters = tile.TargetMillimeters == null ? (double[])defaultTargetMillimeters.Clone() : (double[])tile.TargetMillimeters.Clone(),
                    Zoom = tile.Zoom
                };
            }
        }
        return resolved;
    }

    public static RenderRecipe Recipe(RenderConfig config, RenderTileDefinition[][] resolvedTiles)
    {
        return new RenderRecipe
        {
            renderThreads = config.renderThreads,
            pollMilliseconds = config.pollMilliseconds,
            pngCompressionLevel = config.pngCompressionLevel,
            plotWidth = config.plotWidth,
            plotHeight = config.plotHeight,
            marginPixels = config.marginPixels,
            labelFontPixels = config.labelFontPixels,
            backgroundColor = config.backgroundColor,
            labelColor = config.labelColor,
            pointSizePixels = config.pointSizePixels,
            axisTiltDegrees = config.axisTiltDegrees,
            cameraPaddingFraction = config.cameraPaddingFraction,
            renderPressure = config.renderPressure,
            renderVelocityMagnitude = config.renderVelocityMagnitude,
            renderDensity = config.renderDensity,
            renderTemperature = config.renderTemperature,
            tiles = CloneTiles(resolvedTiles)
        };
    }

    public static RenderTileDefinition[][] CloneTiles(RenderTileDefinition[][] tiles)
    {
        var copy = new RenderTileDefinition[tiles.Length][];
        for (int row = 0; row < tiles.Length; row++)
        {
            copy[row] = new RenderTileDefinition[tiles[row].Length];
            for (int column = 0; column < tiles[row].Length; column++)
            {
                RenderTileDefinition tile = tiles[row][column];
                copy[row][column] = tile.Legend
                    ? new RenderTileDefinition { Legend = true }
                    : new RenderTileDefinition
                    {
                        Legend = false,
                        Name = tile.Name,
                        From = tile.From == null ? null : (double[])tile.From.Clone(),
                        Up = tile.Up == null ? null : (double[])tile.Up.Clone(),
                        TargetMillimeters = tile.TargetMillimeters == null ? null : (double[])tile.TargetMillimeters.Clone(),
                        Zoom = tile.Zoom
                    };
            }
        }
        return copy;
    }

    public static string RecipeDifferencePath(RenderRecipe existing, RenderRecipe candidate)
    {
        if (existing == null || candidate == null) return "renderer";
        if (existing.pngCompressionLevel != candidate.pngCompressionLevel) return "renderer.pngCompressionLevel";
        if (existing.plotWidth != candidate.plotWidth) return "renderer.plotWidth";
        if (existing.plotHeight != candidate.plotHeight) return "renderer.plotHeight";
        if (existing.marginPixels != candidate.marginPixels) return "renderer.marginPixels";
        if (existing.labelFontPixels != candidate.labelFontPixels) return "renderer.labelFontPixels";
        if (existing.backgroundColor != candidate.backgroundColor) return "renderer.backgroundColor";
        if (existing.labelColor != candidate.labelColor) return "renderer.labelColor";
        if (existing.pointSizePixels != candidate.pointSizePixels) return "renderer.pointSizePixels";
        if (existing.axisTiltDegrees != candidate.axisTiltDegrees) return "renderer.axisTiltDegrees";
        if (existing.cameraPaddingFraction != candidate.cameraPaddingFraction) return "renderer.cameraPaddingFraction";
        if (existing.renderPressure != candidate.renderPressure) return "renderer.renderPressure";
        if (existing.renderVelocityMagnitude != candidate.renderVelocityMagnitude) return "renderer.renderVelocityMagnitude";
        if (existing.renderDensity != candidate.renderDensity) return "renderer.renderDensity";
        if (existing.renderTemperature != candidate.renderTemperature) return "renderer.renderTemperature";
        if (existing.tiles == null || candidate.tiles == null) return "renderer.tiles";
        if (existing.tiles.Length != candidate.tiles.Length) return "renderer.tiles";
        for (int row = 0; row < existing.tiles.Length; row++)
        {
            if (existing.tiles[row].Length != candidate.tiles[row].Length) return $"renderer.tiles[{row}]";
            for (int column = 0; column < existing.tiles[row].Length; column++)
            {
                RenderTileDefinition a = existing.tiles[row][column];
                RenderTileDefinition b = candidate.tiles[row][column];
                string tilePath = $"renderer.tiles[{row}][{column}]";
                if (a.Legend != b.Legend) return tilePath;
                if (a.Legend) continue;
                if (a.Name != b.Name) return tilePath + ".name";
                if (!EqualVector(a.From, b.From)) return tilePath + ".from";
                if (!EqualVector(a.Up, b.Up)) return tilePath + ".up";
                if (!EqualVector(a.TargetMillimeters, b.TargetMillimeters)) return tilePath + ".targetMillimeters";
                if (a.Zoom != b.Zoom) return tilePath + ".zoom";
            }
        }
        return "";
    }

    public static bool EqualVector(double[] left, double[] right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null || left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
        return true;
    }

    public static void PrintLayoutSummary()
    {
        int compositeWidth = GridColumns * PlotWidth;
        int compositeHeight = GridRows * PlotHeight;
        long bitmapBytes = (long)compositeWidth * compositeHeight * 4;
        long bitmapMib = bitmapBytes / (1024 * 1024);
        Console.WriteLine($"Layout: {GridRows} rows x {GridColumns} columns");
        Console.WriteLine($"Composite: {compositeWidth} x {compositeHeight} px");
        Console.WriteLine($"Tiles: {CameraTileCount} cameras, {LegendTileCount} legend");
        Console.WriteLine($"Bitmap: {bitmapMib} MiB per worker, excluding particle/field/encoding buffers");
        for (int row = 0; row < GridRows; row++)
        {
            string text = string.Join(" | ", Tiles[row].Select(t => t.Legend ? "legend" : "camera \"" + t.Name + "\""));
            Console.WriteLine($"Row {row}: {text}");
        }
    }
}
