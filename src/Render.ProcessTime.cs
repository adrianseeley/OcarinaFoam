using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Process enabled fields serially within this job, writing a composite for each.
    // Each field computes its own extrema and delta maximum across ALL partitions.
    // No scale is shared between fields or between timesteps.
    public static void ProcessTime(string[] processors, int[] offsets, string time, string previousTime, Particle[] cloud, float[] alpha, float font)
    {
        if (IsComplete(time)) return;
        for (int f = 0; f < Fields.Length; f++)
        {
            if (!Fields[f].Enabled)
            {
                continue;
            }
            double minimum;
            double maximum;
            LoadField(processors, offsets, time, previousTime, Fields[f], cloud, alpha, out minimum, out maximum);
            Collect(); // Raw doubles and parsers have gone; cloud holds normalised colours.
            int visibleCount = 0;
            for (int i = 0; i < alpha.Length; i++)
            {
                if (alpha[i] > 0f)
                {
                    visibleCount++;
                }
            }
            Console.WriteLine(time + " " + Fields[f].Name + " [" + minimum.ToString("G17") + ", " + maximum.ToString("G17") + "] "
                + cloud.Length + " points, " + visibleCount + " visible");
            RenderField(TimeFrame(time), time, Fields[f], cloud, alpha, font, minimum, maximum);
            Collect();
        }
        Complete(time, previousTime);
    }
}
