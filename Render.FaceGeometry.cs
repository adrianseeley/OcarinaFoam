using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Triangulate each polygon as a fan around its mean vertex position. Sum oriented
    // triangle areas and magnitude-weighted triangle centroids. Face winding determines
    // the area-vector direction used to distinguish owner from neighbour contributions.
    public static void FaceGeometry(D3[] vertices, int[] face, int count, out D3 centre, out D3 area)
    {
        D3 average = new D3();
        for (int i = 0; i < count; i++)
        {
            average = Add(average, vertices[face[i]]);
        }
        average = Scale(average, 1.0 / count);
        area = new D3();
        centre = new D3();
        double weight = 0;
        for (int i = 0; i < count; i++)
        {
            D3 a = vertices[face[i]];
            D3 b = vertices[face[(i + 1) % count]];
            D3 cross = Cross(Subtract(a, average), Subtract(b, average));
            double magnitude = Math.Sqrt(Dot(cross, cross));
            area = Add(area, Scale(cross, 0.5));
            centre = Add(centre, Scale(Add(Add(a, b), average), magnitude / 3));
            weight += magnitude;
        }
        centre = Scale(centre, 1 / weight);
    }
}
