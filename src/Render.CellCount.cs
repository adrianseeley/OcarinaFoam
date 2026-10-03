using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Infer dense local cell count from the largest owner/neighbour cell label.
    // Faces belong to an owner cell; internal faces also have a neighbour cell.
    // Assumes ordinary OpenFOAM local numbering, with labels starting at zero.
    public static int CellCount(string mesh)
    {
        int maximum = -1;
        string[] files = new string[] { "owner", "neighbour" };
        for (int f = 0; f < files.Length; f++)
        {
            using StreamReader reader = OpenText(Path.Combine(mesh, files[f]));
            Tokens tokens = new Tokens { Reader = reader };
            SkipHeader(tokens);
            int count = Integer(tokens);
            Next(tokens);
            bool repeated = Is(tokens, "{");
            int reads = repeated ? 1 : count;
            for (int i = 0; i < reads; i++)
            {
                maximum = Math.Max(maximum, Integer(tokens));
            }
            Expect(tokens, repeated ? "}" : ")");
        }
        return maximum + 1;
    }
}
