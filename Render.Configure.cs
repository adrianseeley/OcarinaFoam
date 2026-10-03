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
        PointSizePixels=r.pointSizePixels;AxisTiltDegrees=(float)r.axisTiltDegrees;CameraPaddingFraction=(float)r.cameraPaddingFraction;
        Bounds bounds=JsonSerializer.Deserialize<Bounds>(File.ReadAllText(Path.Combine(CaseDirectory,"bounds.json")),Configuration.Json);
        ObjectMinimum=new Vector3((float)(bounds.Min[0]*.001),(float)(bounds.Min[1]*.001),(float)(bounds.Min[2]*.001));
        ObjectMaximum=new Vector3((float)(bounds.Max[0]*.001),(float)(bounds.Max[1]*.001),(float)(bounds.Max[2]*.001));
        Fields[0].Enabled=r.renderPressure;Fields[1].Enabled=r.renderVelocityMagnitude;Fields[2].Enabled=r.renderDensity;Fields[3].Enabled=r.renderTemperature;
        foreach(View view in Views)view.SizePixels=PointSizePixels;
        if(initialize)
        {
            Directory.CreateDirectory(OutputDirectory);Directory.CreateDirectory(Path.Combine(OutputDirectory,".done"));
            string signature=JsonSerializer.Serialize(r,Configuration.Json);
            string stamp=Path.Combine(OutputDirectory,"config.json");
            if(File.Exists(stamp)&&File.ReadAllText(stamp)!=signature)
            {
                var previous=JsonSerializer.Deserialize<RenderConfig>(File.ReadAllText(stamp),Configuration.Json);
                // Scheduling controls can change without making existing images inconsistent.
                previous.renderThreads=r.renderThreads;previous.pollMilliseconds=r.pollMilliseconds;
                if(JsonSerializer.Serialize(previous,Configuration.Json)!=signature)
                    throw new Exception("Render appearance/fields changed after rendering began. Raw data is consumed; use a fresh case for a different render recipe.");
            }
            Paths.Atomic(stamp,signature);
        }
    }
}
