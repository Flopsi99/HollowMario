using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace HollowMario.Setup
{
    public static class Disk
    {
        [DllImport("kernel32.dll", EntryPoint="MoveFileExW", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MoveFileEx(string source, string target, uint flags);
        [DllImport("kernel32.dll", EntryPoint="CreateHardLinkW", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLink(string target, string source, IntPtr security);
        public static string Under(string root, string relative)
        {
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0)
                throw new InvalidDataException("Unexpected package path.");
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if(Directory.Exists(root) && (File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("The installation folder is linked; choose a regular folder.");
            string result = Path.GetFullPath(Path.Combine(prefix, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package path leaves its folder.");
            string cursor = Path.GetDirectoryName(result);
            while (cursor != null && cursor.Length >= prefix.Length)
            {
                if (Directory.Exists(cursor) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The installation contains a linked folder; choose a regular folder.");
                cursor = Path.GetDirectoryName(cursor);
            }
            return result;
        }
        public static string Hash(string file)
        {
            using (var input = File.OpenRead(file)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
        public static void AtomicWrite(string path, byte[] contents)
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, contents);
            if (!MoveFileEx(temporary, path, 0x1 | 0x8))
            {
                int error = Marshal.GetLastWin32Error();
                File.Delete(temporary);
                throw new Win32Exception(error, "The completed setup file could not be saved.");
            }
        }
        public static void Copy(string source, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (File.Exists(target) && Hash(source) == Hash(target)) return;
            AtomicWrite(target, File.ReadAllBytes(source));
        }
        // Game assets are linked locally only; they never enter our release.
        // Mutable Managed assemblies always use Copy, never this function.
        public static void LinkReadOnlyAsset(string source, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (File.Exists(target))
            {
                if (new FileInfo(source).Length == new FileInfo(target).Length && File.GetLastWriteTimeUtc(source) == File.GetLastWriteTimeUtc(target)) return;
                File.Delete(target); // target is a checked private-profile path.
            }
            if (!CreateHardLink(target, source, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "A local game profile could not share the installed game files. Use the community-mod option on this drive.");
        }
    }
}
