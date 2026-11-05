using System;

namespace HollowMario.Host
{
    // The audio callback only drains managed PCM; native mixing belongs to a
    // producer outside Unity's real-time callback. Underruns yield silence.
    public sealed class PcmBuffer
    {
        private readonly float[] data;
        private int read,write,count;
        private readonly object gate=new object();
        public PcmBuffer(int capacity=32768){if(capacity<2)throw new ArgumentOutOfRangeException(nameof(capacity));data=new float[capacity];}
        public int Available{get{lock(gate)return count;}}
        public int Free{get{lock(gate)return data.Length-count;}}
        public bool Write(short[] samples,int length,float volume=1f)
        {
            if(samples==null||length<0||length>samples.Length)throw new ArgumentOutOfRangeException(nameof(length));
            lock(gate)
            {
                if(length>data.Length-count)return false;
                for(int i=0;i<length;i++){data[write]=samples[i]*(Math.Max(0,Math.Min(1,volume))/32768f);write=(write+1)%data.Length;}count+=length;return true;
            }
        }
        public int Read(float[] output)
        {
            if(output==null)throw new ArgumentNullException(nameof(output));
            lock(gate){int copied=Math.Min(output.Length,count);for(int i=0;i<copied;i++){output[i]=data[read];read=(read+1)%data.Length;}Array.Clear(output,copied,output.Length-copied);count-=copied;return copied;}
        }
        public void Clear(){lock(gate){count=read=write=0;}}
        // Unity's DSP runs at the device rate, not necessarily SM64's 32 kHz.
        // Keep fractional source position across callbacks; never wait or call
        // the native engine from the audio thread.
        public int Resample(float[] output,int channels,int outputRate,ref double phase,float gain)
        {
            if(output==null||channels<1||outputRate<8000||output.Length%channels!=0)throw new ArgumentException("Invalid DSP buffer.");
            double ratio=32000.0/outputRate;int frames=output.Length/channels,written=0;
            lock(gate)
            {
                for(;written<frames;written++)
                {
                    int advance=(int)(phase+ratio);
                    if(count<Math.Max(2,advance)*2)break;
                    float left=(float)(data[read]+(data[(read+2)%data.Length]-data[read])*phase)*gain;
                    float right=(float)(data[(read+1)%data.Length]+(data[(read+3)%data.Length]-data[(read+1)%data.Length])*phase)*gain;
                    for(int channel=0;channel<channels;channel++)output[written*channels+channel]=channels==1?(left+right)*.5f:(channel%2==0?left:right);
                    phase+=ratio;advance=(int)phase;phase-=advance;read=(read+advance*2)%data.Length;count-=advance*2;
                }
                Array.Clear(output,written*channels,output.Length-written*channels);
            }
            return written;
        }
    }
}
