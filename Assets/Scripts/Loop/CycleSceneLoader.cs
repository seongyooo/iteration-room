using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IterationRoom
{
    // LOADS EVERY CYCLE'S SCENE ON TOP OF THE CORE ONE, and hands the `Cycle` components back in order.
    //
    // WHY THE CYCLES ARE THEIR OWN SCENES. Not runtime cost - `Cycle.SetAwake` already answered that by
    // deactivating a cycle nobody is in. It is BUILD time: `SceneBuilder` re-bakes every reflection
    // probe in the project on every run, six faces each, and that is nearly the whole of a rebuild.
    // With a scene per cycle the bake can be confined to the cycle actually being worked on. See
    // `docs/cycle-design.md` §7b, which names the trigger for this as "a rebuild that has become
    // intolerable" rather than a dropped frame.
    //
    // ALL OF THEM, AT STARTUP, RATHER THAN ONE AT A TIME. Lazy-loading the next cycle at the boundary
    // would be the obvious economy and it breaks the one beat the boundary exists for: the player looks
    // DOWN through the hatch at the bed they are about to wake in, so both storeys have to be in the
    // world at once. Loading everything up front keeps that free and leaves `SetAwake` as the only
    // thing that decides what is running - which is exactly how it worked before the split.
    public class CycleSceneLoader : MonoBehaviour
    {
        // In play order. `SceneBuilder` writes this; the names are also what goes into the build
        // settings, and a name here with no scene behind it is a cycle that silently does not exist.
        public string[] cycleSceneNames;

        public bool Loaded { get; private set; }

        // The cycles, in the order their scenes were named. Read once by `LoopManager` after this has
        // finished - see `LoopManager.Start`, which waits on it rather than racing it.
        public Cycle[] Cycles { get; private set; } = new Cycle[0];

        public IEnumerator LoadAll()
        {
            if (Loaded) yield break;

            var found = new List<Cycle>();

            foreach (string name in cycleSceneNames ?? new string[0])
            {
                if (string.IsNullOrEmpty(name)) continue;

                Scene existing = SceneManager.GetSceneByName(name);
                // Already there when the Editor was left with the scene open additively, which is
                // exactly how someone working on a cycle will have it. Loading it twice would give the
                // player two of every room.
                if (!existing.isLoaded)
                {
                    AsyncOperation op = SceneManager.LoadSceneAsync(name, LoadSceneMode.Additive);
                    if (op == null)
                    {
                        Debug.LogError($"[CycleSceneLoader] Scene '{name}' is not in the build settings. "
                                     + "Rebuild from Iteration Room/Build Whitebox Scene.");
                        continue;
                    }
                    while (!op.isDone) yield return null;
                    existing = SceneManager.GetSceneByName(name);
                }

                Cycle cycle = FindCycleIn(existing);
                if (cycle == null)
                {
                    Debug.LogError($"[CycleSceneLoader] Scene '{name}' has no Cycle component at its root.");
                    continue;
                }
                found.Add(cycle);
            }

            Cycles = found.ToArray();
            Loaded = true;
        }

        // Roots only, and inactive included: a cycle that starts asleep is switched off at its
        // `worldRoot`, but the `Cycle` component itself sits above that and stays live.
        private static Cycle FindCycleIn(Scene scene)
        {
            if (!scene.isLoaded) return null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Cycle cycle = root.GetComponent<Cycle>();
                if (cycle != null) return cycle;
            }
            return null;
        }
    }
}
