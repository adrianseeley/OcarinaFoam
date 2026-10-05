using System.Reflection;
using System.Text;

public static class Services
{
    public static string UnitDirectory() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".config","systemd","user");
    public static string Quote(string text) => "\""+text.Replace("\\","\\\\").Replace("\"","\\\"").Replace("%","%%")+"\"";
    public static CommandResult Control(params string[] args) => Commands.Run("systemctl",Environment.CurrentDirectory,new[]{"--user"}.Concat(args).ToArray(),check:false);
    public static void Require(params string[] args)
    {
        CommandResult r=Control(args);
        if(r.ExitCode!=0)throw new Exception("systemctl --user "+string.Join(' ',args)+" failed:\n"+r.Output+"\nUse the same login account that ran prepare-machine.sh; do not sudo ocarina.");
    }
    static readonly Dictionary<string,string> watchers=new();
    // The watcher is chosen per case by config.json: "systemd" or "supervisord".
    public static string Watcher(string root)
    {
        lock(watchers)
        {
            if(!watchers.TryGetValue(root,out string w))watchers[root]=w=Configuration.Load(Path.Combine(root,"config.json")).watcher;
            return w;
        }
    }
    static bool Supervised(string root)=>Watcher(root)=="supervisord";
    public static string TryActive(string root,string kind)
    {
        try{return Active(root,kind);}catch{return "";}
    }
    public static string State(string root,string kind)
    {
        if(Supervised(root))return Supervisor.State(root,kind);
        CommandResult r=Control("show",Paths.Unit(root,kind),"--property=LoadState,ActiveState,SubState,Result,ExecMainStatus","--no-pager");
        return r.ExitCode==0?r.Output.Trim():"systemd unavailable: "+r.Output.Trim();
    }
    public static string Active(string root,string kind)
    {
        if(Supervised(root))return Supervisor.Active(root,kind);
        CommandResult r=Control("show",Paths.Unit(root,kind),"--property=ActiveState","--value");
        if(r.ExitCode!=0)throw new Exception(r.Output);
        return r.Output.Trim();
    }
    public static string UnitText(string root,string kind)
    {
        string exe=Environment.ProcessPath ?? throw new Exception("Cannot resolve running application.");
        var argv=new List<string>{exe};
        if(Path.GetFileNameWithoutExtension(exe)=="dotnet")argv.Add(Assembly.GetExecutingAssembly().Location);
        argv.Add("_"+kind);argv.Add(root);
        return "[Unit]\nDescription=Ocarina "+kind+" "+Paths.Id(root)+"\n\n[Service]\nType=exec\n"+
            "WorkingDirectory="+root.Replace("%","%%")+"/"+"\nExecStart="+string.Join(' ',argv.Select(x=>Quote(x).Replace("$","$$")))+"\n"+
            "Restart=no\nKillMode=control-group\nKillSignal=SIGTERM\nTimeoutStopSec=30\nSendSIGKILL=yes\n"+
            "StandardOutput=append:"+Path.Combine(Paths.Logs(root),kind+".log").Replace("%","%%")+"\nStandardError=inherit\n"+
            "Environment=FOAM_FILEHANDLER=uncollated\n";
    }
    public static void Start(string root,string kind,bool follow=true)
    {
        using(Paths.Lock(root,"build"))
        {
            Config c=Configuration.Built(root);
            if(!OperatingSystem.IsLinux())throw new Exception("Service commands require Linux.");
            bool supervised=c.watcher=="supervisord";
            if(supervised)Supervisor.Prepare();else Require("show-environment");
            string active=Active(root,kind);
            if(active is not ("active" or "activating"))
            {
                if(active=="deactivating")throw new Exception("Service is stopping. Wait until stopped, then retry.");
                if(supervised)Supervisor.Start(root,kind);
                else
                {
                    Directory.CreateDirectory(UnitDirectory());
                    string file=Path.Combine(UnitDirectory(),Paths.Unit(root,kind));
                    Paths.Atomic(file,UnitText(root,kind));
                    Require("daemon-reload");
                    Require("start",Paths.Unit(root,kind));
                }
            }
        }
        Console.WriteLine(State(root,kind));
        if(!follow)return;
        Console.WriteLine("Following "+kind+". Press Ctrl+C to detach; the service keeps running.");
        Follow(root,kind);
    }
    // Operator handle: start both jobs detached; renderer waits for solver output.
    public static void StartAll(string root)
    {
        foreach(string kind in new[]{"simulate","render"})Start(root,kind,false);
        Console.WriteLine("Started. Logs are in "+Paths.Logs(root));
        Progress.Run(root);
    }
    // Stop the renderer first, then the solver. Always attempt both before reporting failure.
    public static void StopAll(string root)
    {
        var errors=new List<string>();
        foreach(string kind in new[]{"render","simulate"})
        {
            try{Stop(root,kind);}catch(Exception e){errors.Add(kind+": "+e.Message);}
        }
        if(errors.Count>0)throw new Exception(string.Join("\n",errors));
    }
    // Stop services, then return a built case to t=0: keep the mesh, decomposition and initial fields, delete
    // every result (solver times, probe histories, renders, audio, report) and the solver/render logs.
    public static void Reset(string root)
    {
        StopAll(root);
        string foam=Paths.Foam(root);
        if(!File.Exists(Path.Combine(foam,".built")))throw new Exception("Case is not fully built. Run ocarina build DIR first.");
        using(var gate=Paths.Lock(root,"build"))
        {
            var targets=new List<string>();
            var parts=new List<string>{foam};
            parts.AddRange(Directory.GetDirectories(foam,"processor*"));
            foreach(string part in parts)
            {
                foreach(string directory in Directory.GetDirectories(part))
                {
                    string name=Path.GetFileName(directory);
                    if(double.TryParse(name,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double time)&&double.IsFinite(time)&&time>0)targets.Add(directory);
                }
                targets.Add(Path.Combine(part,"postProcessing"));
            }
            foreach(string name in new[]{"renders","previews","audio","videos","wav",".report-tmp"})targets.Add(Path.Combine(root,name));
            string logs=Path.Combine(root,"logs");
            if(Directory.Exists(logs))targets.AddRange(Directory.GetFileSystemEntries(logs).Where(e=>!Path.GetFileName(e).StartsWith("build-")&&Directory.Exists(e)));
            foreach(string path in targets)
            {
                if(!Directory.Exists(path))continue;
                Directory.Delete(path,true);
                Console.WriteLine("removed "+path);
            }
            var files=new List<string>{Path.Combine(root,"report.html"),Path.Combine(root,"report.zip")};
            if(Directory.Exists(logs))files.AddRange(Directory.GetFiles(logs));
            foreach(string path in files)
            {
                if(!File.Exists(path))continue;
                File.Delete(path);
                Console.WriteLine("removed "+path);
            }
        }
        Console.WriteLine("Reset to t=0. Run ocarina start "+root+" to begin again.");
    }
    // Stop services, then delete everything generated; inputs (STLs, config.json, model files) stay.
    public static void Clean(string root)
    {
        StopAll(root);
        using(var gate=Paths.Lock(root,"build"))
        {
            var targets=new List<string>{Paths.Foam(root),Path.Combine(root,"renders"),Path.Combine(root,"previews"),Path.Combine(root,"audio"),Path.Combine(root,"logs"),Path.Combine(root,"videos"),Path.Combine(root,"wav"),Path.Combine(root,".report-tmp")};
            targets.AddRange(Directory.GetDirectories(root,".foam-build-*"));
            foreach(string path in targets)
            {
                if(!Directory.Exists(path))continue;
                Directory.Delete(path,true);
                Console.WriteLine("removed "+path);
            }
            foreach(string file in new[]{"report.html","report.zip"})
            {
                string path=Path.Combine(root,file);
                if(!File.Exists(path))continue;
                File.Delete(path);
                Console.WriteLine("removed "+path);
            }
        }
        foreach(string file in Directory.GetFiles(root,".*.lock"))
        {
            File.Delete(file);
            Console.WriteLine("removed "+file);
        }
    }
    public static void Stop(string root,string kind)
    {
        using var gate=Paths.Lock(root,"build");
        if(Supervised(root)){Supervisor.Stop(root,kind);Console.WriteLine(kind+" stopped; program removed. Logs and results retained.");return;}
        Require("show-environment");
        string unit=Paths.Unit(root,kind);
        CommandResult load=Control("show",unit,"--property=LoadState","--value");
        if(load.ExitCode!=0)throw new Exception(load.Output);
        if(load.Output.Trim()!="not-found")
        {
            Require("stop",unit); // waits for the entire control group, including MPI workers
            string state=Active(root,kind);
            if(state is "active" or "activating" or "deactivating")throw new Exception("Service did not stop; unit retained.");
            Control("reset-failed",unit); // a stopped unit may already be unloaded
        }
        string file=Path.Combine(UnitDirectory(),unit);
        if(File.Exists(file))File.Delete(file);
        Require("daemon-reload");
        Console.WriteLine(kind+" stopped; unit removed. Logs and results retained.");
    }
    public static void Follow(string root,string kind)
    {
        bool detach=false;
        ConsoleCancelEventHandler cancel=(_,e)=>{e.Cancel=true;detach=true;};
        Console.CancelKeyPress+=cancel;
        string path=Path.Combine(Paths.Logs(root),kind+".log");
        try
        {
            using var file=new FileStream(path,FileMode.OpenOrCreate,FileAccess.Read,FileShare.ReadWrite);
            // Reattach prints a bounded recent tail, then follows subsequent bytes.
            if(file.Length>32768)file.Seek(-32768,SeekOrigin.End);
            using var reader=new StreamReader(file,Encoding.UTF8);
            while(!detach)
            {
                string text=reader.ReadToEnd(); if(text.Length>0)Console.Write(text);
                string active=Active(root,kind);
                if(active is not ("active" or "activating"))
                {
                    text=reader.ReadToEnd();if(text.Length>0)Console.Write(text);
                    Console.WriteLine(State(root,kind));
                    if(active=="failed")throw new Exception(kind+" failed; see "+path+". It will not restart automatically.");
                    return;
                }
                Thread.Sleep(1000);
            }
        }
        finally {Console.CancelKeyPress-=cancel;}
        Console.WriteLine("Detached.");
    }
}
