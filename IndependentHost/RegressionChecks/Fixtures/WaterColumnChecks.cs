// Run the packaged extension against controlled Physics2D query/scene endpoints.
// These are geometry-policy fixtures, not a live Unity-water playtest.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Runtime.InteropServices;
using HarmonyLib;
using MarioHollow;
using UnityEngine;

class WaterColumnChecks
{
 const BindingFlags A=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 sealed class Shape {
  public Collider2D Collider;public GameObject Object;public Bounds Bounds;
  public string Tag;public int Layer;public bool Trigger,Enabled=true,Active=true,Ignored,Composite;
  public float Left=-2,Right=2,Bottom,Top;
 }
 static readonly Dictionary<int,Shape> shapes=new Dictionary<int,Shape>();
 sealed class Identity<T>:IEqualityComparer<T> where T:class {public bool Equals(T a,T b)=>ReferenceEquals(a,b);public int GetHashCode(T obj)=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);}
 static readonly Dictionary<Collider2D,Shape> colliders=new Dictionary<Collider2D,Shape>(new Identity<Collider2D>());
 static readonly Dictionary<GameObject,Shape> objects=new Dictionary<GameObject,Shape>(new Identity<GameObject>());
 static readonly List<Shape> scene=new List<Shape>();
 static readonly FieldInfo colliderId=typeof(RaycastHit2D).GetField("m_Collider",A),distanceField=typeof(RaycastHit2D).GetField("m_Distance",A);
 static int serial,checks,queries;static bool acidAllowed,maskAvailable=true;static MethodInfo find;static Type columns;
 static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static T Empty<T>() where T:UnityEngine.Object {
  var obj=(T)FormatterServices.GetUninitializedObject(typeof(T));
  typeof(UnityEngine.Object).GetField("m_CachedPtr",A).SetValue(obj,new IntPtr(++serial));return obj;
 }
 static Shape Add(string tag,float bottom,float top,bool trigger=true,int layer=0) {
  var s=new Shape{Collider=Empty<BoxCollider2D>(),Object=Empty<GameObject>(),Tag=tag,Bottom=bottom,Top=top,Trigger=trigger,Layer=layer};
  s.Bounds=new Bounds(new Vector3(0,(bottom+top)/2,50),new Vector3(4,top-bottom,1));
  int id=(int)((IntPtr)typeof(UnityEngine.Object).GetField("m_CachedPtr",A).GetValue(s.Collider)).ToInt64();
  shapes.Add(id,s);colliders.Add(s.Collider,s);objects.Add(s.Object,s);scene.Add(s);return s;
 }
 static bool Equal(UnityEngine.Object a,UnityEngine.Object b)=>ReferenceEquals(a,b);
 static bool Unequal(UnityEngine.Object a,UnityEngine.Object b)=>!ReferenceEquals(a,b);
 static GameObject ObjectOf(Component c)=>colliders[(Collider2D)c].Object;
 static bool Enabled(Behaviour c)=>colliders[(Collider2D)c].Enabled;
 static bool Active(GameObject obj)=>objects[obj].Active;
 static bool Trigger(Collider2D c)=>colliders[c].Trigger;
 static bool Composite(Collider2D c)=>colliders[c].Composite;
 static string Tag(GameObject obj)=>objects[obj].Tag;
 static int Layer(GameObject obj)=>objects[obj].Layer;
 static Bounds BoundsOf(Collider2D c)=>colliders[c].Bounds;
 static bool Ignore(Collider2D c)=>colliders[c].Ignored;
 static bool CanAcid(object hero)=>acidAllowed;
 static int Mask(string[] names)=>maskAvailable?1<<8|1<<11:0;
 static Collider2D ColliderOf(ref RaycastHit2D hit)=>shapes[(int)colliderId.GetValue(hit)].Collider;
 static int Ray(Vector2 origin,Vector2 direction,ContactFilter2D filter,RaycastHit2D[] results,float distance) {
  queries++;Check(direction==Vector2.up&&float.IsPositiveInfinity(distance)&&filter.useTriggers,"Only an upward column query with triggers; no global physics setting writes.");
  int count=0;
  foreach(var s in scene){
   if(!s.Enabled||!s.Active||origin.x<s.Left||origin.x>s.Right||origin.y>s.Top)continue;
   if(count==results.Length)return count;
   int id=(int)((IntPtr)typeof(UnityEngine.Object).GetField("m_CachedPtr",A).GetValue(s.Collider)).ToInt64();
   object value=default(RaycastHit2D);colliderId.SetValue(value,id);distanceField.SetValue(value,Math.Max(0,s.Bottom-origin.y));results[count++]=(RaycastHit2D)value;
  }
  // Deliberately not distance sorted: policy must not trust enumeration order.
  return count;
 }
 static IEnumerable<CodeInstruction> Endpoints(IEnumerable<CodeInstruction> instructions) {
  foreach(var ins in instructions){
   var m=ins.operand as MethodInfo;string replacement=null;
   if(m!=null){
    if(m.DeclaringType==typeof(Physics2D)&&m.Name=="Raycast")replacement="Ray";
    else if(m.DeclaringType==typeof(RaycastHit2D)&&m.Name=="get_collider")replacement="ColliderOf";
    else if(m.DeclaringType==typeof(LayerMask)&&m.Name=="GetMask")replacement="Mask";
    else if(m.DeclaringType==typeof(Component)&&m.Name=="get_gameObject")replacement="ObjectOf";
    else if(m.DeclaringType==typeof(Behaviour)&&m.Name=="get_enabled")replacement="Enabled";
    else if(m.DeclaringType==typeof(GameObject)&&m.Name=="get_activeInHierarchy")replacement="Active";
    else if(m.DeclaringType==typeof(GameObject)&&m.Name=="get_tag")replacement="Tag";
    else if(m.DeclaringType==typeof(GameObject)&&m.Name=="get_layer")replacement="Layer";
    else if(m.DeclaringType==typeof(Collider2D)&&m.Name=="get_isTrigger")replacement="Trigger";
    else if(m.DeclaringType==typeof(Collider2D)&&m.Name=="get_usedByComposite")replacement="Composite";
    else if(m.DeclaringType==typeof(Collider2D)&&m.Name=="get_bounds")replacement="BoundsOf";
    else if(m.DeclaringType==typeof(UnityEngine.Object)&&m.Name=="op_Equality")replacement="Equal";
    else if(m.DeclaringType==typeof(UnityEngine.Object)&&m.Name=="op_Inequality")replacement="Unequal";
    else if(m.DeclaringType.Name=="HeroIgnores"&&m.Name=="ShouldIgnore")replacement="Ignore";
    else if(m.DeclaringType.Name=="AcidSafety"&&m.Name=="CanEnterAcid")replacement="CanAcid";
   }
   if(replacement!=null){ins.opcode=OpCodes.Call;ins.operand=typeof(WaterColumnChecks).GetMethod(replacement,A);}
   yield return ins;
  }
 }
 static bool Find(float x,float y,out float top,out bool acid,out Collider2D pool) {
  object[] args={new Vector2(x,y),null,0f,false,null};bool found=(bool)find.Invoke(null,args);top=(float)args[2];acid=(bool)args[3];pool=(Collider2D)args[4];
  if(!found)Check(float.IsNaN(top)&&!acid&&ReferenceEquals(pool,null),"Rejected column clears every output.");return found;
 }
 static bool Found(float x=0,float y=0){float top;bool acid;Collider2D pool;return Find(x,y,out top,out acid,out pool);}
 static void Reset(){scene.Clear();acidAllowed=false;maskAvailable=true;}
 static void NativeSwimming() {
  Reset();Add("Water Surface",9,10);float top;bool acid;Collider2D pool;
  Check(Find(0,0,out top,out acid,out pool),"Native swim scenario has a clear projected column.");
  TestEnvironment.StartNative();
  var floor=new[]{new SM64Surface{terrain=1,v0x=-8000,v0y=-1000,v0z=-8000,v1x=-8000,v1y=-1000,v1z=8000,v2x=8000,v2y=-1000,v2z=8000},new SM64Surface{terrain=1,v0x=-8000,v0y=-1000,v0z=-8000,v1x=8000,v1y=-1000,v1z=8000,v2x=8000,v2y=-1000,v2z=-8000}};
  var floorPin=GCHandle.Alloc(floor,GCHandleType.Pinned);var pins=new GCHandle[4];uint platform=uint.MaxValue;int id=-1;
  try{
   var obj=new SM64SurfaceObject{surfaceCount=2,surfaces=floorPin.AddrOfPinnedObject()};platform=Native.sm64_surface_object_create(ref obj);id=Native.sm64_mario_create(0,0,0);Check(id>=0,"Native actor starts below the thin water trigger.");
   var buffers=new SM64MarioGeometryBuffers();object boxed=buffers;string[] fields={"position","normal","color","uv"};
   for(int i=0;i<4;i++){pins[i]=GCHandle.Alloc(new float[i==3?6144:9216],GCHandleType.Pinned);typeof(SM64MarioGeometryBuffers).GetField(fields[i]).SetValue(boxed,pins[i].AddrOfPinnedObject());}buffers=(SM64MarioGeometryBuffers)boxed;
   var state=new SM64MarioState();var input=default(SM64MarioInputs);object[] audio={0u,1088u,new short[8192]};
   Native.sm64_set_mario_water_level(id,(int)Math.Round(top/MarioMath.Scale));
   for(int i=0;i<20;i++){Native.sm64_mario_tick(id,ref input,ref state,ref buffers);TestEnvironment.PullAudio(audio);}
   var swimming=Assembly.LoadFrom(TestEnvironment.CompanionPath).GetType("HollowMario.Swimming").GetMethod("UsesSwimControls",A);
   Check((bool)swimming.Invoke(null,new object[]{state.action}),"Projected surface makes the real SM64 actor swim below the trigger instead of falling dry.");
   Add("Untagged",4,5,false,8);Check(!Found(),"Intervening floor withdraws that water surface.");
   Native.sm64_set_mario_water_level(id,-1000000);
   for(int i=0;i<5;i++){Native.sm64_mario_tick(id,ref input,ref state,ref buffers);TestEnvironment.PullAudio(audio);}
   Check(!(bool)swimming.Invoke(null,new object[]{state.action}),"The real SM64 actor leaves swimming when geometry removes the supplied surface.");
  }finally{if(id>=0)Native.sm64_mario_delete(id);if(platform!=uint.MaxValue)Native.sm64_surface_object_delete(platform);floorPin.Free();foreach(var pin in pins)if(pin.IsAllocated)pin.Free();}
 }
 static int Main(string[] args){try{
  var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);columns=mod.GetType("HollowMario.WaterColumns",true);find=columns.GetMethod("Find",A);
  var h=new Harmony("water-column-policy");h.Patch(find,transpiler:new HarmonyMethod(typeof(WaterColumnChecks),"Endpoints"));
  float top;bool acid;Collider2D source;
  Reset();var water=Add("Water Surface",9,10);
  Check(Find(0,-50,out top,out acid,out source)&&top==10&&!acid&&ReferenceEquals(source,water.Collider),"Water extends below a shallow real trigger with its own surface and source.");
  Check(!Found(3)&&!Found(0,11),"No lateral or above-surface expansion.");
  maskAvailable=false;Check(!Found(),"Missing terrain metadata cannot silently flood a column.");maskAvailable=true;
  water.Left=.2f;Check(!Found(),"A ray miss inside the AABB does not fill an irregular liquid shape's notch.");water.Left=-2;
  var floor=Add("Untagged",4,5,false,8);Check(!Found(),"Terrain floor blocks a pool overhead.");
  floor.Bottom=9.4f;floor.Top=9.8f;Check(!Found(),"A floor within the water trigger still cuts off the downward extension.");
  floor.Bottom=11;floor.Top=12;Check(Found(),"Geometry above the actual surface does not drain the pool.");
  floor.Bottom=-3;floor.Top=-2;Check(Found(),"Ground below Mario does not drain water above him.");
  floor.Bottom=4;floor.Top=5;floor.Layer=11;Check(!Found(),"Soft Terrain also stops the column.");
  floor.Ignored=true;Check(Found(),"Hero-ignored terrain does not invent a water barrier.");floor.Ignored=false;
  floor.Trigger=true;Check(Found(),"Unrelated trigger does not block a liquid column.");floor.Trigger=false;
  floor.Layer=9;Check(Found(),"Enemies and other nonterrain bodies do not cut water off.");floor.Layer=8;
  floor.Enabled=false;Check(Found(),"Disabled geometry stops blocking immediately.");floor.Enabled=true;floor.Active=false;Check(Found(),"Inactive geometry stops blocking immediately.");
  floor.Active=true;Check(!Found(),"A newly restored floor cuts off the column with no stale retention.");
  Reset();water=Add("Water Surface",9,10);var lower=Add("Water Surface",3,4);
  Check(Find(0,0,out top,out acid,out source)&&top==4&&ReferenceEquals(source,lower.Collider),"Stacked pools use the nearest real pool, not the highest surface.");
  Reset();Add("waterfall",1,2);Check(!Found(),"Decorative/named water is not a pool.");
  Reset();Add("Water Surface",1,2,false);Check(!Found(),"Only real trigger volumes supply water.");
  Reset();Add("Water Surface",9,10);var acidPool=Add("Acid",3,4);
  Check(!Found(),"Unsafe acid cannot be bypassed by borrowing safe water higher up.");
  acidAllowed=true;Check(Find(0,0,out top,out acid,out source)&&acid&&top==4&&ReferenceEquals(source,acidPool.Collider),"Existing Tear/Metal permission permits the nearest acid column.");
  acidAllowed=false;Check(!Found(),"Revoking acid permission takes effect on the next query.");
  Reset();water=Add("Water Surface",9,10);water.Enabled=false;Check(!Found(),"A removed/disabled pool leaves no cached water plane.");
  Reset();for(int i=0;i<80;i++)Add("Untagged",1,2);Add("Water Surface",9,10);Add("Untagged",4,5,false,8);
  int before=queries;Check(!Found()&&queries>before+1,"Overflow retries cannot hide a late solid barrier.");
  scene.RemoveAt(scene.Count-1);Check(Found(),"A pool beyond the original result capacity is found after growth.");
  Reset();for(int i=0;i<4097;i++)Add("Untagged",1,2);Check(!Found(),"An unresolved result overflow fails closed instead of flooding an unknown column.");
  NativeSwimming();
  h.UnpatchSelf();Console.WriteLine("PASS "+checks+" water-column checks: controlled Physics2D query endpoints, terrain/permission boundaries, moving-state changes, and real native swimming below the trigger. No game or live Unity physics scene was started.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
