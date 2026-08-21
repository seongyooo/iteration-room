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

            // Only the corridor and what it touches. The whole cycle is thousands of renderers and the
            // pairwise scan below is quadratic.
            var corridor = new List<Renderer>();
            foreach (Renderer r in renderers)
            {
                string path = Path(r.transform);
                if (path.Contains("NorthCorridor") || path.Contains("NorthBarrier")
                    || path.Contains("Room3_1/Wall_North") || path.Contains("Room3_1/Floor")
                    || path.Contains("Room3_1/Ceiling"))
                    corridor.Add(r);
            }

            Debug.Log($"[Diagnose] {corridor.Count} renderers in and around the corridor");
            foreach (Renderer r in corridor)
                Debug.Log($"[Diagnose]   {Path(r.transform)}  min {V(r.bounds.min)}  max {V(r.bounds.max)}");

            int found = 0;
            for (int i = 0; i < corridor.Count; i++)
                for (int j = i + 1; j < corridor.Count; j++)
                {
                    Bounds a = corridor[i].bounds, b = corridor[j].bounds;
                    // Only worth reporting when the two actually share space to fight over: boxes that
                    // merely line up edge to edge have no common visible surface.
                    if (!Overlaps(a, b)) continue;

                    string face = SharedFace(a, b);
                    if (face == null) continue;

                    found++;
                    Debug.LogWarning($"[Diagnose] COINCIDENT {face}\n"
                                   + $"    {Path(corridor[i].transform)}  min {V(a.min)} max {V(a.max)}\n"
                                   + $"    {Path(corridor[j].transform)}  min {V(b.min)} max {V(b.max)}");
                }

            Debug.Log($"[Diagnose] {found} coincident-face pairs in the corridor");
            DumpModel("Assets/ArtAssets/Furniture/hanging_monitor.glb");
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
