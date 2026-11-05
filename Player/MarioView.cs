using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Keep camera sampling on the same rendered pose as Mario. The real Hero and
    // its collider still follow simulation ticks; no visual smoothing moves them.
    internal static class MarioView
    {
        private static Type controllerType,mathType;
        private static FieldInfo bufferField,originField,heroTransformField;
        private static MethodInfo targetUpdate;
        private static PropertyInfo viewPosition;
        private static IntPtr geometryPointer;
        private static Vector2 geometryOrigin;
        private static bool pendingGeometry,updatingTarget,cameraReady;
        private static Vector3 renderedHero,sampledHero;
        private static int renderedFrame=-1,lastTargetFrame=-1;
        private static object lastTarget;
        internal static bool HasPendingGeometry=>pendingGeometry;

        internal static void Arm(Harmony harmony,Type controller,Type native)
        {
            cameraReady=false;
            controllerType=controller;mathType=controller.Assembly.GetType("MarioHollow.MarioMath",true);
            originField=AccessTools.Field(mathType,"Origin");
            viewPosition=AccessTools.Property(controller,"ViewPosition");
            Type renderer=controller.Assembly.GetType("MarioHollow.MarioRenderer",true);
            bufferField=AccessTools.Field(renderer,"Buffers");
            harmony.Patch(AccessTools.Method(native,"sm64_mario_tick"),
                postfix:new HarmonyMethod(typeof(MarioView),nameof(AfterNativeTick)){priority=Priority.First});
            harmony.Patch(AccessTools.Method(renderer,"TakeTick"),prefix:new HarmonyMethod(typeof(MarioView),nameof(BeforeGeometry)));
            harmony.Patch(AccessTools.Method(renderer,"Render"),postfix:new HarmonyMethod(typeof(MarioView),nameof(AfterRender)));
            Type target=AccessTools.TypeByName("CameraTarget");
            heroTransformField=AccessTools.Field(target,"heroTransform");targetUpdate=AccessTools.Method(target,"Update");
            harmony.Patch(targetUpdate,
                prefix:new HarmonyMethod(typeof(MarioView),nameof(BeforeTarget)){priority=Priority.First},
                transpiler:new HarmonyMethod(typeof(MarioView),nameof(CameraPositionReads)));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("CameraController"),"LateUpdate"),
                prefix:new HarmonyMethod(typeof(MarioView),nameof(BeforeCamera)){priority=Priority.First});
            cameraReady=true;
        }
        private static object Controller=>CapAccess.Read(controllerType,"Instance");
        private static void AfterNativeTick(int __0,object __3)
        {
            object controller=Controller;
            if(controller==null||Convert.ToInt32(CapAccess.Read(controller,"MarioId"))!=__0)return;
            geometryPointer=(IntPtr)CapAccess.Read(__3,"position");
            geometryOrigin=(Vector2)originField.GetValue(null);pendingGeometry=true;
        }
        private static void BeforeGeometry(object __instance)
        {
            if(!pendingGeometry)return;
            object buffers=bufferField.GetValue(__instance);
            if((IntPtr)CapAccess.Read(buffers,"position")!=geometryPointer)return;
            Vector2 origin=(Vector2)originField.GetValue(null);
            // Native geometry was produced BEFORE Recenter changed the origin.
            // Decode that geometry in its original world position, then interpolate
            // it with the previous mesh normally instead of showing a displaced frame.
            RebaseGeometry(geometryPointer,Convert.ToInt32(CapAccess.Read(buffers,"numTrianglesUsed")),(origin-geometryOrigin)/.0105f);
            pendingGeometry=false;
        }
        internal static unsafe void RebaseGeometry(IntPtr pointer,int triangles,Vector2 delta)
        {
            if(pointer==IntPtr.Zero||triangles<0||triangles>1024||delta==Vector2.zero)return;
            float* vertices=(float*)pointer;
            for(int i=0;i<triangles*9;i+=3){vertices[i]-=delta.x;vertices[i+1]-=delta.y;}
        }
        private static void AfterRender(object __instance,Vector3 feet)
        {
            object controller=Controller;
            if(controller==null||!ReferenceEquals(__instance,CapAccess.Read(controller,"Renderer")))return;
            renderedHero=feet+Vector3.up*CapAccess.Number(controller,"feetOffset");renderedFrame=Time.frameCount;
        }
        private static bool CanFollow(object target)
        {
            object controller=Controller;
            var hero=CapAccess.Read(controller,"hero") as Behaviour;
            object flags=CapAccess.Read(hero,"cState");
            return hero!=null&&!hero.enabled&&Convert.ToInt32(CapAccess.Read(controller,"MarioId"))>=0
                &&ReferenceEquals(hero,CapAccess.Read(target,"hero_ctrl"))
                &&!CapAccess.Flag(flags,"transitioning")
                &&!CapAccess.Flag(flags,"dead")&&!CapAccess.Flag(flags,"hazardDeath")&&!CapAccess.Flag(flags,"hazardRespawning");
        }
        private static bool BeforeTarget(object __instance)
            =>!cameraReady||updatingTarget||!CanFollow(__instance);
        private static void BeforeCamera(object __instance)
        {
            if(!cameraReady)return;
            object target=CapAccess.Read(__instance,"camTarget");
            if(target==null||!CanFollow(target)||(ReferenceEquals(target,lastTarget)&&lastTargetFrame==Time.frameCount))return;
            lastTarget=target;lastTargetFrame=Time.frameCount;
            // Sample the same interpolation as the model, whether its LateUpdate
            // ran before or after the camera. Never alternate with raw tick poses.
            sampledHero=viewPosition!=null?(Vector3)viewPosition.GetValue(Controller,null):
                renderedFrame==Time.frameCount?renderedHero:((Transform)heroTransformField.GetValue(target)).position;
            updatingTarget=true;
            long timing=PresentationTiming.Begin();
            try{targetUpdate.Invoke(target,null);}
            finally{updatingTarget=false;PresentationTiming.End(timing,"camera target");}
        }
        private static Vector3 ReadHeroPosition(Transform transform)
            =>updatingTarget?sampledHero:transform.position;
        private static IEnumerable<CodeInstruction> CameraPositionReads(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo position=AccessTools.PropertyGetter(typeof(Transform),"position");
            MethodInfo replacement=AccessTools.Method(typeof(MarioView),nameof(ReadHeroPosition));
            CodeInstruction previous=null;
            foreach(CodeInstruction instruction in instructions)
            {
                if(previous!=null&&previous.opcode==OpCodes.Ldfld&&Equals(previous.operand,heroTransformField)
                    &&instruction.Calls(position))
                {instruction.opcode=OpCodes.Call;instruction.operand=replacement;}
                yield return instruction;previous=instruction;
            }
        }
        internal static void Forget()
        {pendingGeometry=updatingTarget=false;geometryPointer=IntPtr.Zero;renderedFrame=lastTargetFrame=-1;lastTarget=null;}
    }
}
