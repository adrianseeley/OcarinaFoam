using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Initialise one shared static geometry, then clone a cloud per worker.
    // Only the discovery thread assigns predecessor times in numeric order; workers
    // may finish out of order. Completion records persist across restarts.
    public static void Run()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Directory.CreateDirectory(OutputDirectory);
        string[] processors = FindProcessors();
        Console.WriteLine("Watching " + CaseDirectory + "; keeping " + UntouchedTimes + " newer times.");
        PrintLayoutSummary();

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
        float font = LabelFontPixels;
        RecoverCompleted(processors);

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
            if (queue.Count >= ThreadCount * 2) { Thread.Sleep(PollMilliseconds); continue; }
            string[] ready = ReadyTimes(processors, queued).Take(Math.Max(1, ThreadCount * 2 - queue.Count)).ToArray();
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
}
