using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HollowMario
{
    // While Mario crouches and holds the Dream Nail button, he can't move: the Knight's soul drains,
    // and when the metre is spent one mask of health comes back, exactly as if the Knight had focused.
    [DefaultExecutionOrder(1000)]
    internal sealed class FocusHeal : MonoBehaviour
    {
        internal static bool Frozen;
        internal static FocusHeal Current;

        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const float FocusSeconds = 0.9f;
        private const int DefaultCost = 33;

        private object hero;
        private object data;
        private object game;
        private object actions;
        private FieldInfo focusing;
        private FieldInfo grounded;
        private FieldInfo health;
        private FieldInfo maxHealth;
        private FieldInfo soul;
        private FieldInfo cost;
        private FieldInfo paused;
        private MethodInfo takeSoul;
        private MethodInfo addHealth;
        private MethodInfo canFocus;
        private MethodInfo orbDrain;
        private PropertyInfo pressed;
        private FieldInfo cast;
        private FieldInfo dash;
        private FieldInfo down;
        private FieldInfo dream;

        private readonly List<ParticleSystem> glow = new List<ParticleSystem>();
        private bool active;
        private float timer;
        private int drained;
        private int price = DefaultCost;
        private int healthAtStart;
        private bool toldEffects;

        internal static FocusHeal Attach(object controller)
        {
            if (Plugin.Instance == null || Plugin.Instance.knightFollow == null || controller == null)
            {
                return null;
            }
            Transform knight = Plugin.Instance.knightFollow.HeroTransform(controller);
            if (knight == null)
            {
                return null;
            }
            FocusHeal made = knight.GetComponent<FocusHeal>();
            if (made == null)
            {
                made = knight.gameObject.AddComponent<FocusHeal>();
            }
            Current = made;
            made.Setup(controller);
            return made;
        }

        internal static void Remove()
        {
            Frozen = false;
            if (Current != null)
            {
                Destroy(Current);
                Current = null;
            }
        }

        private void Setup(object controller)
        {
            try
            {
                Type heroType = controller.GetType().Assembly.GetType("HeroController", false);
                if (heroType == null || !(GetComponent(heroType) is Component component))
                {
                    return;
                }
                hero = component;
                data = heroType.GetField("playerData", Any)?.GetValue(hero);
                if (data == null)
                {
                    data = heroType.Assembly.GetType("PlayerData", false)?.GetField("instance", Static)?.GetValue(null);
                }
                Type stateType = heroType.GetField("cState", Any)?.FieldType;
                focusing = stateType?.GetField("focusing", Any);
                grounded = stateType?.GetField("onGround", Any);
                Type dataType = data?.GetType();
                health = dataType?.GetField("health", Any);
                maxHealth = dataType?.GetField("maxHealth", Any);
                soul = dataType?.GetField("MPCharge", Any);
                cost = dataType?.GetField("focusMP_amount", Any);
                takeSoul = heroType.GetMethod("TakeMP", Any, null, new[] { typeof(int) }, null);
                addHealth = heroType.GetMethod("AddHealth", Any, null, new[] { typeof(int) }, null);
                canFocus = heroType.GetMethod("CanFocus", Any, null, Type.EmptyTypes, null);
                Type cameraType = controller.GetType().Assembly.GetType("GameCameras", false);
                game = cameraType?.GetField("instance", Static)?.GetValue(null) ?? cameraType?.GetProperty("instance", Static)?.GetValue(null, null);
                paused = cameraType?.Assembly.GetType("GameManager", false)?.GetField("isPaused", Any);
                object orb = cameraType?.GetField("soulOrb_fsm", Any)?.GetValue(game);
                orbDrain = orb?.GetType().GetMethod("SendEvent", Any, null, new[] { typeof(string) }, null);
                Type inputType = controller.GetType().Assembly.GetType("InputHandler", false);
                actions = inputType?.GetField("Instance", Static)?.GetValue(null) ?? inputType?.GetProperty("Instance", Static)?.GetValue(null, null);
                actions = actions?.GetType().GetField("inputActions", Any)?.GetValue(actions);
                if (actions != null)
                {
                    cast = actions.GetType().GetField("cast", Any);
                    dash = actions.GetType().GetField("dash", Any);
                    down = actions.GetType().GetField("down", Any);
                    dream = actions.GetType().GetField("dreamNail", Any);
                    pressed = cast?.FieldType.GetProperty("IsPressed", Any);
                }
                FindGlow();
            }
            catch (Exception exception)
            {
                Plugin.Instance.Log.LogWarning("Mario's heal couldn't get ready: " + exception.Message);
            }
        }

        private bool Ready => hero != null && data != null && pressed != null && takeSoul != null && addHealth != null && health != null && soul != null;

        private bool Pressed(FieldInfo action)
        {
            if (action == null || actions == null || pressed == null)
            {
                return false;
            }
            object control = action.GetValue(actions);
            return control != null && pressed.GetValue(control, null) is bool on && on;
        }

        private int Number(FieldInfo field)
        {
            return field == null || data == null ? 0 : Convert.ToInt32(field.GetValue(data));
        }

        private bool Flag(FieldInfo field)
        {
            return field != null && hero != null && field.GetValue(hero) is bool on && on;
        }

        private void Update()
        {
            if (!Ready)
            {
                Frozen = false;
                return;
            }
            bool crouching = Pressed(cast) || Pressed(dash) || Pressed(down);
            bool holding = Pressed(dream);
            bool stopped = game != null && paused != null && paused.GetValue(game) is bool away && away;
            if (!active)
            {
                if (!crouching || !holding || stopped || !Flag(grounded))
                {
                    return;
                }
                if (Number(health) >= Number(maxHealth) || Number(soul) < Price() || !(canFocus?.Invoke(hero, null) is bool yes && yes))
                {
                    return;
                }
                Begin();
                return;
            }
            if (!crouching || !holding || stopped || !Flag(grounded) || Number(health) >= Number(maxHealth) || Number(soul) <= 0 || Number(health) < healthAtStart)
            {
                End();
                return;
            }
            timer += Time.deltaTime;
            float each = FocusSeconds / Price();
            int guard = 0;
            while (timer >= each && guard++ < 8)
            {
                timer -= each;
                takeSoul.Invoke(hero, new object[] { 1 });
                drained++;
                if (orbDrain != null && game != null)
                {
                    object orb = game.GetType().GetField("soulOrb_fsm", Any)?.GetValue(game);
                    orbDrain.Invoke(orb, new object[] { "MP DRAIN" });
                }
                if (drained >= Price())
                {
                    drained = 0;
                    timer = 0f;
                    addHealth.Invoke(hero, new object[] { 1 });
                    if (Number(health) >= Number(maxHealth))
                    {
                        End();
                        return;
                    }
                }
            }
        }

        private int Price()
        {
            int value = Number(cost);
            return value > 0 ? value : DefaultCost;
        }

        private void Begin()
        {
            active = true;
            Frozen = true;
            timer = 0f;
            drained = 0;
            price = Price();
            healthAtStart = Number(health);
            if (focusing != null)
            {
                focusing.SetValue(hero, true);
            }
            foreach (ParticleSystem part in glow)
            {
                if (part != null)
                {
                    part.Play(true);
                }
            }
            if (!toldEffects && Plugin.Instance != null)
            {
                toldEffects = true;
                Plugin.Instance.Log.LogInfo("Mario kneels to heal: " + glow.Count + " focus glow(s) found.");
            }
        }

        private void End()
        {
            if (!active)
            {
                Frozen = false;
                return;
            }
            active = false;
            Frozen = false;
            if (focusing != null)
            {
                focusing.SetValue(hero, false);
            }
            foreach (ParticleSystem part in glow)
            {
                if (part != null)
                {
                    part.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }

        private void LateUpdate()
        {
            if (!active)
            {
                return;
            }
            foreach (ParticleSystem part in glow)
            {
                if (part == null)
                {
                    continue;
                }
                Renderer show = part.GetComponent<Renderer>();
                if (show != null)
                {
                    show.enabled = true;
                    show.forceRenderingOff = false;
                }
            }
        }

        private void FindGlow()
        {
            glow.Clear();
            foreach (ParticleSystem part in GetComponentsInChildren<ParticleSystem>(true))
            {
                if (part == null)
                {
                    continue;
                }
                string name = part.gameObject.name;
                if (name.IndexOf("focus", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("heal", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("glow", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    glow.Add(part);
                }
            }
        }

        private void OnDisable()
        {
            End();
        }

        private void OnDestroy()
        {
            End();
        }
    }
}
