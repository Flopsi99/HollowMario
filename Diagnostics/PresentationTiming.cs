using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Fixed storage and timestamp/counter reads during play. Formatting, heap
    // queries and log writes happen only for a hitch or the 30-second summary.
    // Scopes are inclusive: parent/child costs must never be added together.
    internal static class PresentationTiming
    {
        internal sealed class Samples
        {
            internal readonly double[] Total=new double[24],Peak=new double[24];
            internal readonly int[] Calls=new int[24];
            internal int Frame=-1,Gc0,Gc1,Gc2;
            internal long Allocated;
            internal string Context;
            internal void Add(int index,double ms){Total[index]+=ms;Calls[index]++;Peak[index]=Math.Max(Peak[index],ms);}
            internal void Clear(int frame){Array.Clear(Total,0,24);Array.Clear(Peak,0,24);Array.Clear(Calls,0,24);Frame=frame;Context=null;}
        }
        internal sealed class Window
        {
            internal int Frames,Hitches,Suppressed;
            internal double Sum,Max,Seconds;
            private double lastHitch=-5d;
            internal bool Observe(double seconds,double frameMs,double peakMs)
            {
                Frames++;Sum+=frameMs;Max=Math.Max(Max,frameMs);Seconds+=Math.Max(0d,frameMs/1000d);
                if(frameMs<50d&&peakMs<12d)return false;
                Hitches++;
                if(seconds-lastHitch<5d){Suppressed++;return false;}
                lastHitch=seconds;return true;
            }
            internal void Reset(){Frames=Hitches=Suppressed=0;Sum=Max=Seconds=0d;}
        }
        private static readonly Dictionary<MethodBase,int> methods=new Dictionary<MethodBase,int>();
        private static readonly Dictionary<string,int> slots=new Dictionary<string,int>();
        private static readonly string[] names=new string[24];
        private static Samples current=new Samples(),previous=new Samples();
        private static readonly Window window=new Window();
        private static Func<long> allocated;
        private static Type gameType;
        private static int mainThread,slotCount,gc0,gc1,gc2,sceneFrame,failures;
        private static long allocationStart;
        private static string scene="unknown";
        private static bool armed,wasFocused;
        private static int focusedFrames;
        private static string Context=>BenchMario.Presenting?"bench":OpeningMario.Presenting?"opening":ConversationMario.Context;
        internal static void Arm(Harmony harmony,Type controller)
        {
            mainThread=Thread.CurrentThread.ManagedThreadId;gameType=AccessTools.TypeByName("GameManager");
            var getter=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",BindingFlags.Public|BindingFlags.Static);
            if(getter!=null)try{allocated=(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),getter);allocated();}catch{allocated=null;}
            foreach(string name in new[]{"pose setup","pose draw","camera target"})Slot(name);
            Add(harmony,AccessTools.Method(controller,"Update"),"controller update");
            Add(harmony,AccessTools.Method(controller,"Tick"),"simulation");
            Add(harmony,AccessTools.Method(controller.Assembly.GetType("MarioHollow.WorldCollision"),"Tick"),"collision rebuild");
            Add(harmony,AccessTools.Method(controller.Assembly.GetType("MarioHollow.Native"),"sm64_mario_tick"),"native Mario");
            Add(harmony,AccessTools.Method(controller.Assembly.GetType("MarioHollow.MarioAttacks"),"Tick"),"attack processing");
            Add(harmony,AccessTools.Method(typeof(Plugin),"TickEnemyHits"),"extra enemy hits");
            Add(harmony,AccessTools.Method(controller.Assembly.GetType("MarioHollow.MarioRenderer"),"TakeTick"),"mesh upload");
            Add(harmony,AccessTools.Method(controller.Assembly.GetType("MarioHollow.MarioRenderer"),"Render"),"mesh render");
            Add(harmony,AccessTools.Method(AccessTools.TypeByName("CameraController"),"LateUpdate"),"camera");
            Add(harmony,AccessTools.Method(typeof(BenchMario),"Draw"),"bench pose");
            Add(harmony,AccessTools.Method(typeof(Plugin),"LateUpdate"),"companion late update");
            armed=true;
        }
        private static int Slot(string name)
        {
            if(slots.TryGetValue(name,out int index))return index;
            if(slotCount==names.Length)throw new InvalidOperationException("Performance scope capacity");
            index=slotCount++;names[index]=name;slots.Add(name,index);return index;
        }
        private static void Add(Harmony harmony,MethodInfo method,string name)
        {
            if(method==null){Plugin.Instance?.Log.LogWarning("Performance scope unavailable: "+name);return;}
            try
            {
                methods[method]=Slot(name);
                harmony.Patch(method,prefix:new HarmonyMethod(typeof(PresentationTiming),nameof(Before)){priority=Priority.First},
                    finalizer:new HarmonyMethod(typeof(PresentationTiming),nameof(After)){priority=Priority.Last});
            }
            catch(Exception error){Plugin.Instance?.Log.LogWarning("Performance scope "+name+": "+error.GetBaseException().Message);}
        }
        private static void Before(out long __state){__state=Begin();}
        private static Exception After(MethodBase __originalMethod,long __state,Exception __exception)
        {
            if(__state!=0&&methods.TryGetValue(__originalMethod,out int slot))Finish(__state,slot);
            return __exception; // Instrumentation never swallows game exceptions.
        }
        internal static long Begin()=>armed&&DebugSettings.PresentationTiming&&Thread.CurrentThread.ManagedThreadId==mainThread?Stopwatch.GetTimestamp():0L;
        internal static void End(long started,string operation)
        {if(started!=0&&slots.TryGetValue(operation,out int slot))Finish(started,slot);}
        private static void Finish(long started,int slot)
        {
            long ended=Stopwatch.GetTimestamp();
            try
            {
                Advance(Time.frameCount);
                current.Add(slot,(ended-started)*1000d/Stopwatch.Frequency);
                // An expensive setup can change ownership before LateUpdate.
                if(current.Context==null||current.Peak[slot]>=12d)current.Context=Context;
            }
            catch(Exception error){if(failures++<3)Plugin.Instance?.Log.LogWarning("Performance sampling: "+error.GetBaseException().Message);}
        }
        private static void Advance(int frame)
        {
            if(current.Frame==frame)return;
            int a=GC.CollectionCount(0),b=GC.CollectionCount(1),c=GC.CollectionCount(2);
            long bytes=allocated!=null?allocated():0L;
            current.Gc0=a-gc0;current.Gc1=b-gc1;current.Gc2=c-gc2;current.Allocated=bytes-allocationStart;
            gc0=a;gc1=b;gc2=c;allocationStart=bytes;
            Samples old=previous;previous=current;current=old;current.Clear(frame);
        }
        internal static void SceneChanged(string name){scene=name;sceneFrame=Time.frameCount;}
        internal static void Frame()
        {
            if(!armed||!DebugSettings.PresentationTiming)return;
            Advance(Time.frameCount);
            bool focused=Application.isFocused&&Time.timeScale>0f;
            focusedFrames=focused?(wasFocused?focusedFrames+1:1):0;wasFocused=focused;
            if(!focused||focusedFrames<3)return;
            object game=CapAccess.Read(gameType,"UnsafeInstance");
            object controller=Plugin.Instance?.knightFollow?.Controller();
            if(Convert.ToInt32(CapAccess.Read(game,"gameState"))!=4||controller==null||!CapAccess.Flag(controller,"Wanted")||previous.Frame<0)return;
            current.Context=Context;
            double peak=0d;for(int i=0;i<slotCount;i++)peak=Math.Max(peak,previous.Peak[i]);
            if(window.Observe(Time.realtimeSinceStartup,Time.unscaledDeltaTime*1000d,peak))Report();
            if(window.Seconds>=30d)
            {
                Plugin.Instance?.Log.LogInfo("Performance summary: "+window.Frames+" frames; avg="+F(window.Sum/window.Frames)
                    +" ms; worst="+F(window.Max)+" ms; hitches="+window.Hitches+"; rate-limited="+window.Suppressed+"; scene="+scene+". Scope times are inclusive.");
                window.Reset();
            }
        }
        private static string F(double value)=>value.ToString("F1",CultureInfo.InvariantCulture);
        private static void Report()
        {
            var text=new StringBuilder(512);
            text.Append("Performance hitch: frame=").Append(F(Time.unscaledDeltaTime*1000d)).Append(" ms; pose=").Append(previous.Context??"gameplay")
                .Append("; scene=").Append(scene).Append("; scene age=").Append(Time.frameCount-sceneFrame).Append(" frames; GC frame=")
                .Append(previous.Gc0).Append('/').Append(previous.Gc1).Append('/').Append(previous.Gc2)
                .Append("; main-thread allocated=").Append(allocated==null?"unavailable":F(previous.Allocated/1024d)+" KiB")
                .Append("; managed heap=").Append(F(GC.GetTotalMemory(false)/1048576d)).Append(" MiB; scopes (total/peak/calls, inclusive): ");
            uint used=0;
            for(int pass=0;pass<6;pass++)
            {
                int best=-1;double largest=.02d;
                for(int i=0;i<slotCount;i++)if((used&(1u<<i))==0&&previous.Total[i]>largest){largest=previous.Total[i];best=i;}
                if(best<0)break;used|=1u<<best;
                text.Append(names[best]).Append('=').Append(F(previous.Total[best])).Append('/').Append(F(previous.Peak[best])).Append('/')
                    .Append(previous.Calls[best]).Append("; ");
            }
            var follow=Plugin.Instance?.knightFollow;object controller=follow?.Controller();
            if(controller!=null)text.Append("current action=").Append(follow.Action(controller).ToString("X")).Append(" anim=")
                .Append(follow.Animation(controller).ToString("X")).Append('/').Append(follow.AnimFrame(controller));
            Plugin.Instance?.Log.LogInfo(text.ToString());
        }
    }
}
