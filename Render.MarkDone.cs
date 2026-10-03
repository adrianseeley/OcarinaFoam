using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
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
            if (Directory.Exists(Path.Combine(processors[p], time)))
                Directory.Delete(Path.Combine(processors[p], time), recursive: true);
        }
        Console.WriteLine("Finished and removed t=" + time);
    }
}
