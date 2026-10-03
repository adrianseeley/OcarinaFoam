using System.Numerics;
using SkiaSharp;

public static partial class Renderer
{
    public static void Preview(string root)
    {
        string surface = Path.Combine(CaseDirectory, "constant", "triSurface", "solidBody.stl");
        if (!File.Exists(surface)) throw new FileNotFoundException("Built solidBody.stl is missing.", surface);
        Facet[] facets = Geometry.Read(surface);
        int width = GridColumns * PlotWidth;
        int height = GridRows * PlotHeight;
        using SKBitmap bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using SKCanvas canvas = new SKCanvas(bitmap);
        canvas.Clear(BackgroundColor);
        using SKPaint linePaint = new SKPaint { IsAntialias = true, Color = LabelColor, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        float font = LabelFontPixels;
        for (int row = 0; row < GridRows; row++)
        {
            for (int column = 0; column < GridColumns; column++)
            {
                float offsetX = column * PlotWidth;
                float offsetY = row * PlotHeight;
                RenderTileDefinition tile = Tiles[row][column];
                if (tile.Legend)
                {
                    PreviewLegendTile(canvas, font, offsetX, offsetY);
                    continue;
                }
                Camera camera = TileCameras[row][column];
                SKRect content = TileContentRect(offsetX, offsetY);
                canvas.Save();
                canvas.ClipRect(content);
                foreach (Facet facet in facets)
                {
                    Vector3 a = new Vector3((float)facet.Vertices[0][0], (float)facet.Vertices[0][1], (float)facet.Vertices[0][2]) * 0.001f;
                    Vector3 b = new Vector3((float)facet.Vertices[1][0], (float)facet.Vertices[1][1], (float)facet.Vertices[1][2]) * 0.001f;
                    Vector3 c = new Vector3((float)facet.Vertices[2][0], (float)facet.Vertices[2][1], (float)facet.Vertices[2][2]) * 0.001f;
                    canvas.DrawLine(Project(a, camera), Project(b, camera), linePaint);
                    canvas.DrawLine(Project(b, camera), Project(c, camera), linePaint);
                    canvas.DrawLine(Project(c, camera), Project(a, camera), linePaint);
                }
                canvas.Restore();
                TileTitle(canvas, font, tile.Name, offsetX, offsetY);
            }
        }
        DrawGrid(canvas, width, height);
        string previewDirectory = Path.Combine(root, "previews");
        Directory.CreateDirectory(previewDirectory);
        string output = Path.Combine(previewDirectory, "layout.png");
        SavePng(bitmap, output);
        Console.WriteLine("Preview saved: " + output);
        if (RecipeCompatibilityNote.Length > 0) Console.WriteLine(RecipeCompatibilityNote);
    }

    public static void PreviewLegendTile(SKCanvas canvas, float font, float offsetX, float offsetY)
    {
        using SKPaint textPaint = new SKPaint { Color = LabelColor, IsAntialias = true };
        float x = offsetX + MarginPixels;
        float y = offsetY + MarginPixels + LabelFontPixels;
        float width = PlotWidth - 2 * MarginPixels;
        LineFont.Draw(canvas, "LAYOUT PREVIEW", x, y, SKTextAlign.Left, font, textPaint, width);
        y += LabelFontPixels + 8;
        LineFont.Draw(canvas, "NO FIELD DATA", x, y, SKTextAlign.Left, font, textPaint, width);
        y += LabelFontPixels + 24;
        LineFont.Draw(canvas, "sample color bar", x, y, SKTextAlign.Left, font, textPaint, width);
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
        LineFont.Draw(canvas, "low", legendLeft, legendBottom + LabelFontPixels + 4, SKTextAlign.Left, font, textPaint, width);
        LineFont.Draw(canvas, "high", legendRight, legendBottom + LabelFontPixels + 4, SKTextAlign.Right, font, textPaint, width);
    }
}
