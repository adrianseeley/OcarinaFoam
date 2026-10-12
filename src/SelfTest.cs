
// Pure-C# checks of the background domain and inlet/probe handling. No OpenFOAM required.
public static class SelfTest
{
    static int failures;
    static void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; }
    static bool Throws(Action a) { try { a(); return false; } catch (Exception) { return true; } }
    static Config Cfg(Padding p) => new Config { worldPaddingMillimeters = p, cellSizeMillimeters = 5 };
    static Bounds B(double[] min, double[] max) => new Bounds { Min = min, Max = max };
    static double Span(List<Band> bands) => bands.Sum(b => b.End - b.Start);
    public static int Run()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "ocarinaZero");
        Config c = Configuration.Load(Path.Combine(dir, "config.json"));
        string full = File.ReadAllText(Path.Combine(dir, "config.json"));
        string Without(string key) { var node = System.Text.Json.Nodes.JsonNode.Parse(full, null, new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip }); var path = key.Split('.'); var o = node.AsObject(); for (int i = 0; i < path.Length - 1; i++) o = o[path[i]].AsObject(); o.Remove(path[^1]); string f = Path.Combine(Path.GetTempPath(), "ocarina-cfg-" + Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(f, node.ToJsonString()); return f; }
        foreach (string key in new[] { "watcher", "points", "points.compression", "probes", "cellSizeMillimeters", "physics.damping" })
        {
            string f = Without(key);
            try { Check(Throws(() => Configuration.Load(f)), "missing config key is an error: " + key); } finally { File.Delete(f); }
        }
        Check(c.points != null, "example points config loads");
        Check(Points.ParseTime("000s000ms000us100ns", "t") == 100 && Points.Seconds(100) == "0.0000001" && Points.Seconds(50_000_000) == "0.05", "time strings parse and print without exponent");
        Check(c.Steps() == 500000 && c.Frames() == 10000 && Points.Seconds(c.EndNs()) == "0.050015", "example timing: 500000 steps, 10000 frames, three guard writes");
        foreach (var (what, from, to) in new[] { ("lastT off the step grid", "\"lastT\": \"000s050ms000us000ns\"", "\"lastT\": \"000s050ms000us050ns\""), ("lastT not a multiple of keepEvery", "\"keepEvery\": 50", "\"keepEvery\": 7"), ("malformed time", "\"deltaT\": \"000s000ms000us100ns\"", "\"deltaT\": \"1e-7\"") })
        {
            Check(full.Contains(from), "timing mutation applies: " + what);
            string f = Path.Combine(Path.GetTempPath(), "ocarina-time-" + Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(f, full.Replace(from, to));
            try { Check(Throws(() => Configuration.Load(f)), "rejected: " + what); } finally { File.Delete(f); }
        }
        { string f = Path.Combine(Path.GetTempPath(), "ocarina-watcher-" + Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(f, full.Replace("\"watcher\": \"systemd\"", "\"watcher\": \"bogus\"")); try { Check(Throws(() => Configuration.Load(f)), "unknown watcher is an error"); } finally { File.Delete(f); } }
        Facet[] solid = Geometry.Read(Path.Combine(dir, "solidBody.stl"));
        Facet[] inlet = Geometry.Inlet(Geometry.Read(Path.Combine(dir, "spawnPlane.stl")));
        Bounds sb = Geometry.Bound(solid), ib = Geometry.Bound(inlet);

        Padding p = c.worldPaddingMillimeters;
        Check(p.xMin == 0 && p.xMax == 0 && p.yMin == 0 && p.yMax == 0 && p.zMin == 0 && p.zMax == 25, "example config: zero padding except zMax 25");
        Domain d = BackgroundMesh.Layout(sb, ib, c);
        Check(d.Min[0] == sb.Min[0] && d.Max[0] == sb.Max[0] && d.Min[1] == sb.Min[1] && d.Max[1] == sb.Max[1] && d.Min[2] == sb.Min[2], "zero-padded sides equal the solid's exact bounds");
        Check(d.Max[2] >= sb.Max[2] + 25 && d.Max[2] < sb.Max[2] + 25 + c.cellSizeMillimeters, "padded +Z side keeps outward grid rounding");
        Check(Math.Abs(Span(d.X) - (d.Max[0] - d.Min[0])) < 1e-9 && Math.Abs(Span(d.Z) - (d.Max[2] - d.Min[2])) < 1e-9, "blocks tile the domain exactly");
        Check(Throws(() => BackgroundMesh.Layout(sb, ib, Cfg(new Padding { xMin = 0, xMax = 0, yMin = 0, yMax = 0, zMin = 0, zMax = 0 }))), "no air above the body is rejected for locationInMesh");

        // Non-grid-aligned spans: neither 60.2 nor 58.3 divides by 5.
        var oddSolid = B(new[] { -30.2, 0, -52.7 }, new[] { 30, 105.3, 7.5 });
        Domain o = BackgroundMesh.Layout(oddSolid, ib, Cfg(new Padding { xMin = 0, xMax = 0, yMin = 0, yMax = 0, zMin = 0, zMax = 100 }));
        Check(o.Min[0] == -30.2 && o.Max[0] == 30 && o.Min[2] == -52.7 && o.Max[1] == 105.3, "non-grid-aligned zero-padded bounds are exact");
        Check(o.Ny == 22 && (o.Max[1] - o.Min[1]) / o.Ny <= 5, "non-grid-aligned Y gets ceil cell count with cells no larger than nominal");
        Check(o.X.All(b => b.Cells >= 1 && (b.End - b.Start) / b.Cells <= 6.25 + 1e-9) && o.Z.All(b => b.Cells >= 1), "non-grid-aligned X/Z bands have valid cell counts");
        Check(Throws(() => BackgroundMesh.Layout(B(new[] { -2.6, 0, -52.5 }, new[] { 30, 105, 7.5 }), ib, Cfg(new Padding { xMin = 0, xMax = 0, yMin = 0, yMax = 0, zMin = 0, zMax = 100 }))), "sliver band next to the inlet is rejected");
        var merged = BackgroundMesh.Bands(0, 30, 20.5, 21.5, 5);
        Check(merged[0].Cells == 4 && merged[0].End == 20.5 && merged.Count == 4, "sub-quarter-cell remainder is merged, not meshed as a sliver");

        // Inlet geometry: source band matches the inlet rectangle exactly and is the only airSource block.
        var s = BackgroundMesh.Build(d);
        double sx = d.X.Where(b => b.Source).Sum(b => b.End - b.Start), sz = d.Z.Where(b => b.Source).Sum(b => b.End - b.Start);
        Check(d.X.Single(b => b.Source).Start == ib.Min[0] && d.X.Single(b => b.Source).End == ib.Max[0] && d.Z.Single(b => b.Source).Start == ib.Min[2] && d.Z.Single(b => b.Source).End == ib.Max[2], "source band edges equal inlet bounds");
        Check(Math.Abs(sx * sz - (ib.Max[0] - ib.Min[0]) * (ib.Max[2] - ib.Min[2])) < 1e-9 && s["SOURCE_FACES"].Split('\n').Length == 1, "airSource area equals inlet area, one block face");
        Check(Math.Abs(d.Min[1]) < 1e-12 && ib.Min[1] == 0, "inlet plane lies on the domain y minimum");

        // Probes and locationInMesh.
        Preflight.ValidateProbes(d, c.ProbeList());
        Check(true, "configured probes lie inside the domain");
        Check(Throws(() => Preflight.ValidateProbes(d, new[] { new Probe { name = "farMic", point = new[] { 0, .15, .05 } } })), "probe beyond the cropped Y bound is rejected");
        Check(!c.probes.ContainsKey("farMic"), "example config no longer contains farMic");
        double cs = d.Cell;
        bool clear = BackgroundMesh.FaceClearance(d.X, d.Location[0]) > 1e-3 * cs && BackgroundMesh.FaceClearanceY(d) > 1e-3 * cs && BackgroundMesh.FaceClearance(d.Z, d.Location[2]) > 1e-3 * cs;
        Check(clear && d.Location[2] > sb.Max[2] + d.Cell, "locationInMesh is off every cell face and above the body");

        // Whole-geometry connectivity through voicing/chamber/throat on the real STLs.
        double[] centre = Enumerable.Range(0, 3).Select(i => (sb.Min[i] + sb.Max[i]) * .5).ToArray();
        double r = Math.Sqrt(Enumerable.Range(0, 3).Sum(i => Math.Pow((sb.Max[i] - sb.Min[i]) * .5, 2))) + c.physics.damping.clearanceMillimeters;
        PreflightResult pre = Preflight.Run(solid, inlet, d, c, c.ProbeList(), centre, r, r + c.physics.damping.thicknessMillimeters);
        Console.WriteLine(pre.Report);
        Check(!pre.Report.Contains("WARNING"), "all fluid, including chamber and throat, connects to locationInMesh");

        // Points: frame names, field order, interleaved parsing.
        Check(Points.FrameName("0.0001") == "000s000ms100us000ns", "frame name for 100 microseconds");
        Check(Points.FrameName("1.234567891") == "001s234ms567us891ns", "frame name is exact to the nanosecond");
        Check(Points.FrameName("0.00001") == "000s000ms010us000ns", "frame name for 10 microseconds");
        Check(string.Join(",", Points.Selected(new PointsConfig { velocity = true, temperature = true, pressure = true, density = false }).Select(f => f.Name)) == "velocity,temperature,pressure", "fields follow the fixed order");
        Check(Throws(() => Points.ParseCompression("zip")), "unknown compression is rejected");
        {
            string tmp = Path.Combine(Path.GetTempPath(), "ocarina-points-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                string header = "FoamFile\n{\n version 2.0;\n format ascii;\n class volVectorField;\n object U;\n}\n";
                File.WriteAllText(Path.Combine(tmp, "U"), header + "internalField nonuniform List<vector>\n2\n(\n(1 2 3)\n(4 5 6)\n)\n;\nboundaryField\n{\n a { type zeroGradient; }\n}\n");
                File.WriteAllText(Path.Combine(tmp, "T"), header + "internalField uniform 300;\nboundaryField\n{\n a { type zeroGradient; }\n}\n");
                float[] frame = new float[8];
                Points.ReadField(Path.Combine(tmp, "U"), true, frame, 0, 2, 4, 0);
                Points.ReadField(Path.Combine(tmp, "T"), false, frame, 0, 2, 4, 3);
                Check(frame.SequenceEqual(new float[] { 1, 2, 3, 300, 4, 5, 6, 300 }), "vector keeps all components and fields interleave per point");
                // One hexahedral cell spanning 0..2: position, frame contents, three-newer holdback.
                string root = Path.Combine(tmp, "case"), foam = Path.Combine(root, "foam"), mesh = Path.Combine(foam, "constant", "polyMesh");
                Directory.CreateDirectory(mesh);
                File.WriteAllText(Path.Combine(mesh, "points"), header + "8\n(\n(0 0 0)\n(2 0 0)\n(2 2 0)\n(0 2 0)\n(0 0 2)\n(2 0 2)\n(2 2 2)\n(0 2 2)\n)\n");
                File.WriteAllText(Path.Combine(mesh, "faces"), header + "6\n(\n4(0 3 2 1)\n4(4 5 6 7)\n4(0 1 5 4)\n4(1 2 6 5)\n4(2 3 7 6)\n4(3 0 4 7)\n)\n");
                File.WriteAllText(Path.Combine(mesh, "owner"), header + "6\n(\n0\n0\n0\n0\n0\n0\n)\n");
                File.WriteAllText(Path.Combine(mesh, "neighbour"), header + "0\n(\n)\n");
                Points.Quality = Points.ParseCompression("fastest");
                Check(Points.WritePositions(root, foam, 1) == 1, "build writes one position per cell");
                float[] position = Points.ReadFloats(Points.PositionFile(root), 3);
                Check(position.All(v => Math.Abs(v - 1f) < 1e-6f), "cell centre is the volume centroid");
                Points.OutputDirectory = Points.PointsDirectory(root); Points.CaseDirectory = foam; Points.ProcessorCount = 1;
                Points.AmbientPressure = 101000;
                Points.Fields = Points.Selected(new PointsConfig { velocity = true, temperature = false, pressure = true, density = false });
                foreach (string t in new[] { "0.0001", "0.0002", "0.0003", "0.0004", "0.0005" })
                {
                    Directory.CreateDirectory(Path.Combine(foam, t));
                    File.WriteAllText(Path.Combine(foam, t, "U"), header + "internalField uniform (" + t + " 0 -1);\nboundaryField\n{\n a { type zeroGradient; }\n}\n");
                    File.WriteAllText(Path.Combine(foam, t, "p"), header + "internalField uniform 101325;\nboundaryField\n{\n a { type zeroGradient; }\n}\n");
                }
                string[] procs = Points.FindProcessors();
                string[] ready = Points.ReadyTimes(procs, new HashSet<string>());
                Check(ready.SequenceEqual(new[] { "0.0001", "0.0002" }), "only times with three newer ones are ready");
                Points.Process(procs, Points.Offsets(procs), ready[0]);
                Check(!Directory.Exists(Path.Combine(foam, "0.0001")) && Directory.Exists(Path.Combine(foam, "0.0002")), "processed time is deleted, others kept");
                float[] one = Points.ReadFloats(Path.Combine(Points.OutputDirectory, "000s000ms100us000ns.bin.br"), 4);
                Check(one.SequenceEqual(new float[] { 0.0001f, 0f, -1f, 325f }), "frame holds vX vY vZ then gauge pressure (p minus ambient)");
                Check(Points.FrameCount(Points.OutputDirectory) == 1, "frame count ignores position.bin.br");
                var rng = new Random(7);
                float[] noisy = Enumerable.Range(0, 3000).Select(_ => (float)(rng.NextDouble() * 200 - 100)).ToArray();
                string br = Path.Combine(tmp, "x.bin.br");
                Points.WriteFloats(br, noisy, 6, 23);
                Check(Points.ReadFloats(br, 6).SequenceEqual(noisy), "frames round-trip bit-exactly at 23 mantissa bits, order preserved");
                long fullSize = new FileInfo(br).Length;
                Points.WriteFloats(br, noisy, 6, 10);
                float[] coarse = Points.ReadFloats(br, 6);
                Check(new FileInfo(br).Length < fullSize && coarse.Zip(noisy, (a, b) => Math.Abs(a - b) <= Math.Abs(b) * 0.001f).All(x => x), "reduced mantissa bits shrink the file within the stated precision");
            }
            finally { Directory.Delete(tmp, true); }
        }

        Console.WriteLine(failures == 0 ? "All checks passed." : failures + " check(s) failed.");
        return failures == 0 ? 0 : 1;
    }
}
