using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IterationRoom.EditorTools
{
    // FINDS EVERY SERIALIZED REFERENCE THAT POINTS INTO ANOTHER SCENE, and names it.
    //
    // WHY THIS EXISTS. Splitting the cycles into scenes of their own (`docs/cycle-design.md` §7b) has
    // exactly one dangerous failure mode, and it is a silent one: **Unity does not carry a serialized
    // reference across a scene boundary.** It drops it. The field is null at runtime, there is no
    // compile error and no warning, and the symptom surfaces as "the console will not take the object"
    // or "the door made no sound" - found by playing, one at a time, in whatever order they happen to
    // be met. On a project whose rebuild takes minutes, that loop is unaffordable.
    //
    // So the reference list is not something to discover by playing. It is something to ENUMERATE, and
    // this enumerates it: every component in the open scenes, every object-typed serialized field on
    // it, reported when the thing it points at lives in a different scene. `SceneBuilder` runs it after
    // the split and fails loudly if anything is left; `CycleBinding` is where each one gets rebound at
    // runtime.
    //
    // ASSETS ARE NOT REFERENCES ACROSS SCENES. A material, a mesh or a prefab belongs to no scene at
    // all, and those are skipped - only pointers at objects that live IN a scene can be broken by the
    // split.
    public static class CrossSceneReferenceCheck
    {
        [MenuItem("Iteration Room/Check Cross-Scene References")]
        public static void CheckOpenScenes()
        {
            int found = Report(out string detail);
            if (found == 0)
            {
                Debug.Log("[CrossSceneCheck] No cross-scene references in the open scenes.");
                return;
            }
            Debug.LogError($"[CrossSceneCheck] {found} cross-scene reference(s):\n{detail}");
        }

        // WRITTEN TO A FILE, NOT ONLY TO THE CONSOLE. The list runs to dozens of entries on a first
        // split, and Unity's console shows one line of a multi-line message until it is clicked - so
        // the one artifact that says what work is left was the one thing that could not be read.
        // Outside `Assets/`, so it is not an asset and does not trigger an import.
        public const string ReportPath = "cross-scene-report.txt";

        public static void WriteReport()
        {
            int found = Report(out string detail);
            string header = found == 0
                ? "No cross-scene references."
                : $"{found} cross-scene reference(s):";
            System.IO.File.WriteAllText(ReportPath, header + "\n" + detail);
            Debug.Log($"[CrossSceneCheck] {header} Full list written to {ReportPath}");
        }

        // Returns how many were found, and fills `detail` with one line each. Kept separate from the
        // menu item so `SceneBuilder` can call it as a build-time assertion.
        public static int Report(out string detail)
        {
            var lines = new List<string>();

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    // Inactive included: every cycle but the first starts asleep, and a reference is
                    // no less broken for being in a subtree that is switched off.
                    foreach (Component component in root.GetComponentsInChildren<Component>(true))
                    {
                        if (component == null) continue;
                        Inspect(component, scene, lines);
                    }
                }
            }

            var sb = new StringBuilder();
            foreach (string line in lines) sb.AppendLine("  " + line);
            detail = sb.ToString();
            return lines.Count;
        }

        // REFERENCES THAT ARE SUPPOSED TO CROSS, because something re-establishes them at runtime.
        //
        // Without this the report is 97 lines, of which about ninety are working as designed - and a
        // tool that cries wolf ninety times is a tool that gets ignored on the one line that matters.
        // Each entry here is a claim that some code writes this field after load; if that code is ever
        // removed, the entry has to go with it or this stops being a check at all.
        //
        // Keyed by "TypeName.fieldPath". Array elements are matched on the field, not the index.
        private static readonly HashSet<string> ReboundAtRuntime = new HashSet<string>
        {
            // Rebuilt from the live cycles - CycleBinding.RebindHints.
            "ControlHintDisplay.interactTargets",
            // The cycle's own signal array, reassigned on entering it - LoopManager, in RunLoop.
            "PlayerRecorder.interactables",
            // Ditto, and for the same reason: these are per-cycle and the loop swaps them.
            "WakeUpSequence.wallPanels",
            // Repointed at the cycle being dropped into - LoopManager.CrossToNextCycle.
            "SleepingGas.emitters",
            // All of CycleBinding's own work, listed so the thing that fixes them does not report them.
            "FinalSlot.hand",
            "SymbolSlot.hand",
            "FinalRoomSequence.cameraShaker",
            "FinalRoomSequence.narration",
            "FinalRoomSequence.wayOut",
            "PanelMessage.narration",
            "PanelMessage.retireOnEndCycle",
            "PanelMessage.retireOnPop",
            "ChessPlacer.board",
            "BedlamPlacer.cube",
            "LadderPlacer.mount",
            // The other direction: the mount moves the player it catches.
            "LadderMount.player",
            // The other direction: cycle 3's break reaches out for the shaker and the PA.
            "FacilityFailure.cameraShaker",
            "FacilityFailure.narration",
            "CycleExit.player",
            // The binder's own inputs, which point at whatever it is about to fix.
            "CycleBinding.coreHintTargets",
            "CycleBinding.wayOuts",
        };

        private static void Inspect(Component component, Scene owning, List<string> lines)
        {
            // Transforms carry the hierarchy itself, which cannot cross a scene - a child is in its
            // parent's scene by definition. Walking them would only produce noise.
            if (component is Transform) return;

            using (var so = new SerializedObject(component))
            {
                SerializedProperty prop = so.GetIterator();
                // `false` on the first call, so it does not descend into m_Script and friends before
                // the loop has started; Next(true) inside walks children.
                bool enterChildren = true;
                while (prop.NextVisible(enterChildren))
                {
                    enterChildren = true;
                    if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;

                    Object target = prop.objectReferenceValue;
                    if (target == null) continue;

                    Scene targetScene = SceneOf(target);
                    // An asset - material, mesh, prefab, clip - belongs to no scene and cannot break.
                    if (!targetScene.IsValid()) continue;
                    if (targetScene == owning) continue;
                    if (ReboundAtRuntime.Contains(Key(component, prop))) continue;

                    lines.Add($"{Path(component.transform)} :: {component.GetType().Name}.{prop.propertyPath}"
                            + $" -> '{target.name}' in scene '{targetScene.name}' (this object is in '{owning.name}')");
                }
            }
        }

        // "TypeName.fieldPath", with any array indexing stripped - an allowlist entry is about the
        // field, not about which slot of it happened to point across.
        private static string Key(Component component, SerializedProperty prop)
        {
            string path = prop.propertyPath;
            int array = path.IndexOf(".Array.data[");
            if (array >= 0) path = path.Substring(0, array);
            return component.GetType().Name + "." + path;
        }

        private static Scene SceneOf(Object target)
        {
            if (target is GameObject go) return go.scene;
            if (target is Component c) return c.gameObject.scene;
            return default;
        }

        private static string Path(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
    }
}
