using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IterationRoom.EditorTools
{
    // MEASURING THE BUILT SCENE INSTEAD OF REASONING ABOUT THE BUILDER.
    //
    // Room3-1's corridor flickered three times and was diagnosed wrong three times - a groove that was
    // too wide, a block overlapping a wall, a slot open onto darkness. Every one of those was a
    // hypothesis about geometry nobody had measured, and each was plausible enough to spend a build on.
    //
    // This is the tool that should have been written first. It opens the scene that actually shipped
    // and looks for pairs of renderers with a FACE IN COMMON - which is the only thing that makes two
    // surfaces fight for a pixel - rather than asking what the code was supposed to have produced.
    //
    // It also dumps a model's hierarchy, because "which mesh is the screen" is the same kind of
    // question: answerable by looking, and expensive to guess at.
    public static class CycleThreeDiagnostics
    {
        private const string CycleThreeScene = "Assets/Scenes/Cycle3.unity";

        // How close two parallel faces have to be before the depth buffer stops telling them apart at
        // the sort of distance this corridor is looked down. Generous on purpose: 2mm was enough to
        // shimmer badly at twenty metres, so anything under a centimetre is worth reporting.
        private const float CoincidenceEpsilon = 0.012f;

        [MenuItem("Iteration Room/Diagnose Cycle 3 Geometry")]
        public static void Diagnose()
        {
            Scene scene = EditorSceneManager.OpenScene(CycleThreeScene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[Diagnose] could not open {CycleThreeScene}");
                return;
            }

            var renderers = new List<Renderer>();
            foreach (GameObject root in scene.GetRootGameObjects())
                renderers.AddRange(root.GetComponentsInChildren<MeshRenderer>(true));

            // TWO GROUPS, NOT THE WHOLE CYCLE. The pairwise scan below is quadratic and the cycle is
            // thousands of renderers, so each group is one place where structures were built to fit
            // against each other and are therefore where a coincidence can be.
            Scan("the corridor", renderers, path =>
                path.Contains("NorthCorridor") || path.Contains("NorthBarrier")
                || path.Contains("Room3_1/Wall_North") || path.Contains("Room3_1/Floor")
                || path.Contains("Room3_1/Ceiling"));

            // ROOM3-2N'S OWN STRUCTURE, added when the decks and the risers were (2026-08-21). Three
            // separate layouts - wall steps, mezzanine slabs, blocks through a holed floor - all
            // authored off the same grid, which is exactly the arrangement that produces two things on
            // one plane by arithmetic rather than by mistake.
            Scan("room3-2N", renderers, path =>
                path.Contains("Room3_2N/Floor") || path.Contains("Room3_2N/Mezzanines")
                // The PANELS, not the walls. Every corner in the building is two backings
                // interpenetrating on purpose, and six of those is all this scan reported the first
                // time it was pointed at a whole room.
                || path.Contains("_Panels/"));

            // **AND AGAIN WITH EVERYTHING OPEN, which no scan had ever covered.** Both scans above
            // read the scene as AUTHORED, and everything in this cycle is authored in its resting
            // pose - the corridor full, every gate shut, every coloured panel in. That is half the
            // states the player sees. Play found the other half: with the block fully raised, the
            // corridor's ceiling - which IS the underside of that block - flickered.
            //
            // Moving the movers by their own offsets and re-scanning is the whole test, and it is the
            // same lesson as before: the tool has to be able to look at the state that is wrong.
            int moved = OpenEverything(scene);
            Debug.Log($"[Diagnose] --- re-scanning with {moved} movers in their OPEN pose ---");

            Scan("the corridor, opened", renderers, path =>
                path.Contains("NorthCorridor") || path.Contains("NorthBarrier")
                || path.Contains("Room3_1/Wall_North") || path.Contains("Room3_1/Floor")
                || path.Contains("Room3_1/Ceiling"));

            // **THE BARRIER IS IN THIS GROUP TOO, because it reaches into room3-2N's wall.** The two
            // filters used to meet exactly at the two rooms' walls and a pair that straddled them fell
            // between both scans - which is its own lesson: a group is only as good as its edges.
            Scan("room3-2N, opened", renderers, path =>
                path.Contains("Room3_2N/Floor") || path.Contains("Room3_2N/Mezzanines")
                || path.Contains("NorthBarrier") || path.Contains("_Panels/"));

            DumpModel("Assets/ArtAssets/Furniture/hanging_monitor.glb");
            DumpModel("Assets/ArtAssets/Furniture/mirror_trensum.glb");
        }

        // Every retractable thing in the cycle, put where it goes when the puzzle is solved. Nothing
        // is saved - the scene is opened read-only for this and never written back - so this is a
        // measurement, not an edit.
        private static int OpenEverything(Scene scene)
        {
            int moved = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (CrushingBarrier barrier in root.GetComponentsInChildren<CrushingBarrier>(true))
                {
                    if (barrier.slab == null) continue;
                    barrier.slab.localPosition += barrier.openLocalOffset;
                    // AND WHAT IT STOPS DRAWING, or the scan reports a pair the runtime never shows.
                    // The block's face grid is the wall while the block is a wall and is switched off
                    // the moment it moves - a scan that only moved transforms went on reporting it
                    // against the panels it lands on, which is a false positive of exactly the kind
                    // that makes a measuring tool stop being believed.
                    if (barrier.faceRenderers != null)
                        foreach (Renderer face in barrier.faceRenderers)
                            if (face != null) face.enabled = false;
                    moved++;
                }

                foreach (Door door in root.GetComponentsInChildren<Door>(true))
                {
                    if (door.doorPanel == null) continue;
                    door.doorPanel.localPosition += door.pushInOffset + door.openLocalOffset;
                    moved++;
                }
            }
            return moved;
        }

        private static void Scan(string label, List<Renderer> all, System.Func<string, bool> wanted)
        {
            var group = new List<Renderer>();
            foreach (Renderer r in all)
            {
                // A renderer that is switched off cannot fight anything for a pixel. This matters on
                // the opened pass, where `OpenEverything` turns off the things the runtime turns off.
                if (r == null || !r.enabled) continue;
                if (wanted(Path(r.transform))) group.Add(r);
            }

            Debug.Log($"[Diagnose] {group.Count} renderers in {label}");

            int found = 0;
            for (int i = 0; i < group.Count; i++)
                for (int j = i + 1; j < group.Count; j++)
                {
                    Bounds a = group[i].bounds, b = group[j].bounds;
                    // Only worth reporting when the two actually share space to fight over: boxes that
                    // merely line up edge to edge have no common visible surface.
                    if (!Overlaps(a, b)) continue;

                    string face = SharedFace(a, b);
                    if (face == null) continue;

                    found++;
                    Debug.LogWarning($"[Diagnose] COINCIDENT {face}\n"
                                   + $"    {Path(group[i].transform)}  min {V(a.min)} max {V(a.max)}\n"
                                   + $"    {Path(group[j].transform)}  min {V(b.min)} max {V(b.max)}");
                }

            Debug.Log($"[Diagnose] {found} coincident-face pairs in {label}");
        }

        // Two boxes overlap in every axis with something to spare. A shared FACE with no overlapping
        // volume is two things abutting, which is how the floors in this building are laid.
        private static bool Overlaps(Bounds a, Bounds b)
        {
            const float slack = 0.002f;
            return a.min.x < b.max.x - slack && b.min.x < a.max.x - slack
                && a.min.y < b.max.y - slack && b.min.y < a.max.y - slack
                && a.min.z < b.max.z - slack && b.min.z < a.max.z - slack;
        }

        // Which of the six bounding planes the two have in common, if any. Boxes are axis-aligned
        // here - everything in this corridor is - so this is the whole test.
        private static string SharedFace(Bounds a, Bounds b)
        {
            string[] names = { "x-min", "x-max", "y-min", "y-max", "z-min", "z-max" };
            float[] av = { a.min.x, a.max.x, a.min.y, a.max.y, a.min.z, a.max.z };
            float[] bv = { b.min.x, b.max.x, b.min.y, b.max.y, b.min.z, b.max.z };

            for (int p = 0; p < 6; p++)
                for (int q = 0; q < 6; q++)
                {
                    // Only compare planes on the same axis.
                    if (p / 2 != q / 2) continue;
                    if (Mathf.Abs(av[p] - bv[q]) < CoincidenceEpsilon)
                        return $"{names[p]} vs {names[q]} at {av[p].ToString("0.####", CultureInfo.InvariantCulture)}";
                }
            return null;
        }

        // **THE MODEL DUMP ON ITS OWN, WITHOUT OPENING A SCENE.** `Diagnose` opens Cycle3 in Single
        // mode, which closes whatever the person at the Editor was looking at - fine in batch mode,
        // rude while somebody is working. Reading a `.glb` needs no scene at all, so the half of this
        // tool that gets used most often is its own menu item.
        [MenuItem("Iteration Room/Dump Furniture Models")]
        public static void DumpModels()
        {
            DumpModel("Assets/ArtAssets/Furniture/hanging_monitor.glb");
            DumpModel("Assets/ArtAssets/Furniture/mirror_trensum.glb");
        }

        // WHAT IS ACTUALLY IN A MODEL. "Which submesh is the screen" is not guessable from the
        // filename, and guessing puts a video feed on a bezel.
        private static void DumpModel(string assetPath)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (source == null)
            {
                Debug.LogError($"[Diagnose] no model at {assetPath}");
                return;
            }

            GameObject probe = (GameObject)PrefabUtility.InstantiatePrefab(source);
            probe.transform.position = Vector3.zero;
            probe.transform.rotation = Quaternion.identity;
            probe.transform.localScale = Vector3.one;

            Debug.Log($"[Diagnose] {assetPath}");
            foreach (MeshFilter mf in probe.GetComponentsInChildren<MeshFilter>(true))
            {
                Renderer r = mf.GetComponent<Renderer>();
                Vector3 size = r != null ? r.bounds.size : Vector3.zero;
                Vector3 centre = r != null ? r.bounds.center : Vector3.zero;
                string mats = "";
                if (r != null)
                    foreach (Material m in r.sharedMaterials)
                        mats += (m != null ? m.name : "null") + " ";
                Debug.Log($"[Diagnose]   mesh {Path(mf.transform)}  size {V(size)}  centre {V(centre)}"
                        + $"  tris {(mf.sharedMesh != null ? mf.sharedMesh.triangles.Length / 3 : 0)}"
                        + $"  mats [{mats.Trim()}]");
            }

            Object.DestroyImmediate(probe);
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

        private static string V(Vector3 v) =>
            $"({v.x.ToString("0.####", CultureInfo.InvariantCulture)}, "
          + $"{v.y.ToString("0.####", CultureInfo.InvariantCulture)}, "
          + $"{v.z.ToString("0.####", CultureInfo.InvariantCulture)})";
    }
}
