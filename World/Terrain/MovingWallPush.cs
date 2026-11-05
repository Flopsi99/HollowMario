using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    internal static class MovingWallPush
    {
        private const BindingFlags Any=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private sealed class Copy { internal uint Id;internal Vector3 Basis,Position;internal Bounds Bounds; }
        private static FieldInfo tracked,id,seenAt,nextScan;
        private static object world;
        private static readonly Dictionary<Collider2D,Copy> copies=new Dictionary<Collider2D,Copy>();
        private static readonly Dictionary<Collider2D,bool> candidates=new Dictionary<Collider2D,bool>();
        private static readonly Dictionary<Transform,List<Collider2D>> sources=new Dictionary<Transform,List<Collider2D>>();
        private static readonly HashSet<Collider2D> pending=new HashSet<Collider2D>(),present=new HashSet<Collider2D>();
        private static readonly List<Collider2D> work=new List<Collider2D>(),stale=new List<Collider2D>();
        private static float scannedAt=float.NaN;
        private static ManualLogSource log;
        private static int failures,notes;
        internal static void Arm(Harmony harmony,Type controller,Type native,ManualLogSource logger)
        {
            log=logger;Type collision=controller.GetField("collision",Any)?.FieldType;
            tracked=collision?.GetField("tracked",Any);nextScan=collision?.GetField("nextScan",Any);
            Type entry=tracked?.FieldType.GetGenericArguments()[1];id=entry?.GetField("Id",Any);seenAt=entry?.GetField("SeenAt",Any);
            MethodInfo tick=collision?.GetMethod("Tick",Any);
            if(tracked==null||nextScan==null||id==null||seenAt==null||tick==null||!NativeWallMotion.Configure(controller,native))
            {log.LogWarning("Native moving-wall geometry could not be armed.");return;}
            harmony.Patch(tick,postfix:new HarmonyMethod(typeof(MovingWallPush),nameof(AfterScan)));
            Type tween=AccessTools.TypeByName("iTween");
            foreach(string name in new[]{"ApplyMoveToTargets","ApplyMoveByTargets","ApplyMoveToPathTargets"})
            {MethodInfo move=tween?.GetMethod(name,Any);if(move!=null)harmony.Patch(move,prefix:new HarmonyMethod(typeof(MovingWallPush),nameof(BeforeMotion)));}
        }
        internal static void BeforeMotion(Component __instance)
        {
            if(__instance!=null&&sources.TryGetValue(__instance.transform,out List<Collider2D> walls))
                foreach(Collider2D wall in walls)if(wall!=null)pending.Add(wall);
        }
        private static bool Wall(Collider2D shape)
        {
            if(!(shape is BoxCollider2D)||shape.gameObject.layer!=8)return false;
            Bounds box=shape.bounds;if(box.size.y<1f||box.size.y<box.size.x)return false;
            foreach(MonoBehaviour script in shape.GetComponentsInParent<MonoBehaviour>(true))
                if(script!=null&&(script.GetType().Name=="HeroPlatformStick"||script.GetType().Name=="HealthManager"))return false;
            return true;
        }
        private static void Register(Collider2D shape)
        {
            for(Transform t=shape.transform;t!=null;t=t.parent)
            {if(!sources.TryGetValue(t,out List<Collider2D> list)){list=new List<Collider2D>();sources[t]=list;}if(!list.Contains(shape))list.Add(shape);}
        }
        internal static void AfterScan(object __instance)
        {
            if(!ReferenceEquals(world,__instance)){Forget();world=__instance;}
            float stamp=Convert.ToSingle(nextScan.GetValue(__instance));bool scanned=stamp!=scannedAt;
            if(!scanned&&pending.Count==0)return;
            KnightResetFollow follow=Plugin.Instance?.knightFollow;object controller=follow?.Controller();
            if(controller==null||!follow.MarioAlive(controller))return;
            try
            {
                IDictionary entries=tracked.GetValue(__instance) as IDictionary;if(entries==null)return;
                if(scanned)
                {
                    scannedAt=stamp;present.Clear();
                    foreach(DictionaryEntry entry in entries)
                    {
                        if(!(entry.Key is Collider2D shape)||shape==null)continue;present.Add(shape);
                        if(!candidates.TryGetValue(shape,out bool wall)){wall=Wall(shape);candidates[shape]=wall;if(wall)Register(shape);}
                        if(wall)pending.Add(shape);
                    }
                    stale.Clear();foreach(Collider2D shape in candidates.Keys)if(!present.Contains(shape))stale.Add(shape);
                    foreach(Collider2D shape in stale){copies.Remove(shape);candidates.Remove(shape);pending.Remove(shape);foreach(List<Collider2D> list in sources.Values)list.Remove(shape);}
                }
                work.Clear();work.AddRange(pending);pending.Clear();Transform hero=follow.HeroTransform(controller);
                foreach(Collider2D shape in work)
                {
                    if(shape==null||!entries.Contains(shape)||!shape.enabled||!shape.gameObject.activeInHierarchy||shape.isTrigger
                        ||shape.usedByEffector||shape.usedByComposite||HeroIgnores.ShouldIgnore(shape)
                        ||Mathf.Abs(Mathf.DeltaAngle(0,shape.transform.eulerAngles.z)%90f)>.01f
                        ||(hero?.parent!=null&&shape.transform.IsChildOf(hero.parent)))
                    {copies.Remove(shape);continue;}
                    object entry=entries[shape];uint objectId=Convert.ToUInt32(id.GetValue(entry));Vector3 now=shape.transform.position;Bounds box=shape.bounds;
                    if(!copies.TryGetValue(shape,out Copy copy)||copy.Id!=objectId)
                    {
                        // A normal stream refresh may replace the native ID before this
                        // postfix. Keep the last world pose so the move still gets swept;
                        // only the native vertices' creation basis changes with the new ID.
                        Vector3 at=(Vector3)seenAt.GetValue(entry);
                        if(copy!=null){copy.Id=objectId;copy.Basis=at;}
                        else
                        {
                            Bounds old=box;old.center+=at-now;
                            copy=new Copy{Id=objectId,Basis=at,Position=at,Bounds=old};copies[shape]=copy;
                        }
                    }
                    if(Mathf.Abs(now.x-copy.Position.x)<.00001f)continue;
                    Vector3? feet=follow.Ground(controller);
                    bool near=feet.HasValue&&!follow.Busy(controller)&&NativeWallMotion.Near(copy.Bounds,box,feet.Value);
                    int hits=NativeWallMotion.Move(controller,objectId,copy.Basis,copy.Position,now,near,follow.Grounded(controller));
                    copy.Position=now;copy.Bounds=box;seenAt.SetValue(entry,now);
                    if(notes++<12)log?.LogInfo("Native wall geometry moved: "+shape.name+", nearby="+near+", native contacts="+hits+".");
                }
            }
            catch(Exception error){if(failures++<4)log?.LogWarning("Native wall geometry update failed: "+error.GetBaseException().Message);}
        }
        internal static void Forget(){world=null;copies.Clear();candidates.Clear();sources.Clear();pending.Clear();present.Clear();work.Clear();stale.Clear();scannedAt=float.NaN;notes=0;}
    }
}
