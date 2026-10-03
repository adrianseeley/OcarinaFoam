using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Reconstruct a volume centroid for each polyhedral cell without OpenFOAM/VTK.
    // Pass 1 averages incident face centres for a reference point. Pass 2 sums signed
    // face-pyramid moments about that reference, then divides by total weight.
    // Rereading faces reduces retained connectivity memory. Positions become float
    // only after the double-precision geometry work; degenerate cells are not repaired.
    public static void ReadCentres(string mesh, Particle[] cloud, int offset, int count)
    {
        int[] owner = ReadLabels(Path.Combine(mesh, "owner"));
        int[] neighbour = ReadLabels(Path.Combine(mesh, "neighbour"));
        D3[] vertices = ReadVertices(Path.Combine(mesh, "points"));
        D3[] estimates = new D3[count];
        double[] weights = new double[count];
        D3[] moments = new D3[count];
        int[] face = new int[16];

        // First pass: mean face centres. Second: volume-weighted face pyramids.
        // Reread faces to avoid retaining the complete mesh connectivity/face geometry.
        for (int pass = 0; pass < 2; pass++)
        {
            using StreamReader reader = OpenText(Path.Combine(mesh, "faces"));
            Tokens tokens = new Tokens { Reader = reader };
            SkipHeader(tokens);
            int faces = Integer(tokens);
            Expect(tokens, "(");
            for (int f = 0; f < faces; f++)
            {
                int size = Integer(tokens);
                if (face.Length < size)
                {
                    Array.Resize(ref face, size);
                }
                Expect(tokens, "(");
                for (int j = 0; j < size; j++)
                {
                    face[j] = Integer(tokens);
                }
                Expect(tokens, ")");
                D3 centre;
                D3 area;
                FaceGeometry(vertices, face, size, out centre, out area);
                int a = owner[f];
                int b = f < neighbour.Length ? neighbour[f] : -1;
                if (pass == 0)
                {
                    estimates[a] = Add(estimates[a], centre);
                    weights[a]++;
                    if (b >= 0)
                    {
                        estimates[b] = Add(estimates[b], centre);
                        weights[b]++;
                    }
                }
                else
                {
                    AccumulatePyramid(a, centre, area, 1, estimates, moments, weights);
                    if (b >= 0)
                    {
                        AccumulatePyramid(b, centre, area, -1, estimates, moments, weights);
                    }
                }
            }
            Expect(tokens, ")");
            if (pass == 0)
            {
                for (int c = 0; c < count; c++)
                {
                    estimates[c] = Scale(estimates[c], 1 / weights[c]);
                    weights[c] = 0;
                }
            }
        }
        for (int c = 0; c < count; c++)
        {
            if (!double.IsFinite(weights[c]) || Math.Abs(weights[c]) < 1e-30) throw new FormatException("Degenerate mesh cell " + c);
            cloud[offset + c].Position = Float3(Scale(moments[c], 1 / weights[c]));
        }
    }
}
