using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Mono.Cecil;
using UnityEngine;

class WaterTraceChecks
{
    const BindingFlags A=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static Type trace;static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static object Call(string name,params object[] args)=>trace.GetMethod(name,A).Invoke(null,args);
    public class Float {public string Name;public float Value;}
    public class Variables
    {
        public Dictionary<string,Float> Values=new Dictionary<string,Float>();
        public Float FindFsmFloat(string name)=>Values.TryGetValue(name,out var value)?value:null;
    }
    public class Position {public Vector3 Value;public bool IsNone;}
    public class Target {public string Name;}
    public class FlingObjectsFromGlobalPool {public Target spawnPoint;public Position position;}
    static int Main(string[] args)
    {
        string folder=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"water-trace-check");
        try
        {
            var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);trace=mod.GetType("HollowMario.WaterSurfaceTrace",true);
            var original=CultureInfo.CurrentCulture;
            try{CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");Check((string)Call("Number",.622634f)=="0.623","Trace numbers use an unambiguous decimal point in the creator's locale");}
            finally{CultureInfo.CurrentCulture=original;}
            var vars=new Variables();vars.Values.Add("Hero Offset",new Float{Name="Hero Offset",Value=-2.73f});
            Check((float)Call("FloatVariable",vars,"Hero Offset")==-2.73f,"Acid's authored negative offset is retained, not replaced by the ordinary-water offset");
            Check(Call("FloatVariable",vars,"Hero Surface Y")==null&&!vars.Values.ContainsKey("Hero Surface Y"),"Missing native references remain unknown and are never created");
            vars.Values.Add("Hero Surface Y",new Float{Name="Hero Surface Y",Value=0});
            Check((float)Call("FloatVariable",vars,"Hero Surface Y")==0,"A genuine zero height is distinguishable from a missing reference");
            var fling=new FlingObjectsFromGlobalPool{spawnPoint=new Target{Name="Hero"},position=new Position{Value=new Vector3(.2f,-.7f,0)}};
            Check(Math.Abs((float)Call("DropletPlane",(float?)1.53f,fling)-.83f)<.00001f,"Splash candidate uses the actual native Hero target and the actual action offset");
            Check(Math.Abs((float)Call("DropletPlane",(float?)-2.73f,fling)+3.43f)<.00001f,"The same calculation handles a differently authored acid region");
            Check(Call("DropletPlane",null,fling)==null,"No invented target when native placement is unknown");
            fling.position.IsNone=true;Check(Call("DropletPlane",(float?)1.53f,fling)==null,"An unset vector is not treated as an authored splash offset");
            fling.position.IsNone=false;fling.spawnPoint.Name="Other";Check(Call("DropletPlane",(float?)1.53f,fling)==null,"A non-Hero spawn cannot be added to the Knight placement target");
            foreach(string name in new[]{"lake_water_v02","acid_top_version_03","black_water_surface"})Check((bool)Call("ArtName",name),"Different liquid artwork names remain candidates: "+name);
            foreach(string name in new[]{"waterfall","acid_splash","water_fog","water_drip","water_stream","Mario"})Check(!(bool)Call("ArtName",name),"Decoration is not silently reported as a surface: "+name);
            var bounds=new Bounds(new Vector3(3,2,0),new Vector3(4,1,1));
            Check(!float.IsPositiveInfinity((float)Call("ArtDistance",bounds,new Vector3(3,0,0),2f)),"Surface geometry overlapping Mario's X is eligible for comparison");
            Check(float.IsPositiveInfinity((float)Call("ArtDistance",bounds,new Vector3(30,0,0),2f)),"A distant pool beside Mario is excluded");
            Check(float.IsPositiveInfinity((float)Call("ArtDistance",bounds,new Vector3(3,0,0),20f)),"An unrelated vertically distant surface is excluded");
            Check((bool)Call("SampleDue",10f,true,0,50f),"Entering another pool samples immediately instead of exhausting a session-global budget");
            Check(!(bool)Call("SampleDue",10f,false,3,11f),"Frames between observation times do not scan renderers or write a log");
            Check((bool)Call("SampleDue",100f,false,80,100f),"Long swims remain observed after the first few samples");

            // Check the real installed PlayMaker fields used by these read-only references.
            var play=Assembly.LoadFrom(Path.Combine(TestEnvironment.Managed,"PlayMaker.dll"));
            var variablesType=play.GetType("HutongGames.PlayMaker.FsmVariables",true);
            Check(variablesType.GetMethod("FindFsmFloat",new[]{typeof(string)})!=null,"Native float lookup exists");
            var game=Assembly.LoadFrom(Path.Combine(TestEnvironment.Managed,"Assembly-CSharp.dll"));
            var flingType=game.GetType("HutongGames.PlayMaker.Actions.FlingObjectsFromGlobalPool",true);
            Check(flingType.GetField("spawnPoint",A)!=null&&flingType.GetField("position",A)!=null,"Actual native splash action exposes the measured target and offset");
            using(var asm=AssemblyDefinition.ReadAssembly(TestEnvironment.CompanionPath))
            {
                var swimming=asm.MainModule.GetType("HollowMario.Swimming");bool observed=false;
                foreach(var method in swimming.Methods)if(method.Name=="Note")foreach(var ins in method.Body.Instructions)
                    if(ins.Operand is MethodReference target&&target.DeclaringType.Name=="WaterSurfaceTrace"&&target.Name=="Observe")observed=true;
                bool available=(bool)mod.GetType("HollowMario.DebugSettings",true).GetProperty("DiagnosticsAvailable",A).GetValue(null,null);
                Check(observed==available,"Only an explicit diagnostics build connects the swimming path to the trace");
                foreach(var method in asm.MainModule.GetType("HollowMario.WaterSurfaceTrace").Methods)
                    if(method.HasBody)foreach(var ins in method.Body.Instructions)
                        if(ins.Operand is MethodReference target&&target.DeclaringType.Namespace.StartsWith("UnityEngine"))
                            Check(!target.Name.StartsWith("set_")&&target.Name!="Destroy"&&target.Name!="Instantiate"&&target.Name!="SetActive",
                                "Trace never mutates game objects: "+target.FullName);
            }
            Directory.CreateDirectory(folder);string log=Path.Combine(folder,"water.log");
            trace.GetField("path",A).SetValue(null,log);Call("Write","POOL kind=acid suppliedY=54.56");
            using(var reader=new StreamReader(new FileStream(log,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)))
                Check(reader.ReadToEnd().Contains("POOL kind=acid suppliedY=54.56"),"Each record is readable from disk while the writer is still open");
            Call("Forget");File.WriteAllText(log,new string('x',2*1024*1024));Call("Write","SAMPLE rotated");Call("Forget");
            Check(new FileInfo(log+".previous").Length==2*1024*1024&&File.ReadAllText(log).Contains("SAMPLE rotated"),"Trace storage rotates with one bounded previous file");
            Console.WriteLine("PASS "+checks+" water trace reference, cadence, native metadata, read-only integration and live-file/rotation checks. No Unity scene was launched.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{if(trace!=null)Call("Forget");}
    }
}
