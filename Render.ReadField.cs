using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Read internalField only: uniform or nonuniform List<scalar>/List<vector>,
    // including a repeated brace value. Require count agreement and finite values.
    // Vector data becomes Euclidean magnitude. BoundaryField is not rendered;
    // binary, collated and arbitrary dictionary/include syntax are outside this parser.
    public static void ReadField(string path, bool vector, double[] values, int offset, int count, ref double minimum, ref double maximum)
    {
        using StreamReader reader = OpenText(path);
        Tokens tokens = new Tokens { Reader = reader };
        SkipHeader(tokens);
        do
        {
            Next(tokens);
        }
        while (!Is(tokens, "internalField"));
        Next(tokens);
        bool uniform = Is(tokens, "uniform");
        bool repeated = false;
        if (!uniform)
        {
            Expect(tokens, "List<" + (vector ? "vector" : "scalar") + ">");
            int entries = Integer(tokens);
            if (entries != count)
            {
                throw new FormatException(path + ": field/mesh cell count differs");
            }
            Next(tokens);
            repeated = Is(tokens, "{");
        }
        int reads = uniform || repeated ? 1 : count;
        double value = 0;
        for (int i = 0; i < reads; i++)
        {
            if (vector)
            {
                Expect(tokens, "(");
                double x = Number(tokens);
                double y = Number(tokens);
                double z = Number(tokens);
                Expect(tokens, ")");
                value = Math.Sqrt(x * x + y * y + z * z);
            }
            else
            {
                value = Number(tokens);
            }
            if (!double.IsFinite(value))
            {
                throw new ArithmeticException(path + ": nonfinite field value");
            }
            if (count > 0)
            {
                values[offset + i] = value;
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
        }
        if (uniform || repeated)
        {
            for (int i = 0; i < count; i++)
            {
                values[offset + i] = value;
            }
        }
        if (!uniform)
        {
            Expect(tokens, repeated ? "}" : ")");
        }
        Expect(tokens, ";");
        // Consume the whole boundary block and gzip trailer before publishing a frame.
        // The three-write delay is a scheduling rule, not a completeness guarantee.
        Expect(tokens, "boundaryField"); Expect(tokens, "{");
        int depth = 1;
        while (depth > 0) { Next(tokens); if (Is(tokens, "{")) depth++; else if (Is(tokens, "}")) depth--; }
        while (Next(tokens, true)) { }
    }
}
