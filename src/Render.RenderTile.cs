using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Draw cell-centre circles with normalised scalar hue and delta-driven opacity.
    // Clip to this tile. Point size is in pixels, not cell volume: refined regions
    // contain more samples and can appear denser/brighter through overlap.
    public static void RenderTile(SKCanvas canvas, SKPaint paint, RenderTileDefinition tile, Camera camera, Particle[] cloud, float[] alpha, int[] order, float font, float offsetX, float offsetY)
    {
        SKRect content = TileContentRect(offsetX, offsetY);
        canvas.Save();
        canvas.ClipRect(content); // Confine points to this tile content only.
        SKColor[] pointPalette = Palette();
        float radius = PointSizePixels * 0.5f;
        for (int i = 0; i < order.Length; i++)
        {
            int index = order[i];
            if (alpha[index] <= 0f)
            {
                continue; // Unchanged since the previous frame: nothing new to show here.
            }
            Particle point = cloud[index];
            SKPoint screen = Project(point.Position, camera);
            int colorIndex = (int)(point.Color * (pointPalette.Length - 1));
            paint.Color = pointPalette[colorIndex].WithAlpha((byte)Math.Round(alpha[index] * 255));
            canvas.DrawCircle(screen, radius, paint);
        }
        canvas.Restore();
        TileTitle(canvas, font, tile.Name, offsetX, offsetY);
    }
}
