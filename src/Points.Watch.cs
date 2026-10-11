public static partial class Points
{
    // Foreground loop of the watcher service. Raw OpenFOAM times are kept until three newer ones exist;
    // the oldest is then converted to a frame and deleted from every processor directory.
    public static void Watch()
    {
        if (Fields.Length == 0) throw new InvalidOperationException("Enable at least one points field.");
        if (!File.Exists(Path.Combine(OutputDirectory, "position.bin.br"))) throw new FileNotFoundException("points/position.bin.br is missing; build the case first.");
        string[] processors = FindProcessors();
        int[] offsets = Offsets(processors);
        Console.WriteLine("Watching " + CaseDirectory + "; keeping " + UntouchedTimes + " newer times; " + offsets[^1] + " points; fields " + string.Join(",", Fields.Select(f => f.Name)));
        while (true)
        {
            string[] ready = ReadyTimes(processors, new HashSet<string>());
            if (ready.Length == 0) { Thread.Sleep(PollMilliseconds); continue; }
            // Oldest first, one at a time, so only a single frame is ever held in memory.
            Process(processors, offsets, ready[0]);
        }
    }

    public static void Process(string[] processors, int[] offsets, string time)
    {
        if (!IsComplete(time))
        {
            int stride = Stride;
            float[] frame = new float[checked(offsets[^1] * stride)];
            int slot = 0;
            foreach (Layout field in Fields)
            {
                int at = slot;
                Parallel.For(0, processors.Length, p => ReadField(Path.Combine(processors[p], time, field.File), field.Vector, frame, offsets[p], offsets[p + 1] - offsets[p], stride, at, field.File == "p" ? AmbientPressure : 0));
                slot += field.Vector ? 3 : 1;
            }
            WriteFloats(FramePath(time), frame, Stride, MantissaBits);
            Console.WriteLine("Wrote " + FrameName(time) + " (t=" + time + ")");
            frame = null;
            Collect();
        }
        for (int p = processors.Length - 1; p >= 0; p--)
        {
            string path = Path.Combine(processors[p], time);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }
}
