public static class Status
{
    public static void Run(string root)
    {
        Console.WriteLine(root);
        foreach(string kind in new[]{"simulate","render"})
        {
            Console.WriteLine("\n"+kind+":");
            try {Console.WriteLine(Services.State(root,kind));} catch(Exception e){Console.WriteLine(e.Message);}
        }
        string foam=Paths.Foam(root);
        if(!File.Exists(Path.Combine(foam,".built"))){Console.WriteLine("\nBuild: incomplete or not built.");return;}
        Config c=Configuration.Built(root);Renderer.Configure(root,c,false);
        Console.WriteLine();
        Renderer.PrintLayoutSummary();
        string[] processors=Renderer.FindProcessors();
        var done=Renderer.CompletedTimes();
        var eligible=Renderer.ReadyTimes(processors,new HashSet<string>());
        var all=new HashSet<string>(Renderer.Times(processors[0],out _));
        foreach(string processor in processors.Skip(1))all.IntersectWith(Renderer.Times(processor,out _));
        int waiting=eligible.Count(t=>!done.Contains(t));
        int held=all.Count(t=>!done.Contains(t)&&!eligible.Contains(t));
        Console.WriteLine($"\nComplete rendered frames: {done.Count}\nWaiting and eligible: {waiting}\nHeld for three newer writes: {held}\nRaw times common to all ranks: {all.Count}");
        for(int i=0;i<processors.Length;i++)
        {
            string[] times=Renderer.Times(processors[i],out _);
            Console.WriteLine($"Rank {i}: {times.Length} positive times; latest {times.LastOrDefault()??"initial"}");
        }
        Console.WriteLine("Logs: "+Path.Combine(root,"logs"));
    }
}
