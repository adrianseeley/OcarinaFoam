using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;

public static partial class Points
{
    // Consume one required token and fail with context when the input differs.
    public static void Expect(Tokens tokens, string value)
    {
        Next(tokens);
        if (!Is(tokens, value))
        {
            throw new FormatException("Expected " + value + ", got " + new string(tokens.Text, 0, tokens.Length));
        }
    }
}
