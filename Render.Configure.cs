using System.Numerics;
using System.Text.Json;
using SkiaSharp;

public static partial class Renderer
{
    public static void Configure(string root,Config c,bool initialize=true)
    {
        CaseDirectory=Paths.Foam(root);OutputDirectory=Path.Combine(root,"renders");ProcessorCount=c.processorCount;
        RenderConfig r=c.renderer;
        ThreadCount=r.renderThreads;WriteTimeStep=c.deltaTSeconds*c.fieldWriteIntervalTimeSteps;
        PollMilliseconds=r.pollMilliseconds;PngCompressionLevel=r.pngCompressionLevel;PlotWidth=r.plotWidth;PlotHeight=r.plotHeight;
        MarginPixels=r.marginPixels;LabelFontPixels=r.labelFontPixels;BackgroundColor=SKColor.Parse(r.backgroundColor);LabelColor=SKColor.Parse(r.labelColor);
        PointSizePixels=r.pointSizePixels;AxisTiltDegrees=(float)r.axisTiltDegrees;CameraPaddingFraction=(float)r.cameraPaddingFraction;
        Bounds bounds=JsonSerializer.Deserialize<Bounds>(File.ReadAllText(Path.Combine(CaseDirectory,"bounds.json")),Configuration.Json);
        ObjectMinimum=new Vector3((float)(bounds.Min[0]*.001),(float)(bounds.Min[1]*.001),(float)(bounds.Min[2]*.001));
        ObjectMaximum=new Vector3((float)(bounds.Max[0]*.001),(float)(bounds.Max[1]*.001),(float)(bounds.Max[2]*.001));
        Fields[0].Enabled=r.renderPressure;Fields[1].Enabled=r.renderVelocityMagnitude;Fields[2].Enabled=r.renderDensity;Fields[3].Enabled=r.renderTemperature;
        RecipeCompatibilityNote = "";

        RenderTileDefinition[][] parsed = ParseTiles(r);
        double[] defaultTarget = new[] { (bounds.Min[0] + bounds.Max[0]) * .5, (bounds.Min[1] + bounds.Max[1]) * .5, (bounds.Min[2] + bounds.Max[2]) * .5 };
        Tiles = ResolveTileDefaults(parsed, defaultTarget);
        TileCameras = new Camera[GridRows][];
        for (int row = 0; row < GridRows; row++)
        {
            TileCameras[row] = new Camera[GridColumns];
            for (int column = 0; column < GridColumns; column++)
            {
                if (Tiles[row][column].Legend) continue;
                float offsetX = column * PlotWidth;
                float offsetY = row * PlotHeight;
                try
                {
                    TileCameras[row][column] = MakeCamera(Tiles[row][column], TileContentRect(offsetX, offsetY));
                }
                catch (Exception error)
                {
                    throw new Exception($"renderer.tiles[{row}][{column}] camera invalid: {error.Message}");
                }
            }
        }

        RenderRecipe candidate = Recipe(r, Tiles);
        string stamp=Path.Combine(OutputDirectory,"config.json");
        bool hasDone = Directory.Exists(Path.Combine(OutputDirectory, ".done")) && Directory.EnumerateFiles(Path.Combine(OutputDirectory, ".done"), "*.json").Any();
        bool hasPng = Directory.Exists(OutputDirectory) && Directory.EnumerateFiles(OutputDirectory, "*.png", SearchOption.TopDirectoryOnly).Any();
        bool hasOutputs = hasDone || hasPng;
        if (File.Exists(stamp))
        {
            RenderRecipe existing = JsonSerializer.Deserialize<RenderRecipe>(File.ReadAllText(stamp), Configuration.Json);
            if (existing == null) throw new Exception("Saved render recipe is empty: renders/config.json");
            existing.renderThreads = candidate.renderThreads;
            existing.pollMilliseconds = candidate.pollMilliseconds;
            string path = RecipeDifferencePath(existing, candidate);
            if (path.Length > 0)
            {
                string message = "Render recipe differs at " + path + ".\nExisting output belongs to the saved recipe in renders/config.json.\nEarlier raw fields may already have been consumed; use a new case for this layout.";
                if (initialize) throw new Exception(message);
                RecipeCompatibilityNote = message;
            }
        }
        else if (hasOutputs)
        {
            string message = "renders/config.json is missing while frame output exists. Existing render output cannot be adopted without the saved recipe.";
            if (initialize) throw new Exception(message);
            RecipeCompatibilityNote = message;
        }

        if(initialize)
        {
            Directory.CreateDirectory(OutputDirectory);
            Directory.CreateDirectory(Path.Combine(OutputDirectory,".done"));
            if (!File.Exists(stamp) && !hasOutputs) Paths.Atomic(stamp,JsonSerializer.Serialize(candidate,Configuration.Json));
        }
    }
}
