using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HollowMario.Setup
{
    // Removal is independent of the downloaded payload/version. Never require
    // a ROM or a complete install package to remove an older installation.
    public sealed class Uninstaller
    {
        private readonly string game,data,legacy;
        private readonly List<string> trees=new List<string>(),files=new List<string>();
        private readonly List<string> legacyProfiles=new List<string>();
        internal IEnumerable<string> LegacyProfiles => legacyProfiles;
        private bool clearHandoff;
        public int LocationsFound => trees.Count+files.Count+(clearHandoff?1:0);
        public int UserFilesKept { get; private set; }
        public Uninstaller(string gameDirectory,string dataDirectory=null,string legacyDirectory=null)
        {
            game=Canonical(gameDirectory);
            if(!Directory.Exists(game)||!File.Exists(Path.Combine(game,"hollow_knight.exe")))
                throw new InvalidDataException("Choose your original Hollow Knight installation to remove Hollow Mario.");
            if(File.Exists(Path.Combine(game,".hollowmario-profile.json")))
                throw new InvalidDataException("Choose your original Hollow Knight installation, rather than Hollow Mario's separate profile.");
            data=Canonical(dataDirectory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HollowMario"));
            legacy=Canonical(legacyDirectory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MarioHollow"));
            Regular(game);
        }
        private static string Canonical(string path)
        {
            string full=Path.GetFullPath(path);
            return full.Length==Path.GetPathRoot(full).Length?full:full.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        }
        private static bool Same(string a,string b)=>string.Equals(Canonical(a),Canonical(b),StringComparison.OrdinalIgnoreCase);
        private static bool Inside(string path,string parent)=>Canonical(path).StartsWith(Canonical(parent)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        private static void Regular(string path)
        {
            for(string at=Path.GetFullPath(path);!string.IsNullOrEmpty(at);at=Path.GetDirectoryName(at))
                if((File.Exists(at)||Directory.Exists(at))&&(File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException("Removal stopped at a linked folder or file. Its target was left alone; choose a regular installation folder.");
        }
        private static void InspectTree(string root)
        {
            Regular(root);
            foreach(string file in Directory.GetFiles(root))Regular(file);
            foreach(string folder in Directory.GetDirectories(root))InspectTree(folder);
        }
        private static bool OwnedPackage(string path)=>new[]{"HollowMario.dll","MarioHollowMovingFloor.dll","HollowMario.ApiRuntime.dll",
            "settings/gg.melty.hollowmario.cfg","settings/gg.melty.mariohollow.moving-floor.cfg"}.Any(p=>File.Exists(Disk.Under(path,p)));
        private void Tree(string path)
        {
            if(!Directory.Exists(path))return;
            if(Same(path,game)||Inside(game,path))throw new InvalidDataException("Removal would include the original game. Nothing was removed.");
            InspectTree(path);
            trees.Add(Path.GetFullPath(path));
        }
        private void FileAt(string root,string relative)
        {
            string path=Disk.Under(root,relative);
            if(File.Exists(path)){Regular(path);files.Add(path);}
        }
        private void Packages(string root,bool keepCurrentApi=false)
        {
            string mods=Disk.Under(root,"hollow_knight_Data/Managed/Mods");
            foreach(string parent in new[]{mods,Disk.Under(mods,"Disabled")})
            {
                Regular(parent);
                if(!Directory.Exists(parent))continue;
                foreach(string folder in Directory.GetDirectories(parent))
                {
                    string name=Path.GetFileName(folder);
                    if(name!="HollowMario"&&name!="FloorFix"&&!name.StartsWith("HollowMario-previous-",StringComparison.OrdinalIgnoreCase))continue;
                    if(keepCurrentApi&&Same(parent,mods)&&name=="HollowMario")continue;
                    Regular(folder);
                    if(OwnedPackage(folder))Tree(folder);
                }
            }
            string plugins=Disk.Under(root,"BepInEx/plugins");
            bool injection=false;
            foreach(string name in new[]{"HollowMario","FloorFix"})
            {
                string folder=Disk.Under(plugins,name);
                if(Directory.Exists(folder)&&OwnedPackage(folder)){Tree(folder);injection=true;}
            }
            foreach(string name in new[]{"HollowMario.dll","MarioHollowMovingFloor.dll"})
            {
                if(File.Exists(Disk.Under(plugins,name)))injection=true;
                FileAt(plugins,name);
            }
            foreach(string name in new[]{"HollowMarioEngine","MarioHollow"})
            {
                string folder=Disk.Under(plugins,name);
                if((injection||name=="HollowMarioEngine")&&
                    (File.Exists(Disk.Under(folder,"MarioHollow.dll"))||name=="HollowMarioEngine"&&File.Exists(Disk.Under(folder,"sm64.dll"))))Tree(folder);
            }
            if(injection)FileAt(plugins,"MarioHollow.dll"); // Shared loader stays in place.
            foreach(string name in new[]{"gg.melty.hollowmario.cfg","gg.melty.mariohollow.cfg","gg.melty.mariohollow.moving-floor.cfg","gg.hollowmario.independenthost.cfg"})
                FileAt(root,"BepInEx/config/"+name);
        }
        private static bool OtherMods(string root)
        {
            foreach(string relative in new[]{"BepInEx/plugins","BepInEx/patchers","hollow_knight_Data/Managed/Mods"})
            {
                string path=Disk.Under(root,relative);
                if(!Directory.Exists(path))continue;
                foreach(string folder in Directory.GetDirectories(path))
                {
                    string name=Path.GetFileName(folder);
                    bool own=(name=="HollowMario"||name=="FloorFix")&&OwnedPackage(folder)
                        ||(name=="HollowMarioEngine"||name=="MarioHollow")&&File.Exists(Disk.Under(folder,"MarioHollow.dll"));
                    if(!own&&Directory.EnumerateFileSystemEntries(folder).Any())return true;
                }
                foreach(string file in Directory.GetFiles(path,"*.dll"))
                    if(!new[]{"HollowMario.dll","MarioHollowMovingFloor.dll","MarioHollow.dll"}.Contains(Path.GetFileName(file),StringComparer.OrdinalIgnoreCase))return true;
            }
            return false;
        }
        public void Inspect() => Inspect(false);
        // Upgrade cleanup keeps the active API package and all current shared
        // preferences/cache/ROM handoff. Only obsolete installs are selected.
        public void InspectUpgrade() => Inspect(true);
        private void Inspect(bool upgrading)
        {
            trees.Clear();files.Clear();legacyProfiles.Clear();clearHandoff=false;UserFilesKept=0;
            Packages(game,keepCurrentApi:upgrading);
            string profiles=Disk.Under(Path.GetDirectoryName(game),"HollowMario-Profiles");
            Regular(profiles);
            if(Directory.Exists(profiles))
                foreach(string identity in Directory.GetDirectories(profiles))
                {
                    if(!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(identity),"^[0-9a-fA-F]{12}$"))continue;
                    string profile=Disk.Under(identity,"Standalone"),receipt=Disk.Under(profile,".hollowmario-profile.json");
                    if(!File.Exists(receipt))continue;
                    Regular(receipt);
                    ProfileReceipt saved=JsonFile.Read<ProfileReceipt>(receipt);
                    if(!string.IsNullOrWhiteSpace(saved.SourceGame)&&Same(saved.SourceGame,game))
                    {
                        InspectTree(profile);
                        legacyProfiles.Add(profile);
                        if(OtherMods(profile))
                        {
                            // A player may have added other mods to the private
                            // profile. Keep their game/loader as well as their files.
                            Packages(profile);FileAt(profile,".hollowmario-profile.json");
                        }
                        else Tree(profile);
                    }
                }
            if(upgrading)return;
            // Settings/cache are shared by this product. Do not clear a remembered
            // setup which belongs to a different selected game installation.
            string preferences=Disk.Under(data,"settings.json");
            Preferences previous=Preferences.Load(preferences);
            if(string.IsNullOrWhiteSpace(previous.GameDirectory)||Same(previous.GameDirectory,game))
            {
                if(!string.Equals(Path.GetFileName(data),"HollowMario",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unexpected Hollow Mario settings folder.");
                Tree(data);
                string handoff=Disk.Under(legacy,"melty.properties");
                if(File.Exists(handoff))
                {
                    Regular(handoff);
                    clearHandoff=File.ReadAllLines(handoff).Any(IsRomHandoff);
                }
            }
        }
        private static bool IsRomHandoff(string line)=>line.StartsWith("romdir=",StringComparison.Ordinal);
        private static bool PlayerFile(string path)
        {
            string name=Path.GetFileName(path),ext=Path.GetExtension(path);
            return ext.Equals(".z64",StringComparison.OrdinalIgnoreCase)||ext.Equals(".v64",StringComparison.OrdinalIgnoreCase)
                ||ext.Equals(".n64",StringComparison.OrdinalIgnoreCase)
                ||System.Text.RegularExpressions.Regex.IsMatch(name,@"^user\d+\.dat(?:\..*)?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                ||RomResolver.Valid(path); // The ROM chooser also accepts extensionless/renamed copies.
        }
        private void RemoveTree(string path)
        {
            Regular(path);
            foreach(string file in Directory.GetFiles(path))
            {
                Regular(file);
                if(PlayerFile(file)){UserFilesKept++;continue;}
                File.Delete(file); // Removing a profile hardlink never writes to the original asset.
            }
            foreach(string child in Directory.GetDirectories(path))RemoveTree(child);
            if(!Directory.EnumerateFileSystemEntries(path).Any())Directory.Delete(path);
        }
        public void Remove(Action requireGameClosed,Action<string> progress=null)
        {
            if(requireGameClosed==null)throw new ArgumentNullException(nameof(requireGameClosed));
            requireGameClosed();
            // Repeat complete path/link checks before the FIRST removal, so a
            // changed/linked install never causes a partially traversed delete.
            foreach(string folder in trees)if(Directory.Exists(folder))InspectTree(folder);
            foreach(string file in files)Regular(file);
            if(clearHandoff)Regular(Disk.Under(legacy,"melty.properties"));
            requireGameClosed();
            foreach(string folder in trees)
            {requireGameClosed();progress?.Invoke("Removing Hollow Mario files…");if(Directory.Exists(folder))RemoveTree(folder);}
            foreach(string file in files){requireGameClosed();if(File.Exists(file))File.Delete(file);}
            if(clearHandoff)
            {
                requireGameClosed();string path=Disk.Under(legacy,"melty.properties");
                if(File.Exists(path))
                {
                    var remaining=File.ReadAllLines(path).Where(x=>!IsRomHandoff(x)).ToArray();
                    if(remaining.Length==0)File.Delete(path);
                    else Disk.AtomicWrite(path,new UTF8Encoding(false).GetBytes(string.Join(Environment.NewLine,remaining)+Environment.NewLine));
                }
            }
        }
    }
}
