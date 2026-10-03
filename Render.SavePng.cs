using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Encode to a neighbouring temporary file, flush it, then replace the final PNG.
    // This avoids advertising an incomplete PNG as finished during normal operation.
    // Publication is per image, not a transaction across fields or raw-data deletion.
    public static void SavePng(SKBitmap bitmap, string path)
    {
        using (SKPixmap pixels = bitmap.PeekPixels())
        using (FileStream output = File.Create(path + ".tmp"))
        {
            SKPngEncoderOptions options = new SKPngEncoderOptions(SKPngEncoderFilterFlags.None, PngCompressionLevel);
            if (!pixels.Encode(output, options))
            {
                throw new IOException("PNG encoding failed: " + path);
            }
            output.Flush(flushToDisk: true);
        }
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
