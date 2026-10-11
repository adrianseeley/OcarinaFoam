using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;

public static partial class Points
{
    // Convert final mesh positions to the float representation used by drawing.
    public static Vector3 Float3(D3 a)
    {
        return new Vector3((float)a.X, (float)a.Y, (float)a.Z);
    }
}
