using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Build a 4096-entry blue-to-red HSV hue ramp. This is a qualitative rainbow
    // scale, not perceptually uniform; read the numeric legend for each frame.
    public static SKColor[] Palette()
    {
        SKColor[] colors = new SKColor[4096];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = SKColor.FromHsv(240f * (1f - (float)i / (colors.Length - 1)), 100, 100, 255);
        }
        return colors;
    }
}
