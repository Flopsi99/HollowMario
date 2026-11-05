using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using HollowMario.Setup;

namespace HollowMario.Host
{
    public sealed class Engine:IDisposable
    {
        private static readonly object lifecycle=new object();
        private static Engine current;
        internal readonly object Gate=new object();
        internal readonly NativeApi Api;
        private GCHandle romPin;
        internal GCHandle RomHandle=>romPin;
        private bool ended;
        private readonly HashSet<Actor> actors=new HashSet<Actor>();
        private readonly Stack<int> parked=new Stack<int>();
        private readonly HashSet<int> handles=new HashSet<int>();
        public readonly byte[] Atlas=new byte[704*64*4];
        public int LiveActors{get{lock(Gate)return actors.Count;}}
        public Engine(string library,string rom)
        {
            lock(lifecycle)
            {
                if(current!=null)throw new InvalidOperationException("One Mario engine owns this process. Stop the previous host first.");
                if(!RomResolver.Valid(rom))throw new InvalidDataException("The host needs your supported Super Mario 64 US ROM.");
                byte[] bytes=File.ReadAllBytes(rom);
                if(bytes[0]==0x37)for(int i=0;i<bytes.Length;i+=2){byte a=bytes[i];bytes[i]=bytes[i+1];bytes[i+1]=a;}
                else if(bytes[0]==0x40)for(int i=0;i<bytes.Length;i+=4){Array.Reverse(bytes,i,4);}
                Api=new NativeApi(library);romPin=GCHandle.Alloc(bytes,GCHandleType.Pinned);
                var atlasPin=GCHandle.Alloc(Atlas,GCHandleType.Pinned);
                try{Api.GlobalInit(romPin.AddrOfPinnedObject(),atlasPin.AddrOfPinnedObject());Api.InitAudio(romPin.AddrOfPinnedObject());current=this;}
                catch{romPin.Free();throw;}
                finally{atlasPin.Free();}
            }
        }
        internal void RequireOpen(){if(ended)throw new ObjectDisposedException(nameof(Engine));}
        public void SetWorld(Surface[] surfaces)
        {
            if(surfaces==null)throw new ArgumentNullException(nameof(surfaces));
            lock(Gate)
            {
                RequireOpen();
                if(handles.Count>0)throw new InvalidOperationException("Prototype world replacement requires restarting this engine; live surface streaming is not connected yet.");
                var pin=GCHandle.Alloc(surfaces,GCHandleType.Pinned);
                try{Api.LoadSurfaces(pin.AddrOfPinnedObject(),(uint)surfaces.Length);}finally{pin.Free();}
            }
        }
        public Actor Spawn(float x,float y,float z=0f)
        {
            lock(Gate)
            {
                RequireOpen();int id=parked.Count>0?parked.Pop():Api.CreateActor(x,y,z);
                if(id<0)throw new InvalidOperationException("No native floor below Mario's spawn point.");
                handles.Add(id);Api.Position(id,x,y,z);Api.Velocity(id,0,0,0);Api.SetAction(id,0x0C400201u);
                Api.Water(id,-1000000);Api.Gas(id,-1000000);Api.SetHealth(id,2176);
                var actor=new Actor(this,id);actors.Add(actor);return actor;
            }
        }
        internal void Park(Actor actor)
        {
            lock(Gate)
            {
                if(!actors.Remove(actor)||ended)return;
                Api.Velocity(actor.Id,0,0,0);Api.Position(actor.Id,0,-1000000,0);
                // Audio can retain native actor pointers. Only destroy at shutdown.
                parked.Push(actor.Id);
            }
        }
        public int Audio(short[] stereo,uint desiredFrames=1088,uint queuedFrames=0)
        {
            // The pinned engine writes TWO stereo blocks, each 528 or 544
            // frames, but returns the length of ONE block. The desired count
            // chooses its queue target; it does not bound the write size.
            if(stereo==null||desiredFrames==0||desiredFrames>2048||stereo.Length<544*4)throw new ArgumentException("Invalid native audio request or stereo buffer capacity.");
            lock(Gate)
            {
                RequireOpen();var pin=GCHandle.Alloc(stereo,GCHandleType.Pinned);
                try
                {
                    uint block=Api.PullAudio(queuedFrames,desiredFrames,pin.AddrOfPinnedObject());
                    if(block!=528&&block!=544)throw new InvalidDataException("Unexpected audio block from the pinned native engine.");
                    return checked((int)block*2);
                }
                finally{pin.Free();}
            }
        }
        public void Sound(uint sound){lock(Gate){RequireOpen();Api.PlaySound(sound);}}
        public void Dispose()
        {
            lock(lifecycle)lock(Gate)
            {
                if(ended)return;ended=true;
                foreach(var actor in actors)actor.Detach();actors.Clear();
                foreach(int id in handles)Api.DeleteActor(id);handles.Clear();parked.Clear();
                Api.GlobalEnd();if(romPin.IsAllocated)romPin.Free();current=null;
            }
        }
    }
    public sealed class Actor:IDisposable
    {
        private readonly Engine engine;
        internal readonly int Id;
        public State State;
        public readonly GeometryFrame Geometry=new GeometryFrame();
        private bool disposed;
        internal Actor(Engine engine,int id){this.engine=engine;Id=id;}
        public void Tick(Input input)
        {
            lock(engine.Gate){engine.RequireOpen();if(disposed)throw new ObjectDisposedException(nameof(Actor));engine.Api.TickActor(Id,ref input,ref State,ref Geometry.Pointers);}
        }
        public void Place(float x,float y,float z=0f)
        {lock(engine.Gate){engine.RequireOpen();if(disposed)throw new ObjectDisposedException(nameof(Actor));engine.Api.Position(Id,x,y,z);}}
        public void SetAction(uint action)
        {lock(engine.Gate){engine.RequireOpen();if(disposed)throw new ObjectDisposedException(nameof(Actor));engine.Api.SetAction(Id,action);}}
        internal void Detach(){disposed=true;Geometry.Dispose();}
        public void Dispose(){lock(engine.Gate){if(disposed)return;engine.Park(this);Detach();}}
    }
}
