"""Run CPU checks on the installed Unity Mono runtime, without starting Unity."""
import ctypes
import os
import pathlib
import sys
import subprocess

root = pathlib.Path(__file__).resolve().parents[2]
game = pathlib.Path(sys.argv[1])
managed = game / "hollow_knight_Data/Managed"
runtime = game / "MonoBleedingEdge/EmbedRuntime"
checks = root / "Tools/ApiCompatibility/bin/Release/net472/ApiChecks.exe"
package = pathlib.Path(sys.argv[2]).resolve() if len(sys.argv) > 2 else pathlib.Path((root / ".artifacts/independent-host/latest-package.txt").read_text().strip())
if not (package / "MarioHollow-native/HollowMario.Host.Integration.dll").is_file():
    raise FileNotFoundError("An independent host package is required")
subprocess.run(["dotnet", "build", str(root / "Tools/ApiCompatibility/ApiChecks.csproj"),
                "-c", "Release", "--no-restore", "-m:1", "--nologo", "-v", "quiet"], check=True)
os.add_dll_directory(str(runtime))
mono = ctypes.CDLL(str(runtime / "mono-2.0-bdwgc.dll"))
mono.mono_set_dirs.argtypes = [ctypes.c_char_p, ctypes.c_char_p]
mono.mono_set_dirs(str(managed).encode(), str(game / "MonoBleedingEdge/etc").encode())
mono.mono_set_assemblies_path.argtypes = [ctypes.c_char_p]
mono.mono_set_assemblies_path((";".join(map(str, [checks.parent, package, package / "MarioHollow-native", managed]))).encode())
mono.mono_config_parse.argtypes = [ctypes.c_char_p]
mono.mono_config_parse(None)
mono.mono_jit_init_version.argtypes = [ctypes.c_char_p, ctypes.c_char_p]
mono.mono_jit_init_version.restype = ctypes.c_void_p
domain = mono.mono_jit_init_version(b"HollowMarioCpuChecks", b"v4.0.30319")
if not domain:
    raise RuntimeError("Mono CPU test domain failed to initialize")
mono.mono_domain_assembly_open.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
mono.mono_domain_assembly_open.restype = ctypes.c_void_p
assembly = mono.mono_domain_assembly_open(domain, str(checks).encode())
if not assembly:
    raise RuntimeError("CPU test assembly failed to load")
mono.mono_jit_exec.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_int, ctypes.POINTER(ctypes.c_char_p)]
mono.mono_jit_exec.restype = ctypes.c_int
arguments = [str(checks), str(package), str(game), str(checks.parent)]
argv = (ctypes.c_char_p * len(arguments))(*(value.encode() for value in arguments))
result = mono.mono_jit_exec(domain, assembly, len(argv), argv)
# Detours and Mono runtime TLS are process-owned; process exit releases them.
sys.exit(result)
