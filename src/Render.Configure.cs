using System.Numerics;
using System.Text.Json;
using SkiaSharp;

public static partial class Renderer
{
    public static void Configure(string root,Config c,bool initialize=true)
    {
        CaseDirectory=Paths.Foam(root);OutputDirectory=Path.Combine(root,"renders");ProcessorCount=c.processorCount;
        RenderConfig r=c.renderer;
        ThreadCount=r.renderThreads;WriteTimeStep=c.deltaTSeconds*c.fieldWriteIntervalTimeSteps;
        PollMilliseconds=r.pollMilliseconds;PngCompressionLevel=r.pngCompressionLevel;PlotWidth=r.plotWidth;PlotHeight=r.plotHeight;
        MarginPixels=r.marginPixels;LabelFontPixels=r.labelFontPixels;BackgroundColor=SKColor.Parse(r.backgroundColor);LabelColor=SKColor.Parse(r.labelColor);
        PointSizePixels=r.pointSizePixels;CameraPaddingFraction=(float)r.cameraPaddingFraction;MinimumAlpha=(float)r.minimumAlpha;
        Bounds bounds=JsonSerializer.Deserialize<Bounds>(File.ReadAllText(Path.Combine(CaseDirectory,"bounds.json")),Configuration.Json);
        ObjectMinimum=new Vector3((float)(bounds.Min[0]*.001),(float)(bounds.Min[1]*.001),(float)(bounds.Min[2]*.001));
        ObjectMaximum=new Vector3((float)(bounds.Max[0]*.001),(float)(bounds.Max[1]*.001),(float)(bounds.Max[2]*.001));
        Fields[0].Enabled=r.renderPressure;Fields[1].Enabled=r.renderVelocityMagnitude;Fields[2].Enabled=r.renderDensity;Fields[3].Enabled=r.renderTemperature;

        RenderTileDefinition[] parsed = ParseViews(r);
        double[] defaultTarget = new[] { (bounds.Min[0] + bounds.Max[0]) * .5, (bounds.Min[1] + bounds.Max[1]) * .5, (bounds.Min[2] + bounds.Max[2]) * .5 };
        Views = ResolveViewDefaults(parsed, defaultTarget);
        ViewCameras = new Camera[Views.Length];
        for (int i = 0; i < Views.Length; i++)
        {
            try { ViewCameras[i] = MakeCamera(Views[i], TileContentRect()); }
            catch (Exception error) { throw new Exception($"renderer.views[{i}] camera invalid: {error.Message}"); }
        }

        if(initialize)
        {
            Directory.CreateDirectory(OutputDirectory);
            Directory.CreateDirectory(Path.Combine(OutputDirectory,".done"));
        }
    }
}
