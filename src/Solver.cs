public static class Solver
{
    public static void Run(string root)
    {
        using var gate=Paths.Lock(root,"simulate");
        Config c=Configuration.Built(root);
        string foam=Paths.Foam(root);
        string[] parts=c.processorCount==1?new[]{foam}:Enumerable.Range(0,c.processorCount).Select(i=>Path.Combine(foam,"processor"+i)).ToArray();
        string latest=null;
        foreach(string part in parts)
            if(!Directory.Exists(Path.Combine(part,"constant","polyMesh")))throw new Exception("Missing mesh: "+part);
        DiscardIncompleteWrites(parts);
        foreach(string part in parts)
        {
            string[] times=Renderer.Times(part,out _);string candidate=times.LastOrDefault()??ZeroTime(part);
            if(latest!=null&&candidate!=latest)throw new Exception("Latest times differ across processors. Refusing an inconsistent restart; inspect the last write.");
            latest=candidate;
            foreach(string field in new[]{"p","U","T","nut","alphat"})
            {
                string path=Path.Combine(part,candidate,field);
                if(!File.Exists(path)&&!File.Exists(path+".gz"))throw new Exception("Incomplete restart field: "+path);
                // Fully decompress and parse each mandatory field before starting MPI.
                int count=Renderer.CellCount(Path.Combine(part,"constant","polyMesh"));
                double min=double.PositiveInfinity,max=double.NegativeInfinity;
                Renderer.ReadField(path,field=="U",new double[count],0,count,ref min,ref max);
            }
        }
        RestoreReference(parts,latest);
        string log=Path.Combine(Paths.Logs(root),"solver-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".log");
        Console.WriteLine("Solver starts from t="+latest+"; log "+log);
        if(c.processorCount==1) Commands.Foam(foam,log,"rhoPimpleFoam");
        else
        {
            // Open MPI refuses to run as root (typical in containers) unless told otherwise.
            var mpi=new List<string>();
            if(Environment.IsPrivilegedProcess)mpi.Add("--allow-run-as-root");
            mpi.AddRange(new[]{"-np",c.processorCount.ToString(),"rhoPimpleFoam","-parallel"});
            Commands.Foam(foam,log,"mpirun",mpi.ToArray());
        }
    }
    // A solver killed mid-write leaves a newest time directory that is missing or has truncated fields.
    // Delete such directories (newest first, on every processor) so the run resumes from the last complete write.
    static void DiscardIncompleteWrites(string[] parts)
    {
        var all=parts.SelectMany(part=>Renderer.Times(part,out _)).Distinct().OrderByDescending(t=>double.Parse(t,System.Globalization.CultureInfo.InvariantCulture)).ToList();
        foreach(string time in all)
        {
            if(parts.All(part=>IsCompleteWrite(part,time)))return;
            foreach(string part in parts)
            {
                string dir=Path.Combine(part,time);
                if(Directory.Exists(dir)){Directory.Delete(dir,true);}
            }
            Console.WriteLine("Discarded incomplete write at t="+time+"; resuming from the previous complete write.");
        }
    }
    static bool IsCompleteWrite(string part,string time)
    {
        try
        {
            int count=Renderer.CellCount(Path.Combine(part,"constant","polyMesh"));
            foreach(string field in new[]{"p","U","T","nut","alphat"})
            {
                string path=Path.Combine(part,time,field);
                if(!File.Exists(path)&&!File.Exists(path+".gz"))return false;
                double min=double.PositiveInfinity,max=double.NegativeInfinity;
                Renderer.ReadField(path,field=="U",new double[count],0,count,ref min,ref max);
            }
            return true;
        }
        catch(Exception){return false;}
    }
    // The initial directory is named by timeFormat (e.g. "0" or "0.0000000000"), so match it numerically.
    public static string ZeroTime(string part)
    {
        foreach(string d in Directory.GetDirectories(part))
        {
            string name=Path.GetFileName(d);
            if(double.TryParse(name,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double v)&&v==0)return name;
        }
        throw new Exception("Missing initial time directory in "+part);
    }
    public static void RestoreReference(string[] parts,string latest)
    {
        // readFields registers UMean with NO_WRITE; it is absent from later writes.
        // Restore the fixed reference from each decomposed initial directory before
        // resuming. This is input setup, not a fallback for incomplete solver output.
        if(latest==ZeroTime(parts[0]))return;
        foreach(string part in parts)
        {
            string source=Path.Combine(part,ZeroTime(part),"UMean");
            string target=Path.Combine(part,latest,"UMean");
            if(File.Exists(source))
            {
                File.Copy(source,target,true);
                if(File.Exists(target+".gz"))File.Delete(target+".gz");
            }
            else if(File.Exists(source+".gz"))
            {
                File.Copy(source+".gz",target+".gz",true);
                if(File.Exists(target))File.Delete(target);
            }
            else throw new FileNotFoundException("Missing initial damping reference.",source);
        }
    }
}
