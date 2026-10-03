public class Domain
{
    public double[] Min, Max, Location;
    public List<Band> X, Z;
    public int Ny;
    public double Cell;
    public long BackgroundCells => (long)X.Sum(b => b.Cells) * Ny * Z.Sum(b => b.Cells);
}

public static class BackgroundMesh
{
    // Splits [a,b] into a nominal-cell main band plus at most one narrower remainder band.
    // The remainder sits next to the inlet; a remainder under a quarter cell is merged into
    // the main band, and a lone sliver that thin is rejected rather than meshed.
    static void Fill(List<Band> result, double a, double b, double cell, bool remainderAtEnd)
    {
        double span = b - a;
        if (span <= 1e-9) return;
        int n = (int)Math.Floor(span / cell + 1e-9);
        double rem = span - n * cell;
        if (rem < 1e-9 || (n >= 1 && rem < cell / 4)) { result.Add(new Band { Start = a, End = b, Cells = Math.Max(1, n) }); return; }
        if (n == 0 && rem < cell / 4) throw new Exception($"World span {span:G6} mm between the inlet and the domain edge is thinner than a quarter cell ({cell / 4:G6} mm).");
        var main = n >= 1 ? new Band { Start = remainderAtEnd ? a : a + rem, End = remainderAtEnd ? b - rem : b, Cells = n } : null;
        var remainder = new Band { Start = remainderAtEnd ? b - rem : a, End = remainderAtEnd ? b : a + rem, Cells = 1 };
        if (remainderAtEnd) { if (main != null) result.Add(main); result.Add(remainder); }
        else { result.Add(remainder); if (main != null) result.Add(main); }
    }
    public static List<Band> Bands(double min, double max, double sourceMin, double sourceMax, double cell)
    {
        if (sourceMin < min - 1e-9 || sourceMax > max + 1e-9 || sourceMin >= sourceMax) throw new Exception("Inlet outside world.");
        var result = new List<Band>();
        // Outer band first on the low side (remainder next to the inlet), inner remainder first on the high side.
        Fill(result, min, sourceMin, cell, true);
        result.Add(new Band { Start = sourceMin, End = sourceMax, Cells = Math.Max(1, (int)Math.Ceiling((sourceMax - sourceMin) / cell - 1e-9)), Source = true });
        Fill(result, sourceMax, max, cell, false);
        return result;
    }
    // Zero padding keeps the solid's exact coordinate. Positive padding keeps the original
    // behaviour of rounding outwards to the background grid.
    public static double Edge(double solid, double padding, double cell, bool upper)
    {
        if (padding == 0) return solid;
        return upper ? Math.Ceiling((solid + padding) / cell - 1e-10) * cell : Math.Floor((solid - padding) / cell + 1e-10) * cell;
    }
    public static Domain Layout(Bounds solid, Bounds inlet, Config c)
    {
        double cell = c.backgroundCellSizeMillimeters;
        Padding p = c.worldPaddingMillimeters;
        double[] low = { p.xMin, p.yMin, p.zMin }, high = { p.xMax, p.yMax, p.zMax };
        double[] min = Enumerable.Range(0, 3).Select(a => Edge(solid.Min[a], low[a], cell, false)).ToArray();
        double[] max = Enumerable.Range(0, 3).Select(a => Edge(solid.Max[a], high[a], cell, true)).ToArray();
        if (Math.Abs(min[1]) > 1e-9 || Math.Abs(inlet.Min[1]) > 1e-9) throw new Exception("Solid/world minimum Y and inlet must lie on y=0.");
        var d = new Domain { Min = min, Max = max, Cell = cell };
        d.X = Bands(min[0], max[0], inlet.Min[0], inlet.Max[0], cell);
        d.Z = Bands(min[2], max[2], inlet.Min[2], inlet.Max[2], cell);
        d.Ny = (int)Math.Ceiling((max[1] - min[1]) / cell - 1e-9);
        if (d.Ny < 1) throw new Exception("Empty Y domain.");
        // Retained exterior air above the body hosts locationInMesh. The offsets are irrational
        // fractions of a cell so the point cannot sit on a face at any refinement level.
        double air = max[2] - solid.Max[2];
        if (air < 2 * cell) throw new Exception($"locationInMesh needs at least two background cells of air above the body (+Z); found {air:G6} mm. Set zMax padding of at least {2 * cell:G6} mm.");
        d.Location = new[] { (min[0] + max[0]) / 2 + cell * 0.31830988618379067, (min[1] + max[1]) / 2 + cell * 0.27182818284590452, solid.Max[2] + air / 2 + cell * 0.14142135623730951 };
        Validate(d);
        return d;
    }
    public static void Validate(Domain d)
    {
        void Axis(List<Band> bands, double min, double max, string name)
        {
            if (Math.Abs(bands[0].Start - min) > 1e-9 || Math.Abs(bands[^1].End - max) > 1e-9) throw new Exception(name + " bands do not span the domain.");
            for (int i = 0; i < bands.Count; i++)
            {
                if (bands[i].Cells < 1 || bands[i].End - bands[i].Start <= 1e-9) throw new Exception($"{name} band {i} is empty.");
                if (i > 0 && Math.Abs(bands[i].Start - bands[i - 1].End) > 1e-9) throw new Exception($"{name} bands {i - 1} and {i} are not contiguous.");
                if ((bands[i].End - bands[i].Start) / bands[i].Cells > d.Cell * 1.25 + 1e-9) throw new Exception($"{name} band {i} cells exceed 1.25 nominal cells.");
            }
        }
        Axis(d.X, d.Min[0], d.Max[0], "X"); Axis(d.Z, d.Min[2], d.Max[2], "Z");
        if (d.Ny < 1) throw new Exception("Empty Y domain.");
    }
    // Distance from v to the nearest cell face when the bands are refined by 2^level.
    public static double FaceClearance(List<Band> bands, double v, int level)
    {
        Band b = bands.First(x => v >= x.Start && v <= x.End);
        double w = (b.End - b.Start) / b.Cells / Math.Pow(2, level), t = (v - b.Start) / w;
        return Math.Abs(t - Math.Round(t)) * w;
    }
    public static double FaceClearanceY(Domain d, int level)
    {
        double w = (d.Max[1] - d.Min[1]) / d.Ny / Math.Pow(2, level), t = (d.Location[1] - d.Min[1]) / w;
        return Math.Abs(t - Math.Round(t)) * w;
    }
    public static Dictionary<string, string> Build(Bounds solid, Bounds inlet, Config c) => Build(Layout(solid, inlet, c));
    public static Dictionary<string, string> Build(Domain d)
    {
        var xb = d.X; var zb = d.Z; int ny = d.Ny;
        double[] x = xb.Select(b => b.Start * .001).Append(d.Max[0] * .001).ToArray();
        double[] y = { d.Min[1] * .001, d.Max[1] * .001 };
        double[] z = zb.Select(b => b.Start * .001).Append(d.Max[2] * .001).ToArray();
        int V(int xi, int yi, int zi) => xi + x.Length * (yi + 2 * zi);
        var vertices = new List<string>(); var blocks = new List<string>(); var source = new List<string>(); var atmosphere = new List<string>();
        for (int zi = 0; zi < z.Length; zi++) for (int yi = 0; yi < 2; yi++) for (int xi = 0; xi < x.Length; xi++) vertices.Add("    (" + Geometry.Point(new[] { x[xi], y[yi], z[zi] }) + ")");
        for (int zi = 0; zi < zb.Count; zi++) for (int xi = 0; xi < xb.Count; xi++)
        {
            int a = V(xi, 0, zi), b = V(xi + 1, 0, zi), dd = V(xi, 1, zi), e = V(xi, 0, zi + 1), f = V(xi + 1, 0, zi + 1), g = V(xi + 1, 1, zi + 1), h = V(xi, 1, zi + 1), cc = V(xi + 1, 1, zi);
            blocks.Add($"    hex ({a} {b} {cc} {dd} {e} {f} {g} {h}) ({xb[xi].Cells} {ny} {zb[zi].Cells}) simpleGrading (1 1 1)");
            (xb[xi].Source && zb[zi].Source ? source : atmosphere).Add($"({a} {b} {f} {e})");
            atmosphere.Add($"({dd} {h} {g} {cc})");
            if (xi == 0) atmosphere.Add($"({a} {e} {h} {dd})");
            if (xi == xb.Count - 1) atmosphere.Add($"({b} {cc} {g} {f})");
            if (zi == 0) atmosphere.Add($"({a} {dd} {cc} {b})");
            if (zi == zb.Count - 1) atmosphere.Add($"({e} {f} {g} {h})");
        }
        return new Dictionary<string, string> { ["VERTICES"] = string.Join('\n', vertices), ["BLOCKS"] = string.Join('\n', blocks), ["SOURCE_FACES"] = string.Join('\n', source), ["ATMOSPHERE_FACES"] = string.Join('\n', atmosphere), ["LOCATION_IN_MESH"] = Geometry.Point(d.Location.Select(v => v * .001)) };
    }
}
