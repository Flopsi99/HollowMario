using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("gg.hollowmario.independenthost", BepInDependency.DependencyFlags.HardDependency)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        internal const string PluginGuid = "gg.melty.hollowmario";

        internal const string PluginName = "Hollow Mario";

        internal const string PluginVersion = "0.10.117";

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            ConfigMigration.ImportLegacy(Config);
            if(Array.Exists(AppDomain.CurrentDomain.GetAssemblies(),a=>a.GetName().Name=="MarioHollow"))
                throw new InvalidOperationException("The retired Mario bridge is still loaded. Remove the old installation before starting Hollow Mario.");
            Assembly hostAssembly=Array.Find(AppDomain.CurrentDomain.GetAssemblies(),a=>a.GetName().Name=="HollowMario.Host.Integration");
            controllerType = hostAssembly?.GetType(MovingSafetyFloorSheet.ControllerType,false);
            if (controllerType == null)
            {
                Log.LogError("Moving safety floor could not find Mario's controller.");
                return;
            }
            MethodInfo tick = AccessTools.Method(controllerType, MovingSafetyFloorSheet.UpdateHook);
            MethodInfo deactivate = AccessTools.Method(controllerType, MovingSafetyFloorSheet.DisposeHook);
            if (tick == null || deactivate == null)
            {
                Log.LogError("Moving safety floor could not find Mario's simulation lifecycle methods.");
                return;
            }
            floor = new MovingSafetyFloor(Log);
            knightFollow = new KnightResetFollow(Log, controllerType, MovingSafetyFloorSheet.TeleportThresholdMilli);
            if (!knightFollow.Armed)
            {
                Log.LogWarning("Knight reset follow is not armed: " + knightFollow.Problem);
            }
            harmony = new Harmony(PluginGuid);
            InteractionContacts.Arm(harmony);
            MageRangeContacts.Arm(harmony);
            GeoPickupContact.Arm(harmony);
            try{MarioVolume.Arm(harmony,controllerType.Assembly);}
            catch(Exception error){Log.LogWarning("Mario volume menu: "+error.GetBaseException().Message);}
            DebugSettings.Arm(harmony,Config,Log);
            PresentationTiming.Arm(harmony,controllerType);
            ArmMarioDefault();
            PickupArtwork.Arm(harmony,controllerType,Log);
            CapIcons.Arm(harmony,Log);
            PickupMessages.Arm(harmony,Log);
            UnlockText.Arm(harmony,Log);
            CharmDescriptions.Arm(harmony,Log);
            CapDescriptions.Arm(harmony,Log);
            BossHpHud.Arm(harmony,Log);
            NativeCharmEffects.Arm(harmony,controllerType,Log);
            DamageReaction.Confirmed+=IndependentHit;
            harmony.Patch(tick,
                prefix: new HarmonyMethod(typeof(Plugin), nameof(TickPrefix)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(TickPostfix)));
            harmony.Patch(deactivate, prefix: new HarmonyMethod(typeof(Plugin), nameof(DeactivatePrefix)));
            MethodInfo knightFree = AccessTools.Method(controllerType, "KnightFree");
            if (knightFree != null)
            {
                harmony.Patch(knightFree, postfix: new HarmonyMethod(typeof(Plugin), nameof(KnightFreePostfix)));
            }
            else
            {
                Log.LogWarning("Mario can't tell the game the Knight is held while swimming.");
            }
            MethodInfo activate = AccessTools.Method(controllerType, "Activate");
            if (activate != null)
            {
                harmony.Patch(activate, prefix: new HarmonyMethod(typeof(Plugin), nameof(ActivatePrefix)), postfix: new HarmonyMethod(typeof(Plugin), nameof(ActivatePostfix)));
            }
            else
            {
                Log.LogWarning("Mario's activation step wasn't found; Mario's sound may stop for the rest of the session.");
            }
            Type nativeType = hostAssembly.GetType("MarioHollow.Native",true);
            gameObject.AddComponent<LakituDialogue>();
            try{OpeningMario.Arm(harmony,controllerType,nativeType,Log);}
            catch(Exception error){Log.LogWarning("Mario's opening replacement could not be armed: "+error.GetBaseException().Message);}
            try{BenchMario.Arm(harmony,controllerType,nativeType,Log);}
            catch(Exception error){Log.LogWarning("Mario's bench replacement could not be armed: "+error.GetBaseException().Message);}
            try{ConversationMario.Arm(harmony,controllerType,nativeType,Log);}
            catch(Exception error){Log.LogWarning("Mario's conversation replacement could not be armed: "+error.GetBaseException().Message);}
            try{SpellMario.Arm(harmony,nativeType);}
            catch(Exception error){Log.LogWarning("Mario's spell presentation could not be armed: "+error.GetBaseException().Message);}
            try{DeathMario.Arm(harmony,controllerType,nativeType);}
            catch(Exception error){Log.LogWarning("Mario's death presentation could not be armed: "+error.GetBaseException().Message);}
            try{MarioView.Arm(harmony,controllerType,nativeType);}
            catch(Exception error){Log.LogWarning("Mario's camera sampling could not be armed: "+error.GetBaseException().Message);}
            SurfaceSoul.Arm(nativeType);
            MethodInfo audioTick = nativeType == null ? null : AccessTools.Method(nativeType, "sm64_audio_tick");
            if (audioTick != null)
            {
                harmony.Patch(audioTick, prefix: new HarmonyMethod(typeof(Plugin), nameof(AudioTickPrefix)));
            }
            else
            {
                Log.LogWarning("Mario's sound step wasn't found; the safety fix for the room-change crash isn't active.");
            }
            if (!HeroIgnores.Arm(harmony, Log))
            {
                Log.LogWarning("Mario's world wasn't found, so colliders the Knight ignores stay solid for Mario.");
            }
            Swimming.Arm(nativeType);
            MovingWallPush.Arm(harmony,controllerType,nativeType,Log);
            ShadeGateCollision.Arm(harmony,nativeType,Log);
            ShadeLongJump.Arm(controllerType,Log);
            MarioAtlasCutouts.Arm(harmony,controllerType.Assembly,Log);
            MarioMetalTexture.Arm(harmony,controllerType,Log);
            try
            {
                CapSystem.Arm(harmony,controllerType,nativeType,Config,Log);
                CapControls.Arm(harmony,Config,Log);
                gameObject.AddComponent<CapHud>();
            }
            catch(Exception error)
            { Log.LogWarning("Cap setup could not finish: "+error.GetBaseException().Message); }
            KnightLook.Arm(harmony, Log);
            bool legacyTerrain = Config.Bind("Terrain", "UseLegacyTerrain", false,
                "Use the replacement host's simple per-collider terrain instead of connected outlines. Restart the game after changing this.").Value;
            TerrainConversion.Arm(harmony, Log, legacyTerrain);
            bool terrainRecovery = Config.Bind("Terrain", "RecoverInsideTerrain", true,
                "Recover to a recent verified clear position only if Mario enters joined solid terrain.").Value;
            TerrainRecovery.Arm(controllerType,nativeType,!legacyTerrain&&terrainRecovery);
            DoubleJump.Arm(nativeType);
            PoundLongJump.Arm(nativeType);
            if (!Swimming.Armed)
            {
                Log.LogWarning("Mario's water level wasn't found, so he can't swim.");
            }
            else if (Swimming.TickMethod != null)
            {
                var waterInput = new HarmonyMethod(typeof(Swimming), nameof(Swimming.WaterInputs));
                waterInput.priority = Priority.First + 300;
                harmony.Patch(Swimming.TickMethod, prefix: waterInput);
            }
            if (!DoubleJump.Armed || DoubleJump.TickMethod == null)
            {
                Log.LogWarning("Mario's double-jump step wasn't found; Monarch Wings stays with the Knight.");
            }
            else
            {
                HarmonyMethod doubleJumpInput = new HarmonyMethod(typeof(DoubleJump), nameof(DoubleJump.TickPrefix));
                doubleJumpInput.priority = Priority.First;
                harmony.Patch(DoubleJump.TickMethod, prefix: doubleJumpInput);
            }
            KeepMarioLoaded(nativeType);
            MethodInfo nativeTick = nativeType == null ? null : AccessTools.Method(nativeType, "sm64_mario_tick");
            if (nativeTick != null)
            {
                MapFallFlourish.Configure(nativeType,Log);
                harmony.Patch(nativeTick,
                    prefix:new HarmonyMethod(typeof(MapFallFlourish),nameof(MapFallFlourish.BeforeTick)),
                    postfix:new HarmonyMethod(typeof(MapFallFlourish),nameof(MapFallFlourish.AfterTick)),
                    finalizer:new HarmonyMethod(typeof(MapFallFlourish),nameof(MapFallFlourish.Finish)));
                MushroomFallGuard.Configure(nativeType,Log);
                var mushroomFall=new HarmonyMethod(typeof(MushroomFallGuard),nameof(MushroomFallGuard.BeforeTick));
                mushroomFall.priority=Priority.First+200;
                harmony.Patch(nativeTick,prefix:mushroomFall,
                    postfix:new HarmonyMethod(typeof(MushroomFallGuard),nameof(MushroomFallGuard.AfterTick)),
                    finalizer:new HarmonyMethod(typeof(MushroomFallGuard),nameof(MushroomFallGuard.Finish)));
                MethodInfo mode=AccessTools.Method(controllerType,"SetWanted",new[]{typeof(bool)});
                if(mode!=null)harmony.Patch(mode,postfix:new HarmonyMethod(typeof(MushroomFallGuard),nameof(MushroomFallGuard.Wanted)));
                NativeMovementGates.Configure(nativeType);
                ThinPlatformLedges.Configure(Config,Log);
                var upgradeGate = new HarmonyMethod(typeof(NativeMovementGates), nameof(NativeMovementGates.BeforeTick));
                upgradeGate.priority = Priority.First + 100;
                harmony.Patch(nativeTick, prefix: upgradeGate,
                    finalizer: new HarmonyMethod(typeof(NativeMovementGates), nameof(NativeMovementGates.AfterTick)));
                QuickSlash.Bind(Config);
                QuickSlash.Configure(nativeType, Log);
                harmony.Patch(nativeTick,
                    prefix: new HarmonyMethod(typeof(QuickSlash), nameof(QuickSlash.BeforeTick)) { priority = Priority.First + 50 },
                    finalizer: new HarmonyMethod(typeof(QuickSlash), nameof(QuickSlash.FinishTick)));
            }
            if (PoundLongJump.Armed && nativeTick != null)
            {
                harmony.Patch(nativeTick,
                    prefix: new HarmonyMethod(typeof(PoundLongJump), nameof(PoundLongJump.TickPrefix)),
                    postfix: new HarmonyMethod(typeof(PoundLongJump), nameof(PoundLongJump.TickPostfix)));
            }
            else Log.LogWarning("Mario's ground-pound long-jump cancel could not be armed.");
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            Type attacksType = hostAssembly.GetType("MarioHollow.MarioAttacks",true);
            MethodInfo attackTick = attacksType == null ? null : AccessTools.Method(attacksType, "Tick");
            if (attackTick != null)
            {
                BindIndependentAttackGeometry(attacksType);
                harmony.Patch(attackTick, prefix: new HarmonyMethod(typeof(Plugin), nameof(StompsBeforeAttacks)));
            }
            MethodInfo strike = attacksType == null ? null : AccessTools.Method(attacksType, "Strike");
            if (strike != null)
            {
                strikeMethod = strike;
                marioAttacksField = AccessTools.Field(controllerType, "Attacks");
                Type rowType = strike.GetParameters().Length > 3 ? strike.GetParameters()[3].ParameterType : null;
                attackRadiusField = rowType == null ? null : rowType.GetField("Radius", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                harmony.Patch(strike, prefix: new HarmonyMethod(typeof(Plugin), nameof(StrikePrefix)),
                    postfix: new HarmonyMethod(typeof(Plugin), nameof(StrikePostfix)),
                    transpiler: new HarmonyMethod(typeof(NailHitDirection), nameof(NailHitDirection.BuildHits)));
                harmony.Patch(strike,transpiler:new HarmonyMethod(typeof(NailCharmModifiers),nameof(NailCharmModifiers.BuildHits)));
                DiveSlideFollowup.Arm(harmony, attacksType, AccessTools.TypeByName("HealthManager"), Log);
            }
            else
            {
                Log.LogWarning("Mario's attack step wasn't found; move hits stay as the original calculates them.");
            }
            ResolveSmacks();
            MethodInfo findDamager = fsmType == null ? null : AccessTools.Method(fsmType, "FindFsmOnGameObject",
                new[] { typeof(GameObject), typeof(string) });
            if (findDamager != null)
                harmony.Patch(findDamager, prefix: new HarmonyMethod(typeof(NailDamagerLookup), nameof(NailDamagerLookup.Find)));
            else Log.LogWarning("Mario's inert nail metadata lookup could not be armed.");
            Type rendererType = hostAssembly.GetType("MarioHollow.MarioRenderer",true);
            MethodInfo render = rendererType == null ? null : AccessTools.Method(rendererType, "Render");
            if (render != null)
            {
                harmony.Patch(render, prefix: new HarmonyMethod(typeof(Plugin), nameof(RenderPrefix)));
            }
            MethodInfo recenter = AccessTools.Method(controllerType, "Recenter");
            if (recenter != null)
            {
                harmony.Patch(recenter, prefix: new HarmonyMethod(typeof(Plugin), nameof(RecenterPrefix)));
            }
            Type heroType = AccessTools.TypeByName("HeroController");
            if (!DropPlatformContact.Arm(harmony, AccessTools.TypeByName("DropPlatform")))
                Log.LogWarning("Mario's foot contact could not be connected to native dropping platforms.");
            try{CrumblingFloorContact.Arm();}
            catch(Exception error){Log.LogWarning("Crumbling floor contact unavailable: "+error.GetBaseException().Message);}
            MethodInfo nativeWaterState = Swimming.ArmKnightWater(controllerType, AccessTools.TypeByName("HutongGames.PlayMaker.Fsm"));
            if (nativeWaterState != null)
                harmony.Patch(nativeWaterState, prefix: new HarmonyMethod(typeof(Swimming), nameof(Swimming.BeforeKnightWaterState)));
            else Log.LogWarning("The Knight's native swimming entry could not be disabled for Mario mode.");
            MethodInfo waterPosition=Swimming.ArmWaterPosition(AccessTools.TypeByName("HutongGames.PlayMaker.Actions.SetPosition"));
            if(waterPosition!=null)
                harmony.Patch(waterPosition,prefix:new HarmonyMethod(typeof(Swimming),nameof(Swimming.BeforeWaterPosition)));
            else Log.LogWarning("Mario's water-entry surface snap could not be disabled.");
            KnightFocus.Arm(controllerType);
            MethodInfo takeControl = heroType == null ? null : AccessTools.Method(heroType, "RelinquishControl");
            if (heroType != null && takeControl != null)
            {
                foreach (string gate in new[] { "CanCast", "CanFocus", "CanQuickMap", "CanInspect", "CanTalk", "CanInteract" })
                {
                    MethodInfo check = AccessTools.Method(heroType, gate, Type.EmptyTypes);
                    if (check != null)
                        harmony.Patch(check, prefix: new HarmonyMethod(typeof(KnightFocus), nameof(KnightFocus.PrepareFocus)));
                }
                harmony.Patch(takeControl, postfix: new HarmonyMethod(typeof(KnightFocus), nameof(KnightFocus.ControlTaken)));
            }
            else Log.LogWarning("The Knight's native action handover could not be armed.");
            Type castListener = AccessTools.TypeByName("HutongGames.PlayMaker.Actions.ListenForCast");
            Type walkAction = AccessTools.TypeByName("HutongGames.PlayMaker.Actions.SetVelocity2d");
            if (KnightFocus.ArmScriptedWalk(walkAction))
                harmony.Patch(AccessTools.Method(walkAction,"DoSetVelocity"),prefix:new HarmonyMethod(typeof(KnightFocus),nameof(KnightFocus.BeforeScriptedWalk)));
            else Log.LogWarning("The native scripted interaction walk could not be armed.");
            MethodInfo castInput = castListener == null ? null : AccessTools.Method(castListener, "CheckForInput", Type.EmptyTypes);
            if (castInput != null && KnightFocus.ArmCastInput(castListener))
            {
                harmony.Patch(castInput, postfix: new HarmonyMethod(typeof(KnightFocus), nameof(KnightFocus.AfterCastInput)));
            }
            else Log.LogWarning("The Knight's held-heal input recovery could not be armed.");
            ArmHardLandings(heroType);
            MethodInfo invulnerable = heroType == null ? null : AccessTools.Method(heroType, "Invulnerable");
            MethodInfo parry = heroType == null ? null : AccessTools.Method(heroType, "NailParry", Type.EmptyTypes);
            if (parry != null) harmony.Patch(parry, prefix: new HarmonyMethod(typeof(NativeDamageTimer), nameof(NativeDamageTimer.BeforeParry)));
            if (invulnerable != null && knightFollow.InvincibilityArmed)
            {
                harmony.Patch(invulnerable, prefix: new HarmonyMethod(typeof(Plugin), nameof(InvulnerablePrefix)));
            }
            else
            {
                Log.LogWarning("The knight's invincibility wasn't found; it keeps running out while Mario is down.");
            }
            MethodInfo takeDamage = heroType == null ? null : AccessTools.Method(heroType, "TakeDamage");
            if (takeDamage != null)
            {
                harmony.Patch(takeDamage,prefix:new HarmonyMethod(typeof(Plugin),nameof(TraceIncomingProjectile)){priority=Priority.First+500});
                HarmonyMethod damageSnapshot = new HarmonyMethod(typeof(DamageReaction),nameof(DamageReaction.BeforeDamage));
                damageSnapshot.priority = Priority.First + 100;
                harmony.Patch(takeDamage, prefix: damageSnapshot,
                    finalizer: new HarmonyMethod(typeof(DamageReaction), nameof(DamageReaction.FinishDamage)));
                // Keep __state on the original snapshot prefix. The API's
                // argument adapter deliberately does not combine it with __args.
                harmony.Patch(takeDamage,prefix:new HarmonyMethod(typeof(DamageReaction),nameof(DamageReaction.CaptureDamageInputs)){priority=Priority.First+90});
                harmony.Patch(takeDamage,prefix:new HarmonyMethod(typeof(Plugin),nameof(IndependentContactGuard)));
                HarmonyMethod acidGuard = new HarmonyMethod(typeof(AcidSafety), nameof(AcidSafety.BeforeDamage));
                acidGuard.priority = Priority.First;
                harmony.Patch(takeDamage, prefix: acidGuard,
                    finalizer: new HarmonyMethod(typeof(AcidSafety), nameof(AcidSafety.AfterDamage)));
                harmony.Patch(takeDamage, prefix: new HarmonyMethod(typeof(Plugin), nameof(TakeDamagePrefix)));
            }
            else
            {
                Log.LogWarning("couldn't watch the Knight getting hurt, so spikes can't be pogoed.");
            }
            MethodInfo onKnightHit = AccessTools.Method(controllerType, "OnKnightHit");
            if (onKnightHit != null)
            {
            }
            if (DebugSettings.VerboseLogging)
            {
                WatchBreakables();
                WatchHeroState();
            }
            Log.LogInfo("Moving safety floor is ready; it follows Mario 7000 SM64 units below him.");
            MeltyTest.Start();
            if(MeltyTest.Active||TestPersistenceGuard.IndependentRun) TestPersistenceGuard.Arm(harmony);
            string actualRoute=Convert.ToString(AppDomain.CurrentDomain.GetData("HollowMario.LoadingRoute"));
            MeltyTest.Report("loader-route",actualRoute=="CommunityApi","The enabled API must select only the API build.");

            StartCoroutine(StartDraftTestCompatibility());
            if (FindChecksPath() != null)
            {
                StartCoroutine(WatchMeltyState());
                StartCoroutine(PrepareBenchForMeltyTest());
                StartCoroutine(ResetFollowTest());
            }
            ApiStartupComplete = true;

        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            PresentationTiming.SceneChanged(scene.name);
            PickupArtwork.SceneLoaded();
            HitboxOverlay.Clear();
            OpeningMario.SceneLoaded(scene.name);
            BenchMario.SceneLoaded(scene.name);
            ConversationMario.Stop();
            MarioView.Forget();
            BossHpHud.Clear();
            DiveSlideFollowup.Forget();
            QuickSlash.Forget();
            NoBonk.Forget();
            ProjectileTrace.Reset(scene.name);
            StartupHurtbox.Forget();
            KnightLook.Forget();
            CapSystem.Stop();
            CapControls.Close();
            ShadeLongJump.Cancel();
            ShadeGateCollision.Forget();
            MovingWallPush.Forget();
            KnightFocus.Forget();
            TerrainConversion.Forget();
            TerrainRecovery.Forget();
            VoidGuard.Forget();
            Swimming.Forget();
            stompTickStart = null;
            rejectedEnemyHits = 0;
            enemyContactReports.Clear();
            DoubleJump.Forget();
            PoundLongJump.Forget();
            ConveyorCarry.Forget();
            Probe.Reset();
            HeroIgnores.Forget();
            LiftCarry.Forget();
            if (Instance != null && Instance.knightFollow != null)
            {
                Instance.knightFollow.NoteSceneChange();
            }
        }

        private static long GameplayEpoch(object controller)=>Convert.ToInt64(CapAccess.Read(controller,"OwnershipEpoch")??0L);
        private static bool GameplayTickCurrent(object controller,long epoch)
            =>epoch>=0&&!FocusHeal.Frozen&&Instance?.knightFollow!=null&&CapAccess.Flag(controller,"Wanted")&&Instance.knightFollow.MarioAlive(controller)
                &&ReferenceEquals(controller,Instance.knightFollow.Controller())&&GameplayEpoch(controller)==epoch;
        private static bool TickPrefix(object __instance,out long __state)
        {
            __state=-1;
            if (Instance == null)
            {
                return true;
            }
            if (FocusHeal.Frozen)
            {
                AudioTicksAllowed = true;
                return false;
            }
            if (Instance.knightFollow != null)
            {
                AudioTicksAllowed = Instance.knightFollow.MarioAlive(__instance);
            }
            if (Instance.knightFollow != null)
            {
                VoidGuard.Tick(__instance, Instance.knightFollow);
                Instance.WatchQuakeFloors(__instance);
                Instance.knightFollow.BeforeTick(__instance);
            }
            if (Instance.floor != null)
            {
                Instance.floor.Follow(__instance, false);
            }
            ConveyorCarry.BeforeMarioTick(__instance, Instance.knightFollow);
            stompTickStart = Instance.knightFollow?.Ground(__instance);
            HitboxOverlay.BeginTick();
            stompChecked = false;
            __state=GameplayEpoch(__instance);
            return GameplayTickCurrent(__instance,__state);
        }

        private static void TickPostfix(object __instance,long __state)
        {
            if(!GameplayTickCurrent(__instance,__state))
            {
                if(__state>=0&&DebugSettings.VerboseLogging)Instance?.Log?.LogInfo("Discarded completed tick after ownership changed: started="+__state+", current="+GameplayEpoch(__instance)+", presentation="+ConversationMario.Context);
                return;
            }
            if (!FocusHeal.Frozen && !stompChecked)
                TickStomps(__instance);
            if(!GameplayTickCurrent(__instance,__state))return;
            if (!FocusHeal.Frozen) TickEnemyHits(__instance);
            if(!GameplayTickCurrent(__instance,__state))return;
            TickBody(__instance);
            if(!GameplayTickCurrent(__instance,__state))return;
            // Run once per actual 30 Hz simulation tick, including catch-up ticks in one frame.
            // TickBody's other work is intentionally guarded to one pass per rendered frame.
            if (!FocusHeal.Frozen)
            {
                ConveyorCarry.AfterMarioTick(__instance, Instance.knightFollow);
                PoundLongJump.Sync(__instance, Instance.knightFollow);
                DoubleJump.Sync(__instance, Instance.knightFollow);
                TerrainRecovery.Tick(__instance, Instance.knightFollow);
                if(GameplayTickCurrent(__instance,__state))
                    Instance.knightFollow?.RememberRoomEntry(__instance, UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle);
            }
        }

        // The same per-tick work, reachable from the water guard below: blocking the original's
        // step-aside also skips its postfix, so the tick has to be run by hand there.
        private static void TickBody(object __instance)
        {
            long epoch=GameplayEpoch(__instance);
            if(!GameplayTickCurrent(__instance,epoch))return;
            if(lastTickFrame==Time.frameCount&&lastTickEpoch==epoch)return;
            lastTickFrame=Time.frameCount;lastTickEpoch=epoch;
            lastController = __instance;
            if (Instance.floor != null)
            {
                Instance.floor.Follow(__instance, true);
            }
            if(!GameplayTickCurrent(__instance,epoch))return;
            if (Instance.knightFollow != null)
            {
                StartupHurtbox.Sync(__instance,Instance.knightFollow);
                Instance.knightFollow.KeepInteractable(__instance);
                Instance.knightFollow.GuardTick(__instance);
                Instance.knightFollow.ShareMarioSpeed(__instance);
                Probe.Tick(__instance, Instance.knightFollow);
            }
            if(!GameplayTickCurrent(__instance,epoch))return;
            TickFacing(__instance);
            TickPogos(__instance);
        }
        private static void DeactivatePrefix(object __instance)
        {
            MarioView.Forget();
            DiveSlideFollowup.Forget();
            QuickSlash.Forget();
            StartupHurtbox.Forget();
            NativeCharmEffects.Forget();
            KnightLook.Forget();
            CapSystem.Stop();
            CapControls.Close();
            ShadeLongJump.Cancel();
            ShadeGateCollision.Suspend();
            MovingWallPush.Forget();
            MapFallFlourish.Forget();
            KnightFocus.SteppedAside(__instance);
            TerrainRecovery.Forget();
            if (Instance != null && Instance.knightFollow != null && stepAsideLogs < StepAsideLogLimit)
            {
                stepAsideLogs++;
                Instance.Log.LogInfo("Mario steps aside in " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                    + ": " + Instance.knightFollow.GateReport(__instance)
                    + " swimming=" + Instance.knightFollow.Swimming(__instance)
                    + " transitionState=" + Instance.knightFollow.TransitionState(__instance)
                    + " presentation=" + ConversationMario.Context);
            }
            stompTickStart = null;
            Swimming.Forget();
            DoubleJump.Forget();
            PoundLongJump.Forget();
            ConveyorCarry.Forget();
            Probe.Reset();
            AudioTicksAllowed = false;
            RemoveFeetPad();
            FocusHeal.Remove();
            if (Instance != null && Instance.floor != null)
            {
                Instance.floor.Dispose();
            }
        }

        private void LateUpdate()
        {
            PresentationTiming.Frame();
            BossHpHud.Tick();
            object active=knightFollow?.Controller();
            TickHitProtection(Time.deltaTime);
            knightFollow?.GuardTick(active);
            BenchMario.Draw(active);
            ConversationMario.Draw(active);
            CapSystem.Tick(active,knightFollow,Time.deltaTime);
            CapControls.Tick(active,knightFollow);
            HitboxOverlay.Tick(active,knightFollow);
            ShadeLongJump.Tick(active,knightFollow,Time.deltaTime);
            NativeCharmEffects.Tick(Time.deltaTime);
            if(active!=null&&knightFollow.MarioAlive(active))NativeDamageTimer.Tick(knightFollow.Hero(active),Time.deltaTime);
            // Surface refill uses the game clock on rendered frames, so the eight deadlines
            // need not be rounded to Mario's 30 Hz simulation. No additional liquid query.
            if (!Swimming.InWater || knightFollow == null) { SurfaceSoul.Reset(); return; }
            object controller = knightFollow.Controller();
            if (!FocusHeal.Frozen && knightFollow.MarioAlive(controller))
                Swimming.RegenerateSoul(controller, knightFollow);
            else SurfaceSoul.Reset();
        }

        private void OnGUI() { BossHpHud.Draw();HitboxOverlay.Legend(); }

        private void OnDestroy()
        {
            DamageReaction.Confirmed-=IndependentHit;
            knightFollow?.CancelGuard();
            hitProtection.Cancel();
            BossHpHud.Clear();
            DiveSlideFollowup.Dispose();
            QuickSlash.Dispose();
            KnightLook.Dispose();
            CapSystem.Stop();
            CapControls.Dispose();
            MarioAppearance.Reset();
            MarioAtlasCutouts.Dispose();
            MarioMetalTexture.Dispose();
            ShadeLongJump.Dispose();
            ShadeGateCollision.Forget();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            RemoveFeetPad();
            BenchMario.Stop();
            ConversationMario.Stop();
            BenchRestAudio.Dispose();
            NativeDamageEligibility.Dispose();
            if (harmony != null)
            {
                harmony.UnpatchSelf();
            }
            NativeMovementGates.Dispose();
            MapFallFlourish.Dispose();
            OpeningMario.Stop();
            LakituDialogue.Cancel();
            MushroomFallGuard.Dispose();
            PickupArtwork.Dispose();
            HitboxOverlay.Dispose();
            CapIcons.Dispose();
            NailDamagerLookup.Clear();
            NativeCharmEffects.Dispose();
            StartupHurtbox.Forget();
            if (floor != null)
            {
                floor.Dispose();
            }
        }

    }
}
