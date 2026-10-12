using System.Globalization;

public static class Program
{
    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                Console.WriteLine("ocarina DIR        build if needed, then start the solver and watcher and monitor them (Ctrl+C leaves them running)\nocarina stop DIR   stop the solver and watcher\nocarina clean DIR  stop, then delete everything generated\nocarina render DIR FIELD T VIEW COLOR PIXELSxALPHA [CONDITION]  write DIR/renders/FIELD_T.bmp (2048x2048); T is a frame time, index, -t for last, or * for all frames with one shared colour range; VIEW is fromLeft, fromRight, fromAbove, fromBelow, fromTop, fromBottom, or fromAboveRight; COLOR is inferno; PIXELSxALPHA is required, e.g. 10x0.5; CONDITION joins x/y/z comparisons with &&, e.g. 'y>=0&&x<5'\nocarina self-test");
                return 0;
            }
            if (args.Length == 1 && args[0] == "self-test") return SelfTest.Run();
            if (args.Length > 0 && args[0] == "render") return Render.Run(args);
            if (args.Length == 2 && args[0] is ("stop" or "clean"))
            {
                string target = Paths.Canonical(args[1]);
                if (args[0] == "stop") Services.StopAll(target); else Services.Clean(target);
                return 0;
            }
            if (args.Length == 1)
            {
                string root = Paths.Canonical(args[0]);
                if (!File.Exists(Path.Combine(Paths.Foam(root), ".built"))) BuildCase.Run(root);
                Services.StartAll(root);
                return 0;
            }
            if (args.Length != 2 || args[0] is not ("_simulate" or "_watch")) throw new ArgumentException("Use: ocarina help");
            string command = args[0];
            string service = Paths.Canonical(args[1]);
            // Service entrypoints; intentionally absent from the public help.
            if (command == "_simulate") { Solver.Run(service); return 0; }
            if (command == "_watch")
            {
                using var gate = Paths.Lock(service, "watch");
                Config config = Configuration.Built(service);
                Points.Configure(service, config);
                Points.Watch();
                return 0;
            }
            throw new ArgumentException("Unknown command. Use: ocarina help");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.ToString());
            return 1;
        }
    }
}
