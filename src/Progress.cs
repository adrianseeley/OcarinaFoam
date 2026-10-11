using System.Globalization;

public static class Progress
{
    public const int PollSeconds = 30;

    // Foreground head for `ocarina DIR`: poll until both services stop; Ctrl+C only detaches.
    public static void Run(string root)
    {
        Config c = Configuration.Built(root);
        double interval = c.deltaTSeconds * c.fieldWriteIntervalTimeSteps;
        int total = (int)(Math.Ceiling(c.endTimeSeconds / interval - 1e-9) + 3);
        bool detach = false;
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; detach = true; };
        Console.CancelKeyPress += cancel;
        Console.WriteLine($"Monitoring every {PollSeconds}s. Press Ctrl+C to detach; the services keep running.");
        try
        {
            while (!detach)
            {
                double written_t = SimulatedTime(root, c);
                int simulated = (int)Math.Round(written_t / interval, MidpointRounding.AwayFromZero);
                double time = Math.Max(written_t, LogTime(root));
                int written = Points.FrameCount(Points.PointsDirectory(root));
                string sim = Active(root, "simulate"), watch = Active(root, "watch");
                Console.WriteLine(Report(time, simulated, total, written, sim, watch));
                if (sim is not ("active" or "activating") && watch is not ("active" or "activating"))
                {
                    Console.WriteLine("Both services have stopped.");
                    return;
                }
                for (int i = 0; i < PollSeconds * 10 && !detach; i++) Thread.Sleep(100);
            }
            Console.WriteLine("Detached. Services still running; stop with: ocarina stop " + root);
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    public static string Report(double time, int simulated, int total, int written, string sim, string watch)
    {
        double percent = total > 0 ? 100.0 * Math.Min(simulated, total) / total : 0;
        string t = Points.FrameName(time.ToString("R", CultureInfo.InvariantCulture));
        return $"simulate={sim} watch={watch} | time {t} | simulated {simulated}/{total} | written {written}/{total} | {percent.ToString("0.0", CultureInfo.InvariantCulture)}%";
    }

    // Latest frame written by every rank; a half-written write on one rank does not count.
    static double SimulatedTime(string root, Config c)
    {
        string foam = Paths.Foam(root);
        string[] parts = c.processorCount == 1 ? new[] { foam } : Enumerable.Range(0, c.processorCount).Select(i => Path.Combine(foam, "processor" + i)).ToArray();
        double latest = double.PositiveInfinity;
        foreach (string part in parts)
        {
            if (!Directory.Exists(part)) return 0;
            Points.Times(part, out double[] numbers);
            latest = Math.Min(latest, numbers.Length == 0 ? 0 : numbers[^1]);
        }
        return latest;
    }

    // The solver only writes fields every N steps, so the live time comes from its log.
    static double LogTime(string root)
    {
        try
        {
            string logs = Path.Combine(root, "logs");
            string log = Directory.GetFiles(logs, "solver-*.log").OrderBy(f => f, StringComparer.Ordinal).LastOrDefault();
            if (log == null) return 0;
            using var f = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int n = (int)Math.Min(f.Length, 65536);
            f.Seek(-n, SeekOrigin.End);
            var buf = new byte[n];
            f.ReadExactly(buf);
            var m = System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.UTF8.GetString(buf), @"^Time = (\S+)\s*$", System.Text.RegularExpressions.RegexOptions.Multiline);
            return m.Count == 0 ? 0 : double.Parse(m[^1].Groups[1].Value, CultureInfo.InvariantCulture);
        }
        catch { return 0; }
    }

    static string Active(string root, string kind)
    {
        try { return Services.Active(root, kind); } catch { return "unknown"; }
    }
}
