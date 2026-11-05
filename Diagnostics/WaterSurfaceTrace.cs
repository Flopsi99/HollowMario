using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Observations only. Collider tops, native Hero placement, splash offsets and
    // artwork bounds are different references: none is silently declared the visual
    // waterline. Keep this separate file flushed during play, even with Info logs off.
    internal static class WaterSurfaceTrace
    {
        private const long MaxBytes=2*1024*1024;
        private static StreamWriter writer;
        private static bool failed,active;
        private static int sourceId,scene=-1,samples;
        private static float nextSample,previousSurface;
        private static bool previousAcid,previousProjected;
        private static readonly List<Component> fsms=new List<Component>();
        private static readonly List<Renderer> art=new List<Renderer>();
        private static readonly List<Renderer> sceneArt=new List<Renderer>();
        private static int artScene=-1;
        private static Type fsmType,spriteType;
        private static string path;

        internal static string Number(float value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
        private static string Vector(Vector3 value)=>"("+Number(value.x)+","+Number(value.y)+","+Number(value.z)+")";
        private static string BoundsText(Bounds value)=>"min="+Vector(value.min)+" max="+Vector(value.max);
        private static string Hierarchy(Transform value)
        {
            string name=value.name;
            for(int i=0;value.parent!=null&&i<6;i++){value=value.parent;name=value.name+"/"+name;}
            return name;
        }
        internal static bool ArtName(string name)
        {
            name=name.ToLowerInvariant();
            if(!name.Contains("water")&&!name.Contains("acid")&&!name.Contains("lake"))return false;
            foreach(string excluded in new[]{"fall","drip","splash","fog","particle","stream","bounce","spill"})
                if(name.Contains(excluded))return false;
            return true;
        }
        internal static float ArtDistance(Bounds bounds,Vector3 feet,float surface)
        {
            if(feet.x<bounds.min.x-.5f||feet.x>bounds.max.x+.5f)return float.PositiveInfinity;
            float distance=Mathf.Abs(bounds.max.y-surface);
            return distance<=6f?distance:float.PositiveInfinity;
        }
        internal static bool SampleDue(float now,bool changed,int count,float next)
            =>changed||now>=next;
        internal static float? FloatVariable(object variables,string name)
        {
            if(variables==null)return null;
            object variable=CapAccess.Call(variables,"FindFsmFloat",name);
            return CapAccess.Read(variable,"Value") is float value?value:(float?)null;
        }
        internal static string VariableName(object variable)
            =>Convert.ToString(CapAccess.Read(variable,"Name"));
        internal static float? DropletPlane(float? heroTarget,object action)
        {
            if(!heroTarget.HasValue||action==null||action.GetType().Name!="FlingObjectsFromGlobalPool")return null;
            object spawn=CapAccess.Read(action,"spawnPoint"),offset=CapAccess.Read(action,"position");
            if(VariableName(spawn)!="Hero"||CapAccess.Flag(offset,"IsNone"))return null;
            return CapAccess.Read(offset,"Value") is Vector3 value?heroTarget+value.y:null;
        }
        [System.Diagnostics.Conditional("HOLLOW_MARIO_DIAGNOSTICS")]
        internal static void Observe(Collider2D source,bool acid,bool projected,float surface,object controller,KnightResetFollow follow)
        {
            if(!DebugSettings.DiagnosticsAvailable||failed||!DebugSettings.Enabled||source==null||follow==null)return;
            try
            {
                float now=Time.unscaledTime;int id=source.GetInstanceID();
                bool changed=!active||sourceId!=id||scene!=source.gameObject.scene.handle
                    ||previousAcid!=acid;
                if(!SampleDue(now,changed,samples,nextSample))return;
                Vector3? ground=follow.Ground(controller);if(!ground.HasValue)return;
                if(changed)
                {
                    sourceId=id;scene=source.gameObject.scene.handle;samples=0;active=true;
                    previousSurface=surface;previousAcid=acid;previousProjected=projected;
                    Gather(source);
                    Write("POOL scene="+source.gameObject.scene.name+" source="+Hierarchy(source.transform)+"#"+id
                        +" tag="+source.tag+" kind="+(acid?"acid":"water")+" detection="+(projected?"extended column":"direct trigger")
                        +" suppliedY="+Number(surface)+" trigger "+BoundsText(source.bounds)
                        +" origin="+Vector(source.transform.position)+" scale="+Vector(source.transform.lossyScale));
                    foreach(var fsm in fsms)DescribeFsm(fsm);
                    if(fsms.Count==0)Write("NATIVE no water/acid FSM on the source or its first three ancestors; native surface reference UNKNOWN.");
                    if(source is BoxCollider2D box)Write("TRIGGER localOffset="+box.offset+" localSize="+box.size);
                }
                if(!changed&&(previousProjected!=projected||Mathf.Abs(previousSurface-surface)>.02f))
                    Write("REFERENCE sourceId="+id+" suppliedY="+Number(surface)+" detection="+(projected?"extended column":"direct trigger"));
                previousSurface=surface;previousProjected=projected;
                samples++;nextSample=now+(samples<8?1f:5f);
                Vector3 feet=ground.Value;
                var model=CapAccess.Read(CapAccess.Read(controller,"Renderer"),"Object") as GameObject;
                var renderer=model!=null?model.GetComponent<MeshRenderer>():null;
                string mesh=renderer!=null?" lastRenderedMesh "+BoundsText(renderer.bounds)
                    +" enabled="+renderer.enabled+" hidden="+renderer.forceRenderingOff
                    +" meshTopMinusSupplied="+Number(renderer.bounds.max.y-surface):" lastRenderedMesh=unavailable";
                Write("SAMPLE scene="+source.gameObject.scene.name+" sourceId="+id+" suppliedY="+Number(surface)
                    +" feet="+Vector(feet)+" feetDepth="+Number(surface-feet.y)
                    +" action="+follow.Action(controller).ToString("X")+" frame="+Time.frameCount+mesh);
                foreach(var fsm in fsms)DescribeTargets(fsm,surface);
                SelectArt(feet,surface);
                if(art.Count==0)Write("ART no nearby named water/acid renderer found; visual surface UNKNOWN.");
                foreach(var item in art)if(item!=null&&Visible(item))
                    Write("ART candidate="+Hierarchy(item.transform)+"#"+item.GetInstanceID()+" "+BoundsText(item.bounds)
                        +" pivot="+Vector(item.transform.position)+" topMinusSupplied="+Number(item.bounds.max.y-surface)
                        +" scale="+Vector(item.transform.lossyScale)+" (geometry bounds; alpha/wave waterline not measured)");
            }
            catch(Exception error){Fail(error);}
        }
        private static bool Visible(Renderer renderer)
        {
            if(!renderer.enabled||renderer.forceRenderingOff||!renderer.gameObject.activeInHierarchy)return false;
            if(renderer is SpriteRenderer standard&&standard.color.a<=0f)return false;
            if(spriteType==null)spriteType=AccessTools.TypeByName("tk2dBaseSprite");
            Component sprite=spriteType!=null?renderer.GetComponent(spriteType):null;
            return !(CapAccess.Read(sprite,"color") is Color colour)||colour.a>0f;
        }
        private static void Gather(Collider2D source)
        {
            fsms.Clear();art.Clear();
            if(fsmType==null)fsmType=AccessTools.TypeByName("PlayMakerFSM");
            Transform owner=source.transform;
            for(int i=0;owner!=null&&i<4;i++,owner=owner.parent)
                if(fsmType!=null)foreach(Component fsm in owner.GetComponents(fsmType))
                {
                    string name=Convert.ToString(CapAccess.Read(fsm,"FsmName"));
                    if(name.IndexOf("water",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("acid",StringComparison.OrdinalIgnoreCase)>=0)fsms.Add(fsm);
                }
            // Once per scene, never a per-tick scene scan. Candidates
            // must overlap Mario's X and belong to the same loaded scene. These are
            // reported for comparison only; selection never changes swimming.
            if(artScene==source.gameObject.scene.handle)return;
            artScene=source.gameObject.scene.handle;sceneArt.Clear();
            foreach(Renderer renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if(renderer.gameObject.scene.handle==artScene&&ArtName(Hierarchy(renderer.transform)))sceneArt.Add(renderer);
            }
        }
        private static void SelectArt(Vector3 feet,float surface)
        {
            art.Clear();
            foreach(Renderer renderer in sceneArt)
            {
                if(renderer==null||!Visible(renderer))continue;
                float distance=ArtDistance(renderer.bounds,feet,surface);
                if(float.IsPositiveInfinity(distance))continue;
                int at=0;while(at<art.Count&&ArtDistance(art[at].bounds,feet,surface)<=distance)at++;
                art.Insert(at,renderer);if(art.Count>6)art.RemoveAt(6);
            }
        }
        private static IEnumerable<object> Actions(object fsm)
        {
            if(!(CapAccess.Read(fsm,"FsmStates") is IEnumerable states))yield break;
            foreach(object state in states)
                if(Convert.ToString(CapAccess.Read(state,"Name"))=="Splash In Norm"
                    &&CapAccess.Read(state,"Actions") is IEnumerable actions)
                    foreach(object action in actions)yield return action;
        }
        private static void DescribeFsm(Component fsm)
        {
            object variables=CapAccess.Read(fsm,"FsmVariables");
            var text=new StringBuilder("NATIVE owner=").Append(Hierarchy(fsm.transform)).Append(" fsm=").Append(CapAccess.Read(fsm,"FsmName"))
                .Append(" origin=").Append(Vector(fsm.transform.position));
            foreach(string name in new[]{"Hero Offset","Hero Surface Y","Surface Y","Water Y","Acid Y"})
            {float? value=FloatVariable(variables,name);if(value.HasValue)text.Append(" ").Append(name).Append('=').Append(Number(value.Value));}
            Write(text.ToString());
            foreach(object action in Actions(fsm))
            {
                string type=action.GetType().Name;
                if(type=="FlingObjectsFromGlobalPool")
                {
                    object offset=CapAccess.Read(action,"position");
                    Write("NATIVE splash state=Splash In Norm action="+type+" spawnVariable="+VariableName(CapAccess.Read(action,"spawnPoint"))
                        +" offset="+(CapAccess.Read(offset,"Value") is Vector3 v?Vector(v):"unavailable")+" offsetIsNone="+CapAccess.Flag(offset,"IsNone"));
                }
            }
        }
        private static void DescribeTargets(Component fsm,float surface)
        {
            float? hero=FloatVariable(CapAccess.Read(fsm,"FsmVariables"),"Hero Surface Y");
            if(!hero.HasValue)return;
            Write("NATIVE heroTargetY="+Number(hero.Value)+" heroTargetMinusSupplied="+Number(hero.Value-surface)
                +" fsm="+CapAccess.Read(fsm,"FsmName")+" (Knight placement, not automatically the waterline)");
            foreach(object action in Actions(fsm))
            {
                float? splash=DropletPlane(hero,action);if(!splash.HasValue)continue;
                Write("NATIVE dropletPlaneCandidateY="+Number(splash.Value)+" dropletMinusSupplied="+Number(splash.Value-surface)
                    +" (native Hero target plus authored droplet offset; not a visual measurement)");break;
            }
        }
        [System.Diagnostics.Conditional("HOLLOW_MARIO_DIAGNOSTICS")]
        internal static void Leave()
        {
            if(!active)return;
            try{Write("LEAVE sourceId="+sourceId);}catch(Exception error){Fail(error);}
            active=false;samples=0;fsms.Clear();art.Clear();
        }
        [System.Diagnostics.Conditional("HOLLOW_MARIO_DIAGNOSTICS")]
        internal static void Forget()
        {
            Leave();
            sceneArt.Clear();artScene=-1;
            try{writer?.Dispose();}catch(Exception){ }
            writer=null;
        }
        private static void Write(string text)
        {
            if(path==null)path=Path.Combine(Path.GetDirectoryName(typeof(WaterSurfaceTrace).Assembly.Location),"diagnostics","water-surface.log");
            if(writer!=null&&writer.BaseStream.Length>=MaxBytes){writer.Dispose();writer=null;}
            if(writer==null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if(File.Exists(path)&&new FileInfo(path).Length>=MaxBytes)
                {string previous=path+".previous";if(File.Exists(previous))File.Delete(previous);File.Move(path,previous);}
                writer=new StreamWriter(new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.ReadWrite)){AutoFlush=true};
                writer.WriteLine("SESSION version="+Plugin.PluginVersion+" utc="+DateTime.UtcNow.ToString("o")+"; read-only surface comparisons; one-second entry samples then every five seconds while in liquid.");
            }
            writer.WriteLine(DateTime.UtcNow.ToString("o")+" "+text);
        }
        private static void Fail(Exception error)
        {
            if(failed)return;failed=true;
            try{writer?.Dispose();}catch(Exception){ }writer=null;
            Plugin.Instance?.Log.LogWarning("Water surface diagnostics stopped: "+error.GetBaseException().Message);
        }
    }
}
