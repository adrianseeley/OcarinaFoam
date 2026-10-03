using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Read an ASCII label list, including repeated-value brace representation.
    // The array holds mesh connectivity only while geometry is reconstructed.
    public static int[] ReadLabels(string path)
    {
        using StreamReader reader = OpenText(path);
        Tokens tokens = new Tokens { Reader = reader };
        SkipHeader(tokens);
        int[] labels = new int[Integer(tokens)];
        Next(tokens);
        bool repeated = Is(tokens, "{");
        int value = repeated ? Integer(tokens) : 0;
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i] = repeated ? value : Integer(tokens);
        }
        Expect(tokens, repeated ? "}" : ")");
        return labels;
    }
}
