using System;
using System.Globalization;

namespace MarioHollow
{
    public static class HostTrace
    {
        public static Action<string> Sink;
        public static bool Available
        {
            get
            {
#if HOLLOW_MARIO_DIAGNOSTICS
                return true;
#else
                return false;
#endif
            }
        }
        public static bool Enabled=false;
        private static int lines;
        [System.Diagnostics.Conditional("HOLLOW_MARIO_DIAGNOSTICS")]
        public static void Write(string message)
        {
            if(!Available||!Enabled||Sink==null)return;
            // Native/Unity hot paths emit state changes or periodic totals,
            // never every vertex or every frame. Avoid megabytes of stack spam.
            Sink("Host trace #"+(++lines).ToString(CultureInfo.InvariantCulture)+": "+message);
        }
    }
}
