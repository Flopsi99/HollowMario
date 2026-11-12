using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace HollowMario.Host.Unity
{
    // Runs on Unity's actual DSP block cadence. A streamed clip can ask for an
    // entire clip-sized block, which exceeds a bounded queue and inserts silence.
    public sealed class AudioOutput:IDisposable
    {
        private readonly Engine engine;
        private readonly PcmBuffer queue=new PcmBuffer(16384);
        private readonly Thread producer;
        private readonly GameObject owner;
        private readonly AudioSource source;
        private readonly AudioClip clip;
        private readonly DspOutput filter;
        private readonly int sampleRate;
        private volatile bool stopping;
        private bool disposed;
        private Exception producerError;
        private float gain=.8f;
        private double phase;
        private long callbacks,missingFrames,mixes,largestMixTicks;
        private int largestRequest,lastChannels;
        public Exception Error=>producerError;
        public string Diagnostics=>"deviceHz="+sampleRate+", callbacks="+Interlocked.Read(ref callbacks)+", channels="+Volatile.Read(ref lastChannels)+", maxSamples="+Volatile.Read(ref largestRequest)+", underflowFrames="+Interlocked.Read(ref missingFrames)+", bufferedFrames="+(queue.Available/2)+", mixes="+Interlocked.Read(ref mixes)+", peakMixMs="+(Interlocked.Read(ref largestMixTicks)*1000.0/Stopwatch.Frequency).ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+", error="+(producerError?.Message??"none");
        public AudioOutput(Engine engine)
        {
            this.engine=engine??throw new ArgumentNullException(nameof(engine));
            sampleRate=AudioSettings.outputSampleRate;
            owner=new GameObject("Hollow Mario independent audio",typeof(AudioSource));UnityEngine.Object.DontDestroyOnLoad(owner);
            source=owner.GetComponent<AudioSource>();source.spatialBlend=0;source.loop=true;source.volume=1;source.priority=0;
            // Silent carrier only; real PCM is supplied in OnAudioFilterRead.
            clip=AudioClip.Create("Hollow Mario DSP carrier",1024,1,sampleRate,false);source.clip=clip;
            filter=owner.AddComponent<DspOutput>();filter.Output=this;
            producer=new Thread(Produce){IsBackground=true,Name="Hollow Mario PCM producer"};producer.Start();source.Play();
        }
        public float Volume{get=>Volatile.Read(ref gain);set=>Volatile.Write(ref gain,float.IsNaN(value)?.8f:Math.Max(0f,Math.Min(1f,value)));}
        private void Produce()
        {
            var samples=new short[8192];
            try
            {
                while(!stopping)
                {
                    if(queue.Available>=6144){Thread.Sleep(1);continue;}
                    long before=Stopwatch.GetTimestamp();int frames=engine.Audio(samples,1088,(uint)(queue.Available/2));long ticks=Stopwatch.GetTimestamp()-before;
                    Interlocked.Increment(ref mixes);if(ticks>Interlocked.Read(ref largestMixTicks))Interlocked.Exchange(ref largestMixTicks,ticks);
                    if(!queue.Write(samples,frames*2))throw new InvalidOperationException("PCM producer exceeded its bounded queue.");
                }
            }
            catch(Exception error){producerError=error;stopping=true;}
        }
        internal void Read(float[] samples,int channels)
        {
            if(stopping){Array.Clear(samples,0,samples.Length);return;}
            int received=queue.Resample(samples,channels,sampleRate,ref phase,Volatile.Read(ref gain));Interlocked.Increment(ref callbacks);
            if(received<samples.Length/channels)Interlocked.Add(ref missingFrames,samples.Length/channels-received);
            if(samples.Length>largestRequest)Volatile.Write(ref largestRequest,samples.Length);Volatile.Write(ref lastChannels,channels);
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;stopping=true;
            // Unity may destroy the carrier before the plugin during shutdown.
            // Always join the producer before its native engine is released.
            try
            {
                if(filter)filter.Output=null;
                if(source)source.Stop();
            }
            finally
            {
                producer.Join();queue.Clear();
                if(owner)UnityEngine.Object.Destroy(owner);
                if(clip)UnityEngine.Object.Destroy(clip);
            }
        }
    }
    public sealed class DspOutput:MonoBehaviour
    {
        internal AudioOutput Output;
        private void OnAudioFilterRead(float[] data,int channels)
        {var output=Output;if(output==null)Array.Clear(data,0,data.Length);else output.Read(data,channels);}
    }
}
