using System.Diagnostics;
using System.Text;

public static class Commands
{
    public const string Bashrc = "/usr/lib/openfoam/openfoam2606/etc/bashrc";
    public static string Shell(string text) => "'" + text.Replace("'", "'\"'\"'") + "'";
    public static ProcessStartInfo Info(string exe, string directory, params string[] args)
    {
        var info = new ProcessStartInfo(exe) { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        return info;
    }
    public static CommandResult Run(string exe, string directory, string[] args, string log = null, bool echo = false, bool check = true)
    {
        using var file = log == null ? null : new StreamWriter(new FileStream(log, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        file?.WriteLine($"UTC {DateTime.UtcNow:O}\n{exe} {string.Join(" ", args.Select(Shell))}\n");
        using var process = new Process { StartInfo = Info(exe, directory, args) };
        var output = new StringBuilder(); object gate = new();
        void Line(string line)
        {
            if (line == null) return;
            lock (gate)
            {
                file?.WriteLine(line);
                // Bounded diagnostic tail: solver output belongs on disk, not in memory.
                output.AppendLine(line);
                if (output.Length > 131072) output.Remove(0, output.Length - 65536);
                if (echo) Console.WriteLine(line);
            }
        }
        process.OutputDataReceived += (_, e) => Line(e.Data); process.ErrorDataReceived += (_, e) => Line(e.Data);
        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; try { process.Kill(true); } catch (InvalidOperationException) { } };
        Console.CancelKeyPress += cancel;
        try { process.WaitForExit(); } finally { Console.CancelKeyPress -= cancel; }
        file?.WriteLine($"Exit {process.ExitCode}; UTC {DateTime.UtcNow:O}");
        var result = new CommandResult { ExitCode = process.ExitCode, Output = output.ToString() };
        if (check && result.ExitCode != 0) throw new Exception($"{exe} exited {result.ExitCode}. Log: {log ?? "(terminal)"}\n{result.Output}");
        return result;
    }
    public static CommandResult Foam(string directory, string log, string exe, params string[] args)
    {
        if (!File.Exists(Bashrc)) throw new FileNotFoundException("Install OpenCFD OpenFOAM v2606 with prepare-machine.sh.", Bashrc);
        string script = "source " + Shell(Bashrc) + " >/dev/null || exit $?; export FOAM_FILEHANDLER=uncollated; exec " + Shell(exe) + " " + string.Join(" ", args.Select(Shell));
        return Run("/bin/bash", directory, new[] { "--noprofile", "--norc", "-c", script }, log, true);
    }
}
