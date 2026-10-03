using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Parse a floating-point token, including exponent notation, using invariant culture.
    public static double Number(Tokens tokens)
    {
        Next(tokens);
        return double.Parse(tokens.Text.AsSpan(0, tokens.Length), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
