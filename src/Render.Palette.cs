using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Blue-to-red hue ramp (see Theme).
    public static SKColor[] Palette() => Theme.Table();
}
