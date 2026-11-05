using System;
using System.Globalization;

namespace MarioHollow
{
    public static class HostTrace
    {
        public static Action<string> Sink;
        public static bool Enabled=true;
        private static int lines;
        public static void Write(string message)
        {
            if(!Enabled||Sink==null)return;
            // Native/Unity hot paths emit state changes or periodic totals,
            // never every vertex or every frame. Avoid megabytes of stack spam.
            Sink("Host trace #"+(++lines).ToString(CultureInfo.InvariantCulture)+": "+message);
        }
    }
}
