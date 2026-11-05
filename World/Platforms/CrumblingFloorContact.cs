using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // These native floors expect a solid Player collision. Mario's kinematic
    // support body can hover a fraction above them; its existing foot trigger
    // supplies the missing contact only when he really reaches the top surface.
    public sealed class CrumblingFloorContact : MonoBehaviour
    {
        private static Type fsmType;
        private static FieldInfo colliderId,otherId,bodyId,otherBodyId,relativeVelocity,collisionEnabled;
        private readonly HashSet<object> delivered=new HashSet<object>();
        private int scene,failures;

        internal static void Arm()
        {
            fsmType=AccessTools.TypeByName("PlayMakerFSM");
            colliderId=AccessTools.Field(typeof(Collision2D),"m_Collider");
            otherId=AccessTools.Field(typeof(Collision2D),"m_OtherCollider");
            bodyId=AccessTools.Field(typeof(Collision2D),"m_Rigidbody");
            otherBodyId=AccessTools.Field(typeof(Collision2D),"m_OtherRigidbody");
            relativeVelocity=AccessTools.Field(typeof(Collision2D),"m_RelativeVelocity");
            collisionEnabled=AccessTools.Field(typeof(Collision2D),"m_Enabled");
            if(fsmType==null||colliderId==null||otherId==null||bodyId==null||otherBodyId==null
                ||relativeVelocity==null||collisionEnabled==null)throw new MissingMemberException("Native floor contact layout");
        }
        private void OnTriggerEnter2D(Collider2D other)=>Touch(other);
        private void OnTriggerStay2D(Collider2D other)=>Touch(other);
        private void OnTriggerExit2D(Collider2D other)=>delivered.Clear();
        private void OnDisable()=>delivered.Clear();

        internal static bool IsFloor(string name)=>name=="collapse small"||name=="collapse tute";
        internal static bool Supports(Bounds floor,Vector3 feet,float halfWidth)
            =>feet.y-floor.max.y>=-.08f&&feet.y-floor.max.y<=.12f
                &&Mathf.Min(feet.x+halfWidth,floor.max.x)-Mathf.Max(feet.x-halfWidth,floor.min.x)>.02f;
        internal static bool Accepts(object action)
        {
            if(action==null||action.GetType().Name!="Collision2dEvent"
                ||(CapAccess.Read(action,"Enabled") is bool enabled&&!enabled))return false;
            object tag=CapAccess.Read(action,"collideTag");
            return !CapAccess.Flag(tag,"IsNone")&&Convert.ToString(CapAccess.Read(tag,"Value"))=="Player"
                &&Convert.ToString(CapAccess.Read(action,"collision"))=="OnCollisionEnter2D"
                &&CapAccess.Read(action,"sendEvent")!=null;
        }
        internal static Collision2D Contact(int playerCollider,int floorCollider,int playerBody,int floorBody,Vector2 velocity)
        {
            var contact=new Collision2D();
            colliderId.SetValue(contact,playerCollider);otherId.SetValue(contact,floorCollider);
            bodyId.SetValue(contact,playerBody);otherBodyId.SetValue(contact,floorBody);
            relativeVelocity.SetValue(contact,velocity);collisionEnabled.SetValue(contact,1);
            return contact;
        }
        internal void Touch(Collider2D other)
        {
            if(fsmType==null||other==null||other.isTrigger||other.gameObject.layer!=8
                ||!other.enabled||!other.gameObject.activeInHierarchy)return;
            KnightResetFollow follow=Plugin.Instance?.knightFollow;
            object controller=follow?.Controller();
            if(controller==null||!follow.MarioAlive(controller)||follow.Busy(controller)||follow.ControlHeld(controller))
            {delivered.Clear();return;}
            if(!follow.Grounded(controller)&&follow.VerticalSpeed(controller)>0f)return;
            Component hero=follow.Hero(controller) as Component;
            var body=hero==null?null:hero.GetComponent<BoxCollider2D>();
            Vector3? feet=follow.Ground(controller);
            if(hero==null||hero.gameObject!=gameObject||body==null||!feet.HasValue
                ||!Supports(other.bounds,feet.Value,body.bounds.extents.x))return;
            int current=UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if(scene!=current){scene=current;delivered.Clear();}
            try
            {
                foreach(Component component in other.GetComponentsInParent(fsmType,true))
                {
                    if(!(component is Behaviour behaviour)||!behaviour.isActiveAndEnabled
                        ||!IsFloor(Convert.ToString(CapAccess.Read(component,"FsmName"))))continue;
                    object fsm=CapAccess.Read(component,"Fsm");
                    object state=CapAccess.Read(fsm,"ActiveState");
                    if(!(CapAccess.Read(state,"Actions") is IEnumerable actions))continue;
                    foreach(object action in actions)
                    {
                        if(delivered.Contains(action)||!Accepts(action))continue;
                        var rb=body.attachedRigidbody;var platform=other.attachedRigidbody;
                        Vector2 velocity=new Vector2(follow.SideSpeed(controller),follow.VerticalSpeed(controller))*(30f*.0105f);
                        if(platform!=null)velocity-=platform.velocity;
                        Collision2D contact=Contact(body.GetInstanceID(),other.GetInstanceID(),rb==null?0:rb.GetInstanceID(),
                            platform==null?0:platform.GetInstanceID(),velocity);
                        // Run the real action's tag check, stored collision values
                        // and configured transition. Its split/break/save sequence stays native.
                        CapAccess.Call(action,"DoCollisionEnter2D",contact);
                        delivered.Add(action);
                        Plugin.Instance?.Log.LogInfo("Mario landed on a crumbling floor: "+component.gameObject.name);
                        break;
                    }
                }
            }
            catch(Exception error)
            {if(failures++<3)Plugin.Instance?.Log.LogWarning("Crumbling floor contact: "+error.GetBaseException().Message);}
        }
    }
}
