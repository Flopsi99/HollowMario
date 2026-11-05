using System;
using System.Reflection;

namespace HollowMario
{
    // An in-game test pad. Uses InControl's normal device update/commit path;
    // never writes HeroController, PlayerData, an FSM or a save to unlock play.
    internal sealed class MeltyBenchInput : IDisposable
    {
        readonly object device;
        readonly Type manager;
        readonly MethodInfo update, clear, detach;
        readonly EventInfo beforeCommit, afterActions;
        readonly PropertyInfo suspend;
        readonly object priorSuspend;
        readonly object[] controls;
        readonly Action<ulong,float> feed, observe;
        readonly Func<object> downAction;
        bool attached, subscribedFeed, subscribedObserve, disposed;
        internal bool Held;
        internal Func<string,bool> Pressed{get;set;}
        internal bool Sent { get; private set; }
        internal bool Acknowledged { get; private set; }

        internal MeltyBenchInput(Assembly game,Func<object> readDownAction,string[] controlNames=null)
        {
            manager=game.GetType("InControl.InputManager",true);
            Type deviceType=game.GetType("InControl.InputDevice",true);
            Type controlType=game.GetType("InControl.InputControlType",true);
            const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
            if(!Equals(manager.GetProperty("IsSetup",flags).GetValue(null,null),true))
                throw new InvalidOperationException("Hollow Knight's input system is not ready.");
            const BindingFlags instance=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
            update=deviceType.GetMethod("UpdateWithState",instance,null,new[]{controlType,typeof(bool),typeof(ulong),typeof(float)},null);
            clear=deviceType.GetMethod("ClearInputState",instance,null,Type.EmptyTypes,null);
            detach=manager.GetMethod("DetachDevice",flags);
            beforeCommit=manager.GetEvent("OnUpdateDevices",flags);
            afterActions=manager.GetEvent("OnUpdate",flags);
            suspend=manager.GetProperty("SuspendInBackground",flags);
            MethodInfo add=deviceType.GetMethod("AddControl",new[]{controlType,typeof(string)});
            MethodInfo attach=manager.GetMethod("AttachDevice",flags);
            if(update==null||clear==null||detach==null||beforeCommit==null||afterActions==null||suspend==null||add==null||attach==null)
                throw new MissingMemberException("The supported InControl test-device interface is incomplete.");
            device=Activator.CreateInstance(deviceType,new object[]{"Hollow Mario test pad"});
            string[] names=controlNames??new[]{"LeftStickUp","LeftStickDown","LeftStickLeft","LeftStickRight","DPadUp","DPadDown","DPadLeft","DPadRight"};
            controls=new object[names.Length];
            for(int i=0;i<names.Length;i++)
            {
                controls[i]=Enum.Parse(controlType,names[i]);
                add.Invoke(device,new[]{controls[i],(object)names[i]});
            }
            downAction=readDownAction;
            priorSuspend=suspend.GetValue(null,null);
            feed=Feed;observe=Observe;
            try
            {
                suspend.SetValue(null,false,null);
                beforeCommit.GetAddMethod(true).Invoke(null,new object[]{feed});
                subscribedFeed=true;
                afterActions.GetAddMethod(true).Invoke(null,new object[]{observe});
                subscribedObserve=true;
                attach.Invoke(null,new[]{device});
                attached=Equals(deviceType.GetProperty("IsAttached").GetValue(device,null),true);
                if(!attached)throw new InvalidOperationException("The game did not attach its test pad.");
            }
            catch{Dispose();throw;}
        }

        void Feed(ulong tick,float dt)
        {
            // Feed all directions every update, including releases, before
            // InputManager commits devices and updates HeroActions bindings.
            for(int i=0;i<controls.Length;i++)
                update.Invoke(device,new[]{controls[i],(object)(Pressed!=null?Pressed(controls[i].ToString()):Held&&(i==1||i==5)),tick,dt});
            if(Held)Sent=true;
        }
        void Observe(ulong tick,float dt)
        {
            if(!Held)return;
            object action=downAction();
            if(action!=null&&Equals(action.GetType().GetProperty("IsPressed").GetValue(action,null),true))Acknowledged=true;
        }
        public void Dispose()
        {
            if(disposed)return;
            disposed=true;Held=false;
            try
            {
                if(subscribedFeed)beforeCommit.GetRemoveMethod(true).Invoke(null,new object[]{feed});
                if(subscribedObserve)afterActions.GetRemoveMethod(true).Invoke(null,new object[]{observe});
            }
            finally
            {
                try{clear?.Invoke(device,null);if(attached)detach?.Invoke(null,new[]{device});}
                finally{if(suspend!=null&&priorSuspend!=null)suspend.SetValue(null,priorSuspend,null);}
            }
        }
    }
}
