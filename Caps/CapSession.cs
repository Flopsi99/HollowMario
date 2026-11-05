using System;

namespace HollowMario
{
    // One authority for cap state/fuel. No Unity, input, native pointer or game-state dependencies.
    internal sealed class CapSession
    {
        private readonly ICapHost host;
        private double debt;
        private int ceiling;
        internal CapKind Current { get; private set; }
        internal bool Active => Current!=CapKind.None;
        internal double DrainPerSecond { get; set; } = 8;
        internal CapSession(ICapHost host) { this.host=host; }

        internal bool Select(CapKind kind, out string message)
        {
            message="";
            if(kind==CapKind.None) { End(); return true; }
            CapDefinition cap=CapDefinition.For(kind);
            if(cap==null) { message="Unknown cap."; return false; }
            if(!host.Available) { message="Caps need Mario in control."; return false; }
            if(!host.Unlocked(cap)) { message=cap.Name+": learn its Nail Art first."; return false; }
            if(!cap.Implemented) { message=cap.Name+" is coming in a later build."; return false; }
            if(host.Soul<=0) { message="You need soul to wear a cap."; return false; }
            if(Current==kind) return true;
            double pendingDebt=debt;
            End();
            try
            {
                if(!host.Apply(cap)) { host.Clear();message="The cap could not be equipped.";return false; }
            }
            catch { host.Clear();throw; }
            Current=kind; ceiling=host.Soul; debt=pendingDebt;
            return true;
        }

        internal void Tick(double dt)
        {
            if(!Active) return;
            if(!host.Available || !host.Unlocked(CapDefinition.For(Current)) || host.Soul<=0) { End(); return; }
            if(dt<=0 || double.IsNaN(dt) || double.IsInfinity(dt)) return;
            double rate=Math.Max(1,DrainPerSecond)*CapDefinition.For(Current).UpkeepMultiplier*(host.Glory?.75:1);
            debt+=dt*rate;
            int due=(int)Math.Min(host.Soul,Math.Floor(debt+1e-8));
            // Native TakeMPQuick animates amounts above one. Batch tiny upkeep into pairs,
            // but always consume the final point at its proper deadline.
            if(due==0 || (due==1 && host.Soul>1)) return;
            ceiling=Math.Max(0,ceiling-due);
            int spent=host.Spend(due);
            debt-=due;
            ceiling=Math.Min(ceiling,host.Soul);
            if(spent!=due || host.Soul<=0) End();
        }

        internal int AllowSoul(int requested)
        {
            if(!Active || requested<=0) return requested;
            // Reserve->primary transfers may refill a temporary hole in this total.
            // Generation cannot increase the cap's remaining, already-owned fuel budget.
            return Math.Min(requested,Math.Max(0,ceiling-host.Soul));
        }

        internal int Spend(int amount)
        {
            if(!Active||amount<=0)return 0;
            int due=Math.Min(amount,host.Soul);
            ceiling=Math.Max(0,ceiling-due);
            int spent=host.Spend(due);
            ceiling=Math.Min(ceiling,host.Soul);
            if(spent!=due||host.Soul<=0)End();
            return spent;
        }

        internal void End()
        {
            if(!Active) return;
            Current=CapKind.None; debt=0; ceiling=0;
            host.Clear();
        }
    }
}
