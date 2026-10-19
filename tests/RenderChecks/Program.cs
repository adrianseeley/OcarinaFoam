using System.Reflection;
using System.Text.Json.Nodes;

// Small synthetic saved-frame fixture: no solver, services, or existing renders are touched.
string repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string root = Path.Combine(Path.GetTempPath(), "ocarina-render-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "foam"));
try
{
    string config = File.ReadAllText(Path.Combine(repository, "ocarinaZeroSlim/config.json"));
    File.WriteAllText(Path.Combine(root, "config.json"), config);
    File.WriteAllText(Path.Combine(root, "foam/config.json"), config);
    File.WriteAllText(Path.Combine(root, "foam/.built"), "test fixture");
    // Four visible points and one outside the x filter. All six recorded channels are interleaved.
    float[] positions = { 0, 0, 0, 0, .01f, 0, 0, .02f, 0, 0, .03f, 0, .002f, .04f, 0 };
    Points.WriteFloats(Points.PositionFile(root), positions, 3, 23);
    float[][] pressure = { new float[] { 10, 20, 30, 40, 1000 }, new float[] { 10, 22, 32, 38, 2000 }, new float[] { 10, 26, 34, 34, 4000 } };
    string[] frames = Enumerable.Range(0, 3).Select(i => Points.FrameName((i + 1).ToString())).ToArray();
    for (int n = 0; n < frames.Length; n++)
    {
        float[] interleaved = new float[30];
        for (int i = 0; i < 5; i++)
        {
            interleaved[i * 6] = 999;
            interleaved[i * 6 + 4] = pressure[n][i];
        }
        Points.WriteFloats(Path.Combine(root, "points", frames[n] + ".bin.br"), interleaved, 6, 23);
    }

    MethodInfo dataFrames = typeof(Render).GetMethod("DataFrames", BindingFlags.Static | BindingFlags.NonPublic)!;
    (int Index, double[] Values, double[] Activity)[] Data(int order, int first = 0, int last = int.MaxValue) =>
        ((IEnumerable<(int, double[], double[])>)dataFrames.Invoke(null, new object[] { Path.Combine(root, "points"), frames, 6, 5, 4, order, first, last })!).ToArray();
    var standard = Data(0);
    Check(standard.Length == 3 && standard[0].Activity == null, "standard keeps the first frame without inventing activity");
    Check(standard[2].Values.SequenceEqual(new double[] { 10, 26, 34, 34, 4000 }), "standard uses the pressure channel");
    Check(standard[2].Activity.SequenceEqual(new double[] { 0, 4, 2, -4, 2000 }), "standard activity uses first differences");
    var first = Data(1);
    Check(first.Length == 2 && first[0].Index == 1, "first differences skip one frame");
    Check(first[1].Values.SequenceEqual(new double[] { 0, 4, 2, -4, 2000 }), "signed first differences");
    var second = Data(2);
    Check(second.Length == 1 && second[0].Index == 2, "second differences skip two frames");
    Check(second[0].Values.SequenceEqual(new double[] { 0, 2, 0, -2, 1000 }), "second differences remove constant-rate changes");
    Check(Data(2, 2, 2)[0].Values.SequenceEqual(second[0].Values), "single-frame selection still loads history");
    Check(Data(0, 2, 2)[0].Activity.SequenceEqual(standard[2].Activity), "single standard frame still loads opacity history");

    JsonObject settings = JsonNode.Parse(File.ReadAllText(Path.Combine(repository, "renderSettings.json")))!.AsObject();
    string settingsFile = Path.Combine(root, "settings.json");
    void Run()
    {
        File.WriteAllText(settingsFile, settings.ToJsonString());
        Render.Run(new[] { "render", root, settingsFile });
    }
    string Output(string mode, int index) => Path.Combine(root, "renders", mode, "pressure_" + frames[index] + ".bmp");
    // fromRight projects y horizontally; framing includes the filtered-out point at y=40 mm.
    (byte R, byte G, byte B) Pixel(string file, int point)
    {
        int x = (int)Math.Floor(Render.Size / 2.0 + (positions[point * 3 + 1] * 1000f - 20) * Render.Size / 42.0);
        int y = Render.Size / 2;
        byte[] bmp = File.ReadAllBytes(file);
        int offset = 54 + ((Render.Size - 1 - y) * Render.Size + x) * 3;
        return (bmp[offset + 2], bmp[offset + 1], bmp[offset]);
    }
    Run();
    Check(Directory.GetFiles(Path.Combine(root, "renders/standard")).Length == 3, "standard renders every frame");
    Check(Pixel(Output("standard", 0), 0) == (255, 0, 0), "first standard frame uses full alpha");
    Check(Pixel(Output("standard", 1), 1) == (0, 255, 102), "each frame normalizes opacity independently of larger changes in other frames and filtered points");
    Check(Pixel(Output("standard", 1), 0) == (38, 0, 0), "unchanged pressure retains the configured faint opacity");
    Check(Pixel(Output("standard", 2), 1) == (0, 204, 255), "pressure colour is independent of change and filtered outliers");
    Check(Pixel(Output("standard", 2), 4) == (0, 0, 0), "x filter excludes outliers");
    settings["fadeUnchanged"] = false;
    settings["frames"] = "-t";
    Run();
    Check(Pixel(Output("standard", 2), 0) == (255, 0, 0), "fading can be disabled in standard mode");
    settings["fadeUnchanged"] = true;
    settings["unchangedAlpha"] = 0;
    Run();
    Check(Pixel(Output("standard", 2), 0) == (0, 0, 0), "zero floor hides unchanged pressure completely");
    settings["frames"] = "*";
    foreach (string mode in new[] { "firstDifferences", "secondDifferences" })
    {
        settings["dataMode"] = mode;
        Run();
        Check(Directory.GetFiles(Path.Combine(root, "renders", mode)).Length == (mode == "firstDifferences" ? 2 : 1), "eligible output count for " + mode);
        Check(Pixel(Output(mode, 2), 0) == (0, 0, 0), "stationary points hidden in " + mode);
        Check(Pixel(Output(mode, 2), 1) == (255, 0, 0), "nonzero difference uses its own scale in " + mode);
    }
    Check(Pixel(Output("secondDifferences", 2), 2) == (0, 0, 0), "constant-rate changes disappear in second differences");
    settings["frames"] = "0";
    Reject(Run, "explicit frame without history");
    settings["frames"] = "*";
    settings["filter"]!["x"]!["typo"] = 1;
    Reject(Run, "unknown filter comparison");
    settings["filter"]!["x"]!.AsObject().Remove("typo");
    settings["typo"] = true;
    Reject(Run, "unknown settings key");
    settings.Remove("typo");
    settings["unchangedAlpha"] = 2;
    Reject(Run, "invalid opacity floor");
    settings["unchangedAlpha"] = 0;
    settings.Remove("dataMode");
    Reject(Run, "missing required setting");
    // Nonfinite history must not become a spurious finite difference.
    Points.WriteFloats(Path.Combine(root, "points", frames[1] + ".bin.br"), Enumerable.Range(0, 30).Select(i => i == 4 ? float.NaN : 0f).ToArray(), 6, 23);
    Check(double.IsNaN(Data(1)[1].Values[0]) && double.IsNaN(Data(2)[0].Values[0]), "nonfinite history propagates for exclusion");
    File.Delete(Path.Combine(root, "points", frames[2] + ".bin.br"));
    settings["dataMode"] = "secondDifferences";
    Reject(Run, "insufficient saved frames");
    string video = Path.Combine(root, "pressure.mp4");
    File.WriteAllText(video, "keep video");
    Check(Render.Run(new[] { "render", "clean", root }) == 0, "render clean succeeds");
    Check(!Directory.Exists(Path.Combine(root, "renders")), "render clean removes all data-mode render folders");
    Check(File.ReadAllText(video) == "keep video" && File.ReadAllText(Path.Combine(root, "config.json")) == config
        && File.Exists(Points.PositionFile(root)) && File.Exists(Path.Combine(root, "points", frames[0] + ".bin.br"))
        && File.Exists(Path.Combine(root, "foam/.built")) && File.Exists(settingsFile), "render clean preserves simulation data, settings, and videos");
    Check(Render.Run(new[] { "render", "clean", root }) == 0, "render clean succeeds when already clean");
    Console.WriteLine("All render checks passed.");
}
finally { Directory.Delete(root, true); }

static void Check(bool success, string description)
{
    if (!success) throw new Exception("FAILED: " + description);
    Console.WriteLine("PASS: " + description);
}
static void Reject(Action action, string description)
{
    try { action(); }
    catch (Exception error) when (error is ArgumentException or System.Text.Json.JsonException)
    { Console.WriteLine("PASS: rejects " + description); return; }
    throw new Exception("FAILED: expected rejection for " + description);
}
