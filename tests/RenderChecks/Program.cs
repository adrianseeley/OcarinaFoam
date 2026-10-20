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

    MethodInfo colorMethod = typeof(Render).GetMethod("Color", BindingFlags.Static | BindingFlags.NonPublic)!;
    (byte R, byte G, byte B) Gray(double value) => ((byte, byte, byte))colorMethod.Invoke(null, new object[] { "monochrome", value });
    Check(Gray(0) == (0, 0, 0) && Gray(0.5) == (128, 128, 128) && Gray(1) == (255, 255, 255),
        "monochrome runs from black through gray to white");

    (byte R, byte G, byte B) Contrast(double value) => ((byte, byte, byte))colorMethod.Invoke(null, new object[] { "monochrome-contrast", value });
    Check(Contrast(0) == (0, 0, 0) && Contrast(0.5) == (128, 128, 128) && Contrast(1) == (255, 255, 255)
        && Contrast(0.25) == (26, 26, 26) && Contrast(0.75) == (230, 230, 230),
        "contrast monochrome deepens shadows and lifts highlights while preserving midpoint and endpoints");

    (byte R, byte G, byte B) Polarize(double value) => ((byte, byte, byte))colorMethod.Invoke(null, new object[] { "monochrome-polarize", value });
    Check(Polarize(0) == (0, 0, 0) && Polarize(0.5) == (128, 128, 128) && Polarize(1) == (255, 255, 255)
        && Polarize(0.45) == (10, 10, 10) && Polarize(0.55) == (245, 245, 245),
        "polarized monochrome has a steep transition while preserving midpoint and endpoints");

    MethodInfo dataFrames = typeof(Render).GetMethod("DataFrames", BindingFlags.Static | BindingFlags.NonPublic)!;
    (int Index, double[] Values, double[] Delta)[] Data(int order, int first = 0, int last = int.MaxValue) =>
        ((IEnumerable<(int, double[], double[])>)dataFrames.Invoke(null, new object[] { Path.Combine(root, "points"), frames, 6, 5, 4, order, first, last })!).ToArray();
    var standard = Data(0);
    Check(standard.Length == 3, "standard keeps the first frame");
    Check(standard[2].Values.SequenceEqual(new double[] { 10, 26, 34, 34, 4000 }), "standard uses the pressure channel");
    var first = Data(1);
    Check(first.Length == 2 && first[0].Index == 1, "first differences skip one frame");
    Check(first[1].Values.SequenceEqual(new double[] { 0, 4, 2, -4, 2000 }), "signed first differences");
    var second = Data(2);
    Check(second.Length == 1 && second[0].Index == 2, "second differences skip two frames");
    Check(second[0].Values.SequenceEqual(new double[] { 0, 2, 0, -2, 1000 }), "second differences remove constant-rate changes");
    Check(Data(2, 2, 2)[0].Values.SequenceEqual(second[0].Values), "single-frame selection still loads history");

    Check(standard[0].Delta == null && standard[2].Delta.SequenceEqual(new double[] { 0, 4, 2, -4, 2000 }), "delta preserves signed frame-to-frame changes");
    Check(Data(0, 2, 2)[0].Delta.SequenceEqual(standard[2].Delta), "single-frame render reads delta history");
    Check(first[0].Delta == null && first[1].Delta.SequenceEqual(new double[] { 0, 2, 0, -2, 1000 }), "difference-mode delta compares displayed values");

    var settingsNode = JsonNode.Parse(File.ReadAllText(Path.Combine(repository, "renderSettings.json")))!;
    JsonObject settings = (settingsNode is JsonArray array ? array[0]! : settingsNode).DeepClone().AsObject();
    settings["color"] = "hsv";
    settings["dataMode"] = "standard";
    settings["frames"] = "*";
    settings["removeRankBandMiddle"] = 0.5;
    settings["removeRankBandLow"] = null;
    settings["removeRankBandHigh"] = null;
    settings["alpha"] = 1;
    settings["filter"] = JsonNode.Parse("""{"x":{"greaterThanEqual":-1,"lessThanEqual":1}}""");
    settings["removeDeltaRankBandMiddle"] = null;
    settings["removeDeltaRankBandLow"] = null;
    settings["removeDeltaRankBandHigh"] = null;
    string settingsFile = Path.Combine(root, "settings.json");
    void Run()
    {
        File.WriteAllText(settingsFile, settings.ToJsonString());
        Render.Run(new[] { "render", root, settingsFile });
    }
    string Output(string mode, int index) => Path.Combine(root, "renders", mode, "pressure_" + frames[index] + ".png");
    // fromRight projects y horizontally; framing includes the filtered-out point at y=40 mm.
    (byte R, byte G, byte B) Pixel(string file, int point)
    {
        int x = (int)Math.Floor(Render.Size / 2.0 + (positions[point * 3 + 1] * 1000f - 20) * Render.Size / 42.0);
        int y = Render.HeaderHeight + Render.Size / 2;
        byte[] bmp = ReadBitmap(file);
        int offset = 54 + ((BitConverter.ToInt32(bmp, 22) - 1 - y) * Render.Size + x) * 3;
        return (bmp[offset + 2], bmp[offset + 1], bmp[offset]);
    }
    Run();
    byte[] legendBitmap = ReadBitmap(Output("standard", 2));
    int bitmapHeight = BitConverter.ToInt32(legendBitmap, 22);
    Check(BitConverter.ToInt32(legendBitmap, 18) == Render.Size && bitmapHeight >= Render.ImageHeight
        && legendBitmap.Length == 54 + Render.Size * bitmapHeight * 3, "legend footer is added below the original chart with correct bitmap dimensions");
    (byte R, byte G, byte B) FooterPixel(int x, int y)
    {
        int p = 54 + ((bitmapHeight - 1 - y) * Render.Size + x) * 3;
        return (legendBitmap[p + 2], legendBitmap[p + 1], legendBitmap[p]);
    }
    int footer = Render.HeaderHeight + Render.Size;
    Check(FooterPixel(32, footer + Render.SectionPadding + 72) == (255, 0, 0), "pressure palette begins at legend inset");
    Check(Enumerable.Range(0, Render.Size).Any(x => FooterPixel(x, 12) == (255,255,255)), "summary appears above plot");
    Check(Enumerable.Range(0, Render.Size).Any(x => FooterPixel(x, 56) == (240,90,90)), "disabled mute labels are red");
    for (int section = 0; section < 3; section++)
        Check(FooterPixel(8, footer + section * (Render.SectionHeight + Render.SectionGap)) == (255,255,255)
            && FooterPixel(Render.Size - 9, footer + section * (Render.SectionHeight + Render.SectionGap) + Render.SectionHeight - 2) == (255,255,255),
            "footer section has white outline");
    Check(FooterPixel(31, footer + Render.SectionPadding + 68) == (255,255,255), "color bar has white outline");
    int timeY = footer + (Render.SectionHeight + Render.SectionGap) * 2 + Render.SectionPadding + 82;
    foreach (var tripletColor in new[] { (255,195,80), (80,205,255), (205,145,255), (255,135,165) })
        Check(Enumerable.Range(0, Render.Size).Any(x => FooterPixel(x, timeY) == ((byte)tripletColor.Item1, (byte)tripletColor.Item2, (byte)tripletColor.Item3)),
            "timestamp triplet has its own color");
    Check(Enumerable.Range(0, Render.Size).Any(x => FooterPixel(x, timeY) == (255,255,255)), "timestamp units stay white");
    MethodInfo histogram = typeof(Render).GetMethod("Histogram", BindingFlags.Static | BindingFlags.NonPublic)!;
    int[] Histogram(double[] values, bool[] selected, double min, double max) =>
        (int[])histogram.Invoke(null, new object[] { values, selected, min, max })!;
    var bins = Histogram(new[] { 10.0, 25.0, 40.0, double.NaN, 1000.0 }, new[] { true, true, true, true, false }, 10, 40);
    Check(bins.Sum() == 3 && bins[0] == 1 && bins[64] == 1 && bins[127] == 1, "histogram aligns values with legend scale and excludes invalid/unselected points");
    Check(Histogram(new[] { 0.0, 0.0 }, new[] { true, true }, 0, 0)[0] == 2
        && Histogram(new[] { double.NaN }, new[] { true }, 0, 0).Sum() == 0, "histogram handles constant and empty data");
    string preview = Environment.GetEnvironmentVariable("OCARINA_RENDER_PREVIEW");
    if (!string.IsNullOrEmpty(preview)) File.Copy(Output("standard", 2), preview, true);
    Check(Directory.GetFiles(Path.Combine(root, "renders/standard")).Length == 3, "standard renders every frame");
    Check(Pixel(Output("standard", 0), 0) == (255, 0, 0), "low extreme remains visible");
    Check(Pixel(Output("standard", 1), 1) == (0, 0, 0), "central values are completely hidden");
    Check(Pixel(Output("standard", 2), 4) == (0, 0, 0), "x filter excludes outliers");
    Check(Enumerable.Range(16, Render.Size - 32).Any(x => FooterPixel(x, footer + Render.SectionPadding + 72) == (80,220,120)), "active mute interval is green on pressure legend");
    MethodInfo rankBands = typeof(Render).GetMethod("RankBands", BindingFlags.Static | BindingFlags.NonPublic)!;
    (bool[] Hidden, (double Low, double High)[] Bands, (double Low, double High)[] Ranks, int Count)
        Bands(double[] values, bool[] selected, double? middle, double? low = null, double? high = null) =>
        ((bool[], (double, double)[], (double, double)[], int))rankBands.Invoke(null, new object[] { values, selected, middle, low, high });
    double[] ranked = Enumerable.Range(0, 100).Select(i => (double)i).ToArray();
    ranked[99] = 1_000_000;
    bool[] all = Enumerable.Repeat(true, ranked.Length).ToArray();
    var rankBand = Bands(ranked, all, 0.90);
    Check(rankBand.Bands[0] == (5.0, 94.0) && rankBand.Hidden.Count(h => h) == 90, "rank counts resist a long-tail outlier");
    var tails = Bands(ranked, all, null, 0.1, 0.1);
    Check(tails.Bands.SequenceEqual(new[] { (0.0, 9.0), (90.0, 1_000_000.0) }) && tails.Hidden.Count(h => h) == 20,
        "low and high fractions remove exact rank counts");
    Check(Bands(ranked, all, 0.8, 0.2, 0.2).Bands.SequenceEqual(new[] { (0.0, 1_000_000.0) }), "overlapping rank bands merge");
    var ties = Bands(Enumerable.Repeat(0.0, 100).ToArray(), all, null, 0.05);
    Check(ties.Hidden.Count(h => h) == 5 && ties.Hidden.Take(5).All(h => h) && ties.Hidden.Skip(5).All(h => !h),
        "ties are broken by point index so five percent does not hide every zero");
    Check(Bands(new[] { 2.0, 2.0, double.NaN, 999.0 }, new[] { true, true, true, false }, null, 0.5).Hidden.SequenceEqual(new[] { true, false, false, false }),
        "rank population excludes invalid and spatially filtered points");
    Check(Bands(Array.Empty<double>(), Array.Empty<bool>(), 0.95).Bands.Length == 0 && Bands(ranked, all, 0).Bands.Length == 0,
        "empty selections and zero fraction have no band");

    byte[] rankPlot = new byte[Render.Size * Render.ImageHeight * 3];
    typeof(Render).GetMethod("DrawLegend", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
        new object[] { rankPlot, "hsv", 0.0, 1_000_000.0, ranked, all, new[] { "TEST", "NO FILTER", "100 POINTS", "100 POINTS", "100 POINTS", "100 POINTS" },
            Bands(ranked, all, null), ranked, Bands(ranked, all, null, 0.05),
            new Render.Settings { field="pressure", frames="*", view="fromRight", color="hsv", pointSize=20, alpha=0.5, dataMode="standard" }, frames[1], frames[2], 0.5 });
    (byte R, byte G, byte B) RankPixel(int x, int y)
    {
        int p = (y * Render.Size + x) * 3;
        return (rankPlot[p + 2], rankPlot[p + 1], rankPlot[p]);
    }
    int deltaTop = Render.HeaderHeight + Render.Size + Render.SectionHeight + Render.SectionGap + Render.SectionPadding;
    Check(RankPixel(40, deltaTop + 40) == (18,60,30)
        && RankPixel(100, deltaTop + 40) == (0,0,0),
        "delta mute shading follows rank despite an extreme outlier");
    Check(RankPixel(Render.Size - 33, deltaTop) == (255,255,255), "delta curve reaches maximum in white");
    settings["removeRankBandMiddle"] = null;
    settings["alpha"] = 0.5;
    settings["frames"] = "-t";
    Run();
    Check(Pixel(Output("standard", 2), 0) == (128, 0, 0), "null disables removal and extreme uses shared alpha");
    Check(Pixel(Output("standard", 2), 1) == (0, 102, 128), "central value uses the same shared alpha when hiding is disabled");
    settings["removeRankBandMiddle"] = 1;
    Run();
    Check(Pixel(Output("standard", 2), 0) == (0, 0, 0), "100 percent hides endpoints too");
    settings["removeRankBandMiddle"] = 0.5;
    settings["alpha"] = 1;
    settings["filter"] = JsonNode.Parse("""{"x":{"greaterThanEqual":-1,"lessThanEqual":1}}""");
    settings["frames"] = "*";
    settings["removeRankBandMiddle"] = null;
    settings["removeDeltaRankBandLow"] = 0.5;
    Run();
    Check(Pixel(Output("standard", 2), 0) == (0, 0, 0) && Pixel(Output("standard", 2), 2) == (0, 0, 0),
        "delta low band hides least-changing points");
    Check(Pixel(Output("standard", 2), 1) == (0, 204, 255), "delta low band preserves larger changes with shared alpha");
    Check(Pixel(Output("standard", 0), 1) != (0, 0, 0), "first frame without delta history remains visible");
    byte[] deltaBitmap = ReadBitmap(Output("standard", 2));
    int deltaHeight = BitConverter.ToInt32(deltaBitmap, 22);
    (byte R, byte G, byte B) DeltaPixel(int x, int y)
    {
        int p = 54 + ((deltaHeight - 1 - y) * Render.Size + x) * 3;
        return (deltaBitmap[p + 2], deltaBitmap[p + 1], deltaBitmap[p]);
    }
    Check(DeltaPixel(Render.Size - 33, deltaTop) == (255,255,255), "white delta curve includes all selected finite changes");
    Check(DeltaPixel(32 + (Render.Size - 64)/2, deltaTop + 40) == (80,220,120), "lowest half of delta ranks has its cutoff at half the legend width");
    Check(DeltaPixel(800, deltaTop + 70) == (0,0,0), "delta curve has no bar beneath it");
    settings["removeRankBandHigh"] = 0.5;
    Run();
    Check(Pixel(Output("standard", 2), 1) == (0, 204, 255), "value removal does not change delta ranks or colour scaling");
    settings["removeRankBandHigh"] = null;
    settings["removeDeltaRankBandLow"] = null;
    settings["removeDeltaRankBandHigh"] = 0.5;
    Run();
    Check(Pixel(Output("standard", 2), 1) == (0, 0, 0) && Pixel(Output("standard", 2), 0) == (255, 0, 0),
        "delta high band hides largest absolute changes");
    settings["removeDeltaRankBandHigh"] = null;
    settings["removeDeltaRankBandMiddle"] = 0.5;
    Run();
    Check(Pixel(Output("standard", 2), 2) == (0, 0, 0), "delta middle band removes central activity ranks");
    settings["removeDeltaRankBandMiddle"] = null;
    settings["removeRankBandMiddle"] = 0.5;
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
    foreach (string key in new[] { "removeRankBandMiddle", "removeRankBandLow", "removeRankBandHigh",
        "removeDeltaRankBandLow", "removeDeltaRankBandMiddle", "removeDeltaRankBandHigh" })
    {
        var original = settings[key]?.DeepClone();
        settings[key] = 1.01;
        Reject(Run, "invalid fraction for " + key);
        settings[key] = original;
    }
    settings["removeRankBandMiddle"] = 0.5;
    settings.Remove("dataMode");
    Reject(Run, "missing required setting");
    settings["dataMode"] = "standard";
    settings["frames"] = "-t";
    var panelArray = new JsonArray(settings.DeepClone(), settings.DeepClone());
    panelArray[1]!["color"] = "monochrome";
    File.WriteAllText(settingsFile, panelArray.ToJsonString());
    Render.Run(new[] { "render", root, settingsFile });
    byte[] rowImage = ReadBitmap(Output("standard", 2));
    Check(BitConverter.ToInt32(rowImage, 18) == Render.Size * 2, "array renders panels in one horizontal bitmap");
    int height = BitConverter.ToInt32(rowImage, 22);
    Check(rowImage.Length == 54 + Render.Size * 2 * height * 3, "combined bitmap has complete rows");
    File.WriteAllText(settingsFile, "[]");
    Reject(() => Render.Run(new[] { "render", root, settingsFile }), "empty panel array");
    panelArray[1]!["frames"] = "0";
    File.WriteAllText(settingsFile, panelArray.ToJsonString());
    Reject(() => Render.Run(new[] { "render", root, settingsFile }), "mismatched panel frame selections");
    // Nonfinite history must not become a spurious finite difference.
    Points.WriteFloats(Path.Combine(root, "points", frames[1] + ".bin.br"), Enumerable.Range(0, 30).Select(i => i == 4 ? float.NaN : 0f).ToArray(), 6, 23);
    Check(double.IsNaN(Data(1)[1].Values[0]) && double.IsNaN(Data(2)[0].Values[0]), "nonfinite history propagates for exclusion");
    settings["dataMode"] = "standard";
    settings["frames"] = "-t";
    settings["removeRankBandMiddle"] = null;
    settings["removeDeltaRankBandLow"] = 0.5;
    Run();
    Check(Pixel(Output("standard", 2), 0) == (0, 0, 0), "nonfinite change history is skipped when delta removal is active");
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


static byte[] ReadBitmap(string path)
{
    byte[] png = File.ReadAllBytes(path);
    Check(png.Take(8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}), "valid PNG signature");
    int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
    int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
    using var compressed = new MemoryStream();
    for (int offset = 8; offset < png.Length;)
    {
        int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
        if (System.Text.Encoding.ASCII.GetString(png, offset + 4, 4) == "IDAT") compressed.Write(png, offset + 8, length);
        offset += length + 12;
    }
    compressed.Position = 0;
    using var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionMode.Decompress);
    byte[] result = new byte[54 + width * height * 3];
    BitConverter.GetBytes(width).CopyTo(result,18);
    BitConverter.GetBytes(height).CopyTo(result,22);
    byte[] row = new byte[width * 3 + 1];
    for (int y = 0; y < height; y++)
    {
        zlib.ReadExactly(row);
        if (row[0] != 1) throw new Exception("Expected PNG Sub filter");
        for (int x = 3; x < width * 3; x++) row[x+1] = unchecked((byte)(row[x+1] + row[x-2]));
        for (int x = 0; x < width; x++)
            for (int c = 0; c < 3; c++)
                result[54 + ((height - 1 - y) * width + x) * 3 + c] = row[1 + x * 3 + 2 - c];
    }
    return result;
}
