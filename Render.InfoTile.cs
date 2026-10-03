using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Display timestep, field, total sample count and current scalar range.
    // Pressure and temperature labels use zero decimal places by default;
    // a small real range can therefore have identical rounded endpoint labels.
    // The legend describes colour only; it does not report the opacity delta scale.
    public static void InfoTile(SKCanvas canvas, float font, string time, Field field, int count, double minimum, double maximum, float offsetX, float offsetY)
    {
        canvas.Save();
        canvas.ClipRect(new SKRect(offsetX, offsetY, offsetX + PlotWidth, offsetY + PlotHeight));
        using SKPaint textPaint = new SKPaint { Color = LabelColor, IsAntialias = true };
        float x = offsetX + MarginPixels;
        float y = offsetY + MarginPixels + LabelFontPixels;
        LineFont.Draw(canvas, "t = " + FormatTime(time), x, y, SKTextAlign.Left, font, textPaint, PlotWidth - 2 * MarginPixels);
        y += LabelFontPixels + 8;
        LineFont.Draw(canvas, field.Name, x, y, SKTextAlign.Left, font, textPaint, PlotWidth - 2 * MarginPixels);
        y += LabelFontPixels + 8;
        LineFont.Draw(canvas, count.ToString("N0", CultureInfo.InvariantCulture) + " points", x, y, SKTextAlign.Left, font, textPaint, PlotWidth - 2 * MarginPixels);
        y += LabelFontPixels + 24;

        string unitSuffix = string.IsNullOrEmpty(field.Unit) ? "" : " " + field.Unit;
        string decimalFormat = "F" + field.DecimalPlaces.ToString(CultureInfo.InvariantCulture);
        string scale = minimum == maximum
            ? "uniform: midpoint colour"
            : minimum.ToString(decimalFormat, CultureInfo.InvariantCulture) + unitSuffix + " -> " + maximum.ToString(decimalFormat, CultureInfo.InvariantCulture) + unitSuffix;
        LineFont.Draw(canvas, scale, x, y, SKTextAlign.Left, font, textPaint, PlotWidth - 2 * MarginPixels);
        y += LabelFontPixels + 12;

        float legendLeft = offsetX + MarginPixels;
        float legendRight = offsetX + PlotWidth - MarginPixels;
        float legendTop = y;
        float legendBottom = y + LabelFontPixels * 2;
        SKColor[] palette = Palette();
        using SKPaint barPaint = new SKPaint { IsAntialias = false, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        int bars = (int)(legendRight - legendLeft);
        for (int px = 0; px < bars; px++)
        {
            barPaint.Color = palette[(int)((float)px / (bars - 1) * (palette.Length - 1))];
            float lx = legendLeft + px;
            canvas.DrawLine(lx, legendTop, lx, legendBottom, barPaint);
        }
        LineFont.Draw(canvas, "min", legendLeft, legendBottom + LabelFontPixels + 4, SKTextAlign.Left, font, textPaint, PlotWidth - 2 * MarginPixels);
        LineFont.Draw(canvas, "max", legendRight, legendBottom + LabelFontPixels + 4, SKTextAlign.Right, font, textPaint, PlotWidth - 2 * MarginPixels);
        canvas.Restore();
    }
}
