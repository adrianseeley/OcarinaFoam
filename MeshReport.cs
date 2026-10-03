using System.Text.RegularExpressions;

public static class MeshReport
{
    public static long Cells(string output)
    {
        Match m = Regex.Match(output, @"^\s*(?:cells|nCells):?\s+(\d+)", RegexOptions.Multiline);
        return m.Success ? long.Parse(m.Groups[1].Value) : -1;
    }
    // Reads constant/polyMesh/boundary; the three patches must all exist and be non-empty.
    public static Dictionary<string, (string Type, int Faces)> Patches(string boundaryFile)
    {
        var result = new Dictionary<string, (string, int)>();
        foreach (Match m in Regex.Matches(File.ReadAllText(boundaryFile), @"(\w+)\s*\{[^{}]*?type\s+(\w+);[^{}]*?nFaces\s+(\d+);", RegexOptions.Singleline))
            result[m.Groups[1].Value] = (m.Groups[2].Value, int.Parse(m.Groups[3].Value));
        return result;
    }
    public static string Check(string stage, string checkMeshOutput)
    {
        var patches = Patches(Path.Combine(stage, "constant", "polyMesh", "boundary"));
        foreach (string name in new[] { "airSource", "atmosphere", "solidWalls" })
            if (!patches.TryGetValue(name, out var p) || p.Faces == 0) throw new Exception($"Boundary patch {name} is missing or empty in the final mesh.");
        if (patches["solidWalls"].Type != "wall") throw new Exception("solidWalls is not a wall patch.");
        Match regions = Regex.Match(checkMeshOutput, @"Number of regions:\s+(\d+)");
        if (!regions.Success || regions.Groups[1].Value != "1") throw new Exception("Final mesh is not a single connected region (checkMesh 'Number of regions' != 1).");
        return $"Final mesh cells {Cells(checkMeshOutput)}; patch faces: " + string.Join(", ", patches.Select(p => $"{p.Key} {p.Value.Faces} ({p.Value.Type})"));
    }
}
