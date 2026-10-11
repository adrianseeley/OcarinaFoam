using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;

public static partial class Points
{
    // Read internalField only: uniform or nonuniform List<scalar>/List<vector>,
    // including a repeated brace value. Require count agreement and finite values.
    // Each cell's components are written into the interleaved frame at
    // (cell * stride + slot); vectors keep all three components. A scalar has `bias` subtracted.
    // BoundaryField is skipped; binary, collated and arbitrary dictionary/include syntax are outside this parser.
    public static void ReadField(string path, bool vector, float[] frame, int offset, int count, int stride, int slot, double bias = 0)
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
        float[] value = new float[3];
        for (int i = 0; i < reads; i++)
        {
            if (vector)
            {
                Expect(tokens, "(");
                value[0] = (float)Number(tokens);
                value[1] = (float)Number(tokens);
                value[2] = (float)Number(tokens);
                Expect(tokens, ")");
            }
            else
            {
                // Subtracted in double precision before narrowing, so offset fields keep their small differences.
                value[0] = (float)(Number(tokens) - bias);
            }
            for (int k = 0; k < (vector ? 3 : 1); k++)
            {
                if (!float.IsFinite(value[k]))
                {
                    throw new ArithmeticException(path + ": nonfinite field value");
                }
                if (!(uniform || repeated)) frame[(offset + i) * stride + slot + k] = value[k];
            }
        }
        if (uniform || repeated)
        {
            for (int i = 0; i < count; i++)
            {
                for (int k = 0; k < (vector ? 3 : 1); k++) frame[(offset + i) * stride + slot + k] = value[k];
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
