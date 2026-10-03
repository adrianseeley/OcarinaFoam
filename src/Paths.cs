using System.Security.Cryptography;
using System.Text;

public static class Paths
{
    public static string Canonical(string path)
    {
        string full = Path.GetFullPath(path);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
        // Resolve every component so symlink aliases cannot create a second service.
        string current = Path.GetPathRoot(full);
        foreach (string part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var info = new DirectoryInfo(Path.Combine(current, part));
            current = info.ResolveLinkTarget(true)?.FullName ?? info.FullName;
        }
        if (current.Any(char.IsControl)) throw new ArgumentException("Control characters are not supported in case paths.");
        return Path.TrimEndingDirectorySeparator(current);
    }

    public static string Id(string root) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root))).ToLowerInvariant()[..24];
    public static string Foam(string root) => Path.Combine(root, "foam");
    public static string Logs(string root) => Directory.CreateDirectory(Path.Combine(root, "logs")).FullName;
    public static string Unit(string root, string kind) => $"ocarina-{Id(root)}-{kind}.service";

    // Advisory locks coordinate build and workers, including different CLI invocations.
    // Keep lock files: deleting a locked inode would permit a second independent lock.
    public static FileStream Lock(string root, string name)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Ocarina runs on Linux.");
        string path = Path.Combine(root, "." + name + ".lock");
        var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        try { file.Lock(0, 1); return file; }
        catch { file.Dispose(); throw new IOException($"Case {name} is already in use: {root}"); }
    }

    public static void Atomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        { writer.Write(text); writer.Flush(); stream.Flush(true); }
        File.Move(temporary, path, true);
    }
}
