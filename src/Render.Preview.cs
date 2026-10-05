using System.Numerics;
using SkiaSharp;

public static partial class Renderer
{
    public static void Preview(string root)
    {
        string surface = Path.Combine(CaseDirectory, "constant", "triSurface", "solidBody.stl");
        if (!File.Exists(surface)) throw new FileNotFoundException("Built solidBody.stl is missing.", surface);
        Facet[] facets = Geometry.Read(surface);
        using SKPaint linePaint = new SKPaint { IsAntialias = true, Color = Theme.Secondary, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        string previewDirectory = Path.Combine(root, "previews");
        Directory.CreateDirectory(previewDirectory);
        for (int v = 0; v < Views.Length; v++)
        {
            using SKBitmap bitmap = new SKBitmap(PlotWidth, PlotHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using SKCanvas canvas = new SKCanvas(bitmap);
            canvas.Clear(BackgroundColor);
            Camera camera = ViewCameras[v];
            canvas.Save();
            canvas.ClipRect(TileContentRect());
            foreach (Facet facet in facets)
            {
                Vector3 a = new Vector3((float)facet.Vertices[0][0], (float)facet.Vertices[0][1], (float)facet.Vertices[0][2]);
                Vector3 b = new Vector3((float)facet.Vertices[1][0], (float)facet.Vertices[1][1], (float)facet.Vertices[1][2]);
                Vector3 c = new Vector3((float)facet.Vertices[2][0], (float)facet.Vertices[2][1], (float)facet.Vertices[2][2]);
                canvas.DrawLine(Project(a, camera), Project(b, camera), linePaint);
                canvas.DrawLine(Project(b, camera), Project(c, camera), linePaint);
                canvas.DrawLine(Project(c, camera), Project(a, camera), linePaint);
            }
            canvas.Restore();
            Header(canvas, LabelFontPixels, Views[v].Name, "0", new Field { Name = "layout preview", DecimalPlaces = 0 }, 0, 0, 1);
            string output = Path.Combine(previewDirectory, "layout_" + Views[v].Name + ".png");
            SavePng(bitmap, output);
            Console.WriteLine("Preview saved: " + output);
        }
    }
}
