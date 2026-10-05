using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // One opaque frame per view: header, then the cloud sorted far-to-near across the complete
    // merged cloud (separate partition sorting would mis-order overlap at processor boundaries).
    public static void RenderField(int frame, string time, Field field, Particle[] cloud, float[] alpha, float font, double minimum, double maximum)
    {
        int[] order = new int[cloud.Length];
        float[] depth = new float[cloud.Length];
        for (int v = 0; v < Views.Length; v++)
        {
            Camera camera = ViewCameras[v];
            using SKBitmap bitmap = new SKBitmap(PlotWidth, PlotHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using SKCanvas canvas = new SKCanvas(bitmap);
            canvas.Clear(BackgroundColor);
            using SKPaint paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            for (int i = 0; i < cloud.Length; i++)
            {
                order[i] = i;
                depth[i] = Vector3.Dot(cloud[i].Position - camera.Centre, camera.TowardEye);
            }
            Array.Sort(depth, order);
            RenderTile(canvas, paint, camera, cloud, alpha, order);
            Header(canvas, font, Views[v].Name, time, field, cloud.Length, minimum, maximum);
            string directory = Path.Combine(OutputDirectory, field.Name, Views[v].Name);
            Directory.CreateDirectory(directory);
            SavePng(bitmap, Path.Combine(directory, frame.ToString("D9", CultureInfo.InvariantCulture) + ".png"));
            Collect();
        }
        Console.WriteLine("  " + field.Name + " saved (" + Views.Length + " views, " + PlotWidth + "x" + PlotHeight + ")");
    }
}
