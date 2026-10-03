using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Read static mesh points in double precision for centroid calculations.
    // Parenthesised point lists are expected; this is not a general OpenFOAM reader.
    public static D3[] ReadVertices(string path)
    {
        using StreamReader reader = OpenText(path);
        Tokens tokens = new Tokens { Reader = reader };
        SkipHeader(tokens);
        D3[] vertices = new D3[Integer(tokens)];
        Expect(tokens, "(");
        for (int i = 0; i < vertices.Length; i++)
        {
            Expect(tokens, "(");
            D3 v = new D3 { X = Number(tokens), Y = Number(tokens), Z = Number(tokens) };
            Expect(tokens, ")");
            vertices[i] = v;
        }
        Expect(tokens, ")");
        return vertices;
    }
}
