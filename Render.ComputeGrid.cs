using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Search integer tile layouts for aspect ratio closest to 2:1, allowing spare tiles.
    // Fourteen views plus the legend with square tiles gives 5 columns x 3 rows.
    public static void ComputeGrid(int count, out int columns, out int rows)
    {
        columns = count;
        rows = 1;
        double bestError = double.MaxValue;
        for (int r = 1; r <= count; r++)
        {
            int c = (count + r - 1) / r; // Ceil so every view has a tile.
            double ratio = (c * (double)PlotWidth) / (r * (double)PlotHeight);
            double error = Math.Abs(ratio - 2.0);
            if (error < bestError)
            {
                bestError = error;
                columns = c;
                rows = r;
            }
        }
    }
}
