using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;
using Mesh = HollowMario.ConnectedTerrainMesh;

namespace HollowMario
{
    // Our host owns WorldCollision streaming and its per-collider converter.
    // Static, closed terrain outlines use the joined mesh. Dynamic/special/unsupported
    // shapes use that host converter and the established platform refresh path.
    internal static class TerrainConversion
    {
        internal sealed class CreateState { internal List<Mesh.Triangle> Mesh; internal bool Emitted, Legacy; }
        private sealed class Stamp
        {
            internal EdgeCollider2D Collider;
            internal Matrix4x4 Matrix;
            internal bool Enabled, Active;
            internal int PointCount;
        }
        [ThreadStatic] private static CreateState current;
        private static readonly List<Stamp> stamps = new List<Stamp>();
        private static readonly HashSet<int> known = new HashSet<int>();
        private static Dictionary<int,List<Mesh.Triangle>> meshes;
        private static List<List<Mesh.Point>> boundary;
        private static Type fsm, responder, surface;
        private static FieldInfo origin, nextScan;
        private static FieldInfo[] vertices;
        private static FieldInfo surfaceType, surfaceTerrain;
        private static MethodInfo rebuild;
        private static ManualLogSource log;
        private static bool built, dirty, rebuilding;
        private static int scene, failures;
        internal static float LowestY { get; private set; }

        internal static void Arm(Harmony harmony, ManualLogSource logger, bool legacy)
        {
            log=logger;
            if(legacy){log.LogInfo("Terrain conversion: replacement host per-collider mode.");return;}
            Type world=AccessTools.TypeByName("MarioHollow.WorldCollision");
            Type geometry=AccessTools.TypeByName("MarioHollow.Geometry");
            Type math=AccessTools.TypeByName("MarioHollow.MarioMath");
            surface=AccessTools.TypeByName("MarioHollow.SM64Surface");
            fsm=AccessTools.TypeByName("PlayMakerFSM");responder=AccessTools.TypeByName("IHitResponder");
            MethodInfo create=AccessTools.Method(world,"Create",new[]{typeof(Collider2D)});
            MethodInfo prism=AccessTools.Method(geometry,"Prism");
            MethodInfo tick=AccessTools.Method(world,"Tick",new[]{typeof(Vector2),typeof(bool)});
            rebuild=AccessTools.Method(world,"Rebuild",new[]{typeof(Vector2)});
            nextScan=AccessTools.Field(world,"nextScan");origin=AccessTools.Field(math,"Origin");
            if(create==null||prism==null||tick==null||rebuild==null||origin==null||surface==null||nextScan==null)
            {log.LogWarning("Joined terrain could not be armed; the host's per-collider converter remains active.");return;}
            vertices=new FieldInfo[9];
            for(int i=0;i<9;i++) vertices[i]=AccessTools.Field(surface,"v"+(i/3)+"xyz"[i%3]);
            surfaceType=AccessTools.Field(surface,"type");surfaceTerrain=AccessTools.Field(surface,"terrain");
            foreach(FieldInfo field in vertices)if(field==null)throw new MissingFieldException("Native terrain vertex");
            var enter=new HarmonyMethod(typeof(TerrainConversion),nameof(BeforeCreate));enter.priority=Priority.Last;
            harmony.Patch(create,prefix:enter,finalizer:new HarmonyMethod(typeof(TerrainConversion),nameof(AfterCreate)));
            harmony.Patch(prism,prefix:new HarmonyMethod(typeof(TerrainConversion),nameof(Prism)));
            harmony.Patch(tick,prefix:new HarmonyMethod(typeof(TerrainConversion),nameof(BeforeScan)));
            log.LogInfo("Terrain conversion: joined fixed outlines; host per-collider conversion for platforms and fallback.");
        }
        private static bool Eligible(EdgeCollider2D collider)
        {
            if(collider==null||collider.isTrigger||collider.gameObject.layer!=8||collider.attachedRigidbody!=null
                ||collider.usedByEffector||collider.usedByComposite||collider.GetComponent<MeshFilter>()==null)return false;
            foreach(MonoBehaviour script in collider.GetComponentsInParent<MonoBehaviour>(true))
                if(script!=null && (script.GetType().Name=="HeroPlatformStick"
                    ||(fsm!=null&&fsm.IsInstanceOfType(script))||(responder!=null&&responder.IsInstanceOfType(script))))return false;
            return true;
        }
        private static bool Valid()
        {
            if(dirty||scene!=UnitySceneManager.GetActiveScene().handle)return false;
            foreach(Stamp stamp in stamps)
            {
                EdgeCollider2D c=stamp.Collider;
                if(c==null||c.enabled!=stamp.Enabled||c.gameObject.activeInHierarchy!=stamp.Active
                    ||c.transform.localToWorldMatrix!=stamp.Matrix||c.pointCount!=stamp.PointCount)return false;
            }
            return true;
        }
        private static void Snapshot()
        {
            built=true;dirty=false;meshes=null;boundary=null;known.Clear();stamps.Clear();scene=UnitySceneManager.GetActiveScene().handle;LowestY=0f;
            var sources=new List<Mesh.Outline>();
            try
            {
                foreach(EdgeCollider2D c in Resources.FindObjectsOfTypeAll<EdgeCollider2D>())
                {
                    if(!c.gameObject.scene.IsValid()||c.gameObject.scene.handle!=scene||!Eligible(c))continue;
                    int id=c.GetInstanceID();known.Add(id);
                    stamps.Add(new Stamp{Collider=c,Enabled=c.enabled,Active=c.gameObject.activeInHierarchy,
                        Matrix=c.transform.localToWorldMatrix,PointCount=c.pointCount});
                    if(!c.enabled||!c.gameObject.activeInHierarchy||HeroIgnores.ShouldIgnore(c))continue;
                    LowestY=Math.Min(LowestY,c.bounds.min.y); // Reuse this snapshot; no additional terrain scan.
                    Vector2[] points=c.points;if(points.Length<4)continue;
                    Vector3 first=c.transform.TransformPoint(points[0]+c.offset);
                    Vector3 last=c.transform.TransformPoint(points[points.Length-1]+c.offset);
                    if((first-last).sqrMagnitude>Mesh.Grid*Mesh.Grid)continue; // A real open edge stays open/legacy.
                    var path=new List<Mesh.Point>();
                    for(int i=0;i<points.Length-1;i++)
                    {
                        Vector3 p=c.transform.TransformPoint(points[i]+c.offset);path.Add(new Mesh.Point(p.x,p.y));
                    }
                    sources.Add(new Mesh.Outline{Owner=id,Group=c.gameObject.GetInstanceID(),Points=path});
                }
                Mesh.ClassifyHoles(sources);
                Mesh.Result result=Mesh.Build(sources);meshes=result.Parts;boundary=result.Boundary;
                log.LogInfo("Joined terrain: "+sources.Count+" closed outlines, "+result.InputEdges+" source edges -> "
                    +result.BoundaryEdges+" exterior edges; host per-collider handling for other objects.");
            }
            catch(Exception exception)
            {
                meshes=null;
                if(failures++<4)log.LogWarning("Terrain validation chose the host per-collider converter: "+exception.GetBaseException().Message);
            }
        }
        private static bool BeforeScan(object __instance,Vector2 centre,bool force)
        {
            if(rebuilding)return true;
            if(!force&&Time.unscaledTime<Convert.ToSingle(nextScan.GetValue(__instance)))return true;
            if(!built){Snapshot();return true;}
            if(Valid())return true;
            Snapshot();rebuilding=true;
            try{rebuild.Invoke(__instance,new object[]{centre});}
            finally{rebuilding=false;}
            return false;
        }
        private static void BeforeCreate(Collider2D c,out CreateState __state)
        {
            __state=current;current=null;
            if(!(c is EdgeCollider2D edge)||!Eligible(edge))return;
            if(!built)Snapshot();
            int id=c.GetInstanceID();
            if(!known.Contains(id)){dirty=true;return;}
            if(meshes!=null&&meshes.TryGetValue(id,out List<Mesh.Triangle> mesh))current=new CreateState{Mesh=mesh};
        }
        private static void AfterCreate(CreateState __state){current=__state;}
        private static bool Prism(float depth,object into)
        {
            if(current==null||current.Legacy)return true;
            if(current.Emitted)return false;
            try
            {
                Vector2 at=(Vector2)origin.GetValue(null);
                List<int[]> rows=Mesh.Encode(current.Mesh,at.x,at.y,depth);
                var encoded=new List<object>(rows.Count);
                foreach(int[] row in rows)
                {
                    object face=Activator.CreateInstance(surface);surfaceType.SetValue(face,(short)21);surfaceTerrain.SetValue(face,(ushort)1);
                    for(int i=0;i<9;i++)vertices[i].SetValue(face,row[i]);encoded.Add(face);
                }
                IList list=(IList)into;foreach(object face in encoded)list.Add(face);
                current.Emitted=true;return false;
            }
            catch(Exception exception)
            {
                current.Legacy=true;
                if(failures++<4)log.LogWarning("A terrain shape retained its original conversion: "+exception.GetBaseException().Message);
                return true;
            }
        }
        internal static bool Ready => built&&meshes!=null&&boundary!=null;
        internal static bool ContainsSolid(double x,double y)
        {
            if(!Ready)return false;int winding=0;var p=new Mesh.Point(x,y);
            foreach(List<Mesh.Point> loop in boundary)
                if(Mesh.Contains(loop,p))winding+=Mesh.SignedArea(loop)>0?1:-1;
            return winding>0;
        }
        internal static void Forget(){built=false;dirty=false;meshes=null;boundary=null;stamps.Clear();known.Clear();current=null;LowestY=0f;}
    }
}
