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
    public static int GridColumns;
    public static int GridRows;
    public static float PointSizePixels = 0f;
    public static float AxisTiltDegrees = 0f; // Off-axis peek so grids don't hide points behind one another.

    // Camera look-at point and framing come from the ocarina's own bounding box, not the air domain.
    public static Vector3 ObjectMinimum = new Vector3(0f, 0f, 0f);
    public static Vector3 ObjectMaximum = new Vector3(0f, 0f, 0f);
    public static float CameraPaddingFraction = 0f;

    public static Field[] Fields = new Field[]
    {
        new Field { Enabled = false, File = "p", Name = "pressure", Unit = "Pa", DecimalPlaces = 0 },
        new Field { Enabled = false, File = "U", Name = "velocityMagnitude", Vector = true, Unit = "m/s", DecimalPlaces = 8 },
        new Field { Enabled = false, File = "rho", Name = "density", Unit = "kg/m^3", DecimalPlaces = 8 },
        new Field { Enabled = false, File = "T", Name = "temperature", Unit = "K", DecimalPlaces = 0 }
    };

    // From is relative to the padded SOLID bounding-box half-extents, not the air domain.
    // Corner directions therefore follow the object aspect ratio. All cameras look at
    // its centre. View supports From, Up and SizePixels; opacity is computed per field.
    public static View[] Views = new View[]
    {
        new View { Name = "Xp", From = new Vector3(1, 0, 0) },
        new View { Name = "Xn", From = new Vector3(-1, 0, 0) },
        new View { Name = "Yp", From = new Vector3(0, 1, 0) },
        new View { Name = "Yn", From = new Vector3(0, -1, 0) },
        new View { Name = "Zp", From = new Vector3(0, 0, 1), Up = Vector3.UnitY },
        new View { Name = "Zn", From = new Vector3(0, 0, -1), Up = Vector3.UnitY },
        new View { Name = "XpYpZp", From = new Vector3(1, 1, 1) },
        new View { Name = "XpYpZn", From = new Vector3(1, 1, -1) },
        new View { Name = "XpYnZp", From = new Vector3(1, -1, 1) },
        new View { Name = "XpYnZn", From = new Vector3(1, -1, -1) },
        new View { Name = "XnYpZp", From = new Vector3(-1, 1, 1) },
        new View { Name = "XnYpZn", From = new Vector3(-1, 1, -1) },
        new View { Name = "XnYnZp", From = new Vector3(-1, -1, 1) },
        new View { Name = "XnYnZn", From = new Vector3(-1, -1, -1) }
    };

    public const int SelfRendered = 1;
    public const int PredecessorConsumed = 2;
    public static ConcurrentDictionary<string, int> PendingDeletion = new();
}
