using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

// Our dependency preparation/verification utility. It handles public HarmonyX
// and installed API metadata only; no original mashup assembly is an input.
internal static class ApiDependencies
{
    private static int Main(string[] args)
    {
        try
        {
            if(args[0]=="verify" || args[0]=="verify-public"){Verify(args[1],args[0]=="verify-public");return 0;}
            if(args[0].StartsWith("verify",StringComparison.Ordinal)) throw new InvalidOperationException("Only the community API package is supported.");
            string managed=Path.GetFullPath(args[0]),input=Path.GetFullPath(args[1]),output=Path.GetFullPath(args[2]);
            var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(managed);resolver.AddSearchDirectory(Path.GetDirectoryName(input));
            using(var cecil=AssemblyDefinition.ReadAssembly(Path.Combine(managed,"Mono.Cecil.dll")))
            using(var utils=AssemblyDefinition.ReadAssembly(Path.Combine(managed,"MonoMod.Utils.dll")))
            using(var detour=AssemblyDefinition.ReadAssembly(Path.Combine(managed,"MonoMod.RuntimeDetour.dll")))
            using(var harmony=AssemblyDefinition.ReadAssembly(input,new ReaderParameters{AssemblyResolver=resolver}))
            {
                if(utils.Name.Version!=new Version("21.4.29.1")||cecil.Name.Version!=new Version("0.11.3.0"))
                    throw new InvalidOperationException("Revalidate HarmonyX against this community API dependency version first.");
                foreach(var target in new[]{cecil.Name,utils.Name,detour.Name})
                    foreach(var reference in harmony.MainModule.AssemblyReferences.Where(r=>r.Name==target.Name))
                    {reference.Version=target.Version;reference.PublicKeyToken=target.PublicKeyToken;}
                harmony.Write(output);
            }
            Console.WriteLine("Verified upstream HarmonyX prepared for the installed community API.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}
    }
    private static void Verify(string folder,bool distributable)
    {
        var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(folder);
        foreach(string directory in Directory.GetDirectories(folder,"*",SearchOption.AllDirectories))resolver.AddSearchDirectory(directory);
        string[] forbidden={"MarioHollow.dll","Assembly-CSharp.dll","Assembly-CSharp-firstpass.dll","UnityEngine.CoreModule.dll"};
        string[] allowedBinaries={"HollowMario.dll","HollowMario.ApiRuntime.dll","HollowMario.Setup.Core.dll",
            "HollowMario.Host.Core.dll","HollowMario.Host.Unity.dll","HollowMario.Host.Integration.dll",
            "0Harmony.dll","Mono.Cecil.dll","HollowMario-Setup.exe","sm64.dll"};
        foreach(string file in Directory.GetFiles(folder,"*",SearchOption.AllDirectories))
        {
            string name=Path.GetFileName(file);
            if(forbidden.Contains(name,StringComparer.OrdinalIgnoreCase)||new[]{".z64",".v64",".n64"}.Contains(Path.GetExtension(file),StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Forbidden package input: "+name);
            if(distributable&&name.Equals("sm64.dll",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Player-local runtime cannot enter a public package.");
            if(!name.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)&&!name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))continue;
            if(!allowedBinaries.Contains(name,StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("Unexpected package binary: "+name);
            if(name=="sm64.dll")continue; // Explicitly player-local, private native test output.
            using(var assembly=AssemblyDefinition.ReadAssembly(file,new ReaderParameters{AssemblyResolver=resolver}))
            {
                if(assembly.Name.Name=="MarioHollow"||assembly.MainModule.AssemblyReferences.Any(r=>r.Name=="MarioHollow"||r.Name=="BepInEx"))
                    throw new InvalidDataException("Old bridge/injector assembly dependency in API package: "+name);
                foreach(var attribute in assembly.MainModule.GetTypes().SelectMany(t=>t.CustomAttributes))
                    if(attribute.AttributeType.FullName=="BepInEx.BepInDependency"&&attribute.ConstructorArguments.Count>0
                        &&Convert.ToString(attribute.ConstructorArguments[0].Value)=="gg.melty.mariohollow")
                        throw new InvalidDataException("Old bridge plugin dependency in "+name);
            }
        }
        Console.WriteLine("Host package contains no retired bridge dependency, player ROM or bundled host-game assemblies.");
    }
}
