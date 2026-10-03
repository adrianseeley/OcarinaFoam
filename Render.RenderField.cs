using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Create one opaque composite bitmap for this field/time. For every camera,
    // sort the complete merged cloud far-to-near before alpha blending. Separate
    // partition sorting would produce incorrect overlap at processor boundaries.
    // The sorted order is recomputed per view; image encoding occurs after all tiles.
    public static void RenderField(int frame, string time, Field field, Particle[] cloud, float[] alpha, float font, double minimum, double maximum)
    {
        int compositeWidth = GridColumns * PlotWidth;
        int compositeHeight = GridRows * PlotHeight;
        using SKBitmap bitmap = new SKBitmap(compositeWidth, compositeHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using SKCanvas canvas = new SKCanvas(bitmap);
        canvas.Clear(BackgroundColor);
        using SKPaint paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        int[] order = new int[cloud.Length];
        float[] depth = new float[cloud.Length];
        for (int row = 0; row < GridRows; row++)
        {
            for (int column = 0; column < GridColumns; column++)
            {
                float offsetX = column * PlotWidth;
                float offsetY = row * PlotHeight;
                RenderTileDefinition tile = Tiles[row][column];
                if (tile.Legend)
                {
                    InfoTile(canvas, font, time, field, cloud.Length, minimum, maximum, offsetX, offsetY);
                    continue;
                }
                Camera camera = TileCameras[row][column];
                for (int i = 0; i < cloud.Length; i++)
                {
                    order[i] = i;
                    depth[i] = Vector3.Dot(cloud[i].Position - camera.Centre, camera.TowardEye);
                }
                Array.Sort(depth, order); // Far to near across ALL processors.
                RenderTile(canvas, paint, tile, camera, cloud, alpha, order, font, offsetX, offsetY);
                Collect();
            }
        }
        DrawGrid(canvas, compositeWidth, compositeHeight);
        string stem = Path.Combine(OutputDirectory, frame.ToString("D8", CultureInfo.InvariantCulture) + "." + field.Name);
        SavePng(bitmap, stem + ".png");
        Console.WriteLine("  " + field.Name + " composite saved (" + CameraTileCount + " cameras, " + LegendTileCount + " legends, " + compositeWidth + "x" + compositeHeight + ")");
    }
}
