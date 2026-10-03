using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Use the most conservative cutoff across processors and require each exact time
    // name everywhere. Newer directories are a lag buffer, not atomic completion markers.
    // Already queued names stay in the set for this process lifetime, even after deletion.
    // Exactly three newer times are always retained.
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
}
