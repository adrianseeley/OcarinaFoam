using System.Text.Json;
using System.Text.RegularExpressions;

public static class BuildCase
{
    public static void Run(string root)
    {
        using var buildLock=Paths.Lock(root,"build");
        // systemd can report active before a just-execed worker acquires its lock.
        // Check the manager as well as worker locks to close that startup race.
        foreach(string kind in new[]{"simulate","render"})
        {
            var state=Services.Control("show",Paths.Unit(root,kind),"--property=ActiveState","--value");
            if(state.ExitCode==0 && state.Output.Trim() is "active" or "activating" or "deactivating")
                throw new Exception("Stop "+kind+" before building this case.");
        }
        using var simLock=Paths.Lock(root,"simulate");
        using var renderLock=Paths.Lock(root,"render");
        Config c=Configuration.Load(Path.Combine(root,"config.json"));
        string foam=Paths.Foam(root);
        // Rebuild only generated mesh-only output; never destroy a simulation or its images.
        if(Directory.Exists(foam))
        {
            bool results=Directory.EnumerateDirectories(foam,"processor*").Any(p=>Renderer.Times(p,out _).Length>0)
                || Renderer.Times(foam,out _).Length>0 || Directory.Exists(Path.Combine(foam,"postProcessing"))
                || (Directory.Exists(Path.Combine(root,"renders")) && Directory.EnumerateFiles(Path.Combine(root,"renders"),"*",SearchOption.AllDirectories).Any());
            if(results)throw new Exception("This case contains results. Build a new case directory; existing results are never erased by build.");
            if(!File.Exists(Path.Combine(foam,".ocarina"))) throw new Exception("Refusing to replace an unowned foam directory.");
        }
        string runLogs=Directory.CreateDirectory(Path.Combine(Paths.Logs(root),"build-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N")[..6])).FullName;
        string stage=Path.Combine(root,".foam-build-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            Generate(root,stage,c);
            int step=0;
            CommandResult Run(string tool,params string[] args)=>Commands.Foam(stage,Path.Combine(runLogs,$"{++step:D2}-{tool}.log"),tool,args);
            Run("foamVersion");
            CommandResult surface=Run("surfaceCheck","constant/triSurface/solidBody.stl");
            if(!surface.Output.Contains("Surface is closed",StringComparison.OrdinalIgnoreCase))throw new Exception("solidBody must be a closed surface. See surfaceCheck log.");
            Run("surfaceCheck","constant/triSurface/spawnPlane.stl");
            Run("surfaceFeatureExtract");
            CommandResult block=Run("blockMesh");
            Run("snappyHexMesh","-overwrite");
            CommandResult mesh=Run("checkMesh","-allTopology","-allGeometry");
            if(!mesh.Output.Contains("Mesh OK.",StringComparison.Ordinal))throw new Exception("checkMesh did not report Mesh OK. See log; decomposition was not started.");
            string summary=$"Background cells {MeshReport.Cells(block.Output)}; "+MeshReport.Check(stage,mesh.Output);
            Console.WriteLine(summary);File.WriteAllText(Path.Combine(stage,"meshSummary.txt"),summary+"\n");
            if(c.processorCount>1) Run("decomposePar");
            File.WriteAllText(Path.Combine(stage,".built"),DateTime.UtcNow.ToString("O"));
            if(Directory.Exists(foam))Directory.Delete(foam,true);
            Directory.Move(stage,foam);
            Console.WriteLine("Built "+foam+"\nLogs: "+runLogs);
        }
        catch { Console.Error.WriteLine("Build stopped. Logs: "+runLogs+"\nPartial case retained: "+stage); throw; }
    }
    public static void Generate(string root,string output,Config c)
    {
        Facet[] solid=Geometry.Read(Path.Combine(root,"solidBody.stl"));
        Facet[] inlet=Geometry.Inlet(Geometry.Read(Path.Combine(root,"spawnPlane.stl")));
        Bounds bounds=Geometry.Bound(solid);
        Domain domain=BackgroundMesh.Layout(bounds,Geometry.Bound(inlet),c);
        Dictionary<string,string> v=BackgroundMesh.Build(domain);
        void Set(string key,double value)=>v[key]=Geometry.Number(value);
        double[] center=Enumerable.Range(0,3).Select(i=>(bounds.Min[i]+bounds.Max[i])*.0005).ToArray();
        double radius=Math.Sqrt(Enumerable.Range(0,3).Sum(i=>Math.Pow((bounds.Max[i]-bounds.Min[i])*.0005,2)))+c.acousticDampingClearanceMillimeters*.001;
        PreflightResult pre=Preflight.Run(solid,inlet,domain,c,c.probes,Enumerable.Range(0,3).Select(i=>(bounds.Min[i]+bounds.Max[i])*.5).ToArray(),radius*1000,(radius+c.acousticDampingThicknessMillimeters*.001)*1000);
        Console.WriteLine(pre.Report);
        v["DAMPING_ORIGIN"]=Geometry.Point(center);Set("DAMPING_INNER_RADIUS",radius);Set("DAMPING_OUTER_RADIUS",radius+c.acousticDampingThicknessMillimeters*.001);
        v["ACOUSTIC_DAMPING_ENABLED"]=c.acousticDampingEnabled?"yes":"no";
        // Consistent ideal-gas constants: gamma is derived from R and Cv, not independent.
        const double molWeight=28.9, cv=712, gasConstant=8314.46261815324;
        Set("AIR_CV",cv);Set("AIR_MOL_WEIGHT",molWeight);Set("AIR_GAMMA",1+gasConstant/molWeight/cv);
        Set("DAMPING_STRENGTH_MULTIPLIER",c.acousticDampingStrengthMultiplier);Set("DAMPING_TARGET_FREQUENCY",c.acousticDampingTargetFrequencyHz);
        Set("DELTA_T",c.deltaTSeconds);
        double interval=c.deltaTSeconds*c.fieldWriteIntervalTimeSteps;
        // Round up to an output boundary, then append exactly three guard writes.
        Set("END_TIME",(Math.Ceiling(c.endTimeSeconds/interval-1e-9)+3)*interval);
        Set("FAR_FIELD_RELAXATION_LENGTH",c.farFieldRelaxationLengthMeters);Set("FIELD_WRITE_INTERVAL",c.fieldWriteIntervalTimeSteps);
        Set("FEATURE_INCLUDED_ANGLE_DEGREES",c.featureIncludedAngleDegrees);Set("FEATURE_REFINEMENT_LEVEL",c.featureRefinementLevel);
        Set("N_CELLS_BETWEEN_LEVELS",c.nCellsBetweenLevels);Set("INITIAL_PRESSURE",c.ambientPressureHectopascals*100);
        Set("INITIAL_TEMPERATURE",c.initialTemperatureCelsius+273.15);Set("INLET_VELOCITY",c.inletVelocityMetersPerSecond);
        Set("PROCESSOR_COUNT",c.processorCount);Set("PROBE_WRITE_INTERVAL",c.probeWriteIntervalTimeSteps);
        Set("SURFACE_REFINEMENT_MAX_LEVEL",c.surfaceRefinementMaxLevel);Set("SURFACE_REFINEMENT_MIN_LEVEL",c.surfaceRefinementMinLevel);
        v["PROBE_LOCATIONS"]=string.Join('\n',c.probes.Select(p=>"            ("+Geometry.Point(p.point)+") // "+p.name));
        v["DISTANCE_REFINEMENT_REGIONS"]="solidWalls\n{\n mode distance;\n levels\n(\n"+string.Join('\n',c.bodyDistanceRefinement.OrderBy(x=>x.distanceMillimeters).Select(r=>"("+Geometry.Number(r.distanceMillimeters*.001)+" "+r.level+")"))+"\n);\n}";
        string template=Path.Combine(AppContext.BaseDirectory,"foamTemplate");
        if(!Directory.Exists(template))throw new DirectoryNotFoundException("Publish output is missing foamTemplate: "+template);
        Directory.CreateDirectory(output);
        foreach(string file in Directory.EnumerateFiles(template,"*",SearchOption.AllDirectories))
        {
            string text=Regex.Replace(File.ReadAllText(file),@"\{\{([A-Z0-9_]+)\}\}",m=>v.TryGetValue(m.Groups[1].Value,out string val)?val:throw new Exception("Unknown template token: "+m.Value));
            string dest=Path.Combine(output,Path.GetRelativePath(template,file));Directory.CreateDirectory(Path.GetDirectoryName(dest));File.WriteAllText(dest,text);
        }
        string surfaces=Directory.CreateDirectory(Path.Combine(output,"constant","triSurface")).FullName;
        File.WriteAllText(Path.Combine(surfaces,"solidBody.stl"),Geometry.Stl("solidBody",solid));
        File.WriteAllText(Path.Combine(surfaces,"spawnPlane.stl"),Geometry.Stl("spawnPlane",inlet));
        File.WriteAllText(Path.Combine(output,"config.json"),JsonSerializer.Serialize(c,Configuration.Json));
        File.WriteAllText(Path.Combine(output,"preflight.txt"),pre.Report);
        File.WriteAllText(Path.Combine(output,"bounds.json"),JsonSerializer.Serialize(bounds,Configuration.Json));
        File.WriteAllText(Path.Combine(output,".ocarina"),"beta-1\n");File.WriteAllText(Path.Combine(output,"case.foam"),"");
        double sound=Math.Sqrt((1+gasConstant/molWeight/cv)*gasConstant/molWeight*(c.initialTemperatureCelsius+273.15));
        double nominal=c.backgroundCellSizeMillimeters*.001/Math.Pow(2,Math.Max(c.featureRefinementLevel,c.surfaceRefinementMaxLevel));
        Console.WriteLine($"Nominal smallest surface cell {nominal:G5} m; estimated acoustic Courant {sound*c.deltaTSeconds/nominal:G5}. Actual snapped cells may be smaller.");
    }
}


