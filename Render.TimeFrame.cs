using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Round physical time divided by configured field interval to a stable frame index.
    // Changing the interval mid-run can cause gaps/collisions. The int return type
    // also limits the usable frame range for unusually long or densely sampled runs.
    public static int TimeFrame(string time)
    {
        // Derived from the write-time-step, not from counting existing PNGs, so several
        // output numbering survives missing files. It does not partition jobs across processes.
        double value = double.Parse(time, NumberStyles.Float, CultureInfo.InvariantCulture);
        return (int)Math.Round(value / WriteTimeStep, MidpointRounding.AwayFromZero);
    }
}
