using System.Text.Json;

public class RenderConfig
{
    public int renderThreads = 6;
    public int pollMilliseconds = 2000;
    public int pngCompressionLevel = 6;
    public int plotWidth = 1024;
    public int plotHeight = 1024;
    public int marginPixels = 20;
    public int labelFontPixels = 40;
    public string backgroundColor = "#050505";
    public string labelColor = "#FAFAFA";
    public int pointSizePixels = 3;
    public int axisTiltDegrees = 0;
    public double cameraPaddingFraction = 0.2;
    public bool renderPressure = true;
    public bool renderVelocityMagnitude = true;
    public bool renderDensity = true;
    public bool renderTemperature = true;
    public JsonElement[][] tiles;
}
