using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Dot product for signed volume weights and squared magnitudes.
    public static double Dot(D3 a, D3 b)
    {
        return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }
}
