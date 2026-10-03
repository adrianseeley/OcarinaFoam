using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // STREAMED ASCII READER ---------------------------------------------------
    // One decompressor at a time, a small text buffer, and one reused token buffer.
    // Numbers parse from spans: no giant decompressed strings or string-per-cell churn.
    // Prefer an uncompressed file when both forms exist; otherwise decompress .gz
    // as a stream. At most one file is opened by this call; workers can each open one.
    public static StreamReader OpenText(string path)
    {
        if (File.Exists(path))
        {
            return new StreamReader(File.OpenRead(path), System.Text.Encoding.ASCII, false, 65536);
        }
        return new StreamReader(new GZipStream(File.OpenRead(path + ".gz"), CompressionMode.Decompress), System.Text.Encoding.ASCII, false, 65536);
    }
}
