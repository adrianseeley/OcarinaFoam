// Pure-C# checks of the background domain and inlet/probe handling. No OpenFOAM required.
public static class SelfTest
{
    static int failures;
    static void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; }
    static bool Throws(Action a) { try { a(); return false; } catch (Exception) { return true; } }
    static Config Cfg(Padding p) => new Config { worldPaddingMillimeters = p, backgroundCellSizeMillimeters = 5 };
    static Bounds B(double[] min, double[] max) => new Bounds { Min = min, Max = max };
    static double Span(List<Band> bands) => bands.Sum(b => b.End - b.Start);

    public static int Run()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "ocarinaZero");
        Config c = Configuration.Load(Path.Combine(dir, "config.json"));
        Facet[] solid = Geometry.Read(Path.Combine(dir, "solidBody.stl"));
        Facet[] inlet = Geometry.Inlet(Geometry.Read(Path.Combine(dir, "spawnPlane.stl")));
        Bounds sb = Geometry.Bound(solid), ib = Geometry.Bound(inlet);

        Padding p = c.worldPaddingMillimeters;
        Check(p.xMin == 0 && p.xMax == 0 && p.yMin == 0 && p.yMax == 0 && p.zMin == 0 && p.zMax == 100, "example config: zero padding except zMax 100");
        Domain d = BackgroundMesh.Layout(sb, ib, c);
        Check(d.Min[0] == sb.Min[0] && d.Max[0] == sb.Max[0] && d.Min[1] == sb.Min[1] && d.Max[1] == sb.Max[1] && d.Min[2] == sb.Min[2], "zero-padded sides equal the solid's exact bounds");
        Check(d.Max[2] >= sb.Max[2] + 100 && d.Max[2] < sb.Max[2] + 100 + c.backgroundCellSizeMillimeters, "padded +Z side keeps outward grid rounding");
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
        Preflight.ValidateProbes(d, c.probes);
        Check(true, "configured probes lie inside the domain");
        Check(Throws(() => Preflight.ValidateProbes(d, new[] { new Probe { name = "farMic", point = new[] { 0, .15, .05 } } })), "probe beyond the cropped Y bound is rejected");
        Check(!c.probes.Any(x => x.name == "farMic"), "example config no longer contains farMic");
        int max = Math.Max(c.surfaceRefinementMaxLevel, c.featureRefinementLevel);
        bool clear = true;
        for (int level = 0; level <= max; level++)
        {
            double cs = d.Cell / Math.Pow(2, level);
            clear &= BackgroundMesh.FaceClearance(d.X, d.Location[0], level) > 1e-3 * cs && BackgroundMesh.FaceClearanceY(d, level) > 1e-3 * cs && BackgroundMesh.FaceClearance(d.Z, d.Location[2], level) > 1e-3 * cs;
        }
        Check(clear && d.Location[2] > sb.Max[2] + d.Cell, "locationInMesh is off every cell face and above the body");

        // Whole-geometry connectivity through voicing/chamber/throat on the real STLs.
        double[] centre = Enumerable.Range(0, 3).Select(i => (sb.Min[i] + sb.Max[i]) * .5).ToArray();
        double r = Math.Sqrt(Enumerable.Range(0, 3).Sum(i => Math.Pow((sb.Max[i] - sb.Min[i]) * .5, 2))) + c.acousticDampingClearanceMillimeters;
        PreflightResult pre = Preflight.Run(solid, inlet, d, c, c.probes, centre, r, r + c.acousticDampingThicknessMillimeters);
        Console.WriteLine(pre.Report);
        Check(!pre.Report.Contains("WARNING"), "all fluid, including chamber and throat, connects to locationInMesh");
        Console.WriteLine(failures == 0 ? "All checks passed." : failures + " check(s) failed.");
        return failures == 0 ? 0 : 1;
    }
}

