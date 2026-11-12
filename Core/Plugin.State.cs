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
    public sealed partial class Plugin
    {

        internal static Plugin Instance;

        internal static volatile bool AudioTicksAllowed = true;

        internal ManualLogSource Log;

        private const float BodyCentreUp = 0.6f;

        private const int StrikeLogLimit = 20;

        private const int StepAsideLogLimit = 40;

        private const float WorldScale = 0.0105f;

        private const int EnemiesLayer = 11;

        private const float LandingGap = 0.25f;

        private const float StompGap = 0.7f;

        private const float RepeatStompGap = 0.2f;

        private const float StompBounce = 60f;

        private const float StompGuard = 0.3f;

        private const float PogoBounce = 58f;

        private const uint ActDoubleJump = 0x03000881u;

        private const uint ActGroundPound = 0x008008A9u;

        private const uint ActGroundPoundLand = 0x0080023Cu;

        private static readonly Dictionary<int, float> lastLanding = new Dictionary<int, float>();

        private static int stompLogs;

        private static int pogoLogs;

        private static int pogoSightings;

        private static int pogoProbes;

        private static int lastPoundAt;

        private static int lastPogoAt;

        private static float pogoedAt = -10f;

        private static object lastController;

        private static MethodInfo fsmContains;

        private static MethodInfo fsmLocate;

        private static MethodInfo fsmGetInt;

        private static int spikeLogs;

        private static Type damageHeroType;

        private static FieldInfo hazardTypeField;

        private static Type nonBouncerType;

        private static Type bigBouncerType;

        private static int facingLogs;

        private static Type hitInstanceType;

        private static Type healthManagerType;


        private static PropertyInfo playerDataInstance;

        private static FieldInfo hpField;
        private static FieldInfo enemyDeadField;
        private static readonly HashSet<long> enemyContactReports = new HashSet<long>();

        private static FieldInfo nailDamageField;

        private static FieldInfo hitSourceField;

        private static FieldInfo hitAttackField;

        private static FieldInfo hitDamageField;

        private static FieldInfo hitDirectionField;

        private static FieldInfo hitMultiplierField;

        private static FieldInfo hitMagnitudeField;

        private static FieldInfo hitIgnoreField;

        private Harmony harmony;

        private MovingSafetyFloor floor;

        internal KnightResetFollow knightFollow;

        private Type controllerType;

        private static FieldInfo attackRadiusField;

        private static bool snapNextRender;

        private static int strikeLogs;
        private static int rejectedEnemyHits;

        private static int stepAsideLogs;

        private static int lastTickFrame = -1;
        private static long lastTickEpoch = -1;
        private static Vector3? stompTickStart;
        private static bool stompChecked;

        private static readonly Collider2D[] nearby = new Collider2D[64];

        private static readonly HashSet<int> struckThisSwing = new HashSet<int>();
        private static readonly HashSet<int> nativeStruckThisSwing = new HashSet<int>();
        private static readonly HashSet<object> taggedStruckThisSwing = new HashSet<object>();
        private static int nailTriggerLogs;

        private static FieldInfo sheetAttacksField;

        private static Type responderType;

        private static Type fsmType;

        private static MethodInfo sendFsmEvent;

        private static BoxCollider2D feetPad;
        private static BoxCollider2D feetPadBody,retiredFeetPadBody;
        private static int feetPadId,retiredFeetPadId;
        private static BoxCollider2D retiredFeetPad;

        private static FieldInfo rowActionField;

        private static FieldInfo rowRadiusField;

        private static FieldInfo rowForwardField;

        private static FieldInfo rowUpField;

        private static FieldInfo rowDamageField;

        private static FieldInfo rowIdField;

        private static uint lastSwingAction = uint.MaxValue;

        private static int lastSwingAnim = int.MinValue;

        private static readonly HashSet<int> hitThisSwing = new HashSet<int>();

        private static uint swingAction = uint.MaxValue;

        private static int swingAnim = int.MinValue;

        private static int swingFrame = -1;

        private static float swingStart = float.MinValue;

        private static FieldInfo marioAttacksField;

        private static MethodInfo strikeMethod;

        private static int smackLogs;
        private static int nativeNailFailures;
        private static int swingLogs;
        private static readonly Dictionary<string, int> swingSeen = new Dictionary<string, int>();

        private static GameObject strikeSource;
        private static GameObject upwardStrikeSource;

        private static bool strikeSourceTried;


        private static MethodInfo nativeMarioPosition;

        private static MethodInfo nativeMarioVelocity;

        private static MethodInfo nativeMarioAction;

        private static MethodInfo nativeMarioHealth;


        private static readonly Dictionary<string, int> heroStateSeen = new Dictionary<string, int>();
        private static int heroStateLines;
        private static int proxyEnterLines;

        private static int shakeLines;

        private static readonly Dictionary<string, string> seenFloorStates = new Dictionary<string, string>();
        private static float nextFloorScan;
        private static PropertyInfo fsmNameProp;
        private static PropertyInfo fsmStateProp;
        private static FieldInfo heroInstanceField;
        private static bool lastSpellQuake;
        private static bool lastDownAttack;
        private static bool lastCastPlaying;
        private static bool flagsKnown;
        private static bool knightSpellPartsShown;

        private static readonly HashSet<string> dumpedQuake = new HashSet<string>();

        [ThreadStatic] private static GameObject projectileImpactRoot;
        [ThreadStatic] private static object projectileImpactHero;
        private static int projectileImpactLogs;

        private static MethodInfo hardLandingReset;
        private static int hardLandingFailures;

        private static FieldInfo defaultMarioWanted;
        private static FieldInfo quickSlashBaseLastAction;
        private BenchReturn benchReturn;

    }
}
