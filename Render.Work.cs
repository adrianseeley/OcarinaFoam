using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Own one time job and reuse this worker's position/colour and alpha arrays.
    // Only after every enabled field is saved do both deletion events get recorded.
    // Unhandled parse/render errors stop normal progress; there is no per-job retry here.
    public static void Work(string[] processors, int[] offsets, ConcurrentQueue<TimeJob> queue, Particle[] cloud, float[] alpha, float font)
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
}
