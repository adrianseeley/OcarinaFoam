using System.IO.Compression;

// Cell-centre positions are written once at build time (points/position.bin.gz: XYZXYZ... as float32).
// The watcher then turns each raw OpenFOAM write into points/SSSsMMMmsUUUusNNNns.bin.gz: for every point in
// the same order, the enabled fields' float32 components back to back (see PointsConfig). The mesh never
// changes, so no ids are stored.
public static partial class Points
{
    public const int UntouchedTimes = 3;
    public static string CaseDirectory = "";
    public static string OutputDirectory = "";
    public static int ProcessorCount = 0;
    public static int PollMilliseconds = 0;
    public static CompressionLevel Compression = CompressionLevel.Optimal;

    public class Layout { public string File; public bool Vector; public string Name; }

    // Fixed order; the bytes of a frame follow this list filtered by config.
    public static Layout[] Fields = Array.Empty<Layout>();
    public static int Stride => Fields.Sum(f => f.Vector ? 3 : 1);

    public static Layout[] Selected(PointsConfig c)
    {
        var all = new (bool On, Layout Field)[]
        {
            (c.velocity, new Layout { File = "U", Vector = true, Name = "velocity" }),
            (c.temperature, new Layout { File = "T", Name = "temperature" }),
            (c.pressure, new Layout { File = "p", Name = "pressure" }),
            (c.density, new Layout { File = "rho", Name = "density" })
        };
        return all.Where(a => a.On).Select(a => a.Field).ToArray();
    }

    public static CompressionLevel ParseCompression(string name) => name switch
    {
        "fastest" => CompressionLevel.Fastest,
        "optimal" => CompressionLevel.Optimal,
        "smallest" => CompressionLevel.SmallestSize,
        _ => throw new Exception("points.compression must be \"fastest\", \"optimal\" or \"smallest\".")
    };

    public static string PointsDirectory(string root) => Path.Combine(root, "points");
    public static string PositionFile(string root) => Path.Combine(PointsDirectory(root), "position.bin.gz");

    public static void Configure(string root, Config c)
    {
        CaseDirectory = Paths.Foam(root);
        OutputDirectory = PointsDirectory(root);
        ProcessorCount = c.processorCount;
        PollMilliseconds = c.points.pollMilliseconds;
        Compression = ParseCompression(c.points.compression);
        Fields = Selected(c.points);
    }

    // Frame file stem from an OpenFOAM time name, exact to the nanosecond: 000s000ms000us000ns.
    public static string FrameName(string time)
    {
        decimal ns = Math.Round(decimal.Parse(time, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture) * 1_000_000_000m);
        long n = (long)ns;
        return $"{n / 1_000_000_000:D3}s{n / 1_000_000 % 1000:D3}ms{n / 1000 % 1000:D3}us{n % 1000:D3}ns";
    }
    public static string FramePath(string time) => Path.Combine(OutputDirectory, FrameName(time) + ".bin.gz");
    public static bool IsComplete(string time) => File.Exists(FramePath(time));

    public static int FrameCount(string directory)
    {
        if (!Directory.Exists(directory)) return 0;
        return Directory.EnumerateFiles(directory, "*s*ms*us*ns.bin.gz").Count();
    }

    // Compress to a temporary file, then rename, so readers never see a partial frame.
    public static void WriteFloats(string path, float[] values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            using (var gzip = new GZipStream(file, Compression, true))
                gzip.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()));
            file.Flush(true);
        }
        File.Move(temporary, path, true);
    }

    // Fully parse a field file; throws if it is truncated or malformed.
    public static void Validate(string path, bool vector, int count) => ReadField(path, vector, new float[count * (vector ? 3 : 1)], 0, count, vector ? 3 : 1, 0);

    public static int[] Offsets(string[] processors)
    {
        int[] offsets = new int[processors.Length + 1];
        for (int p = 0; p < processors.Length; p++)
            offsets[p + 1] = checked(offsets[p] + CellCount(Path.Combine(processors[p], "constant", "polyMesh")));
        return offsets;
    }

    // Used by build, before the mesh is moved into place. Processor order fixes the point order for ever.
    public static int WritePositions(string root, string foam, int processorCount)
    {
        string[] processors = processorCount == 1 ? new[] { foam } : Enumerable.Range(0, processorCount).Select(i => Path.Combine(foam, "processor" + i)).ToArray();
        int[] offsets = Offsets(processors);
        float[] positions = new float[checked(offsets[^1] * 3)];
        for (int p = 0; p < processors.Length; p++)
        {
            ReadCentres(Path.Combine(processors[p], "constant", "polyMesh"), positions, offsets[p], offsets[p + 1] - offsets[p]);
            Collect();
        }
        string target = PositionFile(root);
        WriteFloats(target, positions);
        return offsets[^1];
    }
}
