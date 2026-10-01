#:package SkiaSharp@3.119.1
#:package SkiaSharp.NativeAssets.Linux.NoDependencies@3.119.1
#:property PublishAot=false
#:property Nullable=disable

// .NET 10: dotnet run --file render/FoamRenderer.cs -c Release
// Requires no OpenFOAM libraries, GPU or X server.
// Ordinary uncollated processorN cases, static ASCII polyMesh, ASCII fields (.gz allowed).
// Mesh geometry (cell centres) is parsed once on startup and cached for every timestep;
// only the scalar/vector field files are reread per timestep.
// A single discovery loop finds ready times and enqueues them; ThreadCount worker
// threads pull times off that queue and render them in parallel, each with its own
// cloned particle buffer, sleeping when the queue is empty. These are fixed mesh-cell
// sample positions, not moving particles or tracked parcels.
// Output: FRAME.FIELD.png, one composite per field and timestep. FRAME is the
// 8-digit integer round(time / writeTimeStep), independent of existing PNG counts.
// One renderer process owns a case; its workers share queue/deletion coordination.
// Tile 0 is an info/legend panel; the rest carry a small
// top-centered view-name title each, all clipped to their own tile.
// The newest UntouchedTimes positive times on EVERY processor remain untouched (default 3).
// Set OpenFOAM purgeWrite to 0: this program owns removal of rendered time directories.
// Failures propagate; a failed render never reaches the deletion step.
// Per point, per field, opacity is inferred rather than configured: each point's alpha is
// its delta since the previous frame normalised against the largest delta that frame, so
// the point(s) that changed most render solid and untouched points fade to invisible.
// That comparison needs the previous frame's raw field files still on disk, so a time
// directory is deleted only after its own render and the successor job both finish.
// The successor needs that raw data for its delta calculation; the two-event gate
// preserves it even when worker jobs finish out of order.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using System.Threading;
using SkiaSharp;

public static class Program
{
    public static string CaseDirectory = "{{RENDER_CASE_DIRECTORY}}";
    public static string OutputDirectory = "{{RENDER_OUTPUT_DIRECTORY}}";
    public static string FontFile = "{{RENDER_FONT_FILE}}";
    public static int ProcessorCount = {{RENDER_PROCESSOR_COUNT}};

    public static bool RenderPressure = {{RENDER_PRESSURE}};
    public static bool RenderVelocityMagnitude = {{RENDER_VELOCITY_MAGNITUDE}};
    public static bool RenderDensity = {{RENDER_DENSITY}};
    public static bool RenderTemperature = {{RENDER_TEMPERATURE}};

    public static int UntouchedTimes = {{RENDER_UNTOUCHED_TIMES}};
    public static int ThreadCount = {{RENDER_THREAD_COUNT}};
    public static double WriteTimeStep = {{RENDER_WRITE_TIME_STEP}};
    public static int PollMilliseconds = {{RENDER_POLL_MILLISECONDS}};
    public static int PngCompressionLevel = {{RENDER_PNG_COMPRESSION_LEVEL}};
    public static int PlotWidth = {{RENDER_PLOT_WIDTH}};
    public static int PlotHeight = {{RENDER_PLOT_HEIGHT}};
    public static float MarginPixels = {{RENDER_MARGIN_PIXELS}};
    public static float LabelFontPixels = {{RENDER_LABEL_FONT_PIXELS}};
    public static SKColor BackgroundColor = SKColor.Parse("{{RENDER_BACKGROUND_COLOR}}");
    public static SKColor LabelColor = SKColor.Parse("{{RENDER_LABEL_COLOR}}");
    public static int GridColumns;
    public static int GridRows;
    public static float PointSizePixels = {{RENDER_POINT_SIZE_PIXELS}}f;
    public static float AxisTiltDegrees = {{RENDER_AXIS_TILT_DEGREES}}f; // Off-axis peek so grids don't hide points behind one another.

    // Camera look-at point and framing come from the ocarina's own bounding box, not the air domain.
    public static Vector3 ObjectMinimum = new Vector3({{RENDER_OBJECT_MIN_X}}f, {{RENDER_OBJECT_MIN_Y}}f, {{RENDER_OBJECT_MIN_Z}}f);
    public static Vector3 ObjectMaximum = new Vector3({{RENDER_OBJECT_MAX_X}}f, {{RENDER_OBJECT_MAX_Y}}f, {{RENDER_OBJECT_MAX_Z}}f);
    public static float CameraPaddingFraction = {{RENDER_CAMERA_PADDING_FRACTION}}f;

    public static Field[] Fields = new Field[]
    {
        new Field { Enabled = RenderPressure, File = "p", Name = "pressure", Unit = "Pa", DecimalPlaces = 0 },
        new Field { Enabled = RenderVelocityMagnitude, File = "U", Name = "velocityMagnitude", Vector = true, Unit = "m/s", DecimalPlaces = 8 },
        new Field { Enabled = RenderDensity, File = "rho", Name = "density", Unit = "kg/m^3", DecimalPlaces = 8 },
        new Field { Enabled = RenderTemperature, File = "T", Name = "temperature", Unit = "K", DecimalPlaces = 0 }
    };

    // From is relative to the padded SOLID bounding-box half-extents, not the air domain.
    // Corner directions therefore follow the object aspect ratio. All cameras look at
    // its centre. View supports From, Up and SizePixels; opacity is computed per field.
    public static View[] Views = new View[]
    {
        new View { Name = "Xp", From = new Vector3(1, 0, 0) },
        new View { Name = "Xn", From = new Vector3(-1, 0, 0) },
        new View { Name = "Yp", From = new Vector3(0, 1, 0) },
        new View { Name = "Yn", From = new Vector3(0, -1, 0) },
        new View { Name = "Zp", From = new Vector3(0, 0, 1), Up = Vector3.UnitY },
        new View { Name = "Zn", From = new Vector3(0, 0, -1), Up = Vector3.UnitY },
        new View { Name = "XpYpZp", From = new Vector3(1, 1, 1) },
        new View { Name = "XpYpZn", From = new Vector3(1, 1, -1) },
        new View { Name = "XpYnZp", From = new Vector3(1, -1, 1) },
        new View { Name = "XpYnZn", From = new Vector3(1, -1, -1) },
        new View { Name = "XnYpZp", From = new Vector3(-1, 1, 1) },
        new View { Name = "XnYpZn", From = new Vector3(-1, 1, -1) },
        new View { Name = "XnYnZp", From = new Vector3(-1, -1, 1) },
        new View { Name = "XnYnZn", From = new Vector3(-1, -1, -1) }
    };

    // Initialise one shared static geometry, then clone a cloud per worker.
    // Only the discovery thread assigns predecessor times in numeric order; workers
    // may finish out of order. No manifest/checkpoint of this state is persisted.
    public static void Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Directory.CreateDirectory(OutputDirectory);
        string[] processors = FindProcessors();
        Console.WriteLine("Watching " + CaseDirectory + "; keeping " + UntouchedTimes + " newer times.");

        int enabled = 0;
        for (int f = 0; f < Fields.Length; f++)
        {
            if (Fields[f].Enabled)
            {
                enabled++;
            }
        }
        if (enabled == 0)
        {
            throw new InvalidOperationException("Enable a field before running.");
        }

        ComputeGrid(Views.Length + 1, out GridColumns, out GridRows); // +1 reserves tile 0 for the info/legend panel.
        Console.WriteLine("Composite grid " + GridColumns + "x" + GridRows + " tiles, " + PlotWidth + "x" + PlotHeight + " each -> "
            + (GridColumns * PlotWidth) + "x" + (GridRows * PlotHeight) + " px.");

        // The mesh is static: parse geometry once on startup and reuse the cell
        // centres for every timestep instead of reparsing the ASCII polyMesh.
        int[] offsets = new int[processors.Length + 1];
        for (int p = 0; p < processors.Length; p++)
        {
            offsets[p + 1] = checked(offsets[p] + CellCount(Path.Combine(processors[p], "constant", "polyMesh")));
        }
        Particle[] cloud = new Particle[offsets[processors.Length]];
        for (int p = 0; p < processors.Length; p++)
        {
            ReadCentres(Path.Combine(processors[p], "constant", "polyMesh"), cloud, offsets[p], offsets[p + 1] - offsets[p]);
            Collect();
        }
        using SKTypeface typeface = SKTypeface.FromFile(FontFile);
        using SKFont font = new SKFont(typeface, LabelFontPixels);

        // The queue only ever holds time names, so workers never touch the polyMesh cache
        // concurrently; each worker keeps its own cloned Particle[]/alpha[] to work independently.
        ConcurrentQueue<TimeJob> queue = new ConcurrentQueue<TimeJob>();
        Thread[] workers = new Thread[ThreadCount];
        for (int w = 0; w < ThreadCount; w++)
        {
            Particle[] workerCloud = (Particle[])cloud.Clone();
            float[] workerAlpha = new float[workerCloud.Length];
            workers[w] = new Thread(() => Work(processors, offsets, queue, workerCloud, workerAlpha, font));
            workers[w].Start();
        }

        HashSet<string> queued = new HashSet<string>();
        string previousTime = null;
        while (true)
        {
            string[] ready = ReadyTimes(processors, queued);
            if (ready.Length == 0)
            {
                Thread.Sleep(PollMilliseconds);
                continue;
            }
            for (int i = 0; i < ready.Length; i++)
            {
                queued.Add(ready[i]);
                queue.Enqueue(new TimeJob { Time = ready[i], Previous = previousTime });
                previousTime = ready[i];
            }
        }
    }

    // Own one time job and reuse this worker's position/colour and alpha arrays.
    // Only after every enabled field is saved do both deletion events get recorded.
    // Unhandled parse/render errors stop normal progress; there is no per-job retry here.
    public static void Work(string[] processors, int[] offsets, ConcurrentQueue<TimeJob> queue, Particle[] cloud, float[] alpha, SKFont font)
    {
        while (true)
        {
            TimeJob job;
            if (!queue.TryDequeue(out job))
            {
                Thread.Sleep(PollMilliseconds);
                continue;
            }
            Console.WriteLine("Loading t=" + job.Time);
            ProcessTime(processors, offsets, job.Time, job.Previous, cloud, alpha, font);
            Collect(); // ProcessTime has returned: its frame arrays are no longer live.
            MarkDone(processors, job.Previous, PredecessorConsumed); // its delta data has now been read.
            MarkDone(processors, job.Time, SelfRendered); // this time's own render pass is complete.
        }
    }

    public const int SelfRendered = 1;
    public const int PredecessorConsumed = 2;
    public static ConcurrentDictionary<string, int> PendingDeletion = new ConcurrentDictionary<string, int>();

    // Two-event lifetime gate: retain raw time t until its own images exist AND its
    // successor has consumed t for deltas. ConcurrentDictionary combines completion
    // bits across workers. This is in-process coordination, not an interprocess lock.
    // Recursive deletion removes all files at t, including fields not being visualised.
    public static void MarkDone(string[] processors, string time, int bit)
    {
        if (time == null)
        {
            return;
        }
        int updated = PendingDeletion.AddOrUpdate(time, bit, (key, existing) => existing | bit);
        if (updated != (SelfRendered | PredecessorConsumed) || !PendingDeletion.TryRemove(time, out _))
        {
            return; // Not both events yet, or another thread already removed/deleted it.
        }
        for (int p = processors.Length - 1; p >= 0; p--)
        {
            Directory.Delete(Path.Combine(processors[p], time), recursive: true);
        }
        Console.WriteLine("Finished and removed t=" + time);
    }

    // Require exactly the configured processor paths rather than infer a partial count.
    // Extra unrelated processor directories are ignored; collated and serial layouts
    // are not supported. Mesh files are expected in each constant/polyMesh directory.
    public static string[] FindProcessors()
    {
        // Names are generated from ProcessorCount, not scanned, so a decomposition still
        // mid-write (some processorN directories not yet created) fails loudly here
        // instead of silently rendering a partial cloud.
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

    // Return finite positive numeric directory names sorted by numeric time.
    // Leave 0, constant, system and non-time directories outside the rendering lifecycle.
    public static string[] Times(string processor, out double[] numbers)
    {
        string[] directories = Directory.GetDirectories(processor);
        List<string> names = new List<string>();
        List<double> values = new List<double>();
        for (int i = 0; i < directories.Length; i++)
        {
            string name = Path.GetFileName(directories[i]);
            double value;
            if (double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && value > 0)
            {
                names.Add(name);
                values.Add(value);
            }
        }
        string[] result = names.ToArray();
        numbers = values.ToArray();
        Array.Sort(numbers, result);
        return result;
    }

    // Use the most conservative cutoff across processors and require each exact time
    // name everywhere. Newer directories are a lag buffer, not atomic completion markers.
    // Already queued names stay in the set for this process lifetime, even after deletion.
    // UntouchedTimes must be at least one; zero is not a supported flush mechanism.
    public static string[] ReadyTimes(string[] processors, HashSet<string> queued)
    {
        double[] firstNumbers;
        string[] firstNames = Times(processors[0], out firstNumbers);
        if (firstNames.Length <= UntouchedTimes)
        {
            return Array.Empty<string>();
        }
        double cutoff = firstNumbers[firstNumbers.Length - UntouchedTimes];
        for (int p = 1; p < processors.Length; p++)
        {
            double[] numbers;
            Times(processors[p], out numbers);
            if (numbers.Length <= UntouchedTimes)
            {
                return Array.Empty<string>();
            }
            cutoff = Math.Min(cutoff, numbers[numbers.Length - UntouchedTimes]);
        }
        // Names already queued are skipped rather than removed from `queued`, since their
        // directories still exist (a worker may still be processing) until actually deleted.
        List<string> ready = new List<string>();
        for (int i = 0; i < firstNames.Length && firstNumbers[i] < cutoff; i++)
        {
            if (queued.Contains(firstNames[i]))
            {
                continue;
            }
            bool everywhere = true;
            for (int p = 1; p < processors.Length; p++)
            {
                if (!Directory.Exists(Path.Combine(processors[p], firstNames[i])))
                {
                    everywhere = false;
                    break;
                }
            }
            if (everywhere)
            {
                ready.Add(firstNames[i]);
            }
        }
        return ready.ToArray();
    }

    // Process enabled fields serially within this job, writing a composite for each.
    // Each field computes its own extrema and delta maximum across ALL partitions.
    // No scale is shared between fields or between timesteps.
    public static void ProcessTime(string[] processors, int[] offsets, string time, string previousTime, Particle[] cloud, float[] alpha, SKFont font)
    {
        for (int f = 0; f < Fields.Length; f++)
        {
            if (!Fields[f].Enabled)
            {
                continue;
            }
            double minimum;
            double maximum;
            LoadField(processors, offsets, time, previousTime, Fields[f], cloud, alpha, out minimum, out maximum);
            Collect(); // Raw doubles and parsers have gone; cloud holds normalised colours.
            int visibleCount = 0;
            for (int i = 0; i < alpha.Length; i++)
            {
                if (alpha[i] > 0f)
                {
                    visibleCount++;
                }
            }
            Console.WriteLine(time + " " + Fields[f].Name + " [" + minimum.ToString("G17") + ", " + maximum.ToString("G17") + "] "
                + cloud.Length + " points, " + visibleCount + " visible");
            RenderField(TimeFrame(time), time, Fields[f], cloud, alpha, font, minimum, maximum);
            Collect();
        }
    }

    // Infer dense local cell count from the largest owner/neighbour cell label.
    // Faces belong to an owner cell; internal faces also have a neighbour cell.
    // Assumes ordinary OpenFOAM local numbering, with labels starting at zero.
    public static int CellCount(string mesh)
    {
        int maximum = -1;
        string[] files = new string[] { "owner", "neighbour" };
        for (int f = 0; f < files.Length; f++)
        {
            using StreamReader reader = OpenText(Path.Combine(mesh, files[f]));
            Tokens tokens = new Tokens { Reader = reader };
            SkipHeader(tokens);
            int count = Integer(tokens);
            Next(tokens);
            bool repeated = Is(tokens, "{");
            int reads = repeated ? 1 : count;
            for (int i = 0; i < reads; i++)
            {
                maximum = Math.Max(maximum, Integer(tokens));
            }
            Expect(tokens, repeated ? "}" : ")");
        }
        return maximum + 1;
    }

    // Read an ASCII label list, including repeated-value brace representation.
    // The array holds mesh connectivity only while geometry is reconstructed.
    public static int[] ReadLabels(string path)
    {
        using StreamReader reader = OpenText(path);
        Tokens tokens = new Tokens { Reader = reader };
        SkipHeader(tokens);
        int[] labels = new int[Integer(tokens)];
        Next(tokens);
        bool repeated = Is(tokens, "{");
        int value = repeated ? Integer(tokens) : 0;
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i] = repeated ? value : Integer(tokens);
        }
        Expect(tokens, repeated ? "}" : ")");
        return labels;
    }

    // Read static mesh points in double precision for centroid calculations.
    // Parenthesised point lists are expected; this is not a general OpenFOAM reader.
    public static D3[] ReadVertices(string path)
    {
        using StreamReader reader = OpenText(path);
        Tokens tokens = new Tokens { Reader = reader };
        SkipHeader(tokens);
        D3[] vertices = new D3[Integer(tokens)];
        Expect(tokens, "(");
        for (int i = 0; i < vertices.Length; i++)
        {
            Expect(tokens, "(");
            D3 v = new D3 { X = Number(tokens), Y = Number(tokens), Z = Number(tokens) };
            Expect(tokens, ")");
            vertices[i] = v;
        }
        Expect(tokens, ")");
        return vertices;
    }

    // Reconstruct a volume centroid for each polyhedral cell without OpenFOAM/VTK.
    // Pass 1 averages incident face centres for a reference point. Pass 2 sums signed
    // face-pyramid moments about that reference, then divides by total weight.
    // Rereading faces reduces retained connectivity memory. Positions become float
    // only after the double-precision geometry work; degenerate cells are not repaired.
    public static void ReadCentres(string mesh, Particle[] cloud, int offset, int count)
    {
        int[] owner = ReadLabels(Path.Combine(mesh, "owner"));
        int[] neighbour = ReadLabels(Path.Combine(mesh, "neighbour"));
        D3[] vertices = ReadVertices(Path.Combine(mesh, "points"));
        D3[] estimates = new D3[count];
        double[] weights = new double[count];
        D3[] moments = new D3[count];
        int[] face = new int[16];

        // First pass: mean face centres. Second: volume-weighted face pyramids.
        // Reread faces to avoid retaining the complete mesh connectivity/face geometry.
        for (int pass = 0; pass < 2; pass++)
        {
            using StreamReader reader = OpenText(Path.Combine(mesh, "faces"));
            Tokens tokens = new Tokens { Reader = reader };
            SkipHeader(tokens);
            int faces = Integer(tokens);
            Expect(tokens, "(");
            for (int f = 0; f < faces; f++)
            {
                int size = Integer(tokens);
                if (face.Length < size)
                {
                    Array.Resize(ref face, size);
                }
                Expect(tokens, "(");
                for (int j = 0; j < size; j++)
                {
                    face[j] = Integer(tokens);
                }
                Expect(tokens, ")");
                D3 centre;
                D3 area;
                FaceGeometry(vertices, face, size, out centre, out area);
                int a = owner[f];
                int b = f < neighbour.Length ? neighbour[f] : -1;
                if (pass == 0)
                {
                    estimates[a] = Add(estimates[a], centre);
                    weights[a]++;
                    if (b >= 0)
                    {
                        estimates[b] = Add(estimates[b], centre);
                        weights[b]++;
                    }
                }
                else
                {
                    AccumulatePyramid(a, centre, area, 1, estimates, moments, weights);
                    if (b >= 0)
                    {
                        AccumulatePyramid(b, centre, area, -1, estimates, moments, weights);
                    }
                }
            }
            Expect(tokens, ")");
            if (pass == 0)
            {
                for (int c = 0; c < count; c++)
                {
                    estimates[c] = Scale(estimates[c], 1 / weights[c]);
                    weights[c] = 0;
                }
            }
        }
        for (int c = 0; c < count; c++)
        {
            cloud[offset + c].Position = Float3(Scale(moments[c], 1 / weights[c]));
        }
    }

    // Triangulate each polygon as a fan around its mean vertex position. Sum oriented
    // triangle areas and magnitude-weighted triangle centroids. Face winding determines
    // the area-vector direction used to distinguish owner from neighbour contributions.
    public static void FaceGeometry(D3[] vertices, int[] face, int count, out D3 centre, out D3 area)
    {
        D3 average = new D3();
        for (int i = 0; i < count; i++)
        {
            average = Add(average, vertices[face[i]]);
        }
        average = Scale(average, 1.0 / count);
        area = new D3();
        centre = new D3();
        double weight = 0;
        for (int i = 0; i < count; i++)
        {
            D3 a = vertices[face[i]];
            D3 b = vertices[face[(i + 1) % count]];
            D3 cross = Cross(Subtract(a, average), Subtract(b, average));
            double magnitude = Math.Sqrt(Dot(cross, cross));
            area = Add(area, Scale(cross, 0.5));
            centre = Add(centre, Scale(Add(Add(a, b), average), magnitude / 3));
            weight += magnitude;
        }
        centre = Scale(centre, 1 / weight);
    }

    // Accumulate a face pyramid for one cell. sign reverses the shared face for its
    // neighbour. The scalar weight is three times signed pyramid volume; the common
    // factor cancels when moments are divided by weights. A pyramid centroid lies
    // three quarters of the way from its apex to the face centroid.
    public static void AccumulatePyramid(int cell, D3 centre, D3 area, double sign, D3[] estimates, D3[] moments, double[] weights)
    {
        double weight = sign * Dot(area, Subtract(centre, estimates[cell]));
        D3 centroid = Add(Scale(centre, 0.75), Scale(estimates[cell], 0.25));
        moments[cell] = Add(moments[cell], Scale(centroid, weight));
        weights[cell] += weight;
    }

    // Map current scalar values to colour and absolute temporal differences to alpha.
    // Colour = (value - current minimum) / current range; uniform fields use 0.5.
    // Alpha = abs(current - predecessor) / largest such difference; no change gives 0.
    // The first discovered time has no predecessor and uses alpha 1. U is reduced to
    // speed BEFORE differencing, so direction changes at constant speed are invisible.
    // The predecessor is the previous queued available time, not necessarily t-deltaT.
    // This highlights change, not a spatial gradient, acoustic energy or calibrated SPL.
    public static void LoadField(string[] processors, int[] offsets, string time, string previousTime, Field field, Particle[] cloud, float[] alpha, out double minimum, out double maximum)
    {
        // Keep pressure as double until AFTER normalisation (tiny changes near 101325 Pa).
        double[] values = new double[cloud.Length];
        minimum = double.PositiveInfinity;
        maximum = double.NegativeInfinity;
        for (int p = 0; p < processors.Length; p++)
        {
            ReadField(Path.Combine(processors[p], time, field.File), field.Vector, values, offsets[p], offsets[p + 1] - offsets[p], ref minimum, ref maximum);
            Collect();
        }
        if (previousTime == null)
        {
            for (int i = 0; i < alpha.Length; i++)
            {
                alpha[i] = 1f; // No prior frame to compare against: show every point at full strength.
            }
        }
        else
        {
            double[] previousValues = new double[cloud.Length];
            double previousMinimum = double.PositiveInfinity;
            double previousMaximum = double.NegativeInfinity;
            for (int p = 0; p < processors.Length; p++)
            {
                ReadField(Path.Combine(processors[p], previousTime, field.File), field.Vector, previousValues, offsets[p], offsets[p + 1] - offsets[p], ref previousMinimum, ref previousMaximum);
                Collect();
            }
            double[] deltas = new double[cloud.Length];
            double maxDelta = 0;
            for (int i = 0; i < deltas.Length; i++)
            {
                deltas[i] = Math.Abs(values[i] - previousValues[i]);
                maxDelta = Math.Max(maxDelta, deltas[i]);
            }
            for (int i = 0; i < alpha.Length; i++)
            {
                alpha[i] = maxDelta == 0 ? 0f : (float)(deltas[i] / maxDelta); // Brightest where this frame changed most.
            }
        }
        double range = maximum - minimum;
        for (int i = 0; i < cloud.Length; i++)
        {
            cloud[i].Color = range == 0 ? 0.5f : (float)((values[i] - minimum) / range);
        }
    }

    // Read internalField only: uniform or nonuniform List<scalar>/List<vector>,
    // including a repeated brace value. Require count agreement and finite values.
    // Vector data becomes Euclidean magnitude. BoundaryField is not rendered;
    // binary, collated and arbitrary dictionary/include syntax are outside this parser.
    public static void ReadField(string path, bool vector, double[] values, int offset, int count, ref double minimum, ref double maximum)
    {
        using StreamReader reader = OpenText(path);
        Tokens tokens = new Tokens { Reader = reader };
        SkipHeader(tokens);
        do
        {
            Next(tokens);
        }
        while (!Is(tokens, "internalField"));
        Next(tokens);
        bool uniform = Is(tokens, "uniform");
        bool repeated = false;
        if (!uniform)
        {
            Expect(tokens, "List<" + (vector ? "vector" : "scalar") + ">");
            int entries = Integer(tokens);
            if (entries != count)
            {
                throw new FormatException(path + ": field/mesh cell count differs");
            }
            Next(tokens);
            repeated = Is(tokens, "{");
        }
        int reads = uniform || repeated ? 1 : count;
        double value = 0;
        for (int i = 0; i < reads; i++)
        {
            if (vector)
            {
                Expect(tokens, "(");
                double x = Number(tokens);
                double y = Number(tokens);
                double z = Number(tokens);
                Expect(tokens, ")");
                value = Math.Sqrt(x * x + y * y + z * z);
            }
            else
            {
                value = Number(tokens);
            }
            if (!double.IsFinite(value))
            {
                throw new ArithmeticException(path + ": nonfinite field value");
            }
            if (count > 0)
            {
                values[offset + i] = value;
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
        }
        if (uniform || repeated)
        {
            for (int i = 0; i < count; i++)
            {
                values[offset + i] = value;
            }
        }
        if (!uniform)
        {
            Expect(tokens, repeated ? "}" : ")");
        }
        Expect(tokens, ";");
    }

    // Search integer tile layouts for aspect ratio closest to 2:1, allowing spare tiles.
    // Fourteen views plus the legend with square tiles gives 5 columns x 3 rows.
    public static void ComputeGrid(int count, out int columns, out int rows)
    {
        columns = count;
        rows = 1;
        double bestError = double.MaxValue;
        for (int r = 1; r <= count; r++)
        {
            int c = (count + r - 1) / r; // Ceil so every view has a tile.
            double ratio = (c * (double)PlotWidth) / (r * (double)PlotHeight);
            double error = Math.Abs(ratio - 2.0);
            if (error < bestError)
            {
                bestError = error;
                columns = c;
                rows = r;
            }
        }
    }

    // Round physical time divided by configured field interval to a stable frame index.
    // Changing the interval mid-run can cause gaps/collisions. The int return type
    // also limits the usable frame range for unusually long or densely sampled runs.
    public static int TimeFrame(string time)
    {
        // Derived from the write-time-step, not from counting existing PNGs, so several
        // output numbering survives missing files. It does not partition jobs across processes.
        double value = double.Parse(time, NumberStyles.Float, CultureInfo.InvariantCulture);
        return (int)Math.Round(value / WriteTimeStep, MidpointRounding.AwayFromZero);
    }

    // Create one opaque composite bitmap for this field/time. For every camera,
    // sort the complete merged cloud far-to-near before alpha blending. Separate
    // partition sorting would produce incorrect overlap at processor boundaries.
    // The sorted order is recomputed per view; image encoding occurs after all tiles.
    public static void RenderField(int frame, string time, Field field, Particle[] cloud, float[] alpha, SKFont font, double minimum, double maximum)
    {
        int compositeWidth = GridColumns * PlotWidth;
        int compositeHeight = GridRows * PlotHeight;
        using SKBitmap bitmap = new SKBitmap(compositeWidth, compositeHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using SKCanvas canvas = new SKCanvas(bitmap);
        canvas.Clear(BackgroundColor);
        using SKPaint paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        InfoTile(canvas, font, time, field, cloud.Length, minimum, maximum, 0, 0);
        int[] order = new int[cloud.Length];
        float[] depth = new float[cloud.Length];
        for (int v = 0; v < Views.Length; v++)
        {
            View view = Views[v];
            int tileIndex = v + 1; // Tile 0 is the info/legend panel.
            float offsetX = (tileIndex % GridColumns) * PlotWidth;
            float offsetY = (tileIndex / GridColumns) * PlotHeight;
            Camera camera = MakeCamera(view, offsetX, offsetY);
            for (int i = 0; i < cloud.Length; i++)
            {
                order[i] = i;
                depth[i] = Vector3.Dot(cloud[i].Position - camera.Centre, camera.TowardEye);
            }
            Array.Sort(depth, order); // Far to near across ALL processors.
            RenderTile(canvas, paint, view, camera, cloud, alpha, order, font, offsetX, offsetY);
            Collect();
        }
        DrawGrid(canvas, compositeWidth, compositeHeight);
        string stem = Path.Combine(OutputDirectory, frame.ToString("D8", CultureInfo.InvariantCulture) + "." + field.Name);
        SavePng(bitmap, stem + ".png");
        Console.WriteLine("  " + field.Name + " composite saved (" + Views.Length + " views, " + compositeWidth + "x" + compositeHeight + ")");
    }

    // Construct an orthographic camera around the padded solid bounds. All fluid
    // points are eligible to project, but far-field points can fall outside the tile.
    // Corner views are weighted by object dimensions; they are not necessarily equal-
    // angle isometric views. Tilt changes orientation only, never simulation geometry.
    public static Camera MakeCamera(View view, float offsetX, float offsetY)
    {
        Camera camera = new Camera();
        Vector3 half = (ObjectMaximum - ObjectMinimum) * 0.5f * (1f + CameraPaddingFraction);
        camera.Centre = (ObjectMaximum + ObjectMinimum) * 0.5f;
        camera.TowardEye = Vector3.Normalize(view.From * half);
        camera.Right = Vector3.Normalize(Vector3.Cross(view.Up, camera.TowardEye));
        camera.Up = Vector3.Cross(camera.TowardEye, camera.Right);
        float tilt = AxisTiltDegrees * MathF.PI / 180f;
        camera.TowardEye = Vector3.Normalize(RotateAroundAxis(camera.TowardEye, camera.Right, tilt));
        camera.TowardEye = Vector3.Normalize(RotateAroundAxis(camera.TowardEye, camera.Up, tilt));
        camera.Right = Vector3.Normalize(Vector3.Cross(view.Up, camera.TowardEye));
        camera.Up = Vector3.Cross(camera.TowardEye, camera.Right);
        float extentX = Math.Abs(camera.Right.X) * half.X + Math.Abs(camera.Right.Y) * half.Y + Math.Abs(camera.Right.Z) * half.Z;
        float extentY = Math.Abs(camera.Up.X) * half.X + Math.Abs(camera.Up.Y) * half.Y + Math.Abs(camera.Up.Z) * half.Z;
        camera.Scale = Math.Min((PlotWidth - 2 * MarginPixels) / (2 * extentX), (PlotHeight - 2 * MarginPixels) / (2 * extentY));
        camera.ScreenX = offsetX + PlotWidth * 0.5f;
        camera.ScreenY = offsetY + PlotHeight * 0.5f;
        return camera;
    }

    // Rodrigues rotation about a unit axis; used for the optional two-axis camera tilt.
    public static Vector3 RotateAroundAxis(Vector3 v, Vector3 axis, float angleRadians)
    {
        float cos = MathF.Cos(angleRadians);
        float sin = MathF.Sin(angleRadians);
        return v * cos + Vector3.Cross(axis, v) * sin + axis * Vector3.Dot(axis, v) * (1 - cos);
    }

    // Orthographic projection: dot products with camera right/up determine pixels.
    // There is no perspective division, ray integration or reconstruction of a surface.
    public static SKPoint Project(Vector3 position, Camera camera)
    {
        Vector3 relative = position - camera.Centre;
        return new SKPoint(camera.ScreenX + Vector3.Dot(relative, camera.Right) * camera.Scale, camera.ScreenY - Vector3.Dot(relative, camera.Up) * camera.Scale);
    }

    // Draw cell-centre circles with normalised scalar hue and delta-driven opacity.
    // Clip to this tile. Point size is in pixels, not cell volume: refined regions
    // contain more samples and can appear denser/brighter through overlap.
    public static void RenderTile(SKCanvas canvas, SKPaint paint, View view, Camera camera, Particle[] cloud, float[] alpha, int[] order, SKFont font, float offsetX, float offsetY)
    {
        canvas.Save();
        canvas.ClipRect(new SKRect(offsetX, offsetY, offsetX + PlotWidth, offsetY + PlotHeight)); // Confine points to this tile only.
        SKColor[] pointPalette = Palette();
        float radius = view.SizePixels * 0.5f;
        for (int i = 0; i < order.Length; i++)
        {
            int index = order[i];
            if (alpha[index] <= 0f)
            {
                continue; // Unchanged since the previous frame: nothing new to show here.
            }
            Particle point = cloud[index];
            SKPoint screen = Project(point.Position, camera);
            int colorIndex = (int)(point.Color * (pointPalette.Length - 1));
            paint.Color = pointPalette[colorIndex].WithAlpha((byte)Math.Round(alpha[index] * 255));
            canvas.DrawCircle(screen, radius, paint);
        }
        TileTitle(canvas, font, view, offsetX, offsetY);
        canvas.Restore();
    }

    // Build a 4096-entry blue-to-red HSV hue ramp. This is a qualitative rainbow
    // scale, not perceptually uniform; read the numeric legend for each frame.
    public static SKColor[] Palette()
    {
        SKColor[] colors = new SKColor[4096];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = SKColor.FromHsv(240f * (1f - (float)i / (colors.Length - 1)), 100, 100, 255);
        }
        return colors;
    }

    // Label the viewing direction; positive axis names describe the eye side.
    public static void TileTitle(SKCanvas canvas, SKFont font, View view, float offsetX, float offsetY)
    {
        using SKPaint paint = new SKPaint { Color = LabelColor, IsAntialias = true };
        canvas.DrawText(view.Name, offsetX + PlotWidth * 0.5f, offsetY + LabelFontPixels + 12, SKTextAlign.Center, font, paint);
    }

    // Separate views and legend visually without changing projection or field values.
    public static void DrawGrid(SKCanvas canvas, int compositeWidth, int compositeHeight)
    {
        using SKPaint paint = new SKPaint
        {
            Color = LabelColor,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke,
            IsAntialias = false
        };
        for (int c = 1; c < GridColumns; c++) // Interior boundaries only: no double line where tiles meet.
        {
            float x = c * PlotWidth - 0.5f;
            canvas.DrawLine(x, 0, x, compositeHeight, paint);
        }
        for (int r = 1; r < GridRows; r++)
        {
            float y = r * PlotHeight - 0.5f;
            canvas.DrawLine(0, y, compositeWidth, y, paint);
        }
        canvas.DrawRect(0.5f, 0.5f, compositeWidth - 1, compositeHeight - 1, paint); // Outermost border kept fully inside the canvas.
    }

    // Show physical simulation time in seconds, milliseconds, microseconds and
    // nanoseconds; this annotation remains meaningful at any encoded playback speed.
    public static string FormatTime(string time)
    {
        double seconds = double.Parse(time, NumberStyles.Float, CultureInfo.InvariantCulture);
        long totalNanoseconds = (long)Math.Round(seconds * 1_000_000_000d);
        long nanoseconds = totalNanoseconds % 1000;
        long microseconds = (totalNanoseconds / 1_000) % 1000;
        long milliseconds = (totalNanoseconds / 1_000_000) % 1000;
        long wholeSeconds = totalNanoseconds / 1_000_000_000;
        return wholeSeconds + "s " + milliseconds.ToString("000", CultureInfo.InvariantCulture) + "ms "
            + microseconds.ToString("000", CultureInfo.InvariantCulture) + "us "
            + nanoseconds.ToString("000", CultureInfo.InvariantCulture) + "ns";
    }

    // Display timestep, field, total sample count and current scalar range.
    // Pressure and temperature labels use zero decimal places in this frozen version;
    // a small real range can therefore have identical rounded endpoint labels.
    // The legend describes colour only; it does not report the opacity delta scale.
    public static void InfoTile(SKCanvas canvas, SKFont font, string time, Field field, int count, double minimum, double maximum, float offsetX, float offsetY)
    {
        canvas.Save();
        canvas.ClipRect(new SKRect(offsetX, offsetY, offsetX + PlotWidth, offsetY + PlotHeight));
        using SKPaint textPaint = new SKPaint { Color = LabelColor, IsAntialias = true };
        float x = offsetX + MarginPixels;
        float y = offsetY + MarginPixels + LabelFontPixels;
        canvas.DrawText("t = " + FormatTime(time), x, y, SKTextAlign.Left, font, textPaint);
        y += LabelFontPixels + 8;
        canvas.DrawText(field.Name, x, y, SKTextAlign.Left, font, textPaint);
        y += LabelFontPixels + 8;
        canvas.DrawText(count.ToString("N0", CultureInfo.InvariantCulture) + " points", x, y, SKTextAlign.Left, font, textPaint);
        y += LabelFontPixels + 24;

        string unitSuffix = string.IsNullOrEmpty(field.Unit) ? "" : " " + field.Unit;
        string decimalFormat = "F" + field.DecimalPlaces.ToString(CultureInfo.InvariantCulture);
        string scale = minimum == maximum
            ? "uniform: midpoint colour"
            : minimum.ToString(decimalFormat, CultureInfo.InvariantCulture) + unitSuffix + " -> " + maximum.ToString(decimalFormat, CultureInfo.InvariantCulture) + unitSuffix;
        canvas.DrawText(scale, x, y, SKTextAlign.Left, font, textPaint);
        y += LabelFontPixels + 12;

        float legendLeft = offsetX + MarginPixels;
        float legendRight = offsetX + PlotWidth - MarginPixels;
        float legendTop = y;
        float legendBottom = y + LabelFontPixels * 2;
        SKColor[] palette = Palette();
        using SKPaint barPaint = new SKPaint { IsAntialias = false, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        int bars = (int)(legendRight - legendLeft);
        for (int px = 0; px < bars; px++)
        {
            barPaint.Color = palette[(int)((float)px / (bars - 1) * (palette.Length - 1))];
            float lx = legendLeft + px;
            canvas.DrawLine(lx, legendTop, lx, legendBottom, barPaint);
        }
        canvas.DrawText("min", legendLeft, legendBottom + LabelFontPixels + 4, SKTextAlign.Left, font, textPaint);
        canvas.DrawText("max", legendRight, legendBottom + LabelFontPixels + 4, SKTextAlign.Right, font, textPaint);
        canvas.Restore();
    }

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

    // Skip a balanced FoamFile header before reading payload tokens.
    // The format field is not validated here: ASCII input is a caller requirement.
    public static void SkipHeader(Tokens tokens)
    {
        Expect(tokens, "FoamFile");
        Expect(tokens, "{");
        int depth = 1;
        while (depth != 0)
        {
            Next(tokens);
            if (Is(tokens, "{"))
            {
                depth++;
            }
            if (Is(tokens, "}"))
            {
                depth--;
            }
        }
    }

    // Streaming tokenizer for this reader's restricted ASCII grammar: skip whitespace
    // and both comment forms, separate punctuation, and copy a token into a reusable
    // buffer. Quoted strings are simple; this is not a complete OpenFOAM preprocessor.
    public static bool Next(Tokens tokens, bool allowEnd = false)
    {
        StreamReader reader = tokens.Reader;
        int c;
        while (true)
        {
            c = reader.Read();
            if (c < 0)
            {
                if (allowEnd)
                {
                    return false;
                }
                throw new EndOfStreamException();
            }
            if (char.IsWhiteSpace((char)c))
            {
                continue;
            }
            if (c == '/' && reader.Peek() == '/')
            {
                do
                {
                    c = reader.Read();
                }
                while (c >= 0 && c != '\n');
                continue;
            }
            if (c == '/' && reader.Peek() == '*')
            {
                reader.Read();
                int previous = 0;
                while (true)
                {
                    c = reader.Read();
                    if (c < 0)
                    {
                        throw new EndOfStreamException();
                    }
                    if (previous == '*' && c == '/')
                    {
                        break;
                    }
                    previous = c;
                }
                continue;
            }
            break;
        }
        tokens.Length = 0;
        if (c == '"')
        {
            while (true)
            {
                c = reader.Read();
                if (c < 0)
                {
                    throw new EndOfStreamException();
                }
                if (c == '"')
                {
                    break;
                }
                Append(tokens, (char)c);
            }
            return true;
        }
        Append(tokens, (char)c);
        if (Delimiter(c))
        {
            return true;
        }
        while (true)
        {
            c = reader.Peek();
            if (c < 0 || char.IsWhiteSpace((char)c) || Delimiter(c) || c == '/')
            {
                break;
            }
            Append(tokens, (char)reader.Read());
        }
        return true;
    }

    // Recognise punctuation needed by headers, dimensions, vectors and lists.
    public static bool Delimiter(int c)
    {
        return c == '(' || c == ')' || c == '{' || c == '}' || c == '[' || c == ']' || c == ';';
    }

    // Grow the reusable token buffer only when a longer token requires it.
    public static void Append(Tokens tokens, char c)
    {
        if (tokens.Length == tokens.Text.Length)
        {
            Array.Resize(ref tokens.Text, tokens.Text.Length * 2);
        }
        tokens.Text[tokens.Length++] = c;
    }

    // Compare a token span without allocating a temporary string.
    public static bool Is(Tokens tokens, string value)
    {
        return tokens.Text.AsSpan(0, tokens.Length).SequenceEqual(value.AsSpan());
    }

    // Consume one required token and fail with context when the input differs.
    public static void Expect(Tokens tokens, string value)
    {
        Next(tokens);
        if (!Is(tokens, value))
        {
            throw new FormatException("Expected " + value + ", got " + new string(tokens.Text, 0, tokens.Length));
        }
    }

    // Parse a label/count using invariant culture so locale cannot alter syntax.
    public static int Integer(Tokens tokens)
    {
        Next(tokens);
        return int.Parse(tokens.Text.AsSpan(0, tokens.Length), CultureInfo.InvariantCulture);
    }

    // Parse a floating-point token, including exponent notation, using invariant culture.
    public static double Number(Tokens tokens)
    {
        Next(tokens);
        return double.Parse(tokens.Text.AsSpan(0, tokens.Length), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // Force collection and large-object-heap compaction at array lifetime boundaries.
    // This trades pauses and CPU work for releasing large transient buffers. With
    // multiple workers the pause affects the process; it is not a RAM limit.
    public static void Collect()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
    }

    // Double-precision geometry arithmetic used during centroid reconstruction.
    public static D3 Add(D3 a, D3 b)
    {
        return new D3 { X = a.X + b.X, Y = a.Y + b.Y, Z = a.Z + b.Z };
    }
    // Double-precision vector difference.
    public static D3 Subtract(D3 a, D3 b)
    {
        return new D3 { X = a.X - b.X, Y = a.Y - b.Y, Z = a.Z - b.Z };
    }
    // Multiply a geometry vector by a scalar.
    public static D3 Scale(D3 a, double s)
    {
        return new D3 { X = a.X * s, Y = a.Y * s, Z = a.Z * s };
    }
    // Dot product for signed volume weights and squared magnitudes.
    public static double Dot(D3 a, D3 b)
    {
        return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }
    // Oriented area-vector arithmetic for polygon fan triangles.
    public static D3 Cross(D3 a, D3 b)
    {
        return new D3 { X = a.Y * b.Z - a.Z * b.Y, Y = a.Z * b.X - a.X * b.Z, Z = a.X * b.Y - a.Y * b.X };
    }
    // Convert final mesh positions to the float representation used by drawing.
    public static Vector3 Float3(D3 a)
    {
        return new Vector3((float)a.X, (float)a.Y, (float)a.Z);
    }
}

// Data only. All functions live above as public static functions.
public class Field { public bool Enabled; public string File; public string Name; public bool Vector; public string Unit; public int DecimalPlaces; }
public class View
{
    public string Name;
    public Vector3 From;
    public Vector3 Up = Vector3.UnitZ;
    public float SizePixels = Program.PointSizePixels;
}
public class TimeJob { public string Time; public string Previous; }
public class Tokens { public StreamReader Reader; public char[] Text = new char[256]; public int Length; }
public class Camera
{
    public Vector3 Centre, TowardEye, Right, Up;
    public float Scale, ScreenX, ScreenY;
}
public struct Particle { public Vector3 Position; public float Color; }
public struct D3 { public double X, Y, Z; }
