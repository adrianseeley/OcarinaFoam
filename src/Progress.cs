using System.Globalization;

public static class Progress
{
    public const int PollSeconds = 30;

    // Foreground head for `ocarina start`: poll until both services stop; Ctrl+C only detaches.
    public static void Run(string root)
    {
        Config c = Configuration.Built(root);
        double interval = c.deltaTSeconds * c.fieldWriteIntervalTimeSteps;
        int total = (int)(Math.Ceiling(c.endTimeSeconds / interval - 1e-9) + 3);
        bool detach = false;
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; detach = true; };
        Console.CancelKeyPress += cancel;
        Console.WriteLine($"Monitoring every {PollSeconds}s. Press Ctrl+C to detach; the services keep running.");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int? baseFrame = null;
        try
        {
            while (!detach)
            {
                int simulated = SimulatedFrames(root, c, interval);
                int rendered = RenderedFrames(root);
                baseFrame ??= simulated;
                string sim = Active(root, "simulate"), ren = Active(root, "render");
                Console.WriteLine(Report(DateTime.Now, simulated, total, rendered, clock.Elapsed.TotalSeconds, simulated - baseFrame.Value, sim, ren));
                if (sim is not ("active" or "activating") && ren is not ("active" or "activating"))
                {
                    Console.WriteLine("Both services have stopped. Use: ocarina check " + root);
                    return;
                }
                for (int i = 0; i < PollSeconds * 10 && !detach; i++) Thread.Sleep(100);
            }
            Console.WriteLine("Detached. Services still running; stop with: ocarina stop " + root);
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    public static string Report(DateTime now, int simulated, int total, int rendered, double elapsedSeconds, int gained, string sim, string ren)
    {
        double rate = elapsedSeconds > 0 ? gained / elapsedSeconds : 0;
        int left = Math.Max(0, total - simulated);
        string rateText = rate > 0 ? rate.ToString("0.000", CultureInfo.InvariantCulture) + " frames/s" : "n/a";
        string eta = left == 0 ? "0s" : rate > 0 ? Duration(left / rate) : "unknown";
        return $"[{now:HH:mm:ss}] simulated {simulated}/{total} (remaining {left}) rate {rateText} eta {eta} | rendered {rendered}/{total} (remaining {Math.Max(0, total - rendered)}) | simulate={sim} render={ren}";
    }

    public static string Duration(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:D2}m{t.Seconds:D2}s" : t.TotalMinutes >= 1 ? $"{t.Minutes}m{t.Seconds:D2}s" : $"{t.Seconds}s";
    }

    // Latest frame written by every rank; a half-written write on one rank does not count.
    static int SimulatedFrames(string root, Config c, double interval)
    {
        string foam = Paths.Foam(root);
        string[] parts = c.processorCount == 1 ? new[] { foam } : Enumerable.Range(0, c.processorCount).Select(i => Path.Combine(foam, "processor" + i)).ToArray();
        double latest = double.PositiveInfinity;
        foreach (string part in parts)
        {
            if (!Directory.Exists(part)) return 0;
            Renderer.Times(part, out double[] numbers);
            latest = Math.Min(latest, numbers.Length == 0 ? 0 : numbers[^1]);
        }
        return (int)Math.Round(latest / interval, MidpointRounding.AwayFromZero);
    }

    static int RenderedFrames(string root)
    {
        string done = Path.Combine(root, "renders", ".done");
        return Directory.Exists(done) ? Directory.EnumerateFiles(done, "*.json").Count() : 0;
    }

    static string Active(string root, string kind)
    {
        try { return Services.Active(root, kind); } catch { return "unknown"; }
    }
}
