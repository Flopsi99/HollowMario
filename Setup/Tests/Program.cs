using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HollowMario.Setup;

internal static class Program
{
    private static int count;
    private static void Check(bool result,string why){count++;if(!result)throw new Exception(why);}
    private static void Write(string root,string file,string content){string path=Disk.Under(root,file);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,content);}
    private static int Main(string[] args)
    {
        try
        {
            string scratch=Path.GetFullPath(args[0]);Directory.CreateDirectory(scratch);
            string package=Path.Combine(scratch,"package"), game=Path.Combine(scratch,"game"), profile=Path.Combine(scratch,"profile");
            Write(package,"payloads/api/HollowMario.dll","first API version");
            var manifest=new PackageManifest{Version="0.10.0",Files=Directory.GetFiles(package,"*",SearchOption.AllDirectories).Select(x=>new PackageFile{Path=x.Substring(package.Length+1).Replace('\\','/'),Sha256=Disk.Hash(x)}).ToArray()};
            JsonFile.Write(Path.Combine(package,"package.json"),manifest);
            string json=File.ReadAllText(Path.Combine(package,"package.json"));
            File.WriteAllText(Path.Combine(package,"package.json"),json,System.Text.Encoding.UTF8);
            Check(JsonFile.Read<PackageManifest>(Path.Combine(package,"package.json")).Version=="0.10.0","BOM-bearing release manifest reads on .NET Framework.");
            foreach(string invalid in new[]{"../outside","/absolute","C:\\outside","nested/../../escape"})
            {bool rejected=false;try{Disk.Under(package,invalid);}catch(InvalidDataException){rejected=true;}Check(rejected,"Reject path escape "+invalid);}
            var installer=new Installer(package);installer.Verify();
            var choice = new Preferences();
            string choiceFile = Path.Combine(scratch,"loading-choice.json");
            Check(!choice.HasLoadingChoiceFor(game),"A first launch does not assume an existing loading choice.");
            choice.GameDirectory=game;choice.LastMode=InstallMode.CommunityApi;
            Check(!choice.HasLoadingChoiceFor(game),"Remembering a ROM or radio button is not a successful installation.");
            choice.ConfirmLoadingChoice(game,InstallMode.CommunityApi,choiceFile);
            choice=Preferences.Load(choiceFile);
            Check(choice.HasLoadingChoiceFor(game),"A successful loading choice persists for this game.");
            Check(choice.HasLoadingChoiceFor(game+Path.DirectorySeparatorChar),"Equivalent game directory spelling keeps the choice.");
            Check(!choice.HasLoadingChoiceFor(Path.Combine(scratch,"another-game")),"A choice never silently carries over to another game installation.");
            Check(choice.LastMode==InstallMode.CommunityApi,"Remembered community loading route is preserved.");
            Write(package,"payloads/api/HollowMario.dll","tampered");bool integrity=false;
            try{installer.Verify();}catch(InvalidDataException){integrity=true;}Check(integrity,"Damaged package rejected before install.");
            Write(package,"payloads/api/HollowMario.dll","first API version");
            Write(game,"hollow_knight.exe","fixture executable; never run");
            Write(game,"UnityPlayer.dll","fixture runtime");
            Write(game,"hollow_knight_Data/room.assets","read-only game asset");
            Write(game,"hollow_knight_Data/Managed/Assembly-CSharp.dll","player API assembly");
            Write(game,"hollow_knight_Data/Managed/Assembly-CSharp.dll.v","player vanilla backup");
            Write(game,"hollow_knight_Data/Managed/UnityEngine.CoreModule.dll","original mutable assembly");
            Write(game,"hollow_knight_Data/Managed/MonoMod.Utils.dll","API library that must not compete");
            Write(game,"hollow_knight_Data/Managed/Mods/OtherMod/Other.dll","unrelated mod");
            Write(game,"MonoBleedingEdge/EmbedRuntime/mono.dll","fixture runtime asset");
            string vanilla=Path.Combine(game,"hollow_knight_Data/Managed/Assembly-CSharp.dll.v");
            bool injectionRejected=false;
            try{installer.ApplyPayload("injection",profile);}catch(InvalidDataException){injectionRejected=true;}
            Check(injectionRejected&&!Directory.Exists(profile),"Injection payloads cannot be installed.");
            choice.LastMode=InstallMode.LegacyStandalone;choice.Save(choiceFile);
            Check(Preferences.Load(choiceFile).LastMode==InstallMode.LegacyStandalone,"Historical numeric preference remains readable.");
            string old="hollow_knight_Data/Managed/Mods/FloorFix";
            Write(game,old+"/MarioHollowMovingFloor.dll","known previous companion");
            Write(game,old+"/MarioHollow-native/MarioHollow.dll","retired engine fixture");
            Write(game,old+"/MarioHollow-native/sm64.dll","retired bundled native fixture");
            Write(game,old+"/settings/gg.melty.mariohollow.moving-floor.cfg","[Charms]\nQuickSlashAirRecoverySeconds = 0.31\n");
            Write(game,"doorstop_config.ini","[General]\r\nenabled = TRUE\r\ntarget_assembly=BepInEx/core/BepInEx.Preloader.dll\r\n");
            installer.PrepareCommunity(game,Path.Combine(scratch,"backups"));
            Check(!Directory.Exists(Path.Combine(game,old)),"Old named API package deleted after migration.");
            Check(!Directory.Exists(Path.Combine(game,"hollow_knight_Data/Managed/Mods/Disabled/HollowMario")),"No old API copy is retained.");
            Check(File.ReadAllText(Path.Combine(game,"hollow_knight_Data/Managed/Mods/HollowMario/settings/gg.melty.hollowmario.cfg")).Contains("0.31"),"Old charm tuning migrated.");
            Check(File.ReadAllText(Path.Combine(game,"hollow_knight_Data/Managed/Mods/OtherMod/Other.dll"))=="unrelated mod","Unrelated community mod retained.");
            Check(File.ReadAllText(Path.Combine(game,"doorstop_config.ini")).Contains("enabled = false"),"Original injector disabled for API mode.");
            installer.PrepareCommunity(game,Path.Combine(scratch,"backups"));
            Check(!Directory.Exists(Path.Combine(game,old)),"Repeat setup is idempotent.");
            // Apply a genuinely newer API release over a retired bridge bundle.
            Write(package,"payloads/api/HollowMario.dll","updated API version");
            manifest.Version="0.10.78";
            manifest.Files=manifest.Files.Select(x=>new PackageFile{Path=x.Path,Sha256=Disk.Hash(Disk.Under(package,x.Path))}).ToArray();
            JsonFile.Write(Path.Combine(package,"package.json"),manifest);
            var update=new Installer(package);update.Verify();
            Write(game,"hollow_knight_Data/Managed/Mods/HollowMario/MarioHollow-native/MarioHollow.dll","retired API fixture");
            Write(game,"hollow_knight_Data/Managed/Mods/HollowMario/MarioHollow-native/sm64.dll","retired bundled native fixture");
            update.PrepareCommunity(game,Path.Combine(scratch,"backups"));
            Check(!File.Exists(Path.Combine(game,"hollow_knight_Data/Managed/Mods/HollowMario/MarioHollow-native/MarioHollow.dll"))&&!File.Exists(Path.Combine(game,"hollow_knight_Data/Managed/Mods/HollowMario/MarioHollow-native/sm64.dll")),"API upgrade removes retired bridge and bundled runtime.");
            Check(File.ReadAllText(Path.Combine(game,"hollow_knight_Data/Managed/Mods/HollowMario/HollowMario.dll"))=="updated API version","Newer installer updates the existing API mod.");
            Check(File.ReadAllText(Path.Combine(game,"hollow_knight_Data/Managed/Mods/HollowMario/settings/gg.melty.hollowmario.cfg")).Contains("0.31"),"Real version update preserves API charm tuning.");
            Check(File.ReadAllText(Path.Combine(game,"hollow_knight_Data/Managed/Mods/OtherMod/Other.dll"))=="unrelated mod","Switching back to API preserves other community mods.");
            if(args.Length>2)
            {
                string rom=args[2];Check(RomResolver.Valid(rom),"Player-supplied ROM validated read-only.");
                var preferences=new Preferences{LastMode=InstallMode.CommunityApi};
                string pref=Path.Combine(scratch,"user/settings.json"), handoff=Path.Combine(scratch,"user/melty.properties");
                Write(scratch,"user/melty.properties","keep=this\nromdir=old\n");
                RomResolver.Remember(rom,preferences,pref,handoff);
                Check(Preferences.Load(pref).RomPath==Path.GetFullPath(rom),"ROM location persists.");
                Check(File.ReadAllText(handoff).Contains("keep=this")&&!File.ReadAllText(handoff).Contains("romdir=old"),"Shared engine handoff preserves other settings.");
                byte[] data=File.ReadAllBytes(rom), swapped=(byte[])data.Clone();
                for(int i=0;i<swapped.Length;i+=2){byte b=swapped[i];swapped[i]=swapped[i+1];swapped[i+1]=b;}
                Check(RomResolver.Normalize(swapped).SequenceEqual(data),"Byte-swapped ROM format normalized.");
            }
            Check(!RomResolver.Valid(vanilla),"Non-ROM game file rejected.");
            string native=Path.Combine(scratch,"prepared-runtime");
            Write(native,"sm64.dll","platform-prepared runtime");
            Write(game,"BepInEx/plugins/HollowMarioEngine/MarioHollow.dll","retired injection engine fixture");
            Write(game,"BepInEx/plugins/HollowMarioEngine/player.z64","player-owned ROM");
            Write(game,"hollow_knight_Data/Managed/Mods/HollowMario/MarioHollow-native/MarioHollow.dll","retired engine fixture");
            update.PrepareCommunity(game,Path.Combine(scratch,"backups"),preparedNative:native);
            Check(File.ReadAllText(Path.Combine(game,"hollow_knight_Data/Managed/Mods/HollowMario/MarioHollow-native/sm64.dll"))=="platform-prepared runtime","Prepared runtime survives retiring the old bridge.");
            Check(!File.Exists(Path.Combine(game,"BepInEx/plugins/HollowMarioEngine/MarioHollow.dll"))&&File.Exists(Path.Combine(game,"BepInEx/plugins/HollowMarioEngine/player.z64")),"Retired injection bridge removed while player's ROM is kept.");
            string fresh=Path.Combine(scratch,"fresh-game");
            Write(fresh,"hollow_knight.exe","fixture executable; never run");
            Write(fresh,"BepInEx/config/gg.melty.hollowmario.cfg","custom injection settings");
            update.PrepareCommunity(fresh);
            Check(File.ReadAllText(Path.Combine(fresh,"hollow_knight_Data/Managed/Mods/HollowMario/settings/gg.melty.hollowmario.cfg"))=="custom injection settings","Historical injection settings migrate read-only.");
            string identity;
            using(var sha=System.Security.Cryptography.SHA256.Create())
                identity=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(fresh.ToLowerInvariant()))).Replace("-","").Substring(0,12);
            string previousProfile=Path.Combine(scratch,"HollowMario-Profiles",identity,"Standalone");
            JsonFile.Write(Path.Combine(previousProfile,".hollowmario-profile.json"),new ProfileReceipt{SourceGame=fresh,Version="0.10.115"});
            Write(previousProfile,"BepInEx/config/gg.melty.hollowmario.cfg","newer private-profile settings");
            File.Delete(Path.Combine(fresh,"hollow_knight_Data/Managed/Mods/HollowMario/settings/gg.melty.hollowmario.cfg"));
            update.PrepareCommunity(fresh);
            Check(File.ReadAllText(Path.Combine(fresh,"hollow_knight_Data/Managed/Mods/HollowMario/settings/gg.melty.hollowmario.cfg"))=="newer private-profile settings","Receipted private-profile settings migrate before older original-game settings.");
            Check(!Directory.Exists(previousProfile),"Obsolete private profile removed after settings migration.");
            Write(fresh,"doorstop_config.ini","enabled=true\r\ntarget_assembly=OtherMod/Loader.dll\r\n");
            bool refused=false;try{update.PrepareCommunity(fresh);}catch(InvalidOperationException){refused=true;}
            Check(refused&&File.ReadAllText(Path.Combine(fresh,"doorstop_config.ini")).Contains("enabled=true"),"Unknown injector rejected and left alone.");
            Write(profile,".hollowmario-profile.json","{}");
            refused=false;try{Installer.CheckCommunity(profile);}catch(InvalidOperationException){refused=true;}
            Check(refused,"Old private profile cannot become a new API installation.");
            if(args.Length>1)CheckApiInstall(Path.Combine(scratch,"fresh-api-install"),args[1]);
            count+=UninstallChecks.Run(Path.Combine(scratch,"removal"));
            count+=UpgradeChecks.Run(Path.Combine(scratch,"upgrade"),update);
            Console.WriteLine("PASS: "+count+" API-only setup checks: package integrity, update/migration, ROM handoff, legacy removal, linked-path rejection and preservation of saves/ROMs/other mods. No game or installer UI was started.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    private static void CheckApiInstall(string fixture,string installedGame)
    {
        // The player's vanilla backup is copied into a disposable fixture.
        // No actual game file is written and the fixture executable is never run.
        var type=System.Reflection.Assembly.Load("HollowMario-Setup").GetType("HollowMario.Launcher.GameInstall",true);
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
        Write(fixture,"hollow_knight.exe","fixture executable; never run");
        string active=Disk.Under(fixture,"hollow_knight_Data/Managed/Assembly-CSharp.dll");
        Disk.Copy(Path.Combine(installedGame,"hollow_knight_Data/Managed/Assembly-CSharp.dll.v"),active);
        Check(!(bool)type.GetMethod("HasApi",flags).Invoke(null,new object[]{fixture}),"Fresh fixture starts without API.");
        string original=Disk.Hash(active);
        var arguments=new object[]{fixture,new Action<string>(_=>{})};
        type.GetMethod("EnsureApi",flags).Invoke(null,arguments);
        Check((bool)type.GetMethod("HasApi",flags).Invoke(null,new object[]{fixture}),"Supported vanilla installation receives official API automatically.");
        Check(Disk.Hash(active+".v")==original,"Original game assembly backed up.");
        string installed=Disk.Hash(active);
        type.GetMethod("EnsureApi",flags).Invoke(null,arguments);
        Check(Disk.Hash(active)==installed&&Disk.Hash(active+".v")==original,"Repeat setup preserves API and original backup.");
    }
}
