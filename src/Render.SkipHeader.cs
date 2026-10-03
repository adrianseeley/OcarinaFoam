using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Skip a balanced FoamFile header before reading payload tokens.
    // Reject non-ASCII headers before parsing payloads.
    public static void SkipHeader(Tokens tokens)
    {
        Expect(tokens, "FoamFile");
        Expect(tokens, "{");
        int depth = 1;
        bool ascii = false;
        while (depth != 0)
        {
            Next(tokens);
            if (depth == 1 && Is(tokens, "format")) { Expect(tokens, "ascii"); Expect(tokens, ";"); ascii = true; }
            if (Is(tokens, "{"))
            {
                depth++;
            }
            if (Is(tokens, "}"))
            {
                depth--;
            }
        }
        if (!ascii) throw new FormatException("Expected an ASCII FoamFile header.");
    }
}
