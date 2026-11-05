using System.Reflection;

namespace HollowMario
{
    internal static class PogoLaunch
    {
        internal static void Apply(MethodInfo setAction, MethodInfo setVelocity, int id, uint action,
            float x, float up, float z)
        {
            // Setting the SM64 action initializes its own launch velocity. Apply the intended
            // pogo velocity afterward, otherwise the action silently replaces the bounce speed.
            setAction.Invoke(null, new object[] { id, action });
            setVelocity.Invoke(null, new object[] { id, x, up, z });
        }
    }
}
