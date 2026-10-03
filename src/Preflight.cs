using System.Text;

// Approximate voxel check of the solid against the background domain, run before OpenFOAM.
// It supplements, and never replaces, surfaceCheck and checkMesh on the real mesh.
public class PreflightResult
{
    public string Report;
    public double FluidMaxRadiusMm, DomainMaxRadiusMm, RampPresentFraction, RampVolumeFraction;
}

public static class Preflight
{
    public static void ValidateProbes(Domain d, Probe[] probes)
    {
        foreach (Probe p in probes)
        {
            double[] mm = p.point.Select(v => v * 1000).ToArray();
            for (int a = 0; a < 3; a++)
                if (mm[a] <= d.Min[a] + 1e-9 || mm[a] >= d.Max[a] - 1e-9)
                    throw new Exception($"Probe '{p.name}' ({Geometry.Point(p.point)} m) lies outside the domain on axis {"XYZ"[a]}: {mm[a]:G6} mm not within ({d.Min[a]:G6}, {d.Max[a]:G6}) mm. Remove it or choose a documented replacement; probes are never relocated automatically.");
        }
    }
    public static PreflightResult Run(Facet[] solid, Facet[] inlet, Domain d, Config c, Probe[] probes, double[] origin, double r1Mm, double r2Mm)
    {
        ValidateProbes(d, probes);
        double h = d.Cell / 4;
        int[] n = Enumerable.Range(0, 3).Select(a => (int)Math.Ceiling((d.Max[a] - d.Min[a]) / h - 1e-9)).ToArray();
        double[] w = Enumerable.Range(0, 3).Select(a => (d.Max[a] - d.Min[a]) / n[a]).ToArray();
        long total = (long)n[0] * n[1] * n[2];
        if (total > 400_000_000) throw new Exception("Preflight grid too large.");
        var inside = new bool[total];
        long Idx(int i, int j, int k) => i + (long)n[0] * (j + (long)n[1] * k);
        var tris = solid.Select(f => f.Vertices).ToArray();
        var xs = new List<double>();
        for (int k = 0; k < n[2]; k++) for (int j = 0; j < n[1]; j++)
        {
            double y = d.Min[1] + (j + .5) * w[1] + 3.1e-7, z = d.Min[2] + (k + .5) * w[2] + 1.7e-7;
            xs.Clear();
            foreach (var t in tris) { double? x = Cross(t, y, z); if (x.HasValue) xs.Add(x.Value); }
            xs.Sort();
            for (int i = 0; i < n[0]; i++)
            {
                double x = d.Min[0] + (i + .5) * w[0];
                int count = 0; foreach (double cx in xs) { if (cx < x) count++; else break; }
                inside[Idx(i, j, k)] = (count & 1) == 1;
            }
        }
        int[] Cell(double[] mm) => Enumerable.Range(0, 3).Select(a => Math.Clamp((int)Math.Floor((mm[a] - d.Min[a]) / w[a]), 0, n[a] - 1)).ToArray();
        int[] s = Cell(d.Location);
        if (inside[Idx(s[0], s[1], s[2])]) throw new Exception("locationInMesh lies inside the solid.");
        var reach = new bool[total]; var queue = new Queue<long>();
        reach[Idx(s[0], s[1], s[2])] = true; queue.Enqueue(Idx(s[0], s[1], s[2]));
        int[][] dirs = { new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
        while (queue.Count > 0)
        {
            long q = queue.Dequeue(); int i = (int)(q % n[0]), j = (int)(q / n[0] % n[1]), k = (int)(q / n[0] / n[1]);
            foreach (var dir in dirs)
            {
                int a = i + dir[0], b = j + dir[1], e = k + dir[2];
                if (a < 0 || b < 0 || e < 0 || a >= n[0] || b >= n[1] || e >= n[2]) continue;
                long m = Idx(a, b, e);
                if (inside[m] || reach[m]) continue;
                reach[m] = true; queue.Enqueue(m);
            }
        }
        long fluid = 0, reached = 0, ramp = 0; double fluidMax = 0;
        for (long q = 0; q < total; q++)
        {
            if (inside[q]) continue;
            fluid++; if (!reach[q]) continue;
            reached++;
            int i = (int)(q % n[0]), j = (int)(q / n[0] % n[1]), k = (int)(q / n[0] / n[1]);
            double r = Math.Sqrt(Math.Pow(d.Min[0] + (i + .5) * w[0] - origin[0], 2) + Math.Pow(d.Min[1] + (j + .5) * w[1] - origin[1], 2) + Math.Pow(d.Min[2] + (k + .5) * w[2] - origin[2], 2));
            fluidMax = Math.Max(fluidMax, r); if (r > r1Mm) ramp++;
        }
        var text = new StringBuilder();
        text.AppendLine($"Preflight voxel grid {n[0]}x{n[1]}x{n[2]} at {w[0]:G4} x {w[1]:G4} x {w[2]:G4} mm (approximate; real checks are surfaceCheck/checkMesh).");
        text.AppendLine($"Domain mm: X [{d.Min[0]:G6}, {d.Max[0]:G6}] Y [{d.Min[1]:G6}, {d.Max[1]:G6}] Z [{d.Min[2]:G6}, {d.Max[2]:G6}]");
        text.AppendLine($"Background cells {d.X.Sum(b => b.Cells)} x {d.Ny} x {d.Z.Sum(b => b.Cells)} = {d.BackgroundCells}");
        text.AppendLine($"locationInMesh mm: {Geometry.Point(d.Location.Select(v => Math.Round(v, 6)))}; it is in fluid and connected to {reached} of {fluid} fluid voxels ({100.0 * reached / Math.Max(1, fluid):F2}%).");
        if (reached != fluid) text.AppendLine($"WARNING: {fluid - reached} fluid voxels are disconnected from locationInMesh and would be discarded (sealed voids).");
        // Inlet: every voxel of the first Y layer inside the inlet rectangle must be connected fluid.
        Bounds ib = Geometry.Bound(inlet); int inletVoxels = 0, inletBad = 0;
        for (int k = 0; k < n[2]; k++) for (int i = 0; i < n[0]; i++)
        {
            double x = d.Min[0] + (i + .5) * w[0], z = d.Min[2] + (k + .5) * w[2];
            if (x < ib.Min[0] || x > ib.Max[0] || z < ib.Min[2] || z > ib.Max[2]) continue;
            inletVoxels++; if (!reach[Idx(i, 0, k)]) inletBad++;
        }
        if (inletVoxels == 0 || inletBad > 0) throw new Exception($"Inlet is not fully open to the retained fluid ({inletBad} of {inletVoxels} voxels blocked).");
        text.AppendLine($"Inlet {ib.Max[0] - ib.Min[0]:G6} x {ib.Max[2] - ib.Min[2]:G6} mm: all {inletVoxels} first-layer voxels are connected fluid.");
        foreach (Probe p in probes)
        {
            double[] mm = p.point.Select(v => v * 1000).ToArray(); int[] pc = Cell(mm); long q = Idx(pc[0], pc[1], pc[2]);
            if (inside[q]) throw new Exception($"Probe '{p.name}' lies inside the solid.");
            if (!reach[q]) throw new Exception($"Probe '{p.name}' is in fluid disconnected from locationInMesh.");
            text.AppendLine($"Probe {p.name} is inside the domain and in connected fluid.");
        }
        // Reachable fluid exposed on each outer side (approximate atmosphere/inlet area, mm^2).
        string[] side = { "X-", "X+", "Y-", "Y+", "Z-", "Z+" };
        for (int a = 0; a < 3; a++)
        {
            int u = (a + 1) % 3, v = (a + 2) % 3;
            foreach (int hi in new[] { 0, 1 })
            {
                double area = 0; var c3 = new int[3];
                for (int p = 0; p < n[u]; p++) for (int q = 0; q < n[v]; q++)
                { c3[a] = hi == 0 ? 0 : n[a] - 1; c3[u] = p; c3[v] = q; if (reach[Idx(c3[0], c3[1], c3[2])]) area += w[u] * w[v]; }
                text.AppendLine($"Exposed connected fluid on {side[2 * a + hi]}: {area:F0} mm^2");
            }
        }
        double[] corner = new double[3]; double domainMax = 0;
        foreach (int cx in new[] { 0, 1 }) foreach (int cy in new[] { 0, 1 }) foreach (int cz in new[] { 0, 1 })
        {
            double[] pt = { cx == 0 ? d.Min[0] : d.Max[0], cy == 0 ? d.Min[1] : d.Max[1], cz == 0 ? d.Min[2] : d.Max[2] };
            domainMax = Math.Max(domainMax, Math.Sqrt(Enumerable.Range(0, 3).Sum(a => Math.Pow(pt[a] - origin[a], 2))));
        }
        double present = Math.Clamp((fluidMax - r1Mm) / (r2Mm - r1Mm), 0, 1);
        double volume = reached == 0 ? 0 : (double)ramp / reached;
        text.AppendLine($"Damping sphere: radius1 {r1Mm:F2} mm, radius2 {r2Mm:F2} mm (configured ramp {r2Mm - r1Mm:F2} mm).");
        text.AppendLine($"Farthest retained fluid {fluidMax:F2} mm from origin (farthest domain corner {domainMax:F2} mm): {100 * present:F1}% of the ramp thickness exists in fluid ({fluidMax - r1Mm:F2} of {r2Mm - r1Mm:F2} mm); {100 * volume:F1}% of connected fluid volume lies in the ramp. Coefficients are unchanged.");
        return new PreflightResult { Report = text.ToString(), FluidMaxRadiusMm = fluidMax, DomainMaxRadiusMm = domainMax, RampPresentFraction = present, RampVolumeFraction = volume };
    }
    // x of the +X ray from (.,y,z) with the triangle's plane, or null on a miss or a parallel plane.
    static double? Cross(double[][] t, double y, double z)
    {
        double y0 = t[0][1], z0 = t[0][2], y1 = t[1][1] - y0, z1 = t[1][2] - z0, y2 = t[2][1] - y0, z2 = t[2][2] - z0;
        double det = y1 * z2 - y2 * z1; if (Math.Abs(det) < 1e-12) return null;
        double py = y - y0, pz = z - z0;
        double u = (py * z2 - y2 * pz) / det, v = (y1 * pz - py * z1) / det;
        if (u < 0 || v < 0 || u + v > 1) return null;
        return t[0][0] + u * (t[1][0] - t[0][0]) + v * (t[2][0] - t[0][0]);
    }
}
