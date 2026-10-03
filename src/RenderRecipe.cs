public class RenderRecipe
{
    public int renderThreads;
    public int pollMilliseconds;
    public int pngCompressionLevel;
    public int plotWidth;
    public int plotHeight;
    public int marginPixels;
    public int labelFontPixels;
    public string backgroundColor = "";
    public string labelColor = "";
    public int pointSizePixels;
    public int axisTiltDegrees;
    public double cameraPaddingFraction;
    public bool renderPressure;
    public bool renderVelocityMagnitude;
    public bool renderDensity;
    public bool renderTemperature;
    public RenderTileDefinition[][] tiles = Array.Empty<RenderTileDefinition[]>();
}
