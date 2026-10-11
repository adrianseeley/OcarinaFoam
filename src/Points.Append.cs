using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;

public static partial class Points
{
    // Grow the reusable token buffer only when a longer token requires it.
    public static void Append(Tokens tokens, char c)
    {
        if (tokens.Length == tokens.Text.Length)
        {
            Array.Resize(ref tokens.Text, tokens.Text.Length * 2);
        }
        tokens.Text[tokens.Length++] = c;
    }
}
