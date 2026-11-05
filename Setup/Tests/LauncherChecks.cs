using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HollowMario.Setup;

internal static class LauncherChecks
{
    const BindingFlags Any=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static int checks;
    static Type options,program;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static object Parse(params string[] args)=>options.GetMethod("Parse",Any).Invoke(null,new object[]{args});
    static object Read(object value,string name)=>value.GetType().GetField(name,Any)?.GetValue(value)??value.GetType().GetProperty(name,Any)?.GetValue(value,null);
    static string HashIfPresent(string path)=>File.Exists(path)?Disk.Hash(path):null;
    static void Run(string exe,string arguments,int expected)
    {
        using(var child=Process.Start(new ProcessStartInfo(exe,arguments){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardError=true}))
        {
            var error=child.StandardError.ReadToEndAsync();
            bool sawWindow=false;var elapsed=Stopwatch.StartNew();
            while(!child.WaitForExit(40))
            {
                if(elapsed.ElapsedMilliseconds>15000){child.Kill();throw new Exception("Setup did not finish its unattended check; no game was started.");}
                try{sawWindow|=child.MainWindowHandle!=IntPtr.Zero;}
                catch(InvalidOperationException){if(!child.HasExited)throw;}
            }
            Check(child.ExitCode==expected,"Headless setup returns the expected exit status: "+error.Result);
            Check(!sawWindow,"Headless setup does not create an installer window");
        }
    }
    internal static int Run(string scratch,string game,string rom)
    {
        checks=0;Directory.CreateDirectory(scratch);
        var assembly=Assembly.Load("HollowMario-Setup");options=assembly.GetType("HollowMario.Launcher.Options",true);program=assembly.GetType("HollowMario.Launcher.Program",true);
        object manual=Parse();Check(!(bool)Read(manual,"Melty"),"A normal GitHub launch always selects interactive setup");
        Check(program.GetMethod("RunRemembered",Any)==null,"Remembered paths cannot silently bypass the manual installer");
        object automatic=Parse("--melty","--game-dir",scratch,"--rom-dir",scratch,"--native-dir",scratch,"--no-launch");
        Check((bool)Read(automatic,"Melty")&&(bool)Read(automatic,"NoLaunch"),"Melty's explicit background mode and no-launch check parse independently");
        foreach(string[] conflict in new[]{new[]{"--melty","--choose"},new[]{"--melty","--rom-only"},new[]{"--melty","--render","preview.png"}})
        {
            bool refused=false;try{Parse(conflict);}catch(TargetInvocationException error){refused=error.InnerException is ArgumentException;}
            Check(refused,"Background Melty setup rejects interactive options");
        }
        object preview=Parse("--render",Path.Combine(scratch,"preview.png"));
        using(var window=(IDisposable)Activator.CreateInstance(assembly.GetType("HollowMario.Launcher.SetupWindow",true),Any,null,new[]{preview},null))
        {
            Check(Convert.ToString(Read(Read(window,"community"),"Text")).StartsWith("Install / Update"),"Manual setup presents an explicit Install / Update choice");
            Check(Convert.ToString(Read(Read(window,"remove"),"Text")).StartsWith("Uninstall"),"Manual setup presents an explicit Uninstall choice");
            Check(Convert.ToString(Read(Read(window,"gameLabel"),"Text"))==@"C:\Games\Hollow Knight","The selected game folder is shown as an actual path");
            Check(Convert.ToString(Read(Read(window,"romLabel"),"Text"))==@"C:\Games\Super Mario 64\baserom.us.z64","The selected ROM file is shown as an actual path");
            Check((bool)Read(Read(window,"gameLabel"),"ReadOnly")&&(bool)Read(Read(window,"romLabel"),"ReadOnly"),"Displayed paths remain selectable while browse controls own path changes");
            Check(!(bool)Read(Read(window,"launch"),"Checked"),"Installing does not launch a game unless the player explicitly requests it");
            Check(Convert.ToString(Read(Read(window,"play"),"Text"))=="Install / Update","The first manual action is installation, not a hidden launch");
        }
        string preferences=Preferences.DefaultPath,legacy=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MarioHollow","melty.properties");
        string savedPreferences=HashIfPresent(preferences),savedLegacy=HashIfPresent(legacy);
        string empty=Path.Combine(scratch,"empty-own-copy");Directory.CreateDirectory(empty);
        Check(RomResolver.FindSupplied(empty)==null,"An empty Melty own-copy handoff cannot borrow a cached developer ROM");
        string result=Path.Combine(scratch,"missing-game.json");
        Run(assembly.Location,"--melty --no-launch --result-file \""+result+"\"",1);
        var failure=JsonFile.Read<LaunchResult>(result);Check(!failure.Success&&failure.Stage=="setup"&&failure.Message.Contains("Melty did not supply"),"Background failure is machine-readable without a dialog");
        string fixtureGame=Path.Combine(scratch,"empty-game");Directory.CreateDirectory(fixtureGame);File.WriteAllText(Path.Combine(fixtureGame,"hollow_knight.exe"),"fixture; never executed");
        result=Path.Combine(scratch,"missing-own-copy.json");
        Run(assembly.Location,"--melty --no-launch --game-dir \""+fixtureGame+"\" --rom-dir \""+empty+"\" --result-file \""+result+"\"",1);
        failure=JsonFile.Read<LaunchResult>(result);Check(!failure.Success&&failure.Message.Contains("ROM"),"Missing own-copy inputs stop before any installation mutation");
        Check(Directory.GetFiles(fixtureGame,"*",SearchOption.AllDirectories).Length==1,"Failed unattended preparation leaves the selected game untouched");
        string marker=Environment.GetEnvironmentVariable("HOLLOW_MARIO_SETUP_TEST_MARKER");
        try
        {
            Environment.SetEnvironmentVariable("HOLLOW_MARIO_SETUP_TEST_MARKER","inherited");
            var start=(ProcessStartInfo)program.GetMethod("MeltyStartInfo",Any).Invoke(null,new object[]{fixtureGame,Path.Combine(empty,"baserom.us.z64")});
            Check(start.FileName==Path.Combine(fixtureGame,"hollow_knight.exe")&&!start.UseShellExecute,"Melty starts the required host executable as its own child");
            Check(start.EnvironmentVariables["HOLLOW_MARIO_SETUP_TEST_MARKER"]=="inherited"&&start.EnvironmentVariables["MARIO_HK_ROMDIR"]==empty,"Melty session/test variables and its ROM folder reach the actual game");
        }
        finally{Environment.SetEnvironmentVariable("HOLLOW_MARIO_SETUP_TEST_MARKER",marker);}
        if(!string.IsNullOrEmpty(rom)&&RomResolver.Valid(rom))CheckPreparedLaunch(scratch,assembly.Location,game,rom);
        Check(HashIfPresent(preferences)==savedPreferences&&HashIfPresent(legacy)==savedLegacy,"Headless preparation never overwrites manual game/ROM preferences");
        return checks;
    }
    private static void CheckPreparedLaunch(string scratch,string exe,string game,string rom)
    {
        string package=Path.Combine(scratch,"prepared-package"),fixture=Path.Combine(scratch,"prepared-game"),native=Path.Combine(scratch,"prepared-runtime");
        Directory.CreateDirectory(package);Directory.CreateDirectory(Path.Combine(package,"payloads","api"));
        string payload=Path.Combine(package,"payloads","api","HollowMario.dll");File.WriteAllText(payload,"owned payload fixture, never loaded");
        JsonFile.Write(Path.Combine(package,"package.json"),new PackageManifest{Version="fixture",Files=new[]{new PackageFile{Path="payloads/api/HollowMario.dll",Sha256=Disk.Hash(payload)}}});
        foreach(string name in new[]{"HollowMario-Setup.exe","HollowMario-Setup.exe.config","HollowMario.Setup.Core.dll","Mono.Cecil.dll"})
            Disk.Copy(Path.Combine(Path.GetDirectoryName(exe),name),Path.Combine(package,name));
        Directory.CreateDirectory(fixture);File.WriteAllText(Path.Combine(fixture,"hollow_knight.exe"),"fixture host; never run");
        Disk.Copy(Path.Combine(game,"hollow_knight_Data","Managed","Assembly-CSharp.dll.v"),Path.Combine(fixture,"hollow_knight_Data","Managed","Assembly-CSharp.dll"));
        Directory.CreateDirectory(native);File.WriteAllText(Path.Combine(native,"sm64.dll"),"prepared local runtime fixture, never loaded");
        string result=Path.Combine(scratch,"prepared-result.json");
        Run(Path.Combine(package,"HollowMario-Setup.exe"),"--melty --no-launch --game-dir \""+fixture+"\" --rom-file \""+rom+"\" --native-dir \""+native+"\" --result-file \""+result+"\"",0);
        var success=JsonFile.Read<LaunchResult>(result);Check(success.Success&&success.Stage=="ready","Complete unattended preparation succeeds with a player-owned ROM and a prepared runtime");
        Check(Disk.Hash(payload)==Disk.Hash(Path.Combine(fixture,"hollow_knight_Data","Managed","Mods","HollowMario","HollowMario.dll")),"The automatic path installs its actual package payload");
        Check(Disk.Hash(Path.Combine(native,"sm64.dll"))==Disk.Hash(Path.Combine(fixture,"hollow_knight_Data","Managed","Mods","HollowMario","MarioHollow-native","sm64.dll")),"The automatic path copies Melty's prepared runtime instead of invoking a compiler");
        Check(File.ReadAllText(Path.Combine(fixture,"hollow_knight.exe"))=="fixture host; never run","The no-launch preparation check never starts or changes a game executable");
    }
}
