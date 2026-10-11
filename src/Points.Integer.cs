using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;

public static partial class Points
{
    // Parse a label/count using invariant culture so locale cannot alter syntax.
    public static int Integer(Tokens tokens)
    {
        Next(tokens);
        return int.Parse(tokens.Text.AsSpan(0, tokens.Length), CultureInfo.InvariantCulture);
    }
}
