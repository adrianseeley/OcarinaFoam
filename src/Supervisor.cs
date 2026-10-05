using System.Reflection;

// supervisord backend for hosts without systemd (containers). A private supervisord instance
// runs as the current user with its own config, socket and pid under ~/.config/ocarina and
// ~/.local/state/ocarina; each job is one program file that exists only while the job does.
public static class Supervisor
{
    static string Home=>Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string ConfigDirectory=>Path.Combine(Home,".config","ocarina");
    static string ProgramDirectory=>Path.Combine(ConfigDirectory,"supervisor.d");
    static string MainConfig=>Path.Combine(ConfigDirectory,"supervisord.conf");
    static string StateDirectory=>Path.Combine(Home,".local","state","ocarina");
    static string Name(string root,string kind)=>"ocarina-"+Paths.Id(root)+"-"+kind;
    static string Escape(string text)=>text.Replace("%","%%");
    static string Quote(string text)=>"\""+text.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";

    static CommandResult Ctl(params string[] args)=>Commands.Run("supervisorctl",Environment.CurrentDirectory,new[]{"-c",MainConfig}.Concat(args).ToArray(),check:false);
    static void Require(params string[] args)
    {
        CommandResult r=Ctl(args);
        if(r.ExitCode!=0)throw new Exception("supervisorctl "+string.Join(' ',args)+" failed:\n"+r.Output);
    }

    // Write the instance config if needed and make sure its daemon is running.
    public static void Prepare()
    {
        if(Commands.Run("supervisord",Environment.CurrentDirectory,new[]{"--version"},check:false).ExitCode!=0)
            throw new Exception("supervisord is not installed. Run prepare-machine.sh or: sudo apt-get install supervisor");
        Directory.CreateDirectory(ProgramDirectory);
        Directory.CreateDirectory(StateDirectory);
        string socket=Path.Combine(StateDirectory,"supervisor.sock");
        Paths.Atomic(MainConfig,
            "[unix_http_server]\nfile="+Escape(socket)+"\nchmod=0700\n\n"+
            "[supervisord]\nlogfile="+Escape(Path.Combine(StateDirectory,"supervisord.log"))+"\nlogfile_maxbytes=10MB\npidfile="+Escape(Path.Combine(StateDirectory,"supervisord.pid"))+
            "\nchildlogdir="+Escape(StateDirectory)+"\n\n"+
            "[rpcinterface:supervisor]\nsupervisor.rpcinterface_factory=supervisor.rpcinterface:make_main_rpcinterface\n\n"+
            "[supervisorctl]\nserverurl=unix://"+Escape(socket)+"\n\n"+
            "[include]\nfiles="+Escape(ProgramDirectory)+"/*.conf\n");
        if(Ctl("pid").ExitCode==0)return;
        CommandResult started=Commands.Run("supervisord",Environment.CurrentDirectory,new[]{"-c",MainConfig},check:false);
        for(int i=0;i<50;i++)
        {
            if(Ctl("pid").ExitCode==0)return;
            Thread.Sleep(200);
        }
        throw new Exception("supervisord did not start:\n"+started.Output+"\nSee "+Path.Combine(StateDirectory,"supervisord.log"));
    }

    static string ProgramText(string root,string kind)
    {
        string exe=Environment.ProcessPath ?? throw new Exception("Cannot resolve running application.");
        var argv=new List<string>{exe};
        if(Path.GetFileNameWithoutExtension(exe)=="dotnet")argv.Add(Assembly.GetExecutingAssembly().Location);
        argv.Add("_"+kind);argv.Add(root);
        return "[program:"+Name(root,kind)+"]\ncommand="+Escape(string.Join(' ',argv.Select(Quote)))+"\ndirectory="+Escape(root)+
            "\nautostart=false\nautorestart=false\nstartsecs=0\nstartretries=0\nstopsignal=TERM\nstopwaitsecs=30\nstopasgroup=true\nkillasgroup=true\n"+
            "redirect_stderr=true\nstdout_logfile="+Escape(Path.Combine(Paths.Logs(root),kind+".log"))+"\nstdout_logfile_maxbytes=0\nstdout_logfile_backups=0\n"+
            "environment=FOAM_FILEHANDLER=\"uncollated\"\n";
    }
    public static void Start(string root,string kind)
    {
        Directory.CreateDirectory(Paths.Logs(root));
        Paths.Atomic(Path.Combine(ProgramDirectory,Name(root,kind)+".conf"),ProgramText(root,kind));
        Require("reread");
        Require("update");
        Require("start",Name(root,kind));
    }
    public static void Stop(string root,string kind)
    {
        Prepare();
        string name=Name(root,kind);
        if(Active(root,kind) is "active" or "activating" or "deactivating")Ctl("stop",name);
        if(Active(root,kind) is "active" or "activating" or "deactivating")throw new Exception("Service did not stop; program retained.");
        string file=Path.Combine(ProgramDirectory,name+".conf");
        if(File.Exists(file))File.Delete(file);
        Require("reread");
        Require("update");
    }
    static string Line(string root,string kind)
    {
        CommandResult r=Ctl("status",Name(root,kind));
        return r.Output.Trim();
    }
    // Same vocabulary as systemd's ActiveState so callers are backend-agnostic.
    public static string Active(string root,string kind)
    {
        string[] tokens=Line(root,kind).Split(' ',StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length>1?tokens[1] switch
        {
            "RUNNING"=>"active",
            "STARTING" or "BACKOFF"=>"activating",
            "STOPPING"=>"deactivating",
            "FATAL"=>"failed",
            _=>"inactive"
        }:"inactive";
    }
    public static string State(string root,string kind)
    {
        string line=Line(root,kind);
        return line.Length==0||line.Contains("ERROR")||line.Contains("refused")||line.Contains("no such file",StringComparison.OrdinalIgnoreCase)?"supervisord: not running or no such job":line;
    }
}
