using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Rodrigues rotation about a unit axis; used for the optional two-axis camera tilt.
    public static Vector3 RotateAroundAxis(Vector3 v, Vector3 axis, float angleRadians)
    {
        float cos = MathF.Cos(angleRadians);
        float sin = MathF.Sin(angleRadians);
        return v * cos + Vector3.Cross(axis, v) * sin + axis * Vector3.Dot(axis, v) * (1 - cos);
    }
}
