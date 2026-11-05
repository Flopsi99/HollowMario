using System;
using System.Collections.Generic;
using System.IO;

namespace HollowMario.Setup
{
    // Targeted legacy engine cleanup for installer upgrades. Only these owned
    // engine locations may be retired; no assembly inspection or wildcard DLLs.
    public static class RetiredBridge
    {
        public static int RemoveGame(string game)
        {
            var files=new List<string>();
            foreach(string relative in new[]{"BepInEx/plugins/HollowMario/MarioHollow-native","BepInEx/plugins/HollowMarioEngine",
                "hollow_knight_Data/Managed/Mods/HollowMario/MarioHollow-native","hollow_knight_Data/Managed/Mods/FloorFix/MarioHollow-native"})
                Collect(game,relative,false,files);
            return Remove(files);
        }
        public static int RemovePackage(string package,bool nested=true)
        {
            var files=new List<string>();Collect(package,nested?"MarioHollow-native":".",true,files);return Remove(files);
        }
        private static string Under(string root,string relative)
        {
            root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            string path=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
            if(path!=root&&!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Retired engine path leaves its installation.");
            for(string cursor=path;cursor!=null;cursor=Path.GetDirectoryName(cursor))
            {
                if((Directory.Exists(cursor)||File.Exists(cursor))&&(File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException("An old Hollow Mario engine location is linked. Its target was left alone.");
                if(string.Equals(cursor,root,StringComparison.OrdinalIgnoreCase))break;
            }
            return path;
        }
        private static void Collect(string root,string relative,bool removeNative,List<string> files)
        {
            Under(root,relative);string bridge=Under(root,Path.Combine(relative,"MarioHollow.dll"));
            if(!File.Exists(bridge))return;
            foreach(string name in removeNative?new[]{"MarioHollow.dll","MarioHollow.pdb","sm64.dll"}:new[]{"MarioHollow.dll","MarioHollow.pdb"})
            {
                string file=Under(root,Path.Combine(relative,name));if(File.Exists(file))files.Add(file);
            }
        }
        private static int Remove(List<string> files)
        {
            // All affected paths are checked before the first deletion. Melty
            // prepares sm64.dll before launch; RemoveGame deliberately keeps it.
            foreach(string file in files)File.Delete(file);
            return files.Count;
        }
    }
}
