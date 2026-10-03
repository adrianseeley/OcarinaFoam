using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Show physical simulation time in seconds, milliseconds, microseconds and
    // nanoseconds; this annotation remains meaningful at any encoded playback speed.
    public static string FormatTime(string time)
    {
        double seconds = double.Parse(time, NumberStyles.Float, CultureInfo.InvariantCulture);
        long totalNanoseconds = (long)Math.Round(seconds * 1_000_000_000d);
        long nanoseconds = totalNanoseconds % 1000;
        long microseconds = (totalNanoseconds / 1_000) % 1000;
        long milliseconds = (totalNanoseconds / 1_000_000) % 1000;
        long wholeSeconds = totalNanoseconds / 1_000_000_000;
        return wholeSeconds + "s " + milliseconds.ToString("000", CultureInfo.InvariantCulture) + "ms "
            + microseconds.ToString("000", CultureInfo.InvariantCulture) + "us "
            + nanoseconds.ToString("000", CultureInfo.InvariantCulture) + "ns";
    }
}
