using System.Text.Json;

public static partial class Renderer
{
    public static string DoneFile(string time)=>Path.Combine(OutputDirectory,".done",TimeFrame(time).ToString("D8")+".json");
    public static bool IsComplete(string time)=>File.Exists(DoneFile(time));
    public static HashSet<string> CompletedTimes()
    {
        var times=new HashSet<string>(); string directory=Path.Combine(OutputDirectory,".done");
        if(!Directory.Exists(directory))return times;
        foreach(string file in Directory.EnumerateFiles(directory,"*.json"))
        {
            var record=JsonSerializer.Deserialize<Completion>(File.ReadAllText(file),Configuration.Json);
            times.Add(record.Time);
        }
        return times;
    }
    public static void Complete(string time,string previous)
    {
        Paths.Atomic(DoneFile(time),JsonSerializer.Serialize(new Completion{Time=time,Previous=previous},Configuration.Json));
    }
    public static void RecoverCompleted(string[] processors)
    {
        string directory=Path.Combine(OutputDirectory,".done");
        if(!Directory.Exists(directory))return;
        // Complete successor proves its predecessor was read. Finish interrupted deletions
        // across partitions before discovering new work; never rerender a committed frame.
        foreach(string file in Directory.EnumerateFiles(directory,"*.json"))
        {
            var record=JsonSerializer.Deserialize<Completion>(File.ReadAllText(file),Configuration.Json);
            if(record.Previous==null||!IsComplete(record.Previous))continue;
            foreach(string processor in processors)
            {
                string path=Path.Combine(processor,record.Previous);
                if(Directory.Exists(path))Directory.Delete(path,true);
            }
        }
    }
}
