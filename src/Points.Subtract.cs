using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;

public static partial class Points
{
    // Double-precision vector difference.
    public static D3 Subtract(D3 a, D3 b)
    {
        return new D3 { X = a.X - b.X, Y = a.Y - b.Y, Z = a.Z - b.Z };
    }
}
