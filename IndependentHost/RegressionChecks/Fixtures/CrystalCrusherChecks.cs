using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using UnityEngine;

class CrystalCrusherChecks
{
    const BindingFlags All=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static Component crusher;
    static Animator animator;
    static RuntimeAnimatorController controller;
    static Transform transform;
    static string ownerName,controllerName;
    static float speed;
    static bool enabled;
    static Vector3 position;
    static int serial;
    static T Empty<T>()where T:UnityEngine.Object
    {
        var value=(T)FormatterServices.GetUninitializedObject(typeof(T));
        typeof(UnityEngine.Object).GetField("m_CachedPtr",All).SetValue(value,new IntPtr(++serial));return value;
    }
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static bool Equal(UnityEngine.Object a,UnityEngine.Object b)=>ReferenceEquals(a,b);
    static Animator AnimatorOf(Component value)=>animator;
    static RuntimeAnimatorController ControllerOf(Animator value)=>controller;
    static string NameOf(UnityEngine.Object value)=>ReferenceEquals(value,crusher)?ownerName:controllerName;
    static void Speed(Animator value,float next){speed=next;}
    static bool Enabled(Behaviour value)=>enabled;
    static void Enable(Behaviour value,bool next){enabled=next;}
    static Transform TransformOf(Component value)=>transform;
    static Vector3 PositionOf(Transform value)=>position;
    static void Position(Transform value,Vector3 next){position=next;}
    // Run the real mod postfix and native stop methods. Replace only Unity's
    // scene endpoints: this runner has no game scene or graphics device.
    static IEnumerable<CodeInstruction> Endpoints(IEnumerable<CodeInstruction> code)
    {
        foreach(var instruction in code)
        {
            var method=instruction.operand as MethodInfo;string replacement=null;
            if(method?.DeclaringType==typeof(Component)&&method.Name=="GetComponent"&&method.IsGenericMethod)replacement="AnimatorOf";
            else if(method?.DeclaringType==typeof(UnityEngine.Object)&&method.Name=="get_name")replacement="NameOf";
            else if(method?.DeclaringType==typeof(UnityEngine.Object)&&method.Name=="op_Equality")replacement="Equal";
            else if(method?.DeclaringType==typeof(Animator)&&method.Name=="get_runtimeAnimatorController")replacement="ControllerOf";
            else if(method?.DeclaringType==typeof(Animator)&&method.Name=="set_speed")replacement="Speed";
            else if(method?.DeclaringType==typeof(Behaviour)&&method.Name=="get_enabled")replacement="Enabled";
            else if(method?.DeclaringType==typeof(Behaviour)&&method.Name=="set_enabled")replacement="Enable";
            else if(method?.DeclaringType==typeof(Component)&&method.Name=="get_transform")replacement="TransformOf";
            else if(method?.DeclaringType==typeof(Transform)&&method.Name=="get_localPosition")replacement="PositionOf";
            else if(method?.DeclaringType==typeof(Transform)&&method.Name=="set_localPosition")replacement="Position";
            if(replacement!=null){instruction.opcode=OpCodes.Call;instruction.operand=typeof(CrystalCrusherChecks).GetMethod(replacement,All);}
            yield return instruction;
        }
    }
    static int Main(string[] args)
    {
        var hooks=new Harmony("hollowmario.crystal-crusher-check");
        try
        {
            var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);
            var feature=mod.GetType("HollowMario.CrystalPeakCrushers",true);
            var native=typeof(HeroController).Assembly.GetType("StopAnimatorsAtPoint",true);
            var awake=native.GetMethod("Awake",All);var update=native.GetMethod("Update",All);
            var after=feature.GetMethod("AfterAwake",All);
            var endpoints=new HarmonyMethod(typeof(CrystalCrusherChecks),nameof(Endpoints));
            hooks.Patch(after,transpiler:endpoints);hooks.Patch(awake,transpiler:endpoints);
            hooks.Patch(update,transpiler:endpoints);
            var instant=native.GetMethod("<Start>b__7_1",All);hooks.Patch(instant,transpiler:endpoints);
            feature.GetMethod("Arm",All).Invoke(null,new object[]{hooks});
            Check(Harmony.GetPatchInfo(awake).Postfixes.Count==1,"Real native Awake receives the production postfix");
            crusher=(Component)FormatterServices.GetUninitializedObject(native);
            typeof(UnityEngine.Object).GetField("m_CachedPtr",All).SetValue(crusher,new IntPtr(++serial));
            animator=Empty<Animator>();controller=Empty<AnimatorOverrideController>();transform=Empty<Transform>();
            ownerName="mines_stomper_02";
            var variants=new[]{"mines_stomper_02","mines_stomper_02_refine","mines_stomper_offset","mines_stomper_fast1","mines_stomper_fast2","mines_stomper_offset_big_scene","mines_stomper_offset_big_scene 02"};
            var expectedSpeeds=new[]{.5f,.5f,.5f,.35f,.35f,.5f,.35f};
            for(int variant=0;variant<variants.Length;variant++)
            {
                controllerName=variants[variant];speed=1f;enabled=true;
                awake.Invoke(crusher,null);
                Check(ReferenceEquals(native.GetField("animator",All).GetValue(crusher),animator),"Original Awake caches its real animator before the mod uses the injected field");
                Check(speed==expectedSpeeds[variant]&&enabled,"Fast groups remain slow; the other inspected patterns return to their previous rate: "+controllerName);
                awake.Invoke(crusher,null);Check(speed==expectedSpeeds[variant],"Repeated initialization cannot compound the slowdown");
            }
            ownerName="mines_stomper_02(Clone)";speed=1f;awake.Invoke(crusher,null);Check(speed==.35f,"Instantiated crusher copies are covered");
            foreach(string other in new[]{"stomper_door_contraption","Crusher","Conveyor","mines_stomper_02_extra"})
            {ownerName=other;speed=1.3f;awake.Invoke(crusher,null);Check(speed==1.3f,"Lever, scenery and unrelated animation owners stay unchanged: "+other);}
            ownerName="mines_stomper_02";controllerName="lever_control";speed=1f;awake.Invoke(crusher,null);
            Check(speed==1f,"Matching object name alone cannot slow an unrelated controller");
            var saved=controller;controller=null;awake.Invoke(crusher,null);Check(speed==1f,"Missing controller stays untouched");controller=saved;
            controllerName="mines_stomper_fast2";awake.Invoke(crusher,null);
            native.GetField("shouldStop",All).SetValue(crusher,true);
            native.GetMethod("SetCannotStop",All).Invoke(crusher,null);
            enabled=true;update.Invoke(crusher,null);Check(enabled,"Normal stop request still waits for the animation's safe-stop cue");
            native.GetMethod("SetCanStop",All).Invoke(crusher,null);update.Invoke(crusher,null);
            Check(!enabled&&speed==.35f&&!(bool)native.GetField("shouldStop",All).GetValue(crusher),"Native lever still stops and consumes its request at a safe point");
            enabled=true;position=new Vector3(2,4,6);native.GetField("stopInstantHeight",All).SetValue(crusher,1.2f);
            instant.Invoke(crusher,null);
            Check(!enabled&&speed==.35f&&position==new Vector3(2,1.2f,6),"Instant stop still disables the animator and parks the crusher at its native height");
            Console.WriteLine("PASS real crusher Awake hook, seven animation variants, clones, repeated initialization, unrelated owners/controllers and native safe/instant lever stops. Scene endpoints substituted; no game launched.");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{hooks.UnpatchSelf();}
    }
}
