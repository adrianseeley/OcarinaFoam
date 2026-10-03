using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Accumulate a face pyramid for one cell. sign reverses the shared face for its
    // neighbour. The scalar weight is three times signed pyramid volume; the common
    // factor cancels when moments are divided by weights. A pyramid centroid lies
    // three quarters of the way from its apex to the face centroid.
    public static void AccumulatePyramid(int cell, D3 centre, D3 area, double sign, D3[] estimates, D3[] moments, double[] weights)
    {
        double weight = sign * Dot(area, Subtract(centre, estimates[cell]));
        D3 centroid = Add(Scale(centre, 0.75), Scale(estimates[cell], 0.25));
        moments[cell] = Add(moments[cell], Scale(centroid, weight));
        weights[cell] += weight;
    }
}
