using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Orthographic projection: dot products with camera right/up determine pixels.
    // There is no perspective division, ray integration or reconstruction of a surface.
    public static SKPoint Project(Vector3 position, Camera camera)
    {
        Vector3 relative = position - camera.Centre;
        return new SKPoint(camera.ScreenX + Vector3.Dot(relative, camera.Right) * camera.Scale, camera.ScreenY - Vector3.Dot(relative, camera.Up) * camera.Scale);
    }
}
