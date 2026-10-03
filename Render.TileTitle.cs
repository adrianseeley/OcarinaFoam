using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Label the viewing direction; positive axis names describe the eye side.
    public static void TileTitle(SKCanvas canvas, float font, View view, float offsetX, float offsetY)
    {
        using SKPaint paint = new SKPaint { Color = LabelColor, IsAntialias = true };
        LineFont.Draw(canvas, view.Name, offsetX + PlotWidth * 0.5f, offsetY + LabelFontPixels + 12, SKTextAlign.Center, font, paint);
    }
}
