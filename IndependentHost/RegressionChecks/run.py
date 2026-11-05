"""Build and run migrated regressions in isolated Unity Mono processes; no game/window.

Inputs are explicit and package hashes are recorded. Never searches old build or
installed mod folders. Each fixture gets its own process (native globals/detours).
"""
import argparse
import concurrent.futures
import ctypes
import datetime
import hashlib
import json
import msvcrt
import os
import pathlib
import subprocess
import sys
import xml.etree.ElementTree as xml

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]


def worker(args):
    game, package = pathlib.Path(args.game).resolve(), pathlib.Path(args.package).resolve()
    managed = game / "hollow_knight_Data/Managed"
    runtime = game / "MonoBleedingEdge/EmbedRuntime"
    os.add_dll_directory(str(runtime))
    mono = ctypes.CDLL(str(runtime / "mono-2.0-bdwgc.dll"))
    mono.mono_set_dirs.argtypes = [ctypes.c_char_p, ctypes.c_char_p]
    mono.mono_set_dirs(str(managed).encode(), str(game / "MonoBleedingEdge/etc").encode())
    mono.mono_set_assemblies_path.argtypes = [ctypes.c_char_p]
    paths = [args.output, package, package / "MarioHollow-native", managed]
    mono.mono_set_assemblies_path(";".join(map(str, paths)).encode())
    mono.mono_config_parse.argtypes = [ctypes.c_char_p]
    mono.mono_config_parse(None)
    mono.mono_jit_init_version.argtypes = [ctypes.c_char_p, ctypes.c_char_p]
    mono.mono_jit_init_version.restype = ctypes.c_void_p
    domain = mono.mono_jit_init_version(b"HollowMarioRegressionChecks", b"v4.0.30319")
    if not domain:
        raise RuntimeError("Unity Mono initialization failed")
    mono.mono_domain_assembly_open.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
    mono.mono_domain_assembly_open.restype = ctypes.c_void_p
    runner = pathlib.Path(args.output) / "Runner.exe"
    assembly = mono.mono_domain_assembly_open(domain, str(runner).encode())
    if not assembly:
        raise RuntimeError("Regression runner did not load")
    mono.mono_jit_exec.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_int, ctypes.POINTER(ctypes.c_char_p)]
    mono.mono_jit_exec.restype = ctypes.c_int
    values = [str(runner), str(ROOT), str(package), str(game), args.library, args.rom,
              str(pathlib.Path(args.output) / (args.worker + ".dll"))]
    argv = (ctypes.c_char_p * len(values))(*(v.encode() for v in values))
    return mono.mono_jit_exec(domain, assembly, len(values), argv)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game", required=True)
    parser.add_argument("--package", required=True)
    parser.add_argument("--library", required=True)
    parser.add_argument("--rom", required=True)
    parser.add_argument("--only", help="Comma-separated suite names; default all")
    parser.add_argument("--output", default=str(ROOT / ".artifacts/independent-host/regressions"))
    parser.add_argument("--worker", help=argparse.SUPPRESS)
    args = parser.parse_args()
    if args.worker:
        return worker(args)
    output = pathlib.Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    # Prevent another coordinator replacing the shared harness/report while
    # workers are running. Every suite still has its own native process.
    coordinator_lock = (output / ".runner.lock").open("a+b")
    coordinator_lock.seek(0)
    try:
        msvcrt.locking(coordinator_lock.fileno(), msvcrt.LK_NBLCK, 1)
    except OSError:
        raise RuntimeError("Another regression run is using this output folder")
    package = pathlib.Path(args.package).resolve()
    managed = pathlib.Path(args.game).resolve() / "hollow_knight_Data/Managed"
    host = package / "MarioHollow-native"
    for path in [package / "HollowMario.dll", host / "HollowMario.Host.Integration.dll", pathlib.Path(args.library), pathlib.Path(args.rom)]:
        if not path.is_file():
            raise FileNotFoundError(path)
    # A compiler from the installed SDK; no dependency download/build tooling install.
    sdk_line = subprocess.check_output(["dotnet", "--list-sdks"], text=True).splitlines()[-1]
    version, sdk_root = sdk_line.split(" [")
    compiler = pathlib.Path(sdk_root.rstrip("]")) / version / "Roslyn/bincore/csc.dll"
    framework = pathlib.Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework64/v4.0.30319"
    refs = [framework / n for n in ["mscorlib.dll", "System.dll", "System.Core.dll"]]
    refs += [package / n for n in ["HollowMario.dll", "HollowMario.ApiRuntime.dll", "0Harmony.dll"]]
    refs += list(managed.glob("UnityEngine*.dll"))
    refs += [managed / n for n in ["Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll", "PlayMaker.dll", "Mono.Cecil.dll", "MonoMod.Utils.dll", "MonoMod.RuntimeDetour.dll", "netstandard.dll"]]
    def compile_file(source, target, references):
        cmd = ["dotnet", str(compiler), "/nologo", "/noconfig", "/nostdlib+", "/unsafe+", "/deterministic+", "/langversion:latest", "/target:" + ("exe" if target.suffix == ".exe" else "library"), "/out:" + str(target)]
        cmd += ["/reference:" + str(p) for p in references]
        cmd += [str(p) for p in (source if isinstance(source, list) else [source])]
        result = subprocess.run(cmd, capture_output=True, text=True)
        return result.returncode, result.stdout + result.stderr
    support = output / "TestEnvironment.dll"
    for source, target, references in [(HERE / "TestEnvironment.cs", support, refs), (HERE / "Runner.cs", output / "Runner.exe", refs + [support])]:
        code, log = compile_file(source, target, references)
        if code:
            print(log)
            return 1
    suites = json.loads((HERE / "suites.json").read_text())
    if args.only:
        names = set(args.only.split(","))
        missing = names - {s["name"] for s in suites}
        if missing:
            raise ValueError("Unknown suites: " + ", ".join(sorted(missing)))
        suites = [s for s in suites if s["name"] in names]
    def run_suite(suite):
        name = suite["name"]
        source = ROOT / suite["file"] if "file" in suite else HERE / "Fixtures" / (name + ".cs")
        references = refs + [support]
        if suite.get("hostReferences"):
            references += list(host.glob("HollowMario.Host.*.dll"))
        if "project" in suite:
            project = ROOT / suite["project"]
            source = [(project.parent / c.attrib["Include"].replace("\\", "/")).resolve() for c in xml.parse(project).findall(".//Compile")]
            references = [p for p in refs if p.parent == framework or p.name in ["0Harmony.dll", "HollowMario.ApiRuntime.dll", "Mono.Cecil.dll", "MonoMod.Utils.dll", "MonoMod.RuntimeDetour.dll"]]
        code, log = compile_file(source, output / (name + ".dll"), references)
        stage = "compile"
        if not code:
            stage = "run"
            cmd = [sys.executable, "-X", "utf8", str(HERE / "run.py"), "--worker", name,
                   "--game", args.game, "--package", args.package, "--library", args.library, "--rom", args.rom, "--output", str(output)]
            try:
                result = subprocess.run(cmd, capture_output=True, text=True, timeout=90, cwd=output)
                code, log = result.returncode, result.stdout + result.stderr
            except subprocess.TimeoutExpired as error:
                code, log = -1, "Timed out (not a pass). " + str(error)
        (output / (name + ".log")).write_text(log, encoding="utf-8")
        inputs = source if isinstance(source, list) else [source]
        source_hashes = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
        result = dict(suite, status="passed" if code == 0 else "failed", stage=stage, exitCode=code, sourceHashes=source_hashes)
        print(("PASS " if code == 0 else "FAIL ") + name + " [" + suite["kind"] + "]", flush=True)
        return result
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        results = list(pool.map(run_suite, suites))
    hashes = {str(p.relative_to(package)): hashlib.sha256(p.read_bytes()).hexdigest() for p in package.rglob("*.dll")}
    report = {"gameStarted": False, "finishedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "package": str(package), "packageHashes": hashes,
              "nativeLibrarySha256": hashlib.sha256(pathlib.Path(args.library).read_bytes()).hexdigest(),
              "harnessHashes": {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in [HERE / "run.py", HERE / "Runner.cs", HERE / "TestEnvironment.cs"]},
              "gameAssemblySha256": hashlib.sha256((managed / "Assembly-CSharp.dll").read_bytes()).hexdigest(), "suites": results}
    report_name = "results" + ("-selected" if args.only else "") + ".json"
    (output / report_name).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    passed = sum(s["status"] == "passed" for s in results)
    print(f"{passed}/{len(results)} suites passed. Detailed logs and exact package hashes saved.")
    return 0 if passed == len(results) else 1


if __name__ == "__main__":
    sys.exit(main())
