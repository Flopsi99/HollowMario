using System;
using System.Diagnostics;
using System.IO;
using HollowMario.Setup;

internal static class UpgradeChecks
{
    private static int checks;
    private static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    private static void Write(string root,string relative,string content="fixture")
    {string path=Disk.Under(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,content);}
    private static string Profile(string root,string game,string id)
    {
        string path=Disk.Under(root,"HollowMario-Profiles/"+id+"/Standalone");
        Write(path,"hollow_knight.exe");Write(path,"BepInEx/plugins/HollowMario/HollowMario.dll");
        JsonFile.Write(Disk.Under(path,".hollowmario-profile.json"),new ProfileReceipt{SourceGame=game,Version="old"});
        return path;
    }
    internal static int Run(string root,Installer installer)
    {
        root=Path.GetFullPath(root);
        string game=Disk.Under(root,"game"),data=Disk.Under(root,"user/HollowMario"),legacy=Disk.Under(root,"user/MarioHollow");
        const string api="hollow_knight_Data/Managed/Mods/HollowMario";
        const string mods="hollow_knight_Data/Managed/Mods";
        Write(game,"hollow_knight.exe","original game");Write(game,"hollow_knight_Data/Managed/Assembly-CSharp.dll","existing API");
        Write(game,api+"/HollowMario.dll","old API payload");Write(game,api+"/settings/gg.melty.hollowmario.cfg","current tuning");
        Write(game,api+"/settings/gg.hollowmario.independenthost.cfg","current volume");
        Write(game,api+"/MarioHollow-native/sm64.dll","prepared native runtime");
        Write(game,"BepInEx/plugins/HollowMario/HollowMario.dll","old replacement-host injection build");
        Write(game,"BepInEx/plugins/HollowMario/MarioHollow-native/HollowMario.Host.Integration.dll");
        Write(game,"BepInEx/plugins/FloorFix/MarioHollowMovingFloor.dll","old FloorFix injection build");
        Write(game,"BepInEx/plugins/HollowMario.dll");Write(game,"BepInEx/plugins/MarioHollowMovingFloor.dll");
        Write(game,"BepInEx/plugins/HollowMarioEngine/sm64.dll","orphan left by older cleanup");
        Write(game,"BepInEx/plugins/MarioHollow/MarioHollow.dll","old engine");
        Write(game,"BepInEx/plugins/OtherMod/Other.dll","other injection mod");
        Write(game,"BepInEx/core/BepInEx.dll","shared loader");Write(game,"winhttp.dll","shared hook");
        Write(game,"doorstop_config.ini","enabled=true\r\ntarget_assembly=BepInEx/core/BepInEx.Preloader.dll\r\n");
        Write(game,"BepInEx/config/gg.melty.hollowmario.cfg","old tuning");
        Write(game,"BepInEx/config/gg.hollowmario.independenthost.cfg","old volume");
        Write(game,"BepInEx/config/OtherMod.cfg","other config");
        Write(game,mods+"/FloorFix/MarioHollowMovingFloor.dll");
        Write(game,mods+"/FloorFix/settings/gg.melty.mariohollow.moving-floor.cfg","older API tuning");
        Write(game,mods+"/Disabled/HollowMario-previous-123/MarioHollowMovingFloor.dll");
        Write(game,mods+"/Disabled/HollowMario/HollowMario.dll");
        Write(game,mods+"/OtherMod/Other.dll","other API mod");
        Write(data,"downloads/api.zip","keep cache");Write(data,"settings.json","keep preferences");
        Write(legacy,"melty.properties","romdir=keep\n");
        string profile=Profile(root,game,"aaaaaaaaaaaa"),shared=Profile(root,game,"bbbbbbbbbbbb");
        Write(profile,"player.z64","player ROM");Write(profile,"user2.dat","player save");
        Disk.LinkReadOnlyAsset(Disk.Under(game,"hollow_knight.exe"),Disk.Under(profile,"shared-asset.bin"));
        Write(shared,"BepInEx/plugins/OtherMod/Other.dll","shared profile mod");
        string foreign=Profile(root,Disk.Under(root,"other-game"),"cccccccccccc");
        string unmarked=Disk.Under(root,"HollowMario-Profiles/dddddddddddd/Standalone");Write(unmarked,"keep.txt");
        bool stopped=false;
        try{installer.PrepareCommunity(game,requireGameClosed:()=>{throw new InvalidOperationException("game running");});}catch(InvalidOperationException){stopped=true;}
        Check(stopped&&File.ReadAllText(Disk.Under(game,api+"/HollowMario.dll"))=="old API payload"&&File.Exists(Disk.Under(game,"BepInEx/plugins/HollowMario/HollowMario.dll")),"Running game prevents both writes and cleanup.");
        // Exercise the same preflight as an install, with a linked old package.
        string linked=Disk.Under(game,"BepInEx/plugins/HollowMario/linked"),external=Disk.Under(root,"external");
        Write(external,"keep.txt","outside owned install");
        var start=new ProcessStartInfo("cmd.exe","/c mklink /J \""+linked+"\" \""+external+"\"")
        {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
        using(var process=Process.Start(start)){process.StandardOutput.ReadToEnd();string error=process.StandardError.ReadToEnd();process.WaitForExit();if(process.ExitCode!=0)throw new Exception(error);}
        stopped=false;
        try{installer.PrepareCommunity(game);}catch(IOException){stopped=true;}
        Check(stopped&&File.ReadAllText(Disk.Under(game,api+"/HollowMario.dll"))=="old API payload"&&File.Exists(Disk.Under(external,"keep.txt")),"Linked cleanup target rejected before changing the active install.");
        Directory.Delete(linked); // Only the explicit fixture junction, never its target.
        installer.PrepareCommunity(game,backupDirectory:Disk.Under(root,"backups"));
        Check(File.ReadAllText(Disk.Under(game,api+"/HollowMario.dll"))=="updated API version","New API version installed.");
        Check(!Directory.Exists(Disk.Under(game,"BepInEx/plugins/HollowMario"))&&!Directory.Exists(Disk.Under(game,"BepInEx/plugins/FloorFix")),"Both known injection packages deleted.");
        Check(!File.Exists(Disk.Under(game,"BepInEx/plugins/HollowMario.dll"))&&!File.Exists(Disk.Under(game,"BepInEx/plugins/MarioHollowMovingFloor.dll")),"Loose injection builds deleted.");
        Check(!Directory.Exists(Disk.Under(game,"BepInEx/plugins/HollowMarioEngine"))&&!Directory.Exists(Disk.Under(game,"BepInEx/plugins/MarioHollow")),"Old engine folders and orphaned runtime deleted.");
        Check(!Directory.Exists(Disk.Under(game,mods+"/FloorFix"))&&!Directory.Exists(Disk.Under(game,mods+"/Disabled/HollowMario-previous-123"))&&!Directory.Exists(Disk.Under(game,mods+"/Disabled/HollowMario")),"Old and previously parked API builds deleted.");
        Check(File.ReadAllText(Disk.Under(game,api+"/settings/gg.melty.hollowmario.cfg"))=="current tuning"&&File.ReadAllText(Disk.Under(game,api+"/settings/gg.hollowmario.independenthost.cfg"))=="current volume","Current tuning and audio settings preserved.");
        Check(File.ReadAllText(Disk.Under(game,api+"/MarioHollow-native/sm64.dll"))=="prepared native runtime","Active prepared runtime preserved.");
        Check(!File.Exists(Disk.Under(profile,"hollow_knight.exe"))&&File.ReadAllText(Disk.Under(profile,"player.z64"))=="player ROM"&&File.ReadAllText(Disk.Under(profile,"user2.dat"))=="player save","Old private profile removed while ROM and save survive.");
        Check(File.ReadAllText(Disk.Under(game,"hollow_knight.exe"))=="original game"&&File.ReadAllText(Disk.Under(game,"hollow_knight_Data/Managed/Assembly-CSharp.dll"))=="existing API","Original game and API assembly preserved, including hardlinked assets.");
        Check(!Directory.Exists(Disk.Under(shared,"BepInEx/plugins/HollowMario"))&&File.ReadAllText(Disk.Under(shared,"BepInEx/plugins/OtherMod/Other.dll"))=="shared profile mod"&&File.Exists(Disk.Under(shared,"hollow_knight.exe")),"Shared profile keeps other mod and its game while deleting Hollow Mario.");
        Check(File.Exists(Disk.Under(foreign,".hollowmario-profile.json"))&&File.Exists(Disk.Under(unmarked,"keep.txt")),"Other-game and unmarked folders untouched.");
        Check(File.ReadAllText(Disk.Under(game,"BepInEx/plugins/OtherMod/Other.dll"))=="other injection mod"&&File.ReadAllText(Disk.Under(game,mods+"/OtherMod/Other.dll"))=="other API mod"&&File.ReadAllText(Disk.Under(game,"BepInEx/config/OtherMod.cfg"))=="other config","Unrelated mods and configuration preserved.");
        Check(File.Exists(Disk.Under(game,"BepInEx/core/BepInEx.dll"))&&File.Exists(Disk.Under(game,"winhttp.dll"))&&File.ReadAllText(Disk.Under(game,"doorstop_config.ini")).Contains("enabled=false"),"Shared loader files kept and known competing injector disabled.");
        var cleanup=new Uninstaller(game,data,legacy);cleanup.InspectUpgrade();cleanup.Remove(()=>{});
        Check(File.ReadAllText(Disk.Under(data,"downloads/api.zip"))=="keep cache"&&File.ReadAllText(Disk.Under(data,"settings.json"))=="keep preferences"&&File.ReadAllText(Disk.Under(legacy,"melty.properties"))=="romdir=keep\n","Upgrade cleanup preserves shared cache, preferences and ROM handoff.");
        Write(game,"BepInEx/plugins/HollowMario/HollowMario.dll","reintroduced old build");
        installer.PrepareCommunity(game,backupDirectory:Disk.Under(root,"backups"));
        Check(!Directory.Exists(Disk.Under(game,"BepInEx/plugins/HollowMario"))&&File.Exists(Disk.Under(game,api+"/HollowMario.dll")),"Every subsequent update cleans reintroduced old injection builds.");
        string fresh=Disk.Under(root,"fresh");Write(fresh,"hollow_knight.exe");
        string oldProfile=Profile(root,fresh,"eeeeeeeeeeee");
        Write(oldProfile,"BepInEx/config/gg.melty.hollowmario.cfg","migrated private tuning");
        Write(oldProfile,"BepInEx/config/gg.hollowmario.independenthost.cfg","migrated private volume");
        installer.PrepareCommunity(fresh);
        Check(File.ReadAllText(Disk.Under(fresh,api+"/settings/gg.melty.hollowmario.cfg"))=="migrated private tuning"&&File.ReadAllText(Disk.Under(fresh,api+"/settings/gg.hollowmario.independenthost.cfg"))=="migrated private volume"&&!Directory.Exists(oldProfile),"All receipted profile settings migrate before deletion, including the host's audio configuration.");
        Write(fresh,"BepInEx/config/gg.hollowmario.independenthost.cfg","obsolete volume");installer.PrepareCommunity(fresh);
        Check(!File.Exists(Disk.Under(fresh,"BepInEx/config/gg.hollowmario.independenthost.cfg"))&&File.ReadAllText(Disk.Under(fresh,api+"/settings/gg.hollowmario.independenthost.cfg"))=="migrated private volume","Later updates clear obsolete host settings without overwriting current volume.");
        return checks;
    }
}
