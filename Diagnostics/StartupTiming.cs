using System;
using System.Diagnostics;
using System.Globalization;

namespace HollowMario
{
    // A bounded startup report in the existing mod log. No per-frame observer,
    // hooks, external file, or retained gameplay logger is installed.
    internal sealed class StartupTiming : IDisposable
    {
        private readonly Action<string> write;
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private string stage;
        private long stageStart;
        private bool completed;

        internal StartupTiming(Action<string> write, string version, double gameAgeSeconds)
        {
            this.write = write;
            string processAge = "unavailable";
            try
            {
                using (var process = Process.GetCurrentProcess())
                    processAge = (DateTime.UtcNow-process.StartTime.ToUniversalTime()).TotalSeconds.ToString("F3",CultureInfo.InvariantCulture)+"s";
            }
            catch (Exception) { }
            Report("begin "+version+"; utc="+DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture)
                +"; process age="+processAge+"; game uptime="+gameAgeSeconds.ToString("F3",CultureInfo.InvariantCulture)+"s");
        }

        internal void Next(string name)
        {
            if (completed) return;
            FinishStage();
            stage=name;stageStart=elapsed.ElapsedMilliseconds;
            Report("begin "+stage+"; total="+elapsed.ElapsedMilliseconds+"ms");
        }

        internal void Complete()
        {
            if (completed) return;
            FinishStage();completed=true;
            Report("finished; total="+elapsed.ElapsedMilliseconds+"ms");
        }

        private void FinishStage()
        {
            if (stage!=null) Report("finished "+stage+"; step="+(elapsed.ElapsedMilliseconds-stageStart)
                +"ms; total="+elapsed.ElapsedMilliseconds+"ms");
        }

        private void Report(string message)
        {
            try { write?.Invoke("startup: "+message); }
            catch (Exception) { /* Observations must not break initialization or hide its exception. */ }
        }

        public void Dispose()
        {
            if (completed) return;
            completed=true;
            Report("stopped during "+(stage??"entry")+"; step="+(elapsed.ElapsedMilliseconds-stageStart)
                +"ms; total="+elapsed.ElapsedMilliseconds+"ms");
        }
    }
}
