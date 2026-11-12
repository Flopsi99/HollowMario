using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HollowMario.Setup
{
    public sealed class Installer
    {
        public readonly string PackageDirectory;
        public readonly PackageManifest Manifest;
        public Installer(string package)
        {
            PackageDirectory = Path.GetFullPath(package);
            Manifest = JsonFile.Read<PackageManifest>(Disk.Under(package, "package.json"));
            if (Manifest.SchemaVersion != 1 || Manifest.Product != "Hollow Mario" || Manifest.Files == null)
                throw new InvalidDataException("This is not a supported Hollow Mario setup package.");
        }
        public void Verify()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Manifest.Files)
                if (!names.Add(file.Path) || !File.Exists(Disk.Under(PackageDirectory, file.Path)) ||
                    (!Disk.Hash(Disk.Under(PackageDirectory, file.Path)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)&&
                     (string.IsNullOrEmpty(file.SigningSha256)||!string.Equals(Disk.SigningHash(Disk.Under(PackageDirectory,file.Path)),file.SigningSha256,StringComparison.OrdinalIgnoreCase))))
                    throw new InvalidDataException("The Hollow Mario download is incomplete or damaged. Extract the full download and try again.");
        }
        public void ApplyPayload(string variant, string destination)
        {
            if (variant != "api") throw new InvalidDataException("Hollow Mario now supports the community API only.");
            string prefix = "payloads/" + variant + "/";
            var files = Manifest.Files.Where(x => x.Path.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            if (files.Length == 0) throw new InvalidDataException("The selected Hollow Mario variant is missing.");
            RetireBridge(destination);
            foreach (var file in files)
                Disk.Copy(Disk.Under(PackageDirectory, file.Path), Disk.Under(destination, file.Path.Substring(prefix.Length)));
        }
        // Only retire the engine inside our own install locations. Never scan or
        // remove another mod's shared engine, ROMs, settings or game content.
        private static void RetireBridge(string package, bool nested=true)
        {
            RetiredBridge.RemovePackage(package,nested);
        }
        public static void CheckCommunity(string game)
        {
            if (File.Exists(Path.Combine(game, ".hollowmario-profile.json")))
                throw new InvalidOperationException("Select your original Hollow Knight installation, rather than an old Hollow Mario profile.");
            string doorstop = Path.Combine(game, "doorstop_config.ini");
            string text = File.Exists(doorstop) ? File.ReadAllText(doorstop) : null;
            if (text != null && System.Text.RegularExpressions.Regex.IsMatch(text, @"(?im)^enabled[ \t]*=[ \t]*true[ \t]*\r?$") &&
                !System.Text.RegularExpressions.Regex.IsMatch(text, @"(?im)^target_assembly[ \t]*=[ \t]*BepInEx[\\/]core[\\/]BepInEx\.Preloader\.dll[ \t]*\r?$"))
                throw new InvalidOperationException("Another injector is active in this game. Disable it before installing Hollow Mario's community API version.");
        }
        public void PrepareCommunity(string game, string backupDirectory = null, Action<string> progress = null, string preparedNative = null, Action requireGameClosed = null)
        {
            requireGameClosed=requireGameClosed??RequireGameClosed;
            requireGameClosed();
            string doorstop = Path.Combine(game, "doorstop_config.ini");
            CheckCommunity(game);
            if (preparedNative != null && !File.Exists(Disk.Under(preparedNative,"sm64.dll")))
                throw new InvalidDataException("Melty has not finished preparing Mario's engine. Reinstall this release in Melty, then try again.");
            // Melty may prepare directly in the final runtime folder. Preserve
            // its bytes before retiring a legacy bridge in that same folder.
            byte[] preparedBytes=preparedNative==null?null:File.ReadAllBytes(Disk.Under(preparedNative,"sm64.dll"));
            // Validate every obsolete removal target before changing any files.
            // Keep this plan until settings migration and the new payload finish.
            var obsolete=new Uninstaller(game);
            obsolete.InspectUpgrade();
            string mods = Path.Combine(game, "hollow_knight_Data", "Managed", "Mods");
            string target = Disk.Under(mods, "HollowMario");
            Directory.CreateDirectory(target);
            // Migrate the old API settings before removing its package.
            string old = Disk.Under(mods, "FloorFix");
            if (File.Exists(Disk.Under(old, "MarioHollowMovingFloor.dll")))
            {
                string previous = Disk.Under(old, "settings/gg.melty.mariohollow.moving-floor.cfg");
                string current = Disk.Under(target, "settings/gg.melty.hollowmario.cfg");
                if (File.Exists(previous) && !File.Exists(current)) Disk.Copy(previous, current);
            }
            // Import settings before deleting our former injection installs.
            string settings = Disk.Under(target, "settings/gg.melty.hollowmario.cfg");
            if (!File.Exists(settings))
                foreach (string previous in LegacySettings(game,obsolete.LegacyProfiles))
                {
                    if (File.Exists(previous)) { Disk.Copy(previous, settings); break; }
                }
            foreach(string name in new[]{"gg.hollowmario.independenthost.cfg","gg.melty.mariohollow.cfg"})
            {
                string current=Disk.Under(target,"settings/"+name);
                if(!File.Exists(current))
                    foreach(string previous in LegacySettings(game,obsolete.LegacyProfiles,name))
                        if(File.Exists(previous)){Disk.Copy(previous,current);break;}
            }
            requireGameClosed();
            ApplyPayload("api", target);
            if (preparedNative != null)
            {
                Disk.AtomicWrite(Disk.Under(target, "MarioHollow-native/sm64.dll"),preparedBytes);
            }
            else if(Manifest.BuildMarioLocally)NativeRuntime.Ensure(PackageDirectory,Disk.Under(target,"MarioHollow-native"),progress);
            if (File.Exists(doorstop))
            {
                string text = File.ReadAllText(doorstop);
                string changed = System.Text.RegularExpressions.Regex.Replace(text, @"(?im)^(enabled[ \t]*=[ \t]*)true(?=[ \t]*\r?$)", "$1false");
                if (changed != text)
                {
                    string backup = Path.Combine(backupDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HollowMario", "backups"), "doorstop-" + Guid.NewGuid().ToString("N") + ".ini");
                    Disk.Copy(doorstop, backup);
                    Disk.AtomicWrite(doorstop, Encoding.UTF8.GetBytes(changed));
                }
            }
            obsolete.Remove(requireGameClosed,progress);
        }
        private static void RequireGameClosed()
        {
            if(System.Diagnostics.Process.GetProcessesByName("hollow_knight").Length!=0)
                throw new InvalidOperationException("Close Hollow Knight before changing the setup. Your running game will be left alone.");
        }
        private static IEnumerable<string> LegacySettings(string game,IEnumerable<string> profiles,string file=null)
        {
            // Use the same receipted, path-checked profiles as cleanup so settings
            // are migrated even if an old install used a different identity.
            string[] names=file==null?new[]{"gg.melty.hollowmario.cfg","gg.melty.mariohollow.moving-floor.cfg"}:new[]{file};
            foreach(string previous in profiles.SelectMany(profile=>names.Select(name=>Disk.Under(profile,"BepInEx/config/"+name)))
                .Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc))yield return previous;
            foreach (string name in names)
            {
                yield return Disk.Under(game,"BepInEx/config/"+name);
                yield return Disk.Under(game,"hollow_knight_Data/Managed/Mods/FloorFix/settings/"+name);
            }
        }
    }
    [System.Runtime.Serialization.DataContract] public sealed class ProfileReceipt
    {
        [System.Runtime.Serialization.DataMember] public string SourceGame;
        [System.Runtime.Serialization.DataMember] public string Version;
    }
}
