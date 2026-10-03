using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public static class Geometry
{
    public const double Millimetres = 0.001;
    public static string Number(double x) => x.ToString("G17", CultureInfo.InvariantCulture);
    public static string Point(IEnumerable<double> values) => string.Join(" ", values.Select(Number));
    public static Facet[] Read(string path)
    {
        // Strict ASCII STL grammar; binary input is intentionally unsupported.
        string text = File.ReadAllText(path);
        var lines = text.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        if (lines.Length < 2 || !lines[0].StartsWith("solid") || !lines[^1].StartsWith("endsolid")) throw new Exception("Expected ASCII STL: " + path);
        var facets = new List<Facet>(); int i = 1;
        while (i < lines.Length - 1)
        {
            if (!lines[i++].StartsWith("facet normal ") || lines[i++] != "outer loop") throw new Exception("Malformed STL facet: " + path);
            var vertices = new double[3][];
            for (int v = 0; v < 3; v++)
            {
                string[] words = Regex.Split(lines[i++], @"\s+");
                if (words.Length != 4 || words[0] != "vertex") throw new Exception("Malformed STL vertex: " + path);
                vertices[v] = words.Skip(1).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                if (vertices[v].Any(x => !double.IsFinite(x))) throw new Exception("Non-finite STL coordinate.");
            }
            if (lines[i++] != "endloop" || lines[i++] != "endfacet") throw new Exception("Malformed STL ending: " + path);
            facets.Add(new Facet { Vertices = vertices });
        }
        if (facets.Count == 0) throw new Exception("Empty STL: " + path);
        return facets.ToArray();
    }
    public static Bounds Bound(Facet[] facets)
    {
        var points = facets.SelectMany(x => x.Vertices).ToArray();
        return new Bounds { Min = Enumerable.Range(0, 3).Select(a => points.Min(v => v[a])).ToArray(), Max = Enumerable.Range(0, 3).Select(a => points.Max(v => v[a])).ToArray() };
    }
    public static Facet[] Inlet(Facet[] facets)
    {
        // CAD may export a thin box below y=0. Retain only its planar outlet face.
        Facet[] face = facets.Where(f => f.Vertices.All(v => Math.Abs(v[1]) < 1e-8)).ToArray();
        if (face.Length == 0) throw new Exception("spawnPlane.stl needs a rectangular face on y=0.");
        Bounds b = Bound(face);
        double expected = (b.Max[0] - b.Min[0]) * (b.Max[2] - b.Min[2]);
        double area = face.Sum(f => Math.Abs((f.Vertices[1][0]-f.Vertices[0][0])*(f.Vertices[2][2]-f.Vertices[0][2])-(f.Vertices[2][0]-f.Vertices[0][0])*(f.Vertices[1][2]-f.Vertices[0][2])) * .5);
        if (expected <= 0 || Math.Abs(area - expected) > expected * 1e-8) throw new Exception("Inlet face must cover its X/Z bounding rectangle exactly.");
        return face;
    }
    public static string Stl(string name, Facet[] facets)
    {
        var text = new StringBuilder("solid " + name + "\n");
        foreach (Facet f in facets)
        {
            var a=f.Vertices[0]; var b=f.Vertices[1]; var c=f.Vertices[2];
            double[] u=Enumerable.Range(0,3).Select(i=>b[i]-a[i]).ToArray(), v=Enumerable.Range(0,3).Select(i=>c[i]-a[i]).ToArray();
            double[] n={u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]};
            double length=Math.Sqrt(n.Sum(x=>x*x));
            if (length == 0) throw new Exception("Degenerate STL triangle.");
            text.AppendLine("facet normal " + Point(n.Select(x=>x/length))).AppendLine("outer loop");
            foreach (double[] vertex in f.Vertices) text.AppendLine("vertex " + Point(vertex.Select(x => x * Millimetres)));
            text.AppendLine("endloop\nendfacet");
        }
        return text.AppendLine("endsolid " + name).ToString();
    }
}
