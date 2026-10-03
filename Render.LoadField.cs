using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Map current scalar values to colour and absolute temporal differences to alpha.
    // Colour = (value - current minimum) / current range; uniform fields use 0.5.
    // Alpha = abs(current - predecessor) / largest such difference; no change gives 0.
    // The first discovered time has no predecessor and uses alpha 1. U is reduced to
    // speed BEFORE differencing, so direction changes at constant speed are invisible.
    // The predecessor is the previous queued available time, not necessarily t-deltaT.
    // This highlights change, not a spatial gradient, acoustic energy or calibrated SPL.
    public static void LoadField(string[] processors, int[] offsets, string time, string previousTime, Field field, Particle[] cloud, float[] alpha, out double minimum, out double maximum)
    {
        // Keep pressure as double until AFTER normalisation (tiny changes near 101325 Pa).
        double[] values = new double[cloud.Length];
        minimum = double.PositiveInfinity;
        maximum = double.NegativeInfinity;
        for (int p = 0; p < processors.Length; p++)
        {
            ReadField(Path.Combine(processors[p], time, field.File), field.Vector, values, offsets[p], offsets[p + 1] - offsets[p], ref minimum, ref maximum);
            Collect();
        }
        if (previousTime == null)
        {
            for (int i = 0; i < alpha.Length; i++)
            {
                alpha[i] = 1f; // No prior frame to compare against: show every point at full strength.
            }
        }
        else
        {
            double[] previousValues = new double[cloud.Length];
            double previousMinimum = double.PositiveInfinity;
            double previousMaximum = double.NegativeInfinity;
            for (int p = 0; p < processors.Length; p++)
            {
                ReadField(Path.Combine(processors[p], previousTime, field.File), field.Vector, previousValues, offsets[p], offsets[p + 1] - offsets[p], ref previousMinimum, ref previousMaximum);
                Collect();
            }
            double[] deltas = new double[cloud.Length];
            double maxDelta = 0;
            for (int i = 0; i < deltas.Length; i++)
            {
                deltas[i] = Math.Abs(values[i] - previousValues[i]);
                maxDelta = Math.Max(maxDelta, deltas[i]);
            }
            for (int i = 0; i < alpha.Length; i++)
            {
                alpha[i] = maxDelta == 0 ? 0f : (float)(deltas[i] / maxDelta); // Brightest where this frame changed most.
            }
        }
        double range = maximum - minimum;
        for (int i = 0; i < cloud.Length; i++)
        {
            cloud[i].Color = range == 0 ? 0.5f : (float)((values[i] - minimum) / range);
        }
    }
}
