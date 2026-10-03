using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Multiply a geometry vector by a scalar.
    public static D3 Scale(D3 a, double s)
    {
        return new D3 { X = a.X * s, Y = a.Y * s, Z = a.Z * s };
    }
}
