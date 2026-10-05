using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    public static string CaseDirectory = "";
    public static string OutputDirectory = "";
    public static int ProcessorCount = 0;


    public const int UntouchedTimes = 3;
    public static int ThreadCount = 0;
    public static double WriteTimeStep = 0;
    public static int PollMilliseconds = 0;
    public static int PngCompressionLevel = 0;
    public static int PlotWidth = 0;
    public static int PlotHeight = 0;
    public static float MarginPixels = 0;
    public static float LabelFontPixels = 0;
    public static SKColor BackgroundColor = SKColors.Black;
    public static SKColor LabelColor = SKColors.Black;
    public static float PointSizePixels = 0f;

    // Camera look-at point and framing come from the ocarina's own bounding box, not the air domain.
    public static Vector3 ObjectMinimum = new Vector3(0f, 0f, 0f);
    public static Vector3 ObjectMaximum = new Vector3(0f, 0f, 0f);
    public static float CameraPaddingFraction = 0f;
    public static float MinimumAlpha = 0f;

    public static Field[] Fields = new Field[]
    {
        new Field { Enabled = false, File = "p", Name = "pressure", Unit = "Pa", DecimalPlaces = 0 },
        new Field { Enabled = false, File = "U", Name = "velocityMagnitude", Vector = true, Unit = "m/s", DecimalPlaces = 8 },
        new Field { Enabled = false, File = "rho", Name = "density", Unit = "kg/m^3", DecimalPlaces = 8 },
        new Field { Enabled = false, File = "T", Name = "temperature", Unit = "K", DecimalPlaces = 0 }
    };

    public static RenderTileDefinition[] Views = Array.Empty<RenderTileDefinition>();
    public static Camera[] ViewCameras = Array.Empty<Camera>();

    public const int SelfRendered = 1;
    public const int PredecessorConsumed = 2;
    public static ConcurrentDictionary<string, int> PendingDeletion = new();
}
