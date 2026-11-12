using System;
using System.IO;
using HollowMario.Setup;

internal static class SigningChecks
{
    internal static int Run(string root)
    {
        Directory.CreateDirectory(root);
        byte[] original=File.ReadAllBytes(typeof(Disk).Assembly.Location);
        string file=Path.Combine(root,"library.dll");File.WriteAllBytes(file,original);
        string raw=Disk.Hash(file),content=Disk.SigningHash(file);
        if(content==null)throw new Exception("PE content hash missing");
        int optional=BitConverter.ToInt32(original,60)+24;
        int security=optional+(BitConverter.ToUInt16(original,optional)==0x10b?96:112)+32;
        byte[] signed=new byte[((original.Length+7)&~7)+16];Array.Copy(original,signed,original.Length);
        Array.Copy(BitConverter.GetBytes(signed.Length-16),0,signed,security,4);
        Array.Copy(BitConverter.GetBytes(16),0,signed,security+4,4);
        signed[optional+64]^=0x7f; // signing updates the checksum too
        File.WriteAllBytes(file,signed);
        if(Disk.Hash(file)==raw||Disk.SigningHash(file)!=content)throw new Exception("Signing-only change rejected");
        var manifest=new PackageManifest{Files=new[]{new PackageFile{Path="library.dll",Sha256=raw,SigningSha256=content}}};
        JsonFile.Write(Path.Combine(root,"package.json"),manifest);new Installer(root).Verify();
        signed[optional+sizeOfOptional(original,optional)]^=1;File.WriteAllBytes(file,signed);
        bool refused=false;try{new Installer(root).Verify();}catch(InvalidDataException){refused=true;}
        if(!refused)throw new Exception("Changed PE contents accepted");
        File.WriteAllBytes(file,new byte[]{1,2,3});
        if(Disk.SigningHash(file)!=null)throw new Exception("Non-PE has a signing hash");
        return 5;
    }
    static int sizeOfOptional(byte[] bytes,int optional)=>BitConverter.ToUInt16(bytes,optional-4);
}
