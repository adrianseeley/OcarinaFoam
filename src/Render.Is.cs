using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Compare a token span without allocating a temporary string.
    public static bool Is(Tokens tokens, string value)
    {
        return tokens.Text.AsSpan(0, tokens.Length).SequenceEqual(value.AsSpan());
    }
}
