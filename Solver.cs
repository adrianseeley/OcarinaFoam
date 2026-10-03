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
        {
            if(!Directory.Exists(Path.Combine(part,"constant","polyMesh")))throw new Exception("Missing mesh: "+part);
            string[] times=Renderer.Times(part,out _);string candidate=times.LastOrDefault()??"0";
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
        else Commands.Foam(foam,log,"mpirun","-np",c.processorCount.ToString(),"rhoPimpleFoam","-parallel");
    }
    public static void RestoreReference(string[] parts,string latest)
    {
        // readFields registers UMean with NO_WRITE; it is absent from later writes.
        // Restore the fixed reference from each decomposed initial directory before
        // resuming. This is input setup, not a fallback for incomplete solver output.
        if(latest=="0")return;
        foreach(string part in parts)
        {
            string source=Path.Combine(part,"0","UMean");
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
