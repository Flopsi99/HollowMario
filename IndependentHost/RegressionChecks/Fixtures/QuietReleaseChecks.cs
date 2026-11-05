using System;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using Mono.Cecil;

class QuietReleaseChecks
{
    const BindingFlags A=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static bool Calls(MethodDefinition method,string type,string member)
    {
        if(method==null||!method.HasBody)return false;
        foreach(var instruction in method.Body.Instructions)
            if(instruction.Operand is MethodReference call&&call.DeclaringType.Name==type&&call.Name==member)return true;
        return false;
    }
    static MethodDefinition Method(TypeDefinition type,string name)
    {foreach(var method in type.Methods)if(method.Name==name)return method;throw new Exception("Missing method: "+name);}
    static int Main(string[] args)
    {
        string config=Path.Combine(Path.GetTempPath(),"hollowmario-quiet-"+Guid.NewGuid().ToString("N")+".cfg");
        try
        {
            var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var debug=mod.GetType("HollowMario.DebugSettings",true);
            Check(!(bool)debug.GetProperty("DiagnosticsAvailable",A).GetValue(null,null),"Public builds require explicit compilation to restore developer diagnostics");
            var cfg=new ConfigFile(config);cfg.Bind("Debug","Enabled",true,"").Value=true;
            foreach(string key in new[]{"VerboseLogging","TraceCamera","LogPresentationHitches"})cfg.Bind("Debug",key,true,"").Value=true;
            debug.GetMethod("Bind",A).Invoke(null,new object[]{cfg});
            foreach(string property in new[]{"VerboseLogging","CameraTrace","PresentationTiming"})
                Check(!(bool)debug.GetProperty(property,A).GetValue(null,null),"Retained developer settings cannot reactivate "+property);
            Check((bool)debug.GetProperty("RoomReset",A).GetValue(null,null)&&(bool)debug.GetProperty("BenchReturn",A).GetValue(null,null),"Quiet logging preserves the existing recovery controls");

            TestEnvironment.Messages.Clear();var log=new ManualLogSource("Hollow Mario");
            log.LogInfo("hidden gameplay trace");log.LogDebug("hidden native trace");
            Check(TestEnvironment.Messages.Count==0,"API services suppress informational tracing without needing a companion Harmony filter");
            log.LogWarning("needed warning");log.LogError("needed error");log.LogInfo("melty test: pass");
            Check(TestEnvironment.Messages.Count==3,"Real failures and explicit test acknowledgements remain available");

            var host=Assembly.LoadFrom(TestEnvironment.HostPath);var trace=host.GetType("MarioHollow.HostTrace",true);
            Check(!(bool)trace.GetProperty("Available",A).GetValue(null,null),"Native host is built with the same quiet policy");
            int written=0;trace.GetField("Sink",A).SetValue(null,(Action<string>)(s=>written++));trace.GetField("Enabled",A).SetValue(null,true);
            trace.GetMethod("Write",A).Invoke(null,new object[]{"old configuration requested tracing"});
            Check(written==0,"Even a retained enabled host flag cannot emit a diagnostic event");
            trace.GetField("Enabled",A).SetValue(null,false);trace.GetField("Sink",A).SetValue(null,null);

            using(var assembly=AssemblyDefinition.ReadAssembly(TestEnvironment.HostPath))
            {
                foreach(var type in assembly.MainModule.Types)foreach(var method in type.Methods)
                    Check(!Calls(method,"HostTrace","Write"),"Host event call and argument formatting are omitted from "+method.FullName);
                Check(!Calls(Method(assembly.MainModule.GetType("MarioHollow.BridgeContracts"),"Install"),"CameraTrace","Install"),"Camera file observer hooks are omitted from production startup");
            }
            using(var assembly=AssemblyDefinition.ReadAssembly(TestEnvironment.CompanionPath))
            {
                var swimming=assembly.MainModule.GetType("HollowMario.Swimming");
                foreach(var method in swimming.Methods)foreach(string member in new[]{"Observe","Leave","Forget"})
                    Check(!Calls(method,"WaterSurfaceTrace",member),"Water file observer call is omitted from "+method.FullName);
                Check(!Calls(Method(assembly.MainModule.GetType("HollowMario.Plugin"),"Awake"),"PresentationTiming","Arm"),"Performance observation hooks are omitted from production startup");
            }
            Console.WriteLine("PASS "+checks+" quiet-release checks: retained verbose settings, native events, camera/water files, performance hooks, useful failures and recovery controls. No game started.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{if(File.Exists(config))File.Delete(config);}
    }
}
