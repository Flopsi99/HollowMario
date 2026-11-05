"""Run connection checks in Unity Mono, without starting the game or a window."""
import ctypes
import os
import pathlib
import shutil
import sys
import tempfile

root = pathlib.Path(__file__).resolve().parents[2]
game, package, library, rom = (pathlib.Path(v).resolve() for v in sys.argv[1:5])
managed = game / "hollow_knight_Data/Managed"
runtime = game / "MonoBleedingEdge/EmbedRuntime"
compiled = pathlib.Path(__file__).parent / "bin/Release/net472/HollowMario.Host.IntegrationChecks.exe"
# Do not let project-reference copies next to the test EXE shadow the delivered
# host. The isolated runner directory contains ONLY the check executable.
scratch = root / ".artifacts/independent-host/integration-check-runs"
scratch.mkdir(parents=True, exist_ok=True)
checks = pathlib.Path(tempfile.mkdtemp(prefix="run-", dir=scratch)) / compiled.name
shutil.copyfile(compiled, checks)
os.add_dll_directory(str(runtime))
mono = ctypes.CDLL(str(runtime / "mono-2.0-bdwgc.dll"))
mono.mono_set_dirs.argtypes = [ctypes.c_char_p, ctypes.c_char_p]
mono.mono_set_dirs(str(managed).encode(), str(game / "MonoBleedingEdge/etc").encode())
mono.mono_set_assemblies_path.argtypes = [ctypes.c_char_p]
mono.mono_set_assemblies_path(";".join(map(str, (package, library.parent, managed, checks.parent))).encode())
mono.mono_config_parse.argtypes = [ctypes.c_char_p]
mono.mono_config_parse(None)
mono.mono_jit_init_version.argtypes = [ctypes.c_char_p, ctypes.c_char_p]
mono.mono_jit_init_version.restype = ctypes.c_void_p
domain = mono.mono_jit_init_version(b"HollowMarioIndependentChecks", b"v4.0.30319")
if not domain:
    raise RuntimeError("Unity Mono did not initialize")
mono.mono_domain_assembly_open.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
mono.mono_domain_assembly_open.restype = ctypes.c_void_p
assembly = mono.mono_domain_assembly_open(domain, str(checks).encode())
if not assembly:
    raise RuntimeError("Integration check assembly did not load")
mono.mono_jit_exec.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_int, ctypes.POINTER(ctypes.c_char_p)]
mono.mono_jit_exec.restype = ctypes.c_int
args = [str(checks), str(package), str(game), str(library), str(rom)]
argv = (ctypes.c_char_p * len(args))(*(arg.encode() for arg in args))
sys.exit(mono.mono_jit_exec(domain, assembly, len(argv), argv))
