using System.IO.Compression;

// Cell-centre positions are written once at build time (points/position.bin.br: XYZXYZ... as float32).
// The watcher then turns each raw OpenFOAM write into points/SSSsMMMmsUUUusNNNns.bin.br: for every point in
// the same order, the enabled fields' float32 components back to back (see PointsConfig). The mesh never
// changes, so no ids are stored.
// File encoding: the float32 array (point-major, channels interleaved) is byte-shuffled into planes, one per
// (channel, byte index), channel-major, then Brotli compressed. Shuffling only regroups bytes, so point
// order is untouched and decoding is exact; with mantissaBits < 23 the low mantissa bits are rounded away first.
public static partial class Points
{
    public const int UntouchedTimes = 3;
    public static string CaseDirectory = "";
    public static string OutputDirectory = "";
    public static int ProcessorCount = 0;
    public static int PollMilliseconds = 0;
    public static int Quality = 9;
    public static int MantissaBits = 23;
    public const int Window = 24;

    public class Layout { public string File; public bool Vector; public string Name; }
    // Pressure is stored as p minus this (Pa), set from physics.ambientPressureHectopascals; see Configure.
    public static double AmbientPressure = 0;

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

    public static int ParseCompression(string name) => name switch
    {
        "fastest" => 4,
        "optimal" => 9,
        "smallest" => 11,
        _ => throw new Exception("points.compression must be \"fastest\", \"optimal\" or \"smallest\".")
    };

    public static string PointsDirectory(string root) => Path.Combine(root, "points");
    public static string PositionFile(string root) => Path.Combine(PointsDirectory(root), "position.bin.br");

    public static void Configure(string root, Config c)
    {
        CaseDirectory = Paths.Foam(root);
        OutputDirectory = PointsDirectory(root);
        ProcessorCount = c.processorCount;
        PollMilliseconds = c.points.pollMilliseconds;
        Quality = ParseCompression(c.points.compression);
        MantissaBits = c.points.mantissaBits;
        AmbientPressure = c.physics.ambientPressureHectopascals * 100;
        Fields = Selected(c.points);
    }

    // Parses 000s000ms000us000ns into nanoseconds.
    public static long ParseTime(string text, string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text ?? "", @"^(\d{3})s(\d{3})ms(\d{3})us(\d{3})ns$");
        if (!m.Success) throw new Exception($"{name} must look like 000s000ms000us000ns, got '{text}'.");
        long Part(int i) => long.Parse(m.Groups[i].Value, System.Globalization.CultureInfo.InvariantCulture);
        return Part(1) * 1_000_000_000 + Part(2) * 1_000_000 + Part(3) * 1000 + Part(4);
    }
    // Nanoseconds as plain decimal seconds for OpenFOAM, never exponent notation.
    public static string Seconds(long ns) => (ns / 1_000_000_000m).ToString("0.0#########", System.Globalization.CultureInfo.InvariantCulture);

    // Frame file stem from an OpenFOAM time name, exact to the nanosecond: 000s000ms000us000ns.
    public static string FrameName(string time)
    {
        decimal ns = Math.Round(decimal.Parse(time, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture) * 1_000_000_000m);
        long n = (long)ns;
        return $"{n / 1_000_000_000:D3}s{n / 1_000_000 % 1000:D3}ms{n / 1000 % 1000:D3}us{n % 1000:D3}ns";
    }
    public static string FramePath(string time) => Path.Combine(OutputDirectory, FrameName(time) + ".bin.br");
    public static bool IsComplete(string time) => File.Exists(FramePath(time));

    public static int FrameCount(string directory)
    {
        if (!Directory.Exists(directory)) return 0;
        return Directory.EnumerateFiles(directory, "*s*ms*us*ns.bin.br").Count();
    }

    // Round to the nearest value keeping only `bits` of the 23 mantissa bits.
    static float Reduce(float v, int bits)
    {
        if (bits >= 23 || !float.IsFinite(v)) return v;
        int drop = 23 - bits;
        uint u = BitConverter.SingleToUInt32Bits(v);
        u = (u + (1u << (drop - 1))) & ~((1u << drop) - 1);
        float r = BitConverter.UInt32BitsToSingle(u);
        return float.IsFinite(r) ? r : v;
    }

    static byte[] Shuffle(float[] values, int channels, int mantissaBits)
    {
        int points = values.Length / channels;
        byte[] planes = new byte[values.Length * 4];
        Parallel.For(0, channels, c =>
        {
            for (int i = 0; i < points; i++)
            {
                uint u = BitConverter.SingleToUInt32Bits(Reduce(values[i * channels + c], mantissaBits));
                for (int k = 0; k < 4; k++) planes[((long)(c * 4 + k)) * points + i] = (byte)(u >> (8 * k));
            }
        });
        return planes;
    }

    // Compress to a temporary file, then rename, so readers never see a partial frame.
    public static void WriteFloats(string path, float[] values, int channels, int mantissaBits)
    {
        if (values.Length % channels != 0) throw new ArgumentException("value count is not a multiple of the channel count");
        byte[] planes = Shuffle(values, channels, mantissaBits);
        byte[] packed = new byte[System.IO.Compression.BrotliEncoder.GetMaxCompressedLength(planes.Length)];
        if (!System.IO.Compression.BrotliEncoder.TryCompress(planes, packed, out int length, Quality, Window)) throw new IOException("Brotli compression failed.");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            file.Write(packed, 0, length);
            file.Flush(true);
        }
        File.Move(temporary, path, true);
    }

    // Exact inverse of the encoding above (up to the mantissa reduction chosen at write time).
    public static float[] ReadFloats(string path, int channels)
    {
        var planes = new MemoryStream();
        using (var br = new BrotliStream(File.OpenRead(path), CompressionMode.Decompress)) br.CopyTo(planes);
        byte[] b = planes.ToArray();
        int count = b.Length / 4, points = count / channels;
        var values = new float[count];
        for (int c = 0; c < channels; c++)
            for (int i = 0; i < points; i++)
            {
                uint u = 0;
                for (int k = 0; k < 4; k++) u |= (uint)b[((long)(c * 4 + k)) * points + i] << (8 * k);
                values[i * channels + c] = BitConverter.UInt32BitsToSingle(u);
            }
        return values;
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
        WriteFloats(target, positions, 3, 23);
        return offsets[^1];
    }
}
