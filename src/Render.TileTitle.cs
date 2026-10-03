using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Label the viewing direction; positive axis names describe the eye side.
    public static void TileTitle(SKCanvas canvas, float font, string title, float offsetX, float offsetY)
    {
        float textWidth = PlotWidth - 2 * MarginPixels;
        SKRect header = new SKRect(offsetX + MarginPixels, offsetY + MarginPixels, offsetX + PlotWidth - MarginPixels, offsetY + 2 * MarginPixels + LabelFontPixels);
        float baseline = offsetY + MarginPixels + LabelFontPixels;
        using SKPaint paint = new SKPaint { Color = LabelColor, IsAntialias = true };
        canvas.Save();
        canvas.ClipRect(header);
        LineFont.Draw(canvas, title, offsetX + PlotWidth * 0.5f, baseline, SKTextAlign.Center, font, paint, textWidth);
        canvas.Restore();
    }
}
