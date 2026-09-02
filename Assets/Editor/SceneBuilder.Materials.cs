using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IterationRoom.EditorTools
{
    // MATERIALS, AND THE PRIMITIVE HELPERS THAT WEAR THEM. URP only: a `Standard` material renders
    // magenta, transparency needs its keyword as well as its blend modes, and a wear map is inert
    // without `_METALLICSPECGLOSSMAP` (CLAUDE.md 3).
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {

        // THE CEILING, AND IT IS NOT THE FLOOR. They shared `floorMat` until 2026-08-20 - both are
        // white slabs, which is fair - and they are lit from opposite ends: the floor is directly
        // under four spots and the ceiling faces AWAY from every one of them, lit by ambient alone.
        // The albedo that stops one clipping is the albedo that kills the other.
        //
        // So: 1.0, the wall's own brightness, because a ceiling needs everything it can get.
        //
        // SMOOTHNESS 0.3, UP FROM 0 - settled 2026-08-20. The argument for
        // 0 was that there is nothing above a ceiling to reflect, so a specular response there is a
        // highlight of nothing. What that missed is the same thing the floor was fixed for: with no
        // direct light on it at all (the spots point down) and ambient ground as its only term, a
        // ceiling at smoothness 0 is a CONSTANT field across the whole slab - the grain cannot show,
        // because trilight's ground and equator are 0.075 apart and perturbing the normal moves it by
        // under a percent. 0.3 lets the probe put a faint gradient on it, which is the one thing that
        // can give it shape without touching how bright it is.
        //
        // Cached because it is asked for once per room and `MakeNoiseNormalMap` regenerates its
        // texture on every call rather than loading it.
        private static Material ceilingMaterial;

        private static Material CeilingMaterial()
        {
            if (ceilingMaterial != null) return ceilingMaterial;

            ceilingMaterial = MakeColorMaterial("CeilingWhite", PanelLitColor);
            // The floor's grain, at the floor's scale. It is the same slab built the same way; at the
            // 0.3 above it shows in the diffuse as the faint tooth a painted ceiling has, and now also
            // breaks up the probe's reflection so that reflection is not a second flat field.
            ApplySurfaceDetail(ceilingMaterial, MakeNoiseNormalMap("SurfaceGrain", 512, 2.5f),
                               1.8f, new Vector2(26f, 30f), 0.3f);
            return ceilingMaterial;
        }

        private static Material MakeEmissiveMaterial(string name, Color color, float emission)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(OpaqueShader());
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = OpaqueShader();
            mat.color = color;
            SetSmoothness(mat, 0.1f);

            // The keyword matters as much as the colour: set _EmissionColor alone and URP leaves
            // the emission pass compiled out, so the panel renders as plain white.
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * emission);

            // **REALTIME, WHICH IN THIS PROJECT MEANS "CONTRIBUTES NOTHING TO THE BAKE" - AND THAT
            // IS CORRECT, BECAUSE THE FIXTURE IS ALREADY IN THE BAKE AS A LIGHT.**
            //
            // Every ceiling fixture is TWO objects: this emissive panel, which is what you see, and a
            // spot light at `CeilingLightIntensity` inside it, which is what lights the room. They
            // are one physical thing modelled twice, so exactly one of them may contribute to GI.
            //
            // Switching this to `BakedEmissive` (2026-08-26) made both contribute, and the result was
            // a room lit about twice over: a 1.4m panel radiating 3.5 - three and a half times white -
            // stacked on top of a spot at 10.5. **It did not show up immediately**, because at the
            // time the probe volume was being deleted by every rebuild, so no baked data reached the
            // screen at all. The moment `EnsureProbeVolume` fixed that, the double count arrived with
            // it: play reported the bed blown to 255 across its whole frame and the calibration room
            // clipped to pure white, while APV switched off looked correct.
            //
            // **The panel is a light FIXTURE, not a light.** If these are ever to be real area
            // sources - which is the right fix for the hard-edged shadows `docs/rendering-notes.md`
            // complains about - the spot lights have to come out at the same time, and `ShadowBudget`
            // needs a different answer, since a baked emissive surface has no runtime shadow to
            // switch.
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // A POLISHED, COLOURED METAL with a glow still in it. The three escape objects wear this and
        // nothing else does.
        //
        // They were flat emissive at 2.4 with smoothness 0.1, which is a matte object with the light
        // turned up inside it: no highlight anywhere on the surface, the middle blown out to near
        // white, and the colour only readable at the silhouette. Three of those on a console read as
        // three coloured blocks rather than as the things the whole run was spent collecting.
        //
        // EVERY NUMBER HERE WAS RENDERED AND LOOKED AT, not reasoned to, and the first three
        // attempts were worse than what they replaced. What each one is and what it cost to find:
        //
        // - METALLIC 0.9. The project rule says a pure metal renders BLACK here, and that rule is a
        //   symptom: a metal is entirely reflection, and every reflection probe in this scene had a
        //   NULL baked texture, so a metal had nothing to be. Baking the probes (see
        //   BakeReflectionProbes) is what makes this value available at all. At 0.8 with an EMPTY
        //   probe the object came out darker and deader than the flat colour it replaced - worth
        //   knowing, because that is what this looks like if the bake ever silently breaks again.
        // - SMOOTHNESS 0.97, near-mirror, and this is the one that does the work. The obvious
        //   setting is the 0.85-0.88 the walls use, and it is wrong for a small object: at that
        //   roughness a featureless white room blurs into a flat wash and the thing reads as
        //   plastic. Near-mirror instead resolves the CEILING FIXTURES as two distinct bright
        //   shapes on the top face, which is the only structure this white box has to offer - and
        //   structure is what the eye reads as metal.
        // - REFLECTANCE IS THE ACCENT LIFTED 30% TOWARD WHITE, not the accent itself. Base colour
        //   means something different on a metal: it is what the surface reflects, not what it is
        //   painted. A real coloured metal's reflectance is high (gold is ~1.0/0.77/0.34), so
        //   feeding in a display colour like 0.16/0.40/0.95 gives a dark, muddy mirror. The lift
        //   keeps the hue - these three are told apart by colour, in a hurry, across a console.
        // - EMISSION 0.40, down from 2.4. The ORIGINAL reason for emission stands: each of these
        //   arrives in the same second as the biggest lighting change its room has. But 2.4 blew the
        //   middle out to near-white and left the colour readable only at the silhouette, which is
        //   what made these look like coloured blocks. At 0.40 the metal is what is seen and the
        //   glow is a floor under it - this object cannot go black however the probes behave.
        //
        // Emission takes the ACCENT, not the lifted reflectance: the glow should be the object's own
        // colour at full strength, where the mirror should not.
        private static Material MakePolishedMetalMaterial(string name, Color accent, float emission)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(OpaqueShader());
                AssetDatabase.CreateAsset(mat, path);
            }

            Color reflectance = Color.Lerp(accent, Color.white, 0.30f);

            mat.shader = OpaqueShader();
            mat.color = reflectance;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", reflectance);
            SetSmoothness(mat, 0.97f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.9f);

            // The keyword matters as much as the colour: set _EmissionColor alone and URP leaves the
            // emission pass compiled out - the same trap MakeEmissiveMaterial documents.
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", accent * emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // A lit material you can see through. The URP transparent set-up needs all of _Surface,
        // _SrcBlend, _DstBlend and _ZWrite AND the keyword -
        // the blend modes and the _SURFACE_TYPE_TRANSPARENT keyword are BOTH required, and setting
        // the alpha alone leaves the shader opaque - but on Lit rather than Unlit, because unlike a
        // ghost these are real objects in a lit room and have to take the ceiling lights.
        private static Material MakeTranslucentMaterial(string name, Color color, float smoothness)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            Shader lit = OpaqueShader();

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(lit);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = lit;
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            SetSmoothness(mat, smoothness);

            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");

            // PREMULTIPLIED alpha, and all three of the keyword, the _Blend enum and the blend
            // factors have to say so together. They did not: the keyword had been on the asset
            // since before the first ship while this forced SrcAlpha, so a scene build left alpha
            // multiplied TWICE and URP's material validator undid it again on the next build-target
            // switch. The Editor showed duller balloons than the player did, and four .mat files
            // turned up modified after every WebGL build.
            //
            // Settled by capture, three ways: premultiplied + One reads as pink translucent
            // balloons; premultiplied + SrcAlpha washes them grey; keyword off makes the bodies
            // very nearly VANISH, because without it the diffuse no longer carries them and only
            // the opaque knots are left. So the keyword stays and everything else follows it.
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            // _Blend stays 0 (Alpha). Writing 1 (Premultiply) makes URP re-derive the keywords on
            // save and it DISABLES _ALPHAPREMULTIPLY_ON, leaving One blending over non-premultiplied
            // colour - the balloons come out washed toward white. 0 with the keyword forced on is
            // the pairing that has always been on the asset and the one that renders correctly.
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Clear glass - the cube room's cubes, 2026-08-13. Same transparent set-up
        // MakeTranslucentMaterial uses (both keywords, both blend factors, the premultiplied pairing
        // that actually renders instead of vanishing).
        //
        // `map` IS OPTIONAL AND IS NOW ALWAYS NULL. It used to carry the glyph as an alpha channel,
        // etching the symbol into the six faces; the symbol is a solid core suspended INSIDE the
        // glass now (BuildSymbolCube), so the body is one shared clear material for all six cubes
        // rather than six that differ only in what is printed on them.
        //
        // High smoothness reads as glass here for the same reason it reads as metal on the escape
        // objects: this project has no real refraction, so the ONLY thing selling a hard, clear
        // surface is a sharp reflection with real geometry (the ceiling fixtures) to catch in it -
        // see MakePolishedMetalMaterial and the reflection-probe notes in docs/rendering-notes.md.
        //
        // `tint` is deliberately the SAME colour for all six cubes - see CubeSymbols' own note on
        // "symbols, not colours" for why a per-cube tint would undo the room's one accessibility
        // rule - and its ALPHA is how much glass there is to see through. It came down when the
        // symbol moved inside: the body now has something behind it that has to be read.
        private static Material MakeGlassMaterial(string name, Texture2D map, Color tint, float smoothness)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            Shader lit = OpaqueShader();

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(lit);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = lit;
            mat.color = tint;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            mat.SetTexture("_BaseMap", map);
            mat.mainTexture = map;
            SetSmoothness(mat, smoothness);
            // A flat 0 metallic read as thin, flat plastic wrap rather than a hard cut surface -
            // play called it cheap. Real glass is not a metal, but this project has no refraction
            // (see the note above), so a weak dielectric specular is the wrong tool here for the
            // same reason it is the right one everywhere else: without it, the ceiling fixtures this
            // cube ought to be throwing back barely show up at all. Short of the escape objects' own
            // 0.9 - this still has to read as SEE-THROUGH, and a fully metallic surface would not.
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.35f);

            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // **FETCHING A MATERIAL IS NOT THE SAME CALL AS MAKING ONE, AND CONFUSING THE TWO FLATTENED
        // EVERY WALL IN THE GAME.**
        //
        // `MakeColorMaterial` is a WRITE. It loads the asset if it is there, then re-authors it -
        // colour, shader, and the matte 0.03 smoothness below. That is right for the place that owns
        // a material and wrong everywhere else, because a caller that only wants a REFERENCE to an
        // existing material silently republishes it with defaults.
        //
        // Which is what happened: `BuildMouthShutter` fetched the wall's two materials by name, on
        // the good reasoning that the shutter should be built out of the same panels as the wall
        // beside it. It ran after `ApplySurfaceDetail` had given `PanelWhite` its 0.85, so it put
        // 0.03 back - and 0.03 is no specular response at all. Every wall in every room of every
        // cycle stopped reflecting anything, which reads as a lighting fault a long way from
        // anything to do with a shutter, and cost a round of chasing the reflection probes (which
        // were fine, 20/20, capturing rooms correctly).
        //
        // So: use this to REFER to a material somebody else authors, and `MakeColorMaterial` only
        // where the material is yours. `CheckWallSmoothness` catches the next one of these.
        private static Material FindColorMaterial(string name, Color fallback)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsDir}/{name}.mat");
            return mat != null ? mat : MakeColorMaterial(name, fallback);
        }

        private static Material MakeColorMaterial(string name, Color color)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(OpaqueShader());
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = OpaqueShader();
            mat.color = color;
            // Matte: the default 0.5 smoothness mirrors the skybox onto the room surfaces.
            SetSmoothness(mat, 0.03f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat = null, bool trigger = false, bool removeCollider = false)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;

            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;

            Collider col = go.GetComponent<Collider>();
            if (removeCollider && col != null) Object.DestroyImmediate(col);
            else if (col != null) col.isTrigger = trigger;

            return go;
        }

        // Instantiates an imported model (Kenney OBJ furniture) and repositions it so its
        // rendered bounds are centered on targetXZCenter with its base sitting at floorY -
        // needed because these models' pivots are at odd mesh corners, not their footprint center.
        // A MODEL HUNG ON SOMETHING, RATHER THAN STOOD ON THE FLOOR.
        //
        // **`PlaceModel` TAKES WORLD COORDINATES** - its `targetXZCenter` and `floorY` are compared
        // against world-space renderer bounds - and that is right for what it was written for: props
        // that stand somewhere in a room at a floor height. It is wrong, silently, for anything
        // mounted on a parent that already carries the position.
        //
        // Passing `Vector3.zero, 0f` to it does not mean "where the parent is". It means the WORLD
        // ORIGIN, which in this project is the middle of Room1 - and that is exactly what happened:
        // nine monitors and three camera housings all landed in cycle 1's bed room, in a heap, in
        // front of the camera that captures the title screen's photograph. The menu turned into a
        // close-up of a CCTV lens and the fault looked nothing like its cause.
        //
        // So this is the mounted version: everything local, centred on a point in the PARENT's frame,
        // sized by its longest side because a downloaded model's units are never the ones wanted.
        private static (GameObject instance, Bounds worldBounds) PlaceModelLocal(
            string assetPath, Transform parent, string name, Vector3 localPosition,
            Quaternion localRotation, float targetLongestSide)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (source == null)
            {
                Debug.LogError($"[SceneBuilder] no model at {assetPath}");
                return (null, default);
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            instance.name = name;
            instance.transform.localRotation = localRotation;
            instance.transform.localScale = Vector3.one;
            instance.transform.localPosition = Vector3.zero;

            Bounds bounds = MeasuredBounds(instance);
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (targetLongestSide > 0f && longest > 0.0001f)
                instance.transform.localScale = Vector3.one * (targetLongestSide / longest);

            // Re-measured after scaling, then moved so the model's CENTRE lands on the asked-for
            // point. Centre rather than base, because a thing on a wall has no base.
            bounds = MeasuredBounds(instance);
            Vector3 centreInParent = parent != null
                ? parent.InverseTransformPoint(bounds.center)
                : bounds.center;
            instance.transform.localPosition += localPosition - centreInParent;

            return (instance, MeasuredBounds(instance));
        }

        private static Bounds MeasuredBounds(GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(instance.transform.position, Vector3.zero);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static (GameObject instance, Bounds worldBounds) PlaceModel(string objAssetPath, Transform parent, string name, Vector3 targetXZCenter, float floorY, float uniformScale, bool addBoxCollider, Quaternion rotation = default)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(objAssetPath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            instance.name = name;
            instance.transform.localScale = Vector3.one * uniformScale;
            // Bounds are measured AFTER rotating, so a model authored Z-up or facing the wrong way
            // still lands centred and sitting on the floor.
            instance.transform.localRotation = rotation == default ? Quaternion.identity : rotation;
            instance.transform.localPosition = Vector3.zero;

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            // THE DELTA IS A WORLD VECTOR AND `localPosition` IS NOT, so it has to be brought into the
            // parent's frame before it is added. That was a no-op for as long as nothing this
            // function places sat under a rotated parent, and the comment on BuildBed said so out
            // loud. Cycle 2 is turned 180 degrees now (see CycleTwoYaw) and the unconverted delta
            // pushed the bed the wrong way down the room by twice its own offset.
            Vector3 delta = new Vector3(targetXZCenter.x - bounds.center.x, floorY - bounds.min.y, targetXZCenter.z - bounds.center.z);
            instance.transform.localPosition += parent != null
                ? parent.InverseTransformVector(delta)
                : delta;
            Vector3 finalCenter = bounds.center + delta;
            Bounds finalBounds = new Bounds(finalCenter, bounds.size);

            if (addBoxCollider)
            {
                BoxCollider box = instance.AddComponent<BoxCollider>();
                box.center = instance.transform.InverseTransformPoint(finalCenter);
                // The bounds are world-axis-aligned, so bring the size back into the instance's
                // own axes before assigning - otherwise a rotated model gets a collider with its
                // height and depth swapped.
                // The instance's WORLD rotation, not its local one - the two differ the moment a
                // parent carries a turn of its own, and `finalBounds` is world-axis-aligned.
                Vector3 localSize = Quaternion.Inverse(instance.transform.rotation) * finalBounds.size;
                box.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z)) / uniformScale;
            }

            return (instance, finalBounds);
        }
    }
}
