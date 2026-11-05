using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Only the image falls. Never move the Hero, intercept the death coroutine,
    // wait for terrain, or change the native fade, Shade, save or respawn timing.
    internal static class DeathMario
    {
        private static Type heroType,controllerType;
        private static MethodInfo sound;
        private static Component hero;
        private static MeshRenderer nativeBody;
        private static bool oldHidden,landed,observedDeath;
        private static float vertical,gravity,width;
        private static readonly RaycastHit2D[] hits=new RaycastHit2D[16];
        internal const uint DeathSound=0x2415FF80u;
        internal static bool Active {get;private set;}
        internal static Vector3 Feet {get;private set;}
        internal static int Animation=>landed?0x03:0x02;
        internal static void Arm(Harmony harmony,Type controller,Type native)
        {
            heroType=AccessTools.TypeByName("HeroController");controllerType=controller;
            sound=AccessTools.Method(native,"sm64_play_sound_global");
            harmony.Patch(AccessTools.Method(heroType,"Die"),prefix:new HarmonyMethod(typeof(DeathMario),nameof(BeforeDeath)));
            DeathCap.Arm(harmony,controller,native);
        }
        internal static bool OwnsEffect(GameObject effect)=>Active&&hero!=null&&ReferenceEquals(CapAccess.Read(hero,"heroDeathPrefab"),effect);
        private static void BeforeDeath(object __instance)
        {
            try
            {
                if(Active||CapAccess.Flag(CapAccess.Read(__instance,"cState"),"dead"))return;
                var current=__instance as Component;
                if(current==null)return;
                var box=current.GetComponent<BoxCollider2D>();var body=current.GetComponent<Rigidbody2D>();
                Vector3 feet=current.transform.position;
                if(box!=null)feet.y=box.bounds.min.y;
                object controller=CapAccess.Read(controllerType,"Instance");
                bool live=Convert.ToInt32(CapAccess.Read(controller,"MarioId"))>=0;
                float vy=live?CapAccess.Number(CapAccess.Read(controller,"State"),"vy")*.315f:body!=null?body.velocity.y:0f;
                if(!ConversationMario.StartAction(current,0x02,0f,false,holdControl:true,group:"death"))return;
                hero=current;Feet=feet;vertical=Mathf.Clamp(vy,-30f,12f);
                gravity=Mathf.Min(-10f,Physics2D.gravity.y*Mathf.Max(1f,body!=null?body.gravityScale:1f));
                width=box!=null?Mathf.Clamp(box.bounds.size.x*.8f,.25f,1f):.5f;
                Active=true;landed=observedDeath=false;
            }
            catch(Exception error){Stop();Plugin.Instance?.Log.LogWarning("Mario's death image unavailable: "+error.GetBaseException().Message);}
        }
        internal static float FallDistance(ref float velocity,float acceleration,float dt)
        {
            float distance=velocity*dt+.5f*acceleration*dt*dt;
            velocity+=acceleration*dt;return distance;
        }
        internal static void Step(float delta)
        {
            if(!Active||landed)return;
            float left=Mathf.Min(delta,.15f);
            var filter=new ContactFilter2D();filter.SetLayerMask(LayerMask.GetMask("Terrain","Soft Terrain"));filter.useTriggers=false;
            while(left>0f&&!landed)
            {
                float dt=Mathf.Min(left,.02f);left-=dt;
                float distance=FallDistance(ref vertical,gravity,dt);
                if(distance<=0f)
                {
                    // A short foot box also catches thin ledges. Ignore water,
                    // enemies, triggers and walls, so they cannot become a floor.
                    int count=Physics2D.BoxCast((Vector2)Feet+Vector2.up*.08f,new Vector2(width,.1f),0f,
                        Vector2.down,filter,hits,-distance+.05f);
                    float nearest=float.PositiveInfinity;
                    for(int i=0;i<count;i++)
                        if(hits[i].collider!=null&&!hits[i].collider.isTrigger&&hits[i].normal.y>.35f)
                            nearest=Mathf.Min(nearest,hits[i].distance);
                    if(nearest<=-distance+.05f)
                    {
                        Feet+=Vector3.down*Mathf.Max(0f,nearest-.03f);
                        vertical=0f;landed=true;
                        sound?.Invoke(null,new object[]{DeathSound});
                        break;
                    }
                }
                Feet+=Vector3.up*distance;
            }
        }
        internal static void HideNativeBody()
        {
            if(!Active||hero==null)return;
            if(nativeBody==null)
            {
                var death=CapAccess.Read(hero,"heroDeathPrefab") as GameObject;
                if(death!=null){nativeBody=death.GetComponent<MeshRenderer>();if(nativeBody!=null)oldHidden=nativeBody.forceRenderingOff;}
            }
            // The root is the Knight; children are the Shade and death effects.
            if(nativeBody!=null)nativeBody.forceRenderingOff=true;
        }
        internal static bool Finished()
        {
            if(!Active||hero==null)return false;
            object state=CapAccess.Read(hero,"cState");
            var effect=CapAccess.Read(hero,"heroDeathPrefab") as GameObject;
            bool dying=CapAccess.Flag(state,"dead")||(effect!=null&&effect.activeInHierarchy);
            observedDeath|=dying;
            // Also release after a same-scene recovery: a scene-load callback is
            // not required, and reaching a floor is never a condition for release.
            return observedDeath&&!dying&&!CapAccess.Flag(hero,"controlReqlinquished")
                &&CapAccess.Flag(hero,"acceptingInput");
        }
        internal static void Stop()
        {
            DeathCap.Stop();
            if(nativeBody!=null)nativeBody.forceRenderingOff=oldHidden;
            nativeBody=null;hero=null;Active=landed=observedDeath=false;
        }
    }
}
