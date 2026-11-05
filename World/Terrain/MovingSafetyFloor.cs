using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
internal sealed class MovingSafetyFloor    {
        [StructLayout(LayoutKind.Sequential)]

        private struct Surface
        {

            public short type;

            public short force;

            public ushort terrain;

            public int v0x;

            public int v0y;

            public int v0z;

            public int v1x;

            public int v1y;

            public int v1z;

            public int v2x;

            public int v2y;

            public int v2z;
        }
        [StructLayout(LayoutKind.Sequential)]

        private struct Transform
        {

            public float px;

            public float py;

            public float pz;

            public float rx;

            public float ry;

            public float rz;
        }
        [StructLayout(LayoutKind.Sequential)]

        private struct SurfaceObject
        {

            public Transform transform;

            public uint surfaceCount;

            public IntPtr surfaces;
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]

        private delegate uint CreateSurfaceObject(ref SurfaceObject surfaceObject);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]

        private delegate void MoveSurfaceObject(uint id, ref Transform transform);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]

        private delegate void DeleteSurfaceObject(uint id);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]

        private static extern IntPtr LoadLibraryExW(string fileName, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]

        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        private readonly ManualLogSource log;

        private readonly Surface[] floorSurfaces;

        private readonly HashSet<string> reportedCheckPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private FieldInfo marioIdField;

        private FieldInfo stateField;

        private FieldInfo stateXField;

        private FieldInfo stateYField;

        private FieldInfo stateZField;

        private object nativeSync;

        private CreateSurfaceObject create;

        private MoveSurfaceObject move;

        private DeleteSurfaceObject delete;

        private uint floorId;

        private bool apiReady;

        private string failure;

        private int postTickUpdates;

        internal MovingSafetyFloor(ManualLogSource log)
        {
            this.log = log;
            floorSurfaces = CreateFloorSurfaces();
        }

        internal void Follow(object controller, bool afterTick)
        {
            try
            {
                if (controller == null || !ReadMarioPosition(controller, out Transform mario))
                {
                    return;
                }
                if (!EnsureApi(controller.GetType()))
                {
                    return;
                }
                Transform floor = new Transform
                {
                    px = mario.px,
                    py = mario.py - MovingSafetyFloorSheet.ClearanceSm64,
                    pz = mario.pz
                };
                lock (nativeSync)
                {
                    if (floorId == 0)
                    {
                        GCHandle pin = GCHandle.Alloc(floorSurfaces, GCHandleType.Pinned);
                        try
                        {
                            SurfaceObject surfaceObject = new SurfaceObject
                            {
                                transform = floor,
                                surfaceCount = (uint)floorSurfaces.Length,
                                surfaces = pin.AddrOfPinnedObject()
                            };
                            floorId = create(ref surfaceObject);
                        }
                        finally
                        {
                            pin.Free();
                        }
                        if (floorId == 0)
                        {
                            throw new InvalidOperationException("libsm64 returned an invalid safety-floor object id");
                        }
                        log.LogInfo("Moving safety floor created beneath Mario.");
                    }
                    else
                    {
                        move(floorId, ref floor);
                    }
                }
                if (afterTick)
                {
                    postTickUpdates++;
                    if (postTickUpdates >= 2)
                    {
                        ReportPassingTestCheck();
                    }
                }
            }
            catch (Exception exception)
            {
                ReportFailure(exception.Message);
            }
        }

        internal void Dispose()
        {
            if (floorId == 0 || delete == null || nativeSync == null)
            {
                return;
            }
            try
            {
                lock (nativeSync)
                {
                    delete(floorId);
                    floorId = 0;
                }
                postTickUpdates = 0;
            }
            catch (Exception exception)
            {
                log.LogWarning("Moving safety floor cleanup failed: " + exception.Message);
            }
        }

        private bool ReadMarioPosition(object controller, out Transform mario)
        {
            mario = new Transform();
            Type controllerType = controller.GetType();
            if (marioIdField == null)
            {
                marioIdField = controllerType.GetField("MarioId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                stateField = controllerType.GetField("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            if (marioIdField == null || stateField == null || (int)marioIdField.GetValue(controller) < 0)
            {
                return false;
            }
            object state = stateField.GetValue(controller);
            if (state == null)
            {
                return false;
            }
            Type stateType = state.GetType();
            if (stateXField == null)
            {
                stateXField = stateType.GetField("px", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                stateYField = stateType.GetField("py", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                stateZField = stateType.GetField("pz", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            if (stateXField == null || stateYField == null || stateZField == null)
            {
                return false;
            }
            mario.px = Convert.ToSingle(stateXField.GetValue(state));
            mario.py = Convert.ToSingle(stateYField.GetValue(state));
            mario.pz = Convert.ToSingle(stateZField.GetValue(state));
            return true;
        }

        private bool EnsureApi(Type controllerType)
        {
            if (apiReady)
            {
                return true;
            }
            Type nativeType = controllerType.Assembly.GetType("MarioHollow.Native", false);
            if (nativeType == null)
            {
                ReportFailure("the original native bridge is unavailable");
                return false;
            }
            FieldInfo sync = nativeType.GetField("Sync", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            nativeSync = sync == null ? null : sync.GetValue(null);
            string nativePath = Path.Combine(Path.GetDirectoryName(controllerType.Assembly.Location), "sm64.dll");
            IntPtr module = LoadLibraryExW(nativePath, IntPtr.Zero, 8u);
            if (nativeSync == null || module == IntPtr.Zero)
            {
                ReportFailure("libsm64 could not be opened for the moving safety floor");
                return false;
            }
            create = Bind<CreateSurfaceObject>(module, "sm64_surface_object_create");
            move = Bind<MoveSurfaceObject>(module, "sm64_surface_object_move");
            delete = Bind<DeleteSurfaceObject>(module, "sm64_surface_object_delete");
            apiReady = true;
            return true;
        }

        private static T Bind<T>(IntPtr module, string name) where T : class
        {
            IntPtr export = GetProcAddress(module, name);
            if (export == IntPtr.Zero)
            {
                throw new EntryPointNotFoundException(name + " is missing from sm64.dll");
            }
            return Marshal.GetDelegateForFunctionPointer(export, typeof(T)) as T;
        }

        private Surface[] CreateFloorSurfaces()
        {
            int x = MovingSafetyFloorSheet.HalfWidthSm64;
            int z = MovingSafetyFloorSheet.HalfDepthSm64;
            return new[]
            {
                MakeSurface(-x, -z, x, z, x, -z),
                MakeSurface(-x, -z, -x, z, x, z)
            };
        }

        private static Surface MakeSurface(int ax, int az, int bx, int bz, int cx, int cz)
        {
            return new Surface
            {
                type = MovingSafetyFloorSheet.SurfaceType,
                terrain = MovingSafetyFloorSheet.TerrainType,
                v0x = ax,
                v0y = 0,
                v0z = az,
                v1x = bx,
                v1y = 0,
                v1z = bz,
                v2x = cx,
                v2y = 0,
                v2z = cz
            };
        }

        private void ReportPassingTestCheck()
        {
            string path = Plugin.FindChecksPath();
            if (path == null || !reportedCheckPaths.Add(path))
            {
                return;
            }
            string detail = "surface " + floorId + ", clearance " + MovingSafetyFloorSheet.ClearanceSm64 + ", post-tick updates " + postTickUpdates;
            log.LogInfo("melty test: pass " + MovingSafetyFloorSheet.TestCheck + ": " + detail);
            MeltyTest.Report(MovingSafetyFloorSheet.TestCheck, true, null);
        }

        private void ReportFailure(string message)
        {
            if (failure == message)
            {
                return;
            }
            failure = message;
            log.LogError("Moving safety floor failed: " + message);
            string path = Plugin.FindChecksPath();
            if (path != null && reportedCheckPaths.Add(path))
            {
                MeltyTest.Report(MovingSafetyFloorSheet.TestCheck, false, message);
            }
        }    }
}
