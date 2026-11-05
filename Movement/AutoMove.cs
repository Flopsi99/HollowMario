using System;
using System.Reflection;
using UnityEngine;

namespace HollowMario
{
    // Shared position helper for the established moving-platform implementations.
    // It deliberately has no gameplay policy of its own.
    internal static class AutoMove
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const float K = 0.0105f;
        private static FieldInfo idField;
        private static FieldInfo stateField;
        private static FieldInfo prevStateField;
        private static FieldInfo pxField;
        private static FieldInfo pyField;
        private static FieldInfo pzField;
        private static FieldInfo prevPxField;
        private static FieldInfo prevPyField;
        private static MethodInfo setPosition;
        private static bool looked;

        internal static void Prepare(object controller)
        {
            if (!looked && controller != null)
            {
                Look(controller.GetType());
            }
        }

        internal static void Shift(object controller, float dx, float dy)
        {
            int id = Convert.ToInt32(idField.GetValue(controller));
            object state = stateField.GetValue(controller);
            float px = Convert.ToSingle(pxField.GetValue(state));
            float py = Convert.ToSingle(pyField.GetValue(state));
            float pz = Convert.ToSingle(pzField.GetValue(state));
            float stepX = dx / K;
            float stepY = dy / K;
            setPosition.Invoke(null, new object[] { id, px + stepX, py + stepY, pz });
            pxField.SetValue(state, px + stepX);
            pyField.SetValue(state, py + stepY);
            stateField.SetValue(controller, state);
            object prev = prevStateField.GetValue(controller);
            if (prev != null && prevPxField != null)
            {
                prevPxField.SetValue(prev, Convert.ToSingle(prevPxField.GetValue(prev)) + stepX);
                if (prevPyField != null)
                {
                    prevPyField.SetValue(prev, Convert.ToSingle(prevPyField.GetValue(prev)) + stepY);
                }
                prevStateField.SetValue(controller, prev);
            }
        }

        private static void Look(Type controllerType)
        {
            try
            {
                idField = controllerType.GetField("MarioId", Any);
                stateField = controllerType.GetField("State", Any);
                prevStateField = controllerType.GetField("PrevState", Any);
                Type marioState = stateField?.FieldType;
                pxField = marioState?.GetField("px", Any);
                pyField = marioState?.GetField("py", Any);
                pzField = marioState?.GetField("pz", Any);
                Type prevState = prevStateField?.FieldType;
                prevPxField = prevState?.GetField("px", Any);
                prevPyField = prevState?.GetField("py", Any);
                Type nativeType = controllerType.Assembly.GetType("MarioHollow.Native", false);
                setPosition = nativeType?.GetMethod("sm64_set_mario_position", Static);
            }
            catch (Exception exception)
            {
                if (Plugin.Instance != null)
                {
                    Plugin.Instance.Log.LogWarning("moving walkways couldn't be found: " + exception.Message);
                }
            }
            looked = true;
        }
    }
}
