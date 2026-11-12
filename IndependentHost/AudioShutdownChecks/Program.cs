// Unity lifecycle stand-ins: exercises the real AudioOutput source without a game.
using System;
using System.Reflection;
using System.Threading;
using HollowMario.Host;
using HollowMario.Host.Unity;
class Program
{
    static int Main()
    {
        foreach(bool destroyed in new[]{false,true})
        {
            var output=new AudioOutput(new Engine());
            var source=(UnityEngine.AudioSource)typeof(AudioOutput).GetField("source",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(output);
            var filter=(DspOutput)typeof(AudioOutput).GetField("filter",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(output);
            if(destroyed){UnityEngine.Object.Destroy(source);UnityEngine.Object.Destroy(filter);}
            try{output.Dispose();output.Dispose();}
            catch(Exception e){Console.WriteLine("FAIL shutdown: "+e.Message);return 1;}
            var thread=(Thread)typeof(AudioOutput).GetField("producer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(output);
            if(thread.IsAlive||(!destroyed&&filter.Output!=null))throw new Exception("Audio producer or callback survived disposal");
        }
        Console.WriteLine("PASS normal and already-destroyed Unity audio cleanup; repeated disposal; producer joined");return 0;
    }
}
namespace HollowMario.Host
{
    public class Engine { public int Audio(short[] samples,int frames,uint queued){return 544;} }
}
namespace UnityEngine
{
    public class Object
    {
        internal bool destroyed;
        public static implicit operator bool(Object value)=>!ReferenceEquals(value,null)&&!value.destroyed;
        public static void Destroy(Object value){if(!ReferenceEquals(value,null))value.destroyed=true;}
        public static void DontDestroyOnLoad(Object value){}
    }
    public class MonoBehaviour:Object {}
    public class GameObject:Object
    {
        public GameObject(string name,params Type[] types){}
        public T GetComponent<T>() where T:new()=>new T();
        public T AddComponent<T>() where T:new()=>new T();
    }
    public class AudioSource:Object
    {
        public float spatialBlend,volume;public bool loop;public int priority;public AudioClip clip;
        public void Play(){}
        public void Stop(){if(destroyed)throw new NullReferenceException("AudioSource already destroyed");}
    }
    public class AudioClip:Object { public static AudioClip Create(string name,int length,int channels,int hz,bool stream)=>new AudioClip(); }
    public static class AudioSettings { public static int outputSampleRate=>48000; }
}
