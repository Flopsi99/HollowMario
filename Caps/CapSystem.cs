using System;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    internal static class CapSystem
    {
        private const BindingFlags Any=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private static Host host;
        private static CapSession session;
        private static ManualLogSource log;
        private static ConfigEntry<float> upkeep;
        private static ConfigEntry<bool> lavaRefresh;
        private static Type heroType;
        private static FieldInfo playerData;
        private static int failures;
        internal static CapKind Current=>session?.Current??CapKind.None;
        internal static bool Active=>session?.Active==true&&host?.Available==true;
        internal static bool Vanished=>Active&&Current==CapKind.Vanish;
        internal static bool Metallic=>Current==CapKind.Metal&&Active;
        internal static bool MetalFor(object hero)=>Metallic&&ReferenceEquals(host.Hero,hero);
        internal static bool RefreshMetalAirMoves=>lavaRefresh?.Value==true;
        internal static bool CanOpenSelector=>host?.Available==true&&host.Soul>0
            &&(Unlocked(CapKind.Wing)||Unlocked(CapKind.Vanish)||Unlocked(CapKind.Metal));
        internal static event Action Changed;

        internal static void Arm(Harmony harmony,Type controller,Type native,ConfigFile config,ManualLogSource logger)
        {
            log=logger;
            upkeep=config.Bind("Caps","SoulPerSecond",8f,"Passive cap upkeep. Nailmaster's Glory reduces it by 25% (8 to 6 per second).");
            lavaRefresh=config.Bind("Caps","MetalLavaBounceRefreshAirMoves",true,
                "Experimental: each new Metal lava bounce restores double jump, air dash and dash/shade recharge. Requires the normal upgrades; overlap contacts do not refresh again.");
            config.Save();
            // Migrate the previous shipped default while keeping custom rates intact.
            if(upkeep.Value==6f){upkeep.Value=8f;config.Save();}
            try { CapNative.Configure(native,controller); }
            catch(Exception error){Warn(error);}
            MarioAppearance.Configure(controller);
            WingCap.Arm(harmony,native,logger);
            MetalCap.Arm(harmony,controller,logger);
            AcidSafety.AdditionalProtection=MetalFor;
            ShadeGateCollision.AdditionalPassage=()=>Vanished;
            SurfaceSoul.CanRefill=()=>!Active;
            Type data=AccessTools.TypeByName("PlayerData"),hero=AccessTools.TypeByName("HeroController"),health=AccessTools.TypeByName("HealthManager");
            Patch(harmony,AccessTools.Method(data,"AddMPCharge",new[]{typeof(int)}),nameof(BeforeSoul));
            Patch(harmony,AccessTools.Method(hero,"SoulGain"),nameof(BeforeSoulGain));
            Patch(harmony,AccessTools.Method(hero,"TakeDamage"),nameof(BeforeDamage),Priority.First+200);
            Patch(harmony,AccessTools.Method(health,"Hit"),nameof(BeforeEnemyHit));
            Patch(harmony,AccessTools.Method(health,"ApplyExtraDamage"),nameof(BeforeEnemyHit));
            MethodInfo getState=AccessTools.Method(hero,"GetState",new[]{typeof(string)});
            if(getState!=null)harmony.Patch(getState,postfix:new HarmonyMethod(typeof(CapSystem),nameof(StateQuery)));
        }

        private static void Patch(Harmony h,MethodInfo target,string method,int priority=Priority.Normal)
        { if(target!=null)h.Patch(target,prefix:new HarmonyMethod(typeof(CapSystem),method){priority=priority});else log?.LogWarning("Cap integration missing: "+method); }

        internal static void Tick(object controller,KnightResetFollow follow,float dt)
        {
            try
            {
                object hero=controller==null?null:follow?.Hero(controller);
                if(hero==null||!follow.MarioAlive(controller)) { Stop();return; }
                if(heroType!=hero.GetType()){heroType=hero.GetType();playerData=AccessTools.Field(heroType,"playerData");}
                object data=playerData?.GetValue(hero);
                if(data==null){Stop();return;}
                int id=follow.MarioId(controller);
                if(host==null||!ReferenceEquals(host.Hero,hero)||!ReferenceEquals(host.Data,data)||host.Id!=id)
                { Stop();host=new Host(controller,follow,hero,data,id);session=new CapSession(host); }
                if(host.ResetCount!=follow.ResetsFollowed) { host.ResetCount=follow.ResetsFollowed;Stop(); }
                CapKind before=Current;
                session.DrainPerSecond=Math.Max(1,upkeep?.Value??8);
                session.Tick(dt);
                if(before!=Current)Changed?.Invoke();
            }
            catch(Exception error){Stop();Warn(error);}
        }

        internal static bool Select(CapKind kind,out string message)
        {
            if(session==null) { message="Caps need Mario in control.";return false; }
            try
            {
                bool result=session.Select(kind,out message);
                Changed?.Invoke();return result;
            }
            catch(Exception error){Stop();Warn(error);message="The cap could not be equipped.";return false;}
        }
        internal static bool Unlocked(CapKind kind)=>host?.Unlocked(CapDefinition.For(kind))==true;
        internal static void Stop() { if(session?.Active!=true)return;session.End();Changed?.Invoke(); }
        internal static void PayMetalHit()
        {
            if(!Metallic)return;
            int cost=Math.Max(1,CapDefinition.Metal.HitSoulCost-(CapAccess.Flag(host.Data,"equippedCharm_20")?2:0)
                -(CapAccess.Flag(host.Data,"equippedCharm_21")?4:0));
            session.Spend(cost);
            if(!session.Active)Changed?.Invoke();
        }
        internal static void PayMetalImpact()
        {
            // A native hit may already have begun its recoil/reset before its finalizer.
            if(Current!=CapKind.Metal||session?.Active!=true)return;
            session.Spend(10);
            if(!session.Active)Changed?.Invoke();
        }

        private static bool BeforeSoul(object __instance,ref int amount,ref bool __result)
        {
            if(!Active||!ReferenceEquals(__instance,host.Data)||amount<=0)return true;
            amount=session.AllowSoul(amount);
            if(amount>0)return true;
            __result=false;return false;
        }
        private static bool BeforeSoulGain(object __instance)=>!Active||!ReferenceEquals(__instance,host.Hero);
        private static bool BeforeEnemyHit()=>!Vanished;
        private static void StateQuery(object __instance,string __0,ref bool __result)
        { if(Vanished&&ReferenceEquals(__instance,host.Hero)&&__0=="shadowDashing")__result=true; }

        private static bool BeforeDamage(object __instance,int __3)
        {
            // TakeDamage uses 1 for ordinary recoil damage, including invulnerable
            // creatures/obstacles with no HealthManager. Native shade protection uses
            // this same category. Keep spike/acid/lava/pit hazard handling (2..5).
            return __3!=1||!Vanished||!ReferenceEquals(__instance,host.Hero);
        }

        private static void Warn(Exception error)
        { if(failures++<6)log?.LogWarning("Cap system: "+error.GetBaseException().Message); }

        private sealed class Host:ICapHost
        {
            internal readonly object Controller,Hero,Data;
            internal readonly int Id;
            internal int ResetCount;
            private readonly KnightResetFollow follow;
            private readonly FieldInfo primary,reserve,glory;
            private readonly FieldInfo[] unlocks=new FieldInfo[4];
            private readonly MethodInfo spendPrimary,spendReserve,clear;
            internal Host(object controller,KnightResetFollow follow,object hero,object data,int id)
            {
                Controller=controller;this.follow=follow;Hero=hero;Data=data;Id=id;ResetCount=follow.ResetsFollowed;
                Type t=data.GetType();primary=AccessTools.Field(t,"MPCharge");reserve=AccessTools.Field(t,"MPReserve");glory=AccessTools.Field(t,"equippedCharm_26");
                foreach(CapKind cap in new[]{CapKind.Wing,CapKind.Vanish,CapKind.Metal})unlocks[(int)cap]=AccessTools.Field(t,CapDefinition.For(cap).Unlock);
                Type h=hero.GetType();spendPrimary=AccessTools.Method(h,"TakeMPQuick",new[]{typeof(int)});
                spendReserve=AccessTools.Method(h,"TakeReserveMP",new[]{typeof(int)});clear=AccessTools.Method(h,"ClearMPSendEvents");
            }
            public bool Available=>CapNative.Ready&&Data!=null&&follow.MarioAlive(Controller)&&follow.MarioId(Controller)==Id&&!follow.Busy(Controller)
                &&!(Hero is Behaviour behaviour&&behaviour.enabled);
            public int Soul=>Math.Max(0,(int)primary.GetValue(Data))+Math.Max(0,(int)reserve.GetValue(Data));
            public bool Glory=>glory?.GetValue(Data) is bool on&&on;
            public bool Unlocked(CapDefinition cap)=>cap!=null&&unlocks[(int)cap.Kind]?.GetValue(Data) is bool yes&&yes;
            public bool Apply(CapDefinition cap)
            {
                if(cap.Kind==CapKind.Wing&&!WingCap.Ready)return false;
                if(cap.Kind==CapKind.Metal&&!MetalCap.Ready)return false;
                CapNative.Apply(Controller,Id,cap);
                MarioAppearance.Vanish(Controller,cap.Kind==CapKind.Vanish);
                MarioAppearance.Metal(Controller,cap.Kind==CapKind.Metal);
                if(cap.Kind==CapKind.Vanish)ShadeGateCollision.Open(Controller,follow);
                return true;
            }
            public void Clear()
            {
                // Session state is cleared first: even native teardown failure cannot leave
                // the generic damage/soul hooks active after a handover.
                try { CapNative.Clear(Controller,Id); }
                catch(Exception error){Warn(error);}
                MarioAppearance.Vanish(Controller,false);
                MarioAppearance.Metal(Controller,false);
                MetalCap.ForgetAttack();
            }
            public int Spend(int amount)
            {
                int before=Soul,p=(int)primary.GetValue(Data),take=Math.Min(p,amount);
                if(take>0)spendPrimary.Invoke(Hero,new object[]{amount});
                if(amount>take)spendReserve.Invoke(Hero,new object[]{amount-take});
                if(Soul==0)clear?.Invoke(Hero,null);
                return before-Soul;
            }
        }
    }
}
