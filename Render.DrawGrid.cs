using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Separate views and legend visually without changing projection or field values.
    public static void DrawGrid(SKCanvas canvas, int compositeWidth, int compositeHeight)
    {
        using SKPaint paint = new SKPaint
        {
            Color = LabelColor,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke,
            IsAntialias = false
        };
        for (int c = 1; c < GridColumns; c++) // Interior boundaries only: no double line where tiles meet.
        {
            float x = c * PlotWidth - 0.5f;
            canvas.DrawLine(x, 0, x, compositeHeight, paint);
        }
        for (int r = 1; r < GridRows; r++)
        {
            float y = r * PlotHeight - 0.5f;
            canvas.DrawLine(0, y, compositeWidth, y, paint);
        }
        canvas.DrawRect(0.5f, 0.5f, compositeWidth - 1, compositeHeight - 1, paint); // Outermost border kept fully inside the canvas.
    }
}
