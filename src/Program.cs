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
                Console.WriteLine("ocarina build DIR\nocarina simulate [stop] DIR\nocarina render [stop] DIR\nocarina render preview DIR\nocarina start DIR\nocarina stop DIR\nocarina clean DIR\nocarina audio DIR\nocarina check DIR\nocarina self-test");
                return 0;
            }
            if (args.Length == 1 && args[0] == "self-test") return SelfTest.Run();
            if (args.Length == 3 && args[0] == "render" && args[1] == "preview")
            {
                string previewRoot = Paths.Canonical(args[2]);
                Config config = Configuration.Built(previewRoot);
                Renderer.Configure(previewRoot, config, false);
                Renderer.Preview(previewRoot);
                return 0;
            }
            bool stop = args.Length == 3 && args[1] == "stop";
            if (args.Length != 2 && !stop) throw new ArgumentException("Use: ocarina help");
            string command = args[0];
            if (stop && command is not ("simulate" or "render")) throw new ArgumentException("Only simulate and render support stop.");
            string root = Paths.Canonical(args[^1]);
            if (command == "build") { BuildCase.Run(root); return 0; }
            if (command == "start" && !stop) { Services.StartAll(root); return 0; }
            if (command == "stop" && !stop) { Services.StopAll(root); return 0; }
            if (command == "clean" && !stop) { Services.Clean(root); return 0; }
            if (command == "audio" && !stop) { Audio.Run(root); return 0; }
            if (command == "check") { Status.Run(root); return 0; }
            if (command is "simulate" or "render")
            {
                if (stop) Services.Stop(root, command);
                else Services.Start(root, command);
                return 0;
            }
            // systemd's entrypoints; intentionally absent from the public help.
            if (command == "_simulate") { Solver.Run(root); return 0; }
            if (command == "_render")
            {
                using var gate = Paths.Lock(root, "render");
                Config config = Configuration.Built(root);
                Renderer.Configure(root, config);
                Renderer.Run();
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
