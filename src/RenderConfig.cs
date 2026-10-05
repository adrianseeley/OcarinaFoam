using System.Text.Json;

public class RenderConfig
{
    public int renderThreads = 6;
    public int pollMilliseconds = 2000;
    public int pngCompressionLevel = 6;
    public int plotWidth = 1920;
    public int plotHeight = 1080;
    public int marginPixels = 20;
    public int labelFontPixels = 32;
    public string backgroundColor = "#050505";
    public string labelColor = "#FAFAFA";
    public int pointSizePixels = 3;
    public double cameraPaddingFraction = 0.2;
    public double minimumAlpha = 0.1;
    public bool renderPressure = true;
    public bool renderVelocityMagnitude = true;
    public bool renderDensity = true;
    public bool renderTemperature = true;
    public JsonElement[] views;
}
