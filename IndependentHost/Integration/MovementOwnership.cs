using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace MarioHollow
{
    // One boundary between native scripted actions and Mario locomotion. Being
    // parked for a spell/respawn never authorizes ordinary Knight player input.
    public static class MovementOwnership
    {
        private static Func<object,bool> focus,dash;
        private static readonly FieldInfo held=AccessTools.Field(typeof(HeroController),"controlReqlinquished"),
            accepting=AccessTools.Field(typeof(HeroController),"acceptingInput");
        internal static void Install(Harmony hooks,Assembly companion)
        {
            focus=(Func<object,bool>)Delegate.CreateDelegate(typeof(Func<object,bool>),AccessTools.Method(companion.GetType("HollowMario.KnightFocus",true),"KeepOut"));
            dash=(Func<object,bool>)Delegate.CreateDelegate(typeof(Func<object,bool>),AccessTools.Method(companion.GetType("HollowMario.KnightDash",true),"KeepOut"));
            Patch("LookForQueueInput",nameof(BeforePlayerInput));
            foreach(string method in new[]{"Update","FixedUpdate"})Patch(method,nameof(BeforeController));
            foreach(string method in new[]{"Move","LookForInput"})Patch(method,nameof(BeforeWalk));
            foreach(string method in new[]{"Jump","DoubleJump","Dash","BackDash","HeroJump","HeroJumpNoEffect","HeroDash","DoWallJump","DoDoubleJump"})
                Patch(method,nameof(BeforeJumpOrDash));
            hooks.Patch(AccessTools.Method(typeof(HeroController),"StartRecoil"),postfix:new HarmonyMethod(typeof(MovementOwnership),nameof(AfterRecoil)));
            void Patch(string name,string guard)=>hooks.Patch(AccessTools.Method(typeof(HeroController),name),prefix:new HarmonyMethod(typeof(MovementOwnership),guard){priority=Priority.First+300});
        }
        public static bool Selected(HeroController hero)
        {
            var controller=MarioController.Instance;
            return !ReferenceEquals(controller,null)&&controller.Wanted&&Sm64.Ready
                &&(ReferenceEquals(controller.hero,hero)||ReferenceEquals(HeroController.SilentInstance,hero));
        }
        public static bool NativeBodyRequired(MarioController controller,HeroController hero)
        {
            var state=hero.cState;
            return state==null||state.dead||state.hazardDeath||state.hazardRespawning||state.transitioning
                ||(bool)held.GetValue(hero)||!(bool)accepting.GetValue(hero)
                ||(focus!=null&&focus(controller))||(dash!=null&&dash(controller));
        }
        private static bool BeforePlayerInput(HeroController __instance)=>!Selected(__instance);
        private static bool BeforeController(HeroController __instance)
            =>!Selected(__instance)||(MarioController.Instance.MarioId<0&&NativeBodyRequired(MarioController.Instance,__instance));
        private static bool BeforeWalk(HeroController __instance)
            =>!Selected(__instance)||(!__instance.cState.hazardDeath&&!__instance.cState.hazardRespawning
                &&MarioController.Instance.MarioId<0&&NativeBodyRequired(MarioController.Instance,__instance));
        private static bool BeforeJumpOrDash(HeroController __instance)
            =>!Selected(__instance)||(__instance.cState.transitioning&&MarioController.Instance.MarioId<0);
        private static void AfterRecoil(HeroController __instance,ref IEnumerator __result)
        {
            if(__result!=null&&Selected(__instance))__result=HitEffects(__instance,__result);
        }
        public static IEnumerator HitEffects(HeroController hero,IEnumerator original)
        {
            // HK owns hit effects, pause/freeze and protection. Its iterator may
            // finish after a hazard; none of its recoil writes may own movement.
            try
            {
                ClearRecoil(hero);
                while(true)
                {
                    bool more;
                    try{more=original.MoveNext();}
                    finally{ClearRecoil(hero);}
                    if(!more)yield break;
                    yield return original.Current;
                }
            }
            finally{try{(original as IDisposable)?.Dispose();}finally{ClearRecoil(hero);}}
        }
        private static void ClearRecoil(HeroController hero)
        {if(Selected(hero)){hero.cState.recoiling=false;hero.cState.recoilFrozen=false;}}
    }
}
