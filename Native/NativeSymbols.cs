using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HollowMario
{
    // Resolve internal functions from this DLL's own COFF function symbols. No fixed code
    // addresses, pattern guessing, native-library replacement or game assets are involved.
    internal static class NativeSymbols
    {
        internal static Dictionary<string, IntPtr> Functions(string path, IntPtr module, params string[] names)
            =>Addresses(path,module,true,names);
        internal static Dictionary<string, IntPtr> Data(string path, IntPtr module, params string[] names)
            =>Addresses(path,module,false,names);
        private static Dictionary<string, IntPtr> Addresses(string path, IntPtr module,bool functions,params string[] names)
        {
            byte[] file = File.ReadAllBytes(path);
            int pe = BitConverter.ToInt32(file, 60);
            if (BitConverter.ToUInt16(file, 0) != 0x5A4D || BitConverter.ToUInt32(file, pe) != 0x4550
                || BitConverter.ToUInt16(file, pe + 4) != 0x8664 || IntPtr.Size != 8)
                throw new InvalidDataException("Expected the installed 64-bit Mario library");
            int sections = BitConverter.ToUInt16(file, pe + 6);
            int symbolTable = checked((int)BitConverter.ToUInt32(file, pe + 12));
            int symbolCount = checked((int)BitConverter.ToUInt32(file, pe + 16));
            int sectionTable = pe + 24 + BitConverter.ToUInt16(file, pe + 20);
            int strings = checked(symbolTable + symbolCount * 18);
            if (symbolTable <= 0 || symbolCount <= 0 || strings > file.Length - 4)
                throw new InvalidDataException("Mario function symbols are missing");
            int stringEnd = checked(strings + (int)BitConverter.ToUInt32(file, strings));
            if (stringEnd > file.Length) throw new InvalidDataException("Invalid Mario symbol strings");
            var wanted = new HashSet<string>(names);
            var found = new Dictionary<string, IntPtr>();
            for (int i = 0; i < symbolCount;)
            {
                int at = checked(symbolTable + i * 18);
                string name;
                if (BitConverter.ToUInt32(file, at) == 0)
                {
                    int start = checked(strings + (int)BitConverter.ToUInt32(file, at + 4));
                    int end = start;
                    if (start < strings + 4 || start >= stringEnd) throw new InvalidDataException("Invalid function name");
                    while (end < stringEnd && file[end] != 0) end++;
                    name = Encoding.ASCII.GetString(file, start, end - start);
                }
                else name = Encoding.ASCII.GetString(file, at, 8).TrimEnd('\0');
                short section = BitConverter.ToInt16(file, at + 12);
                if (wanted.Contains(name) && section > 0 && section <= sections
                    && ((BitConverter.ToUInt16(file, at + 14) & 0x20) != 0)==functions)
                {
                    int header = sectionTable + (section - 1) * 40;
                    uint offset = BitConverter.ToUInt32(file, at + 8);
                    uint size = Math.Max(BitConverter.ToUInt32(file, header + 8), BitConverter.ToUInt32(file, header + 16));
                    uint characteristics=BitConverter.ToUInt32(file,header+36);
                    if (offset >= size || (functions?(characteristics&0x20000000)==0:(characteristics&0xC0)==0||size-offset<(uint)IntPtr.Size))
                        throw new InvalidDataException("Mario symbol is outside its expected code/data section");
                    long rva = BitConverter.ToUInt32(file, header + 12) + (long)offset;
                    found.Add(name, new IntPtr(checked(module.ToInt64() + rva)));
                }
                i = checked(i + 1 + file[at + 17]);
            }
            foreach (string name in names)
                if (!found.ContainsKey(name)) throw new MissingMethodException("Mario engine function " + name);
            return found;
        }
    }
}
