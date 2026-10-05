using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Title line: view, field, point count (time right-aligned). Subtitle: legend scale. Then the colour bar.
    // The legend describes colour only; it does not report the opacity delta scale.
    public static void Header(SKCanvas canvas, float font, string view, string time, Field field, int count, double minimum, double maximum)
    {
        using SKPaint textPaint = new SKPaint { Color = LabelColor, IsAntialias = true };
        using SKPaint secondaryPaint = new SKPaint { Color = Theme.Secondary, IsAntialias = true };
        float left = MarginPixels;
        float right = PlotWidth - MarginPixels;
        float width = right - left;
        float titleBaseline = MarginPixels + LabelFontPixels;
        string title = view + "  -  " + field.Name + "  -  " + count.ToString("N0", CultureInfo.InvariantCulture) + " points";
        LineFont.Draw(canvas, title, left, titleBaseline, SKTextAlign.Left, font, textPaint, width * 0.75f);
        LineFont.Draw(canvas, "t = " + FormatTime(time), right, titleBaseline, SKTextAlign.Right, font, textPaint, width * 0.25f);

        string unitSuffix = string.IsNullOrEmpty(field.Unit) ? "" : " " + field.Unit;
        string decimalFormat = "F" + field.DecimalPlaces.ToString(CultureInfo.InvariantCulture);
        string scale = minimum == maximum
            ? "uniform: midpoint colour"
            : minimum.ToString(decimalFormat, CultureInfo.InvariantCulture) + unitSuffix + " -> " + maximum.ToString(decimalFormat, CultureInfo.InvariantCulture) + unitSuffix;
        float subtitleBaseline = titleBaseline + LabelFontPixels + 14;
        LineFont.Draw(canvas, scale, left, subtitleBaseline, SKTextAlign.Left, font, secondaryPaint, width);

        float barTop = subtitleBaseline + 14;
        float barBottom = barTop + LabelFontPixels;
        SKColor[] palette = Palette();
        using SKPaint barPaint = new SKPaint { IsAntialias = false, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        int bars = (int)width;
        for (int px = 0; px < bars; px++)
        {
            barPaint.Color = palette[(int)((float)px / (bars - 1) * (palette.Length - 1))];
            canvas.DrawLine(left + px, barTop, left + px, barBottom, barPaint);
        }
        using SKPaint edge = new SKPaint { Color = Theme.Grid, IsAntialias = false, StrokeWidth = 2, Style = SKPaintStyle.Stroke };
        canvas.DrawRect(left, barTop, width, barBottom - barTop, edge);
    }
}
