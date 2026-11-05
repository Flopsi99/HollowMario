using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace HollowMario
{
    // Temporary read-only observations. Never query physics, run a receiver, change
    // collision settings or decide whether an attack is allowed from this class.
    internal static class ProjectileTrace
    {
        internal struct Frame
        {
            internal string Move,Window;
            internal uint Action;
            internal int Animation,AnimFrame,Count;
            internal float Angle;
            internal Bounds Reach;
            internal Vector3 Motion;
            internal bool HasReach,Triggers;
        }
        private const BindingFlags Any=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly HashSet<string> seen=new HashSet<string>();
        private static readonly Dictionary<int,string> families=new Dictionary<int,string>();
        private static string scene="startup";
        private static int scans,contacts,results,failures;
        private static Type fsmType;
        internal static void Reset(string name)
        {scene=name;seen.Clear();families.Clear();scans=contacts=results=0;}

        internal static bool CanObserveBody=>DebugSettings.VerboseLogging&&contacts<48;
        internal static void Query(Frame frame,int capacity)
        {
            if(!DebugSettings.VerboseLogging)return;
            if(scans>=72)return;
            if(frame.Count==0||frame.Count>=capacity||!frame.Triggers)
                Write(frame,null,null,"query status",frame.Count>=capacity?"buffer full; some contacts may be missing"
                    :!frame.Triggers?"trigger colliders omitted by current physics setting":"no contacts found",0);
        }
        internal static void Scan(Frame frame,Collider2D hit,string stage,string reason)
        {
            if(!DebugSettings.VerboseLogging)return;
            if(scans>=72||hit==null)return;
            int layer=hit.gameObject.layer;
            // Don't spend the scan budget on static room floors/player sensors.
            // No projectile whitelist: unusual layers, bodies and rejected shots remain visible.
            if((layer==8||layer==9||layer==13||layer==14||layer==15||layer==20)&&hit.attachedRigidbody==null)return;
            Write(frame,hit,hit.gameObject,stage,reason,0);
        }
        internal static void Result(Frame frame,Collider2D hit,GameObject source,string stage,string detail)
        {if(DebugSettings.VerboseLogging&&results<96)Write(frame,hit,source,stage,detail,1);}
        internal static void NailOffer(Frame frame,Collider2D hit,GameObject source,GameObject nail)
        {
            if(!DebugSettings.VerboseLogging)return;
            if(results>=96)return;
            try{Write(frame,hit,source,"nail source offered","nail tag="+nail.tag+" layer="+nail.layer,1);}
            catch(Exception){}
        }
        internal static void Body(Frame frame,GameObject source,int damage,int hazard,bool attackImpact)
        {
            if(!DebugSettings.VerboseLogging)return;
            if(contacts>=48)return;
            Write(frame,source==null?null:source.GetComponent<Collider2D>(),source,"incoming damage callback",
                "damage="+damage+", hazard="+hazard+"; inside attack-requested impact="+attackImpact+"; observed before combat/cap suppression",2);
        }
        internal static void ImpactResult(Frame frame,Collider2D hit,GameObject source,object action,Collider2D body,bool delivered,bool wasRecorded)
        {
            if(!DebugSettings.VerboseLogging)return;
            if(results>=96)return;
            try
            {
                string name=action?.GetType().Name;
                if(name==null||!name.StartsWith("Trigger2dEvent",StringComparison.Ordinal))return;
                Write(frame,hit,source,delivered?"impact filter accepted":"impact filter rejected",
                    name+"; player layer="+body.gameObject.layer+" tag="+body.gameObject.tag+"; action previously recorded="+wasRecorded,1);
            }
            catch(Exception){} // A response may already have destroyed its objects.
        }
        private static void Write(Frame f,Collider2D hit,GameObject source,string stage,string reason,int channel)
        {
            if(!DebugSettings.VerboseLogging)return;
            try
            {
                int id=source==null?0:source.GetInstanceID();
                if(!families.TryGetValue(id,out string family))
                {
                    var path=new StringBuilder();Transform at=source==null?null:source.transform;
                    for(int i=0;at!=null&&i<4;i++,at=at.parent){if(i>0)path.Insert(0,"/");path.Insert(0,at.name);}
                    family=path.Length==0?"<source missing/destroyed>":path.ToString();
                    if(families.Count<512)families[id]=family;
                }
                string key=channel+"|"+f.Move+"|"+family+"|"+(source==null?-1:source.layer)+"|"+stage+"|"+reason;
                if(!seen.Add(key))return;
                if(channel==0)scans++;else if(channel==1)results++;else contacts++;
                var text=new StringBuilder(512);
                text.Append("Projectile trace: scene=").Append(scene).Append("; move=").Append(f.Move)
                    .Append("; action=").Append(f.Action.ToString("X")).Append("; anim=").Append(f.Animation.ToString("X"))
                    .Append("/").Append(f.AnimFrame).Append("; stage=").Append(stage).Append("; ").Append(reason)
                    .Append("; source=").Append(family).Append('#').Append(id).Append("; active=").Append(source!=null&&source.activeInHierarchy)
                    .Append("; layer=").Append(source==null?-1:source.layer).Append("; window=").Append(f.Window)
                    .Append("; hitAngle=").Append(f.Angle).Append("; queryCount=").Append(f.Count)
                    .Append("; queryTriggers=").Append(f.Triggers);
                if(hit!=null)
                {
                    text.Append("; shape=").Append(hit.GetType().Name).Append(" trigger=").Append(hit.isTrigger)
                        .Append(" enabled=").Append(hit.enabled).Append(" bounds=").Append(hit.bounds);
                    Rigidbody2D moving=hit.attachedRigidbody;
                    text.Append("; body=").Append(moving==null?"none":moving.gameObject.name+" velocity="+moving.velocity);
                    if(f.HasReach)text.Append("; exact=").Append(SceneryContact.Sweeps(f.Reach,Vector3.zero,hit.bounds))
                        .Append(" swept=").Append(SceneryContact.Sweeps(f.Reach,f.Motion,hit.bounds));
                }
                if(f.HasReach)text.Append("; reach=").Append(f.Reach).Append(" motion=").Append(f.Motion);
                text.Append("; receivers=").Append(Snapshot(source));
                Plugin.Instance?.Log.LogInfo(text.ToString());
            }
            catch(Exception error)
            {if(failures++<2)Plugin.Instance?.Log.LogWarning("Projectile trace unavailable: "+error.GetBaseException().Message);}
        }
        private static string Snapshot(GameObject source)
        {
            var text=new StringBuilder();fsmType=fsmType??HarmonyLib.AccessTools.TypeByName("PlayMakerFSM");
            Transform at=source==null?null:source.transform;
            for(int depth=0;at!=null&&depth<4&&text.Length<1400;depth++,at=at.parent)
            {
                foreach(Component component in at.GetComponents<MonoBehaviour>())
                {
                    if(component==null)continue;
                    Type type=component.GetType();
                    if(type.Name=="DamageHero")text.Append("[DamageHero damage=").Append(Read(component,"damageDealt"))
                        .Append(" hazard=").Append(Read(component,"hazardType")).Append(']');
                    if(fsmType==null||!fsmType.IsInstanceOfType(component))
                    {text.Append("[script=").Append(type.FullName).Append(']');continue;}
                    object state=Read(Read(component,"Fsm"),"ActiveState");
                    text.Append('[').Append(at.name).Append(':').Append(Read(component,"FsmName"))
                        .Append('=').Append(Read(state,"Name")).Append(" enabled=").Append(((Behaviour)component).isActiveAndEnabled);
                    if(Read(state,"Actions") is Array actions)
                    {
                        int captured=0;
                        foreach(object action in actions)
                        {
                            if(action==null)continue;string name=action.GetType().Name;
                            if(!name.StartsWith("Trigger2dEvent",StringComparison.Ordinal)&&!name.StartsWith("Collision2dEvent",StringComparison.Ordinal)
                                &&!name.StartsWith("ReceivedDamage",StringComparison.Ordinal))continue;
                            text.Append(" {action=").Append(name).Append(" enabled=").Append(Read(action,"Enabled"))
                                .Append(" tag=").Append(Read(Read(action,"collideTag"),"Value"))
                                .Append(" layer=").Append(Read(Read(action,"collideLayer"),"Value"))
                                .Append(" event-kind=").Append(Read(action,"trigger")??Read(action,"collision"))
                                .Append(" event=").Append(Read(Read(action,"sendEvent"),"Name")).Append('}');
                            if(++captured==4)break;
                        }
                    }
                    text.Append(']');if(text.Length>=1400)break;
                }
            }
            return text.Length>1400?text.ToString(0,1400)+"...":text.ToString();
        }
        private static object Read(object value,string name)
        {if(value==null)return null;Type type=value.GetType();return type.GetProperty(name,Any)?.GetValue(value,null)??type.GetField(name,Any)?.GetValue(value);}
    }

    public sealed partial class Plugin
    {
        private static ProjectileTrace.Frame ProjectileFrame(object controller,string move,Bounds reach,Vector3 motion,bool hasReach,int count,float angle)
        {
            if(!DebugSettings.VerboseLogging)return default(ProjectileTrace.Frame);
            KnightResetFollow follow=Instance.knightFollow;uint action=follow.Action(controller);int anim=follow.Animation(controller);
            string window=action==swingAction&&anim==swingAnim
                ?"age="+(Time.unscaledTime-swingStart).ToString("0.000",CultureInfo.InvariantCulture)+" limit="+HitWindow(move)
                :"not tracked for this action/animation yet";
            return new ProjectileTrace.Frame{Move=move,Action=action,Animation=anim,AnimFrame=follow.AnimFrame(controller),
                Reach=reach,Motion=motion,HasReach=hasReach,Count=count,Angle=angle,Window=window,Triggers=Physics2D.queriesHitTriggers};
        }
        // A void/read-only prefix also observes hits skipped by later cap/pogo guards.
        private static void TraceIncomingProjectile(object __instance,GameObject __0,int __2,int __3)
        {
            if(!ProjectileTrace.CanObserveBody||__2<=0)return;
            try
            {
                KnightResetFollow follow=Instance?.knightFollow;object controller=follow?.Controller();
                if(controller==null||!follow.MarioAlive(controller)||!ReferenceEquals(__instance,follow.Hero(controller)))return;
                object row=FindRow(sheetAttacksField?.GetValue(null) as Array,follow.Action(controller));
                string move=row==null?"<no attack row>":Convert.ToString(rowIdField.GetValue(row));
                Bounds reach=default;Vector3 motion=Vector3.zero;bool hasReach=false;float angle=0;
                Collider2D body=follow.HeroTransform(controller)?.GetComponent<Collider2D>();
                if(body!=null&&MoveShape(move,out float pad,out float forward,out float up))
                {
                    float dir=Mathf.Sin(follow.FaceAngle(controller))>=0?1:-1;
                    reach=SceneryContact.Reach(body.bounds,dir,pad,forward,up,NailCharmReach.Multiplier(__instance));
                    Vector3? feet=follow.Ground(controller);motion=feet.HasValue&&stompTickStart.HasValue?feet.Value-stompTickStart.Value:Vector3.zero;
                    hasReach=true;angle=NailHitDirection.ForMove(dir>0?0:180,move);
                }
                bool attackImpact=projectileImpactRoot!=null&&ReferenceEquals(projectileImpactHero,__instance);
                ProjectileTrace.Body(ProjectileFrame(controller,move,reach,motion,hasReach,-1,angle),__0,__2,__3,attackImpact);
            }
            catch(Exception){} // Diagnostics must never affect damage delivery.
        }
    }
}
