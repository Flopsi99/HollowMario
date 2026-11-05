using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using HollowMario.Host;

class Program
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static void AudioBufferChecks()
    {
        var buffer=new PcmBuffer(8);var output=new float[6];
        Check(buffer.Read(output)==0&&output.All(v=>v==0),"Underrun returns silence.");
        Check(buffer.Write(new short[]{32767,-32768,1024,-1024},4,.5f),"PCM fits.");
        Check(!buffer.Write(new short[8],8),"Full-buffer writes cannot overwrite unread audio.");
        Check(buffer.Read(output)==4&&output[0]>0&&output[1]==-.5f&&output[4]==0&&output[5]==0,"Stereo order, gain and zero tail preserved.");
        var concurrent=new PcmBuffer(4096);Exception producerError=null;
        var worker=new Thread(()=>{try{for(int i=0;i<20000;i++){var sample=new short[]{(short)(i%30000)};while(!concurrent.Write(sample,1))Thread.Yield();}}catch(Exception error){producerError=error;}});
        worker.Start();int received=0;var block=new float[127];
        while(received<20000){int count=concurrent.Read(block);for(int i=0;i<count;i++)Check((int)(block[i]*32768)==received++%30000,"Concurrent PCM ordering.");if(count==0)Thread.Yield();}
        worker.Join();if(producerError!=null)throw producerError;
        foreach(int rate in new[]{32000,44100,48000,96000})
        {
            var stereo=new short[64008];for(int i=0;i<stereo.Length;i+=2){stereo[i]=16384;stereo[i+1]=-8192;}
            var pcm=new PcmBuffer(stereo.Length);pcm.Write(stereo,stereo.Length);double phase=0;var dsp=new float[rate*2];
            Check(pcm.Resample(dsp,2,rate,ref phase,.5f)==rate,"One second of 32 kHz stereo fills an entire device-rate DSP second without periodic gaps.");
            Check(dsp.Where((v,i)=>i%2==0).All(v=>Math.Abs(v-.25f)<.00001f)&&dsp.Where((v,i)=>i%2==1).All(v=>Math.Abs(v+.125f)<.00001f),"Resampling preserves stereo channels and gain.");
            Check(Math.Abs(pcm.Available/2-4)<=1,"Resampling consumes source time rather than changing sound speed.");
        }
        var ramp=new short[12000];for(int i=0;i<ramp.Length;i++)ramp[i]=(short)(i*2);
        var whole=new PcmBuffer(ramp.Length);var split=new PcmBuffer(ramp.Length);whole.Write(ramp,ramp.Length);split.Write(ramp,ramp.Length);
        double a=0,b=0;var reference=new float[7200];whole.Resample(reference,2,48000,ref a,1);
        for(int part=0;part<60;part++){var fragment=new float[120];Check(split.Resample(fragment,2,48000,ref b,1)==60,"Small DSP block fully supplied.");for(int i=0;i<fragment.Length;i++)Check(Math.Abs(fragment[i]-reference[part*120+i])<.000001,"Fractional source phase survives callback boundaries without chopped/restarted sounds.");}
        var empty=new PcmBuffer(8);double cursor=0;var tail=Enumerable.Repeat(1f,128).ToArray();Check(empty.Resample(tail,2,48000,ref cursor,1)==0&&tail.All(v=>v==0),"A true underrun returns silence, never stale repeated audio.");
        Console.WriteLine("PASS DSP resampling at 32/44.1/48/96 kHz, large and split blocks, stereo/gain, source-clock continuity and underflow silence.");
    }
    static Surface[] Floor()
    {
        var floor=new RoomOutline{Owner=1,Group=1};
        floor.Points.AddRange(new[]{new WorldPoint(-60,-3),new WorldPoint(60,-3),new WorldPoint(60,0),new WorldPoint(-60,0)});
        return RoomSurfaces.Build(new[]{floor});
    }
    static int Main(string[] args)
    {
        try
        {
            Check(args.Length==2,"Pass the local native library and player-owned ROM paths.");
            Check(IntPtr.Size==8,"64-bit runtime required.");
            Check(Marshal.SizeOf(typeof(Surface))==44&&Marshal.SizeOf(typeof(Input))==20&&Marshal.SizeOf(typeof(State))==60&&Marshal.SizeOf(typeof(GeometryPointers))==40,"Public native ABI sizes.");
            Check(Marshal.OffsetOf(typeof(State),"Action").ToInt32()==36&&Marshal.OffsetOf(typeof(State),"Frame").ToInt32()==44,"Public state field offsets.");
            AudioBufferChecks();Console.WriteLine("PASS independent ABI and concurrent PCM queue, gain, overflow and silence.");
            for(int run=0;run<2;run++)using(var engine=new Engine(args[0],args[1]))
            {
                var silence=Enumerable.Repeat(short.MaxValue,8192).ToArray();
                int written=engine.Audio(silence);
                Check(written==1088&&silence[written*2-1]!=short.MaxValue&&silence[written*2]==short.MaxValue,"Audio returns the full two-block stereo write, without dropping its second half.");
                bool shortBuffer=false;try{engine.Audio(new short[128],1);}catch(ArgumentException){shortBuffer=true;}
                Check(shortBuffer,"Small desired counts cannot permit an undersized native output buffer.");
                Check(engine.Atlas.Any(b=>b!=0),"Atlas extracted at runtime from the supplied ROM.");engine.SetWorld(Floor());
                bool duplicate=false;try{new Engine(args[0],args[1]);}catch(InvalidOperationException){duplicate=true;}Check(duplicate,"Second owner cannot reset live native globals.");
                using(var mario=engine.Spawn(0,200))
                {
                    bool staleWorld=false;try{engine.SetWorld(Floor());}catch(InvalidOperationException){staleWorld=true;}Check(staleWorld,"Unsupported live static-world replacement cannot invalidate actor floor pointers.");
                    for(int i=0;i<30;i++)mario.Tick(Input.Sideways(0,false,false,false));
                    Check(Math.Abs(mario.State.Y)<2,"Real native gravity lands on independently supplied collision.");
                    float start=mario.State.X,maxY=mario.State.Y;
                    for(int i=0;i<55;i++){mario.Tick(Input.Sideways(.8f,i<8,false,false));maxY=Math.Max(maxY,mario.State.Y);}
                    Check(mario.State.X>start+100&&maxY>60,"Real native move and jump respond through the new host.");
                    Check(mario.Geometry.Triangles>0&&mario.Geometry.Triangles<=GeometryFrame.MaxTriangles,"Real native model geometry returned.");
                    Check(mario.Geometry.Positions.Take(mario.Geometry.Triangles*9).All(v=>!float.IsNaN(v)&&!float.IsInfinity(v)),"Geometry is finite.");
                    engine.Sound(0x24008081u);bool heard=false;var pcm=new short[8192];
                    for(int i=0;i<12;i++){int frames=engine.Audio(pcm);Check(frames>0&&frames*2<=pcm.Length,"Native audio frame count is bounded.");heard|=pcm.Take(frames*2).Any(s=>s!=0);}
                    Check(heard,"Real native audio reaches independent PCM output.");
                    Exception mixerError=null;
                    var mixer=new Thread(()=>{try{var mixed=new short[8192];for(int i=0;i<64;i++)engine.Audio(mixed);}catch(Exception error){mixerError=error;}});
                    mixer.Start();
                    for(int i=0;i<80;i++)mario.Tick(Input.Sideways(0,false,i%20==0,false));
                    using(var presentation=engine.Spawn(300,200))presentation.Tick(default(Input));
                    Check(mixer.Join(5000),"Concurrent native mixer finishes without deadlocking simulation.");
                    if(mixerError!=null)throw mixerError;
                }
                Check(engine.LiveActors==0,"Released presentation actor no longer runs.");
                var recycled=engine.Spawn(100,200);recycled.Tick(default(Input));recycled.Dispose();engine.Audio(new short[8192]);
                Check(engine.LiveActors==0,"Native actors remain safe for audio after release/reuse.");
            }
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="MarioHollow"),"Original mashup assembly was never loaded.");
            Check(!typeof(Engine).Assembly.GetReferencedAssemblies().Any(a=>a.Name=="MarioHollow"||a.Name=="BepInEx"||a.Name=="Assembly-CSharp"),"Independent core has no original bridge/loader/game assembly reference.");
            Console.WriteLine("PASS real ROM/native simulation, collision landing, movement/jump, geometry, audible PCM, parked actor reuse and shutdown/reinitialization WITHOUT original MarioHollow.dll. No game or window started.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
