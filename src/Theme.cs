using SkiaSharp;

// Shared colours: dark neutral chrome, blue-to-red hue ramp for intensities, fixed flat colours per probe.
// The hue ramp is qualitative and magnitude-only; read the numeric legend for values.
public static class Theme
{
    public const string BackgroundHex = "#050505", InkHex = "#FAFAFA", SecondaryHex = "#969696", GridHex = "#303030", StrongGridHex = "#828282";
    public const float Stroke = 1;
    public static readonly SKColor Background = SKColor.Parse(BackgroundHex), Ink = SKColor.Parse(InkHex), Secondary = SKColor.Parse(SecondaryHex),
        Grid = SKColor.Parse(GridHex), StrongGrid = SKColor.Parse(StrongGridHex);

    // One flat, fully opaque colour per probe (by position in config.probes), identical on every plot and legend.
    public static readonly SKColor[] ProbeColors = { SKColor.Parse("#B39AF5"), SKColor.Parse("#79D7E8"), SKColor.Parse("#F291BE"), SKColor.Parse("#E8C878"), SKColor.Parse("#8DD3A8") };
    public static SKColor Probe(int index) => ProbeColors[((index % ProbeColors.Length) + ProbeColors.Length) % ProbeColors.Length];

    // Normalised intensity 0..1 (clamped): blue (0) to red (1).
    public static SKColor Map(double t) => SKColor.FromHsv(240f * (1f - (float)(double.IsNaN(t) ? 0 : Math.Clamp(t, 0, 1))), 100, 100, 255);

    public static SKColor[] Table(int size = 4096) => Enumerable.Range(0, size).Select(i => Map((double)i / (size - 1))).ToArray();
}
