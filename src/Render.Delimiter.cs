using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Recognise punctuation needed by headers, dimensions, vectors and lists.
    public static bool Delimiter(int c)
    {
        return c == '(' || c == ')' || c == '{' || c == '}' || c == '[' || c == ']' || c == ';';
    }
}
