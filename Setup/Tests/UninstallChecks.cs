using System;
using System.Diagnostics;
using System.IO;
using HollowMario.Setup;

internal static class UninstallChecks
{
    private static int checks;
    private static void Check(bool value,string text){checks++;if(!value)throw new Exception(text);}
    private static void Write(string root,string relative,string value="fixture")
    {string path=Disk.Under(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,value);}
    private static string Profile(string root,string game,string id)
    {
        string path=Disk.Under(root,"HollowMario-Profiles/"+id+"/Standalone");
        Write(path,"hollow_knight.exe");Write(path,"BepInEx/plugins/HollowMario/HollowMario.dll");
        JsonFile.Write(Disk.Under(path,".hollowmario-profile.json"),new ProfileReceipt{SourceGame=game,Version="older-version"});
        return path;
    }
    private static void Junction(string link,string target)
    {
        // Both targets are explicit fixture paths. Creation needs no admin rights;
        // cleanup deletes only the junction itself, never recurses into its target.
        var start=new ProcessStartInfo("cmd.exe","/c mklink /J \""+link+"\" \""+target+"\"")
        {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
        using(var process=Process.Start(start)){process.StandardOutput.ReadToEnd();string error=process.StandardError.ReadToEnd();process.WaitForExit();if(process.ExitCode!=0)throw new Exception(error);}
    }
    internal static int Run(string root)
    {
        root=Path.GetFullPath(root);Directory.CreateDirectory(root);
        string game=Disk.Under(root,"game"),data=Disk.Under(root,"user/HollowMario"),legacy=Disk.Under(root,"user/MarioHollow");
        string api="hollow_knight_Data/Managed/Mods/HollowMario";
        Write(game,"hollow_knight.exe","original game");Write(game,"hollow_knight_Data/Managed/Assembly-CSharp.dll","community API");
        Write(game,api+"/HollowMario.dll");Write(game,api+"/settings/test.cfg");
        Write(game,"hollow_knight_Data/Managed/Mods/OtherMod/Other.dll","other mod");
        Write(game,"hollow_knight_Data/Managed/Mods/Disabled/HollowMario/HollowMario.dll");
        Write(game,"hollow_knight_Data/Managed/Mods/Disabled/HollowMario-previous-123/MarioHollowMovingFloor.dll");
        Write(game,"hollow_knight_Data/Managed/Mods/FloorFix/MarioHollowMovingFloor.dll");
        Write(game,"BepInEx/plugins/HollowMario/HollowMario.dll");Write(game,"BepInEx/plugins/HollowMarioEngine/MarioHollow.dll");
        Write(game,"BepInEx/plugins/HollowMarioEngine/sm64.dll");Write(game,"BepInEx/plugins/OtherMod/Other.dll","other injection mod");
        Write(game,"BepInEx/core/BepInEx.dll","shared loader");Write(game,"winhttp.dll","shared hook");Write(game,"doorstop_config.ini","unchanged");
        Write(game,"BepInEx/config/gg.melty.hollowmario.cfg");Write(game,"BepInEx/config/OtherMod.cfg","other config");
        Write(data,"downloads/api.zip");Write(data,"backups/doorstop.ini");Write(data,"my-rom.z64","player ROM");
        JsonFile.Write(Disk.Under(data,"settings.json"),new Preferences{GameDirectory=game});
        Write(legacy,"melty.properties","keep=this\nromdir=somewhere\n");Write(root,"saves/user1.dat","player save");
        string profile=Profile(root,game,"aaaaaaaaaaaa"),shared=Profile(root,game,"bbbbbbbbbbbb");
        Write(profile,"user2.dat","profile save");Disk.LinkReadOnlyAsset(Disk.Under(game,"hollow_knight.exe"),Disk.Under(profile,"shared-asset.bin"));
        Write(shared,"BepInEx/plugins/OtherMod/Other.dll","other profile mod");
        string foreign=Profile(root,Disk.Under(root,"other-game"),"cccccccccccc");
        string unidentified=Disk.Under(root,"HollowMario-Profiles/dddddddddddd/Standalone");Write(unidentified,"keep.txt","unowned folder");
        var remove=new Uninstaller(game,data,legacy);remove.Inspect();Check(remove.LocationsFound>0,"Both routes detected without a payload/ROM");
        bool running=false;try{remove.Remove(()=>{throw new InvalidOperationException("game open");});}catch(InvalidOperationException){running=true;}
        Check(running&&File.Exists(Disk.Under(game,api+"/HollowMario.dll")),"Running-game guard stops before deletion");
        string linked=Disk.Under(game,api+"/linked"),external=Disk.Under(root,"external");Directory.CreateDirectory(linked);Write(external,"keep.txt","outside the owned mod");
        remove.Inspect();Directory.Delete(linked);Junction(linked,external);
        bool escaped=false;try{remove.Remove(()=>{});}catch(IOException){escaped=true;}
        Check(escaped&&File.Exists(Disk.Under(game,api+"/HollowMario.dll"))&&File.Exists(Disk.Under(external,"keep.txt")),"Changed-to-junction tree rejected before any deletion");
        Directory.Delete(linked); // Nonrecursive removal of the checked fixture junction.
        remove.Inspect();remove.Remove(()=>{});
        Check(!Directory.Exists(Disk.Under(game,api)),"Active API package and settings removed");
        Check(!Directory.Exists(Disk.Under(game,"hollow_knight_Data/Managed/Mods/Disabled/HollowMario")),"Disabled copy removed");
        Check(!Directory.Exists(Disk.Under(game,"hollow_knight_Data/Managed/Mods/Disabled/HollowMario-previous-123")),"Parked older copy removed");
        Check(!Directory.Exists(Disk.Under(game,"hollow_knight_Data/Managed/Mods/FloorFix")),"Legacy API copy removed");
        Check(!Directory.Exists(Disk.Under(game,"BepInEx/plugins/HollowMario"))&&!Directory.Exists(Disk.Under(game,"BepInEx/plugins/HollowMarioEngine")),"Injection companion and engine removed");
        Check(File.ReadAllText(Disk.Under(game,"hollow_knight.exe"))=="original game","Original hardlinked asset unchanged");
        Check(File.ReadAllText(Disk.Under(game,"hollow_knight_Data/Managed/Assembly-CSharp.dll"))=="community API","API retained");
        Check(File.Exists(Disk.Under(game,"hollow_knight_Data/Managed/Mods/OtherMod/Other.dll"))&&File.Exists(Disk.Under(game,"BepInEx/plugins/OtherMod/Other.dll")),"Other mods retained in both routes");
        Check(File.ReadAllText(Disk.Under(game,"BepInEx/core/BepInEx.dll"))=="shared loader"&&File.ReadAllText(Disk.Under(game,"doorstop_config.ini"))=="unchanged","Shared injector/loader left intact");
        Check(!File.Exists(Disk.Under(profile,"hollow_knight.exe"))&&File.Exists(Disk.Under(profile,"user2.dat")),"Owned profile removed while keeping player save");
        Check(File.Exists(Disk.Under(shared,"hollow_knight.exe"))&&File.Exists(Disk.Under(shared,"BepInEx/plugins/OtherMod/Other.dll"))&&!Directory.Exists(Disk.Under(shared,"BepInEx/plugins/HollowMario")),"Profile used by other mods is kept while removing Hollow Mario");
        Check(File.Exists(Disk.Under(foreign,".hollowmario-profile.json"))&&File.Exists(Disk.Under(unidentified,"keep.txt")),"Other-game and unmarked profiles kept");
        Check(File.ReadAllText(Disk.Under(data,"my-rom.z64"))=="player ROM"&&File.ReadAllText(Disk.Under(root,"saves/user1.dat"))=="player save","ROM and save content retained");
        Check(!Directory.Exists(Disk.Under(data,"downloads"))&&!File.Exists(Disk.Under(data,"settings.json")),"Own cached downloads/preferences removed");
        Check(File.ReadAllText(Disk.Under(legacy,"melty.properties"))=="keep=this"+Environment.NewLine,"Only own ROM handoff removed");
        remove.Inspect();remove.Remove(()=>{});Check(File.Exists(Disk.Under(data,"my-rom.z64")),"Repeated removal is harmless");
        return checks;
    }
}
