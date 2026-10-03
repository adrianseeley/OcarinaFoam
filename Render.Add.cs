using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Double-precision geometry arithmetic used during centroid reconstruction.
    public static D3 Add(D3 a, D3 b)
    {
        return new D3 { X = a.X + b.X, Y = a.Y + b.Y, Z = a.Z + b.Z };
    }
}
