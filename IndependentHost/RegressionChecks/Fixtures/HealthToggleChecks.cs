// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
class HealthToggleChecks
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static int Main(string[] args)
    {
        try{
            string root=Path.GetFullPath(args[0]);
            string package=TestEnvironment.Package;
            string managed=TestEnvironment.Managed;
            AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
                string file=new AssemblyName(e.Name).Name+".dll";
                foreach(string dir in new[]{package,managed}){string p=Path.Combine(dir,file);if(File.Exists(p))return Assembly.LoadFrom(p);}return null;
            };
            var game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));
            var asm=Assembly.LoadFrom(TestEnvironment.CompanionPath);
            var flags=BindingFlags.Static|BindingFlags.NonPublic;
            var settings=asm.GetType("HollowMario.DebugSettings",true);
            var bind=settings.GetMethod("Bind",flags);var configType=bind.GetParameters()[0].ParameterType;
            string filePath=Path.Combine(root,@".artifacts\hp-toggle-"+Guid.NewGuid().ToString("N")+".cfg");
            object config=Activator.CreateInstance(configType,new object[]{filePath,true});bind.Invoke(null,new[]{config});
            var show=settings.GetProperty("ShowBossHp",flags);var toggle=settings.GetMethod("ToggleBossHp",flags);
            Check(!(bool)show.GetValue(null,null),"Fresh release must start with health hidden");
            toggle.Invoke(null,null);Check((bool)show.GetValue(null,null),"First key press shows health");
            toggle.Invoke(null,null);Check(!(bool)show.GetValue(null,null),"Second key press hides health");
            toggle.Invoke(null,null);config=Activator.CreateInstance(configType,new object[]{filePath,true});bind.Invoke(null,new[]{config});
            Check(!(bool)show.GetValue(null,null),"Session toggle must not turn on next launch's default");
            var hud=asm.GetType("HollowMario.BossHpHud",true);var arm=hud.GetMethod("Arm",flags);var ptypes=arm.GetParameters();
            object harmony=Activator.CreateInstance(ptypes[0].ParameterType,new object[]{"health-toggle-checks"});
            object logger=Activator.CreateInstance(ptypes[1].ParameterType,new object[]{"health-toggle-checks"});
            arm.Invoke(null,new[]{harmony,logger});
            foreach(string name in new[]{"OnEnable","Start"})
                Check(ptypes[0].ParameterType.GetMethod("GetPatchInfo").Invoke(null,new object[]{game.GetType("HealthManager").GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)})!=null,"Health tracking must be armed while hidden");
            Check(ptypes[0].ParameterType.GetMethod("GetPatchInfo").Invoke(null,new object[]{game.GetType("BossSceneController").GetMethod("ReportHealth")})!=null,"Encounter tracking must be armed while hidden");
            ptypes[0].ParameterType.GetMethod("UnpatchSelf").Invoke(harmony,null);
            var entry=configType.GetMethod("Bind").MakeGenericMethod(typeof(bool)).Invoke(config,new object[]{"Debug","Enabled",true,""});
            entry.GetType().GetProperty("Value").SetValue(entry,false,null);toggle.Invoke(null,null);
            Check(!(bool)show.GetValue(null,null),"Shared debug master still gates the hotkey");
            File.Delete(filePath);
            Console.WriteLine("Release HUD defaults, on/off toggle, session reset, disabled-HUD tracking hooks and master switch passed.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
