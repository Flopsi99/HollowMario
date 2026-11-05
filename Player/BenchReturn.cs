using System;
using System.Collections;
using System.Reflection;

namespace HollowMario
{
    // One requested return, using the native saved respawn data and scene loader.
    // No death, save edit, checkpoint substitution or independent Update loop.
    internal sealed class BenchReturn
    {
        internal bool Running { get; private set; }

        internal void Cancel(IEnumerator request)
        { (request as IDisposable)?.Dispose(); Running = false; }

        internal IEnumerator Request(object game, Func<float> clock, Func<bool> allowed,
            Action prepare, Action<string, bool> notice)
        {
            if (Running) return null;
            var api = new NativeApi(game);
            Running = true;
            return Safe(Run(api, clock, allowed, prepare, notice), notice);
        }

        private IEnumerator Safe(IEnumerator work, Action<string, bool> notice)
        {
            try
            {
                while (true)
                {
                    bool more;
                    try { more = work.MoveNext(); }
                    catch (Exception error)
                    {
                        notice("F9 error: " + error.GetBaseException().Message, true);
                        yield break;
                    }
                    if (!more) yield break;
                    yield return work.Current;
                }
            }
            finally { (work as IDisposable)?.Dispose(); Running = false; }
        }

        private static IEnumerator Run(NativeApi api, Func<float> clock, Func<bool> allowed,
            Action prepare, Action<string, bool> notice)
        {
            float deadline = clock() + 15f;
            bool waiting = false;
            while (true)
            {
                if (!allowed()) yield break;
                if (api.State <= 1) { notice("F9 is available after loading a game.", true); yield break; }
                if (clock() >= deadline) { notice("The current transition is still busy. Try F9 again when loading finishes.", true); yield break; }
                if (api.State == 5)
                {
                    IEnumerator resume = api.Resume();
                    // Flatten the native unpause coroutine so failures are caught
                    // by Safe as well; its own realtime waits keep pause UI intact.
                    while (resume.MoveNext()) yield return resume.Current;
                    if (api.State == 5) { notice("The game could not leave pause. Resume it and try F9 again.", true); yield break; }
                }
                if (!api.Busy && (api.State == 4 || api.State == 7)) break;
                if (!waiting) { notice("Waiting for the current room change before returning to the bench.", false); waiting = true; }
                yield return null;
            }
            string destination = api.Destination;
            if (string.IsNullOrWhiteSpace(destination) || string.IsNullOrWhiteSpace(api.Marker))
            { notice("No bench or starting checkpoint is recorded yet.", true); yield break; }
            prepare();
            api.Begin(destination);
            notice("Returning to the last bench...", false);
            deadline = clock() + 60f;
            // Same-room returns reload too: confirmation must not require a
            // DIFFERENT scene name, only a completed native entry at the target.
            while (api.Busy || (api.State != 4 && api.State != 5) || api.CurrentScene != destination)
            {
                if (clock() >= deadline)
                { notice("The bench return has not finished. Loading is still owned by the game.", true); yield break; }
                yield return null;
            }
            notice("Returned to the last bench.", false);
        }

        internal sealed class NativeApi
        {
            private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            private readonly object game;
            private readonly FieldInfo data, state, loading, sceneLoad;
            private readonly PropertyInfo transition, respawning;
            private readonly MethodInfo begin, pause, currentScene;
            private readonly Type infoType;
            internal NativeApi(object game)
            {
                this.game = game ?? throw new InvalidOperationException("GameManager is not available.");
                Type type = game.GetType();
                data = Field(type, "playerData"); state = Field(type, "gameState");
                loading = Field(type, "isLoading"); sceneLoad = Field(type, "sceneLoad");
                transition = type.GetProperty("IsInSceneTransition", Any) ?? throw new MissingMemberException("IsInSceneTransition");
                respawning = type.GetProperty("RespawningHero", Any) ?? throw new MissingMemberException("RespawningHero");
                infoType = type.GetNestedType("SceneLoadInfo", BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new MissingMemberException("SceneLoadInfo");
                begin = type.GetMethod("BeginSceneTransition", Any, null, new[] { infoType }, null)
                    ?? throw new MissingMethodException("BeginSceneTransition");
                pause = type.GetMethod("PauseGameToggle", Any, null, Type.EmptyTypes, null)
                    ?? throw new MissingMethodException("PauseGameToggle");
                currentScene = type.GetMethod("GetSceneNameString", Any, null, Type.EmptyTypes, null)
                    ?? throw new MissingMethodException("GetSceneNameString");
            }
            private static FieldInfo Field(Type type, string name)
                => type.GetField(name, Any) ?? throw new MissingFieldException(type.Name, name);
            internal int State => Convert.ToInt32(state.GetValue(game));
            internal bool Busy => (bool)loading.GetValue(game) || sceneLoad.GetValue(game) != null
                || (bool)transition.GetValue(game) || State == 2 || State == 3 || State == 6 || State == 8;
            private string Saved(string name)
            {
                object saved = data.GetValue(game);
                return saved == null ? null : Field(saved.GetType(), name).GetValue(saved) as string;
            }
            internal string Destination => Saved("respawnScene");
            internal string Marker => Saved("respawnMarkerName");
            internal string CurrentScene => currentScene.Invoke(game, null) as string;
            internal IEnumerator Resume() => pause.Invoke(game, null) as IEnumerator
                ?? throw new InvalidOperationException("The native unpause routine is unavailable.");

            internal object BuildInfo(string destination)
            {
                object info = Activator.CreateInstance(infoType, true);
                Field(infoType, "SceneName").SetValue(info, destination);
                Field(infoType, "EntryGateName").SetValue(info, "");
                // ReadyForRespawn is designed for AFTER the death fade. F9 is
                // a live return: let the normal scene loader perform its fade.
                Field(infoType, "PreventCameraFadeOut").SetValue(info, false);
                Field(infoType, "WaitForSceneTransitionCameraFade").SetValue(info, true);
                Field(infoType, "AlwaysUnloadUnusedAssets").SetValue(info, true);
                Field(infoType, "IsFirstLevelForPlayer").SetValue(info, false);
                FieldInfo visual = Field(infoType, "Visualization");
                visual.SetValue(info, Enum.ToObject(visual.FieldType, 0)); // Native ReadyForRespawn(false): Default.
                return info;
            }
            internal void Begin(string destination)
            {
                if (Busy) throw new InvalidOperationException("Another room transition has already started.");
                object info = BuildInfo(destination);
                bool previous = (bool)respawning.GetValue(game, null);
                respawning.SetValue(game, true, null);
                try
                {
                    begin.Invoke(game, new[] { info });
                    if (!Busy) throw new InvalidOperationException("The game did not accept the bench return.");
                }
                catch
                {
                    // A rejection must not leave the next ordinary door marked
                    // as a respawn. Never roll back a load already in progress.
                    if (!Busy) respawning.SetValue(game, previous, null);
                    throw;
                }
            }
        }
    }
}
