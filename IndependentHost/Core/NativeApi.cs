using System;
using System.Runtime.InteropServices;

namespace HollowMario.Host
{
    internal sealed class NativeApi
    {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryEx(string name,IntPtr reserved,uint flags);
        [DllImport("kernel32.dll",CharSet=CharSet.Ansi)]static extern IntPtr GetProcAddress(IntPtr module,string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate void Init(IntPtr rom,IntPtr atlas);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate void End();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate void AudioInit(IntPtr rom);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate uint AudioTick(uint queued,uint desired,IntPtr output);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate void Surfaces(IntPtr surfaces,uint count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int Create(float x,float y,float z);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate void Tick(int id,ref Input input,ref State state,ref GeometryPointers geometry);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate void Id(int id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate void Vec(int id,float x,float y,float z);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate void Action(int id,uint action);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate void Level(int id,int level);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate void Health(int id,ushort value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate void Sound(uint bits);
        internal readonly Init GlobalInit;
        internal readonly End GlobalEnd;
        internal readonly AudioInit InitAudio;
        internal readonly AudioTick PullAudio;
        internal readonly Surfaces LoadSurfaces;
        internal readonly Create CreateActor;
        internal readonly Tick TickActor;
        internal readonly Id DeleteActor;
        internal readonly Vec Position,Velocity;
        internal readonly Action SetAction;
        internal readonly Level Water,Gas;
        internal readonly Health SetHealth;
        internal readonly Sound PlaySound;
        private readonly IntPtr module;
        internal NativeApi(string path)
        {
            module=LoadLibraryEx(System.IO.Path.GetFullPath(path),IntPtr.Zero,8);
            if(module==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Cannot load the locally built Mario engine.");
            GlobalInit=Bind<Init>("sm64_global_init");GlobalEnd=Bind<End>("sm64_global_terminate");
            InitAudio=Bind<AudioInit>("sm64_audio_init");PullAudio=Bind<AudioTick>("sm64_audio_tick");
            LoadSurfaces=Bind<Surfaces>("sm64_static_surfaces_load");CreateActor=Bind<Create>("sm64_mario_create");
            TickActor=Bind<Tick>("sm64_mario_tick");DeleteActor=Bind<Id>("sm64_mario_delete");
            Position=Bind<Vec>("sm64_set_mario_position");Velocity=Bind<Vec>("sm64_set_mario_velocity");
            SetAction=Bind<Action>("sm64_set_mario_action");Water=Bind<Level>("sm64_set_mario_water_level");
            Gas=Bind<Level>("sm64_set_mario_gas_level");SetHealth=Bind<Health>("sm64_set_mario_health");PlaySound=Bind<Sound>("sm64_play_sound_global");
        }
        internal T Bind<T>(string symbol)where T:class
        {
            IntPtr address=GetProcAddress(module,symbol);
            if(address==IntPtr.Zero)throw new MissingMethodException("Local native engine export missing: "+symbol);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(address,typeof(T));
        }
        // Do not unload the native module while a runtime can retain callbacks.
    }
}
