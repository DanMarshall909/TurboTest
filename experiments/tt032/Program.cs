using CompetitiveBaseline;
if(args.Length==4 && args[0]=="qualify")
{
    if(Directory.Exists(args[2]))throw new ArgumentException("Output already exists");
    return await Qualification.Run(args[1],args[2],int.Parse(args[3]));
}
if(args.Length!=8 || args[0]!="run")
{Console.Error.WriteLine("run <turbotest|respawn|native-template|integresql> <count> <workers> <fresh|warm> <new-output-dir> <integre-lifetime-ms> <durable|relaxed>");return 2;}
var count=int.Parse(args[2]);var workers=int.Parse(args[3]);var lifetime=int.Parse(args[6]);
if(count<1||workers<1||workers>4||args[4] is not ("fresh" or "warm")||Directory.Exists(args[5]))throw new ArgumentException("Invalid experiment plan or output already exists");
return await Runner.Run(args[1],count,workers,args[4],args[5],lifetime,args[7]);
