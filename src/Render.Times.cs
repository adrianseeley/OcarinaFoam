using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Runtime;
using SkiaSharp;

public static partial class Renderer
{
    // Return finite positive numeric directory names sorted by numeric time.
    // Leave 0, constant, system and non-time directories outside the rendering lifecycle.
    public static string[] Times(string processor, out double[] numbers)
    {
        string[] directories = Directory.GetDirectories(processor);
        List<string> names = new List<string>();
        List<double> values = new List<double>();
        for (int i = 0; i < directories.Length; i++)
        {
            string name = Path.GetFileName(directories[i]);
            double value;
            if (double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && value > 0)
            {
                names.Add(name);
                values.Add(value);
            }
        }
        string[] result = names.ToArray();
        numbers = values.ToArray();
        Array.Sort(numbers, result);
        return result;
    }
}
