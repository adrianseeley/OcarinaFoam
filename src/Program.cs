using System.Globalization;
using System.Diagnostics;
using System.Reflection;

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
                Console.WriteLine("ocarina DIR        build if needed, then start the solver and watcher and monitor them (Ctrl+C leaves them running)\nocarina all         start every case below the current directory in detached workers\nocarina stop DIR   stop the solver and watcher; use 'all' for every case below the current directory\nocarina clean DIR  stop, then delete everything generated; use 'all' for every case below the current directory\nocarina render DIR SETTINGS_FILE  render using JSON settings; output: DIR/renders/DATA_MODE/FIELD_T.png (1024x1596 per panel, arrays arranged horizontally); see renderSettings.json and README.md\nocarina render clean DIR  delete only DIR/renders, including all data modes");
                return 0;
            }
            if (args.Length == 1 && args[0] == "all")
            {
                string root = Paths.Canonical(Environment.CurrentDirectory);
                string[] cases = CasesUnder(root);
                if (cases.Length == 0) throw new Exception("No cases found below " + root);
                foreach (string target in cases) Launch(target);
                Console.WriteLine($"Started {cases.Length} detached case workers.");
                return 0;
            }
            if (args.Length > 0 && args[0] == "render") return Render.Run(args);
            if (args.Length == 2 && args[0] is ("stop" or "clean"))
            {
                if (args[1] == "all")
                {
                    string root = Paths.Canonical(Environment.CurrentDirectory);
                    string[] cases = CasesUnder(root);
                    if (cases.Length == 0) throw new Exception("No cases found below " + root);
                    var errors = new List<string>();
                    foreach (string target in cases)
                    {
                        try
                        {
                            if (args[0] == "stop") Services.StopAll(target); else Services.Clean(target);
                        }
                        catch (Exception error) { errors.Add(target + ": " + error.Message); }
                    }
                    if (errors.Count > 0) throw new Exception(string.Join("\n", errors));
                }
                else
                {
                    string target = Paths.Canonical(args[1]);
                    if (args[0] == "stop") Services.StopAll(target); else Services.Clean(target);
                }
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

        static string[] CasesUnder(string root)
        {
            bool Skip(string path) => path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
                .Any(part => part is ".git" or "bin" or "obj" || part.StartsWith(".foam-build-", StringComparison.Ordinal));
            return Directory.EnumerateFiles(root, "config.json", SearchOption.AllDirectories)
                .Where(path => !Skip(path))
                .Select(Path.GetDirectoryName)
                .Where(path => path != null && File.Exists(Path.Combine(path, "solidBody.stl")) && File.Exists(Path.Combine(path, "spawnPlane.stl")))
                .Select(path => Paths.Canonical(path!))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        static void Launch(string root)
        {
            string exe = Environment.ProcessPath ?? throw new Exception("Cannot resolve running application.");
            var info = new ProcessStartInfo(exe)
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            if (Path.GetFileNameWithoutExtension(exe) == "dotnet") info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            info.ArgumentList.Add(root);
            string log = Path.Combine(Paths.Logs(root), "all.log");
            Process child = Process.Start(info) ?? throw new Exception("Could not start case worker: " + root);
            _ = Pump(child, log);
            Console.WriteLine($"Started {root} (pid {child.Id}); output: {log}");
        }

        static async Task Pump(Process child, string log)
        {
            Task<string> output = child.StandardOutput.ReadToEndAsync();
            Task<string> error = child.StandardError.ReadToEndAsync();
            await Task.WhenAll(output, error);
            await child.WaitForExitAsync();
            await File.WriteAllTextAsync(log, output.Result + error.Result);
            child.Dispose();
        }
    }
}
