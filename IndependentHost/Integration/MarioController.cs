using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnitySceneManager=UnityEngine.SceneManagement.SceneManager;

namespace MarioHollow
{
    public sealed class MarioController:MonoBehaviour
    {
        public static MarioController Instance;
        public bool Wanted=true;
        public int MarioId=-1;
        public long OwnershipEpoch {get;private set;}
        public SM64MarioState State,PrevState;
        public Vector3 MarkPosition;
        public MarioRenderer Renderer;
        public MarioAttacks Attacks=new MarioAttacks();
        public HeroController hero;
        internal Rigidbody2D body;
        internal BoxCollider2D box;
        internal float feetOffset;
        internal readonly WorldCollision collision=new WorldCollision();
        private MeshRenderer knight;
        private bool oldHidden,oldEnabled,oldKinematic,oldFullContacts;
        private float landingLeft,landingDuration;
        private bool landingPoseActive;
        private int landingPosePrevious;
        public bool HardLanding => landingLeft>0f;
        public bool RecoveryJumpHeld;
        public void BeginHardLanding(float duration){landingDuration=landingLeft=Mathf.Clamp(duration,.05f,1f);}
        public void CancelHardLanding()
        {
            landingLeft=0;
            if(landingPoseActive){BridgeContracts.EndLandingAnimation(landingPosePrevious);landingPoseActive=false;}
        }
        private Vector3? damageSource;
        private bool damagePending;
        public bool DamagePending => damagePending;
        public bool GameplayReady=>Wanted&&!failed&&Sm64.Ready&&KnightFree(hero);
        private string mode;
        private float lastDriftTrace;
        private float lastSupportTrace;
        private float oldGravity,accumulator;
        private int scene;
        private bool failed;
        private float retryAfter;
        private static readonly BindingFlags Any=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private void Awake(){Instance=this;UnitySceneManager.activeSceneChanged+=Changed;}
        private void Changed(Scene previous,Scene next){Deactivate();collision.Dispose();scene=next.handle;failed=false;}
        public string Diagnostics=>"scene="+scene+", mode="+mode+", epoch="+OwnershipEpoch+", id="+MarioId+", wanted="+Wanted+", action="+State.action.ToString("X")+", anim="+State.animID.ToString("X")+"/"+State.animFrame+", native invinc="+State.invincTimer+", velocity="+State.vx+","+State.vy+", facing="+State.faceAngle;
        private void NoteMode(string value){if(mode==value)return;mode=value;HostTrace.Write("ownership "+Diagnostics+(hero==null?"; no hero":"; controlHeld="+Flag(hero,"controlReqlinquished")+", accepts="+Flag(hero,"acceptingInput")+", recoil="+hero.cState.recoiling+", transition="+hero.cState.transitioning));}
        public bool NativeBodyWasHidden(HeroController current)=>BridgeContracts.BaselineHidden(current);
        public void SetDamageSource(Vector3 world){damageSource=world;}
        // A bonk/fall action carries SM64's protection flag without an HK hit.
        // Only real post-hit protection may turn that flag into mask immunity.
        public bool BlocksContactDamage(int hazardType,bool hitProtection)=>hitProtection&&MarioId>=0&&hazardType==1&&((State.action&0x00020000u)!=0||State.invincTimer>0);
        public static bool DamageOwnsControl(int id,bool recoil,bool frozen,bool pending,uint action)
            =>pending||(id>=0&&(recoil||frozen||(action&0x00020000u)!=0));
        public void SetWanted(bool wanted){Wanted=wanted;if(!wanted)Deactivate();}
        public void Toggle(){SetWanted(!Wanted);}
        public bool KnightFree(HeroController knight)
        {
            if(GameManager.UnsafeInstance==null||GameManager.UnsafeInstance.gameState!=GlobalEnums.GameState.PLAYING)return false;
            if(knight==null||!knight.gameObject.activeInHierarchy)return false;
            var flags=knight.cState;
            if(flags==null||flags.dead||flags.hazardDeath||flags.hazardRespawning||flags.transitioning)return false;
            // A real hit relinquishes Knight input for recoil. Keep the current
            // Mario actor running so its native knockback can actually play.
            if(DamageOwnsControl(MarioId,flags.recoiling,flags.recoilFrozen,damagePending,State.action))return true;
            // Recoil flags and presentation clocks never grant Knight movement.
            // Only an explicit native action owns this body while Mario is parked.
            return !MovementOwnership.NativeBodyRequired(this,knight);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]private void Update()
        {
            HeroController current=HeroController.SilentInstance;
            if(hero!=current){Deactivate();hero=current;failed=false;}
            // Pause is not a scripted handover. Keep the same actor and frozen
            // interpolation state; menu input must not toggle or advance Mario.
            if(GameManager.UnsafeInstance!=null&&GameManager.UnsafeInstance.gameState==GlobalEnums.GameState.PAUSED)
            {NoteMode("Mario paused");return;}
            if(Input.GetKeyDown(KeyCode.M))Toggle();
            if(!Wanted||failed||!Sm64.Ready||!KnightFree(hero)){NoteMode(!Wanted?"Knight selected":failed?"failed":!Sm64.Ready?"ROM not ready":"native scripted control");if(MarioId>=0)Deactivate();return;}
            if(Time.unscaledTime<retryAfter)return;
            if(Time.timeScale<=0f)return;
            try
            {
                if(MarioId<0)Activate();if(MarioId<0)return;
                NoteMode("Mario gameplay");
                accumulator+=Time.deltaTime;
                for(int i=0;accumulator>=1f/30f&&i<4;i++){accumulator-=1f/30f;Tick();if(MarioId<0)return;}
                accumulator=Mathf.Min(accumulator,1f/30f);
            }
            catch(Exception error){failed=true;Deactivate();UnityEngine.Debug.LogError("Hollow Mario independent controller: "+error);}
        }
        private void FixedUpdate()
        {
            if(MarioId>=0&&body!=null)body.velocity=Vector2.zero;
        }
        private void LateUpdate()
        {
            if(MarioId<0||Renderer==null)return;
            body.velocity=Vector2.zero;
            Renderer.Render(Mathf.Clamp01(accumulator*30f),ViewPosition-Vector3.up*feetOffset);
            if(knight!=null)knight.forceRenderingOff=true;
        }
        public Vector3 ViewPosition=>Vector3.Lerp(MarioMath.World(PrevState.px,PrevState.py,PrevState.pz),
            MarioMath.World(State.px,State.py,State.pz),Mathf.Clamp01(accumulator*30f))+Vector3.up*feetOffset;
        [MethodImpl(MethodImplOptions.NoInlining)]public void Activate()
        {
            if(MarioId>=0||hero==null||!KnightFree(hero))return;
            body=hero.GetComponent<Rigidbody2D>();box=hero.GetComponent<BoxCollider2D>();knight=hero.GetComponent<MeshRenderer>();
            if(body==null||box==null||knight==null)throw new InvalidOperationException("Native hero body unavailable.");
            oldEnabled=hero.enabled;oldHidden=NativeBodyWasHidden(hero);oldKinematic=body.isKinematic;oldGravity=body.gravityScale;oldFullContacts=body.useFullKinematicContacts;
            MarioMath.Origin=hero.transform.position;MarioMath.LaneZ=hero.transform.position.z;
            feetOffset=hero.transform.position.y-box.bounds.min.y;
            collision.Rebuild(hero.transform.position);
            Vector3 feet=new Vector3(hero.transform.position.x,box.bounds.min.y,hero.transform.position.z),spawn=MarioMath.Native(feet);
            collision.EnsureSafetyFloor(spawn);
            Vector2 returnVelocity=body.velocity;
            Physics2D.SyncTransforms();
            bool touchingGround=hero.CheckTouchingGround();
            MarioId=Native.sm64_mario_create(spawn.x,spawn.y,0);
            if(MarioId<0){retryAfter=Time.unscaledTime+.5f;return;}
            OwnershipEpoch++;
            State=StartGameplay(MarioId,spawn,returnVelocity,touchingGround,hero.cState.facingRight?Mathf.PI*.5f:-Mathf.PI*.5f);PrevState=State;
            // Keep the game's established colliders. Shared StartupHurtbox owns
            // crouch/startup changes; the host must not introduce a second size.
            var damageBox=hero.transform.Find("HeroBox")?.GetComponent<BoxCollider2D>();
            Renderer=new MarioRenderer("Hollow Mario player");
            Renderer.MeshRenderer.sortingLayerID=knight.sortingLayerID;Renderer.MeshRenderer.sortingOrder=knight.sortingOrder;
            hero.enabled=false;body.isKinematic=true;body.useFullKinematicContacts=true;body.gravityScale=0;body.velocity=Vector2.zero;knight.forceRenderingOff=true;
            accumulator=1f/30f;MarkPosition=hero.transform.position;
            HostTrace.Write("activate "+Diagnostics+"; physical="+box.size+" offset="+box.offset+"; hurt="+(damageBox!=null?damageBox.size.ToString():"missing")+"; feet offset="+feetOffset+"; original body hidden="+oldHidden+"; host grounded="+touchingGround+"; return velocity="+returnVelocity+"; floor adjustment="+(State.py-spawn.y));
        }
        // Use the current host feet and motion, never an artificial spawn drop.
        // Only reconcile small collider/triangle rounding gaps on a grounded return.
        public static SM64MarioState StartGameplay(int id,Vector3 feet,Vector2 velocity,bool touchingGround,float facing)
        {
            float floor=Native.sm64_surface_find_floor(feet.x,feet.y+12f,0,out IntPtr surface);
            bool supported=touchingGround&&velocity.y<=.1f&&surface!=IntPtr.Zero&&Mathf.Abs(feet.y-floor)<=12f;
            if(supported)feet.y=floor;
            Vector2 speed=supported?Vector2.zero:velocity/(30f*MarioMath.Scale);
            float forward=speed.x*Mathf.Sin(facing);
            uint action=supported?0x0C400201u:0x0100088Cu;
            Native.sm64_set_mario_position(id,feet.x,feet.y,0);
            Native.sm64_set_mario_action(id,action);
            Native.sm64_set_mario_velocity(id,speed.x,speed.y,0);
            Native.sm64_set_mario_forward_velocity(id,forward);
            Native.sm64_set_mario_faceangle(id,facing);
            return new SM64MarioState{px=feet.x,py=feet.y,pz=0,vx=speed.x,vy=speed.y,health=2176,action=action,faceAngle=facing,forwardVelocity=forward};
        }
        [MethodImpl(MethodImplOptions.NoInlining)]public void Tick()
        {
            if(MarioId<0)return;
            long owner=OwnershipEpoch;
            float drift=(hero.transform.position-MarkPosition).sqrMagnitude;
            if(drift>.01f&&Time.unscaledTime-lastDriftTrace>1){lastDriftTrace=Time.unscaledTime;HostTrace.Write("proxy moved between native ticks: delta="+(hero.transform.position-MarkPosition)+"; "+Diagnostics);}
            var support=MovingSupport.Capture(State);PrevState=State;
            collision.Tick(hero.transform.position,false);
            float lift=MovingSupport.Follow(support,MarioId,ref State);
            if(lift!=0f)
            {
                if(Time.unscaledTime-lastSupportTrace>1f){lastSupportTrace=Time.unscaledTime;HostTrace.Write("moving floor carry: dy="+lift+"; "+Diagnostics);}
            }
            var input=InputHandler.Instance;
            var native=HostInput.Read(input!=null?input.inputActions:null,HardLanding);
            RecoveryJumpHeld=HardLanding&&input!=null&&input.inputActions!=null&&input.inputActions.jump.IsPressed;
            bool landingOverride=HardLanding;
            landingPosePrevious=landingOverride?BridgeContracts.BeginTimedLanding(MarioId,landingDuration-landingLeft,landingDuration):-1;
            landingPoseActive=landingOverride;
            try{Native.sm64_mario_tick(MarioId,ref native,ref State,ref Renderer.Buffers);}
            finally{if(landingPoseActive){BridgeContracts.EndLandingAnimation(landingPosePrevious);landingPoseActive=false;}}
            if(MarioId<0||OwnershipEpoch!=owner)return;
            MovingSupport.InheritTakeoff(support,lift,MarioId,ref State);
            landingLeft=Mathf.Max(0f,landingLeft-1f/30f);
            damagePending=false;
            if(State.action!=PrevState.action)HostTrace.Write("native action "+PrevState.action.ToString("X")+" -> "+State.action.ToString("X")+"; anim="+State.animID.ToString("X")+"/"+State.animFrame+"; held="+native.buttonA+","+native.buttonB+","+native.buttonZ+"; velocity="+State.vx+","+State.vy+"; facing="+State.faceAngle);
            // The integration is a 2D lane. Never let native steering drift the
            // proxy out of its interaction/camera plane.
            Native.sm64_set_mario_position(MarioId,State.px,State.py,0);State.pz=0;
            Vector3 feet=MarioMath.World(State.px,State.py,0);hero.transform.position=feet+Vector3.up*feetOffset;MarkPosition=hero.transform.position;
            bool grounded=BridgeContracts.Grounded(State.action);
            hero.cState.onGround=grounded;body.velocity=Vector2.zero;
            if(BridgeContracts.FinishedLedgeClimb(PrevState.action,State.action))hero.cState.willHardLand=false;
            Physics2D.SyncTransforms();
            if(BridgeContracts.LandedOnFloor(PrevState.action,State.action))BridgeContracts.Landed(this);
            if(MarioId<0||OwnershipEpoch!=owner||Renderer==null)return; // A native event may transfer ownership, even when the actor ID is reused.
            float facing=Mathf.Sin(State.faceAngle);
            if(Mathf.Abs(facing)>.1f&&hero.cState.facingRight!=(facing>0)){if(facing>0)hero.FaceRight();else hero.FaceLeft();HostTrace.Write("Knight proxy facing synchronized: "+hero.cState.facingRight);}
            if(Mathf.Abs(State.px)>2800||Mathf.Abs(State.py)>2800)Recenter();
            Renderer.TakeTick();Attacks.Tick(this,hero);
        }
        public void Recenter()
        {
            if(MarioId<0)return;
            Vector2 shift=new Vector2(State.px,State.py);MarioMath.Origin+=shift*MarioMath.Scale;
            State.px=State.py=0;PrevState.px-=shift.x;PrevState.py-=shift.y;
            Native.sm64_set_mario_position(MarioId,0,0,0);collision.Rebuild(hero.transform.position);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]public void Deactivate()
        {
            if(MarioId<0)return;
            HostTrace.Write("deactivate "+Diagnostics);
            OwnershipEpoch++;
            Native.sm64_mario_delete(MarioId);MarioId=-1;Renderer?.Dispose();Renderer=null;
            if(hero!=null){hero.enabled=oldEnabled;if(body!=null){body.isKinematic=oldKinematic;body.useFullKinematicContacts=oldFullContacts;body.gravityScale=oldGravity;body.velocity=Vector2.zero;}}
            if(knight!=null)knight.forceRenderingOff=oldHidden;accumulator=0;landingLeft=0;damagePending=false;damageSource=null;
        }
        public void OnKnightHit(int damage,int hazardType)
        {
            landingLeft=0;
            if(MarioId<0)
            {
                // A confirmed hit can interrupt a spell's private presentation
                // while the gameplay actor is parked. Queue ownership first so
                // Activate can resume during the host's already-started hit-stop.
                if(!Wanted||!Sm64.Ready||hero==null||hazardType!=1)return;
                damagePending=true;
                float resumeGravity=oldGravity;
                try{Activate();}catch{damagePending=false;throw;}
                if(MarioId<0){damagePending=false;return;}
                // StartRecoil temporarily disables Knight gravity. Retain the
                // gameplay baseline for the next ordinary scripted handover.
                oldGravity=resumeGravity;
            }
            Native.sm64_set_mario_health(MarioId,2176);
            Vector3 source=damageSource.HasValue?MarioMath.Native(damageSource.Value):new Vector3(State.px+(hero.cState.facingRight?100:-100),State.py,0);damageSource=null;
            HostTrace.Write("accepted hit: damage="+damage+", hazard="+hazardType+", native source="+source+"; "+Diagnostics);
            Native.sm64_mario_take_damage(MarioId,(uint)Math.Max(1,damage),0,source.x,source.y,source.z);
            damagePending=true;
        }
        public void Shutdown(){UnitySceneManager.activeSceneChanged-=Changed;Deactivate();collision.Dispose();if(Instance==this)Instance=null;}
        private void OnDestroy(){if(Instance==this)Shutdown();}
        private static object Read(object value,string name)
        {if(value==null)return null;Type t=value.GetType();return t.GetField(name,Any)?.GetValue(value)??t.GetProperty(name,Any)?.GetValue(value,null);}
        private static bool Flag(object value,string name)=>Read(value,name) is bool flag&&flag;
    }
}
