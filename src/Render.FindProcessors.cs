using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Require exactly the configured processor paths rather than infer a partial count.
    // Extra unrelated processor directories are ignored; collated layouts are not supported; one rank uses the serial case. Mesh files are expected in each constant/polyMesh directory.
    public static string[] FindProcessors()
    {
        // Names are generated from ProcessorCount, not scanned, so a decomposition still
        // mid-write (some processorN directories not yet created) fails loudly here
        // instead of silently rendering a partial cloud.
        if (ProcessorCount == 1) return new[] { CaseDirectory };
        string[] result = new string[ProcessorCount];
        for (int i = 0; i < ProcessorCount; i++)
        {
            string directory = Path.Combine(CaseDirectory, "processor" + i);
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(directory + " not found (expected " + ProcessorCount + " processor directories)");
            }
            result[i] = directory;
        }
        return result;
    }
}
