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
    // THE PLAYER'S BODY AND EVERY COPY OF IT: the ghost prefab, its materials and its animator, the
    // shadow budget, and the layer split that keeps the player's own body out of their eyes. Ghosts
    // have no colliders (CLAUDE.md 1.7).
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {

        private const string GhostModelPath = "Assets/ArtAssets/Smooth_Male_Casual@Walking.fbx";

        // **TWO LAYERS, because a first-person body and a body in a mirror are not the same object.**
        // A renderer cannot be told to draw differently per camera, so there are two of them and every
        // camera in the building is told which one it may see:
        //
        //     PlayerBodyView    headless. ONLY the player's own camera renders it.
        //     PlayerBodyWorld   whole. Everything EXCEPT the player's camera renders it.
        //
        // The head has to go from the first one because the camera is inside it, and a head rendered
        // from the inside is a wall of skull across the top of the frame.
        private const string PlayerBodyViewLayer = "PlayerBodyView";
        private const string PlayerBodyWorldLayer = "PlayerBodyWorld";
        // **AND A THIRD, WHICH IS ONLY EVER A SHADOW** (2026-08-22). The player cast none in their own
        // view and the cause was not the lights: a camera's culling mask culls SHADOW CASTERS too, so
        // excluding `PlayerBodyWorld` from the player's camera removed the body's shadow along with the
        // body. The view body cannot supply it either - it is headless and armless, and a headless
        // armless shadow on the floor is worse than no shadow at all.
        //
        // So a whole third instance, drawn by nobody (`ShadowsOnly`) and rendered by the player's
        // camera alone; the mirrors and the CCTV feeds exclude it and take the world body's shadow
        // instead, or a player standing in front of a mirror would cast two. Each of the three now
        // does exactly one job, which is the same argument that built the first two.
        private const string PlayerBodyShadowLayer = "PlayerBodyShadow";
        // The same 0.377 the ghosts use, and for the same measured reason: the rig stands 4.739m at
        // scale 1, so this puts it at 1.75m - a hair under the 1.8m controller and right for the 1.6m
        // eye it is seen from.
        private const float PlayerBodyScale = 0.377f;


        // ~~`PlayerBodyViewSetback` and `FirstPersonHiddenBones`~~ **BOTH GONE 2026-08-23 with the
        // first-person body they served.** The setback pushed that instance 20cm behind the player so
        // the camera was not inside its chest; the bone list hid its head, its arms and finally its
        // whole upper body. Neither has a subject any more - see `BuildPlayerBody`. `git log` has the
        // measurements if the body ever comes back.

        // The ghosts are real people now - a rigged, animated, opaque figure rather than the six
        // faint primitives that came before. That is a deliberate change of what a ghost IS: it
        // used to read as an afterimage of you, and it now reads as a person who was here. The
        // reference film shows past selves as people, so this is the faithful reading; the cost is
        // that six of them in a room is a crowd rather than a memory.
        //
        // The model is Quaternius' Smooth Male Casual (CC0), 7,932 triangles with twelve clips
        // baked in. Its own materials are already flat-colour URP/Lit with no textures, which is
        // exactly what this room wants - the walls carry the detail, the figures do not.
        // Transform.Find only walks one level and takes a path; a rig's bone can be at any depth
        // under any number of exporter-invented parents, so this searches by name instead.
        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        // A place on a ghost's skeleton that a carried object can be parented to, in METRES.
        //
        // TWO OBJECTS, AND THE PAIR IS THE WHOLE POINT. This FBX has a `HumanArmature` node at scale
        // 100 inside a body scaled to 0.377, so every bone's lossyScale is 37.7: parent a 0.2m key
        // to one and you get a 7.5m key, flung metres away because its local offset is multiplied by
        // the same number. The OUTER node cancels that and must stay at localPosition zero - an
        // offset written there would be in the 37.7x space too, so 3cm arrives as 1.13m. The offset
        // therefore goes on the CHILD, where the scale is already 1 and centimetres mean centimetres.
        //
        // Factored out when the ghost grew a second one of these. Getting it wrong is not a subtle
        // failure - it is an object the size of a room - but it is an easy one to get wrong twice,
        // so the check is built in: the returned anchor's lossyScale is asserted at 1.
        private static Transform MakeGhostAnchor(Transform rigRoot, Transform fallback, string boneName,
                                                 string label, Vector3 localOffset)
        {
            Transform bone = FindDeep(rigRoot, boneName);
            if (bone == null)
                Debug.LogWarning($"[SceneBuilder] ghost rig has no {boneName} - {label} items will ride the root");

            GameObject scaleNode = new GameObject(label + "Scale");
            scaleNode.transform.SetParent(bone != null ? bone : fallback, false);
            scaleNode.transform.localPosition = Vector3.zero;

            Vector3 rigScale = scaleNode.transform.lossyScale;
            scaleNode.transform.localScale = new Vector3(
                Mathf.Approximately(rigScale.x, 0f) ? 1f : 1f / rigScale.x,
                Mathf.Approximately(rigScale.y, 0f) ? 1f : 1f / rigScale.y,
                Mathf.Approximately(rigScale.z, 0f) ? 1f : 1f / rigScale.z);

            GameObject anchor = new GameObject(label + "Anchor");
            anchor.transform.SetParent(scaleNode.transform, false);
            anchor.transform.localPosition = localOffset;

            Debug.Log($"[SceneBuilder] Ghost {label} anchor on {boneName}: rig scale {rigScale.x:0.###} -> "
                + $"{anchor.transform.lossyScale.x:0.###} (expects 1)");

            return anchor.transform;
        }

        // THE PLAYER'S BODY, twice. See `PlayerBody` for why twice.
        //
        // Parented to the player ROOT rather than to the camera rig, so it takes the yaw and not the
        // pitch: a body that tipped forward when you looked at your feet would be a body doing a
        // somersault every time you checked where you were standing. The controller's origin is at
        // the feet (`cc.center` is half its height), so this sits at local zero.
        private static void BuildPlayerBody(Transform player)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(GhostModelPath);
            if (model == null)
            {
                Debug.LogError($"[SceneBuilder] Player body model missing at {GhostModelPath}");
                return;
            }

            // **THE MODEL'S OWN MATERIALS, and that is the fix rather than the shortcut.** It ships
            // with six of them - Shirt, Skin, Pants, Eyes, Socks, Hair - already on
            // `Universal Render Pipeline/Lit`, which the dump says and nobody had asked. They were
            // being overwritten with one flat grey, so the player was a slab and play said so.
            //
            // Not the GHOSTS' materials either: `GhostMaterials` is the afterimage shader, a hollow
            // rim that exists to say "this is a recording". The two now differ the way they should -
            // the living player is a person and a past self is a smear of one - out of one model.
            // ~~A THIRD INSTANCE, `PlayerBody_View`~~ **GONE 2026-08-23, after play, by request: look
            // down and there is nothing there.**
            //
            // It was the headless armless one the player's own camera drew, and the decision to drop
            // it is narrower than it looks. Of the three reasons a body was added at all, only ONE
            // was ever about that instance - "look down and see yourself". The other two, being on the
            // CCTV and being in a mirror, are the WORLD body's job and are untouched.
            //
            // And the remaining reason is thin in this game specifically: cycle 3 is a mirror puzzle,
            // so there is a place built for seeing yourself properly, and a glance at your own shins
            // is the worse version of that moment. Against it: 7,932 untextured triangles are at their
            // closest to the camera here of anywhere in the building, and the legs are scrubbed by
            // travel, so they do not match strafing, jumping or falling. Legs that disagree with the
            // movement read worse than no legs. Same argument the arms lost on 2026-08-22.
            //
            // **What went with it**: `FirstPersonHiddenBones`, `PlayerBody.hideBones`,
            // `PlayerBody.onlyWhileDriving` and `PlayerBodyViewSetback`. All four existed only to make
            // that instance tolerable. `git log` has them if it ever comes back.
            MakePlayerBody(player, "PlayerBody_World", model,
                           EnsureLayer(PlayerBodyWorldLayer),
                           shadows: UnityEngine.Rendering.ShadowCastingMode.On);
            // ~~`PlayerBody_Shadow`~~ **GONE 2026-08-24, after play, by request: the quality was not
            // there.** The player casts nothing in their own view.
            //
            // It was a second copy of the body drawn ShadowsOnly, and it existed for a reason that
            // still holds mechanically: a culling mask culls shadow CASTERS too, so masking the world
            // body out of the player's camera takes its shadow with it, and something else has to put
            // one back. Nothing does now.
            //
            // **Three shapes were tried in one day and the objection outlived all of them**, which is
            // the useful part of this note:
            //
            //   the body, one CORNER fixture casting - read as detached, a long shadow off to one side
            //   the body, all four casting     - "like a skeleton": four silhouettes, eight limbs
            //   a plain capsule                - fixed the limbs by deleting them; not a person
            //   the body, nearest fixture only - short, overhead, correctly shaped, still not good
            //
            // So the remaining suspects are not the caster and not the silhouette. They are that these
            // are POINT lights standing in for 1.4m emissive panels - a real area source gives a soft
            // penumbra where a point gives a hard edge - and URP's default shadow bias, which
            // peter-pans the contact point away from the feet. **Anyone restoring this should fix one
            // of those first rather than trying a fifth shape.**
            //
            // What did NOT go with it: the ceiling fixtures still cast, so the ROOMS still have
            // shadows - see `ShadowBudget`. This removes the player from that, nothing else. And
            // `PlayerBody_World` is untouched, so a mirror and the CCTV still show a whole person.
        }

        private static void MakePlayerBody(Transform player, string name, GameObject model,
                                           int layer,
                                           UnityEngine.Rendering.ShadowCastingMode shadows)
        {
            GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(model);
            body.name = name;
            body.transform.SetParent(player, false);
            // Exactly where the player is. Both remaining instances want the true position - the
            // world body because mirrors and CCTV must agree with the room, the shadow body because
            // it is now the only thing telling the player where their own feet are.
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one * PlayerBodyScale;

            // No colliders, for the reason the ghosts have none: the player already has a capsule, and
            // a second one inside it would fight the first. It also keeps the body out of every
            // raycast in the building - `PlayerLookup.Occluded` and `LaserBeam` both filter the Player
            // tag, but not having a collider at all is cheaper than being filtered.
            foreach (Collider c in body.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);

            foreach (Transform t in body.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;

            SkinnedMeshRenderer skin = body.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skin != null)
            {
                skin.sharedMaterials = PlayerBodyMaterials(skin.sharedMaterials, name);
                // ONE CASTER PER CAMERA, never two: the world body casts for the mirrors and the
                // feeds, the shadow body casts for the player, and the view body casts for nobody.
                skin.shadowCastingMode = shadows;
                // **THE VIEW BODY DOES NOT RECEIVE**, and that is not laziness. The shadow body stands
                // in the same place wearing the same pose, so a receiving view body would be lit
                // through its own coincident shadow caster - depth-fighting on every surface of your
                // own legs, which is the classic way this arrangement produces acne.
                skin.receiveShadows = shadows == UnityEngine.Rendering.ShadowCastingMode.On;
                // The bounds Unity computes for a skinned mesh are the bind pose's, and this rig's are
                // wrong enough that it vanishes when its origin leaves the frustum.
                skin.updateWhenOffscreen = true;
            }

            Animator animator = body.GetComponent<Animator>();
            if (animator == null) animator = body.AddComponent<Animator>();
            animator.runtimeAnimatorController = GhostAnimatorController();
            animator.applyRootMotion = false;

            PlayerBody driver = body.AddComponent<PlayerBody>();
            driver.animator = animator;
        }

        // ENROLS EVERY SHADOW-CASTING CEILING FIXTURE IN ONE BUDGET, and switches them all off.
        //
        // **THE ENROLMENT TEST IS "THE BUILDER GAVE IT SHADOWS"** - `BuildCeilingLights` sets
        // `LightShadows.Soft` on the fixtures of any room built with `castShadows`, and this collects
        // exactly those. No name tag and no second list to keep in step: a room that opts out never
        // had shadows set, so it is never found here.
        //
        // Then every one of them goes to `None`, because from here on WHICH of them casts is
        // `ShadowBudget`'s decision and there must be exactly one owner of it (CLAUDE.md §2). It also
        // means the scene is saved, and the reflection probes are baked, with nothing casting - which
        // is what the bake did anyway when only one light in the building cast, and what stops a
        // 360-degree cubemap render asking for thirty shadow maps.
        //
        // **ONE BUDGET PER CYCLE, ON THE CYCLE'S OWN GameObject** - which is what makes it survive
        // being saved. The cycles are split into scenes of their own further down `Build`, and a
        // single budget sitting in the core scene held references to lights that walked off into
        // `Cycle2` and `Cycle3`: Unity refuses to serialise a cross-scene reference, so two thirds of
        // the list would have arrived at runtime as nulls. Hung off the `Cycle`, the component and
        // every light it names are in one subtree and move together. It is also the right owner on
        // its own merits (CLAUDE.md §2): a cycle owns its rooms, and only the loaded one runs.
        //
        // **OWNERSHIP IS TESTED AGAINST `worldRoot`, NOT BY WALKING UP TO A `Cycle`.** This runs
        // before the bake, and at that point a cycle's rooms are still under its `worldRoot` with
        // that root NOT yet parented to the `Cycle` itself - the split loop does that later. Asking
        // `GetComponentInParent<Cycle>` here finds nothing at all, and the first version of this
        // reported all sixty-four fixtures as orphans and switched them off for good.
        //
        // A scene walk rather than a line in every room's builder, for the same reason
        // `AddFallingToEveryCarryable` is one: a new room gets this without its author knowing.
        private static void WireShadowBudget()
        {
            var byCycle = new System.Collections.Generic.Dictionary<
                Cycle, System.Collections.Generic.List<Light>>();
            int orphans = 0;

            Cycle[] cycles = Object.FindObjectsByType<Cycle>(FindObjectsInactive.Include,
                                                             FindObjectsSortMode.None);

            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include,
                                                                    FindObjectsSortMode.None))
            {
                if (light.shadows == LightShadows.None) continue;

                // **THE CALIBRATION ROOM IS NOT A CYCLE'S BUSINESS, and enrolling it here was a
                // silent bug.** This ran long before the old `ExtractCalibrationRoom` lifted that room into
                // the core scene, so its four fixtures were being written into CYCLE 1's budget - and
                // then the lift put them in a different scene from the component holding them. Unity
                // nulls a serialised reference across a scene boundary, so those four were never
                // switched by anything and kept the `Soft` the builder gave them: four permanent
                // casters nobody was counting. With four more in the player's own room that is eight
                // maps wanting a four-map atlas, which is the one thing `ConfigureUrpAsset` warns
                // about. `cross-scene-report.txt` had named all four.

                light.shadows = LightShadows.None;

                Cycle owner = null;
                foreach (Cycle c in cycles)
                {
                    Transform root = c.worldRoot != null ? c.worldRoot : c.transform;
                    if (light.transform.IsChildOf(root)) { owner = c; break; }
                }

                if (owner == null) { orphans++; continue; }

                if (!byCycle.TryGetValue(owner, out var list))
                    byCycle[owner] = list = new System.Collections.Generic.List<Light>();
                list.Add(light);
            }

            if (orphans > 0)
                Debug.LogWarning($"[SceneBuilder] ShadowBudget: {orphans} shadow-casting light(s) are "
                               + "under no Cycle, so nothing will ever switch them on. They have been "
                               + "left dark rather than left casting.");

            if (byCycle.Count == 0)
            {
                Debug.LogWarning("[SceneBuilder] ShadowBudget: no fixture is set to cast, so nothing "
                               + "in this game will ever have a shadow. Check `castShadows`.");
                return;
            }

            foreach (var pair in byCycle)
            {
                ShadowBudget budget = pair.Key.gameObject.AddComponent<ShadowBudget>();
                budget.fixtures = pair.Value.ToArray();

                // **FOUR CASTERS, AT 2048 EACH IN A 4096 ATLAS** (2026-08-25, by request). Authored
                // here rather than left to the component's own default, which is what CLAUDE.md §2
                // asks and which this had been quietly relying on.
                //
                // **ONE caster was tried for a day and play rejected it**, and the reason is worth
                // keeping: the single caster is whichever fixture is NEAREST THE PLAYER, so walking
                // across a room swaps it and every shadow in the room jumps to a new angle. `range`
                // is sized (10m) precisely so the casting SET does not change while you are inside a
                // room - and that guarantee only holds when the whole room's ceiling is in the set.
                // With one, the set changes constantly and the guarantee is worthless.
                //
                // Four also restores what a ceiling of four panels actually does: overlapping soft
                // shadows, dark where all four meet under an object and faint where only one reaches.
                budget.maxCasters = 4;

                Debug.Log($"[SceneBuilder] ShadowBudget on '{pair.Key.name}': {pair.Value.Count} "
                        + $"eligible ceiling fixtures, at most {budget.maxCasters} casting within "
                        + $"{budget.range}m of the player.");
            }
        }

        // WHAT THE PLAYER IS WEARING, one material per slot, MATCHED BY THE SLOT'S OWN NAME.
        //
        // The model ships six - Shirt, Skin, Pants, Eyes, Socks, Hair - already on URP/Lit, and they
        // are replaced rather than tuned in place because they are sub-assets of the FBX: editing
        // those would edit the asset, and CLAUDE.md §2 puts values in `SceneBuilder` rather than in
        // the thing being valued.
        //
        // **THIS IS BELIEVABLE, NOT PHOTOREAL, AND THAT CEILING IS THE MODEL'S.** 7,932 triangles, no
        // UVs and no textures at all - the dump says so - so every surface is one flat colour and
        // there is no skin, no fabric weave and no wrinkle to be had at any setting. What CAN be done
        // is stop it reading as a mannequin: muted clothing rather than saturated, a plausible skin
        // tone, cloth at low smoothness so it is not plastic, and wet-looking eyes.
        //
        // Anything past that is a different asset, not a different number.
        private static Material[] PlayerBodyMaterials(Material[] source, string bodyName)
        {
            var result = new Material[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                string slot = source[i] != null ? source[i].name : "";
                Material made;

                if (slot.StartsWith("Skin")) made = BodyMaterial("PlayerSkin", 0.80f, 0.62f, 0.51f, 0.25f);
                else if (slot.StartsWith("Hair")) made = BodyMaterial("PlayerHair", 0.15f, 0.12f, 0.10f, 0.30f);
                else if (slot.StartsWith("Shirt")) made = BodyMaterial("PlayerShirt", 0.33f, 0.37f, 0.44f, 0.14f);
                else if (slot.StartsWith("Pants")) made = BodyMaterial("PlayerPants", 0.20f, 0.23f, 0.30f, 0.12f);
                else if (slot.StartsWith("Socks")) made = BodyMaterial("PlayerSocks", 0.74f, 0.74f, 0.71f, 0.10f);
                else if (slot.StartsWith("Eyes")) made = BodyMaterial("PlayerEyes", 0.09f, 0.09f, 0.11f, 0.80f);
                else
                {
                    Debug.LogWarning($"[SceneBuilder] {bodyName}: unrecognised material slot '{slot}' - "
                                   + "left as imported. Run Iteration Room/Dump Furniture Models.");
                    made = source[i];
                }

                result[i] = made;
            }
            return result;
        }

        private static Material BodyMaterial(string name, float r, float g, float b, float smoothness)
        {
            Material m = MakeColorMaterial(name, new Color(r, g, b));
            SetSmoothness(m, smoothness);
            return m;
        }


        private static GhostReplayer BuildGhostPrefab()
        {
            string prefabPath = $"{PrefabsDir}/Ghost.prefab";

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(GhostModelPath);
            if (model == null)
            {
                Debug.LogError($"[SceneBuilder] Ghost model missing at {GhostModelPath}");
                return null;
            }

            GameObject ghost = new GameObject("Ghost");
            GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(model);
            body.transform.SetParent(ghost.transform, false);
            body.name = "Body";

            // Measured, not guessed: the rig stands 4.739m at scale 1 (bone extents, verified in
            // the editor), so 0.377 puts it at 1.75m - a hair under the player's 1.8m controller
            // and right for the 1.6m eye height they are seen from.
            body.transform.localScale = Vector3.one * 0.377f;

            // Colliders would break the game outright: a ghost is something you walk THROUGH, and
            // one that pushed the player would change the recording being made against it.
            foreach (Collider c in body.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);

            SkinnedMeshRenderer skin = body.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skin != null)
            {
                skin.sharedMaterials = GhostMaterials(skin.sharedMaterials);
                // Shadows are OFF again, and that reverses a call made when the ghosts were opaque
                // ("a person with no shadow reads as a bug"). It does not survive the figures
                // becoming afterimages: a hollow rim that throws a solid, fully detailed shadow on
                // the floor puts every piece of detail this shader exists to remove straight back
                // into the room, in the one place the eye is guaranteed to read it as real.
                skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                skin.receiveShadows = false;
                // The bounds Unity computes for a skinned mesh are the bind pose's, and this rig's
                // are wrong enough that ghosts vanish when their origin leaves the frustum.
                skin.updateWhenOffscreen = true;
            }

            Animator animator = body.GetComponent<Animator>();
            if (animator == null) animator = body.AddComponent<Animator>();
            animator.runtimeAnimatorController = GhostAnimatorController();
            animator.applyRootMotion = false;

            GhostReplayer replayer = ghost.AddComponent<GhostReplayer>();
            replayer.animator = animator;
            // A ghost pops only while holding the tool, the same test BalloonTool applies to the
            // player. Same constant on both sides so the two gates cannot drift apart.
            replayer.popToolItemId = ToolItemId;
            // The player's own put-down distance. Same constant on both sides so the two cannot
            // drift apart - the same reasoning `popToolItemId` above is written from here under.
            replayer.dropAhead = DropAheadDistance;

            // Where the EQUIPPED item rides. Hung off the rig's right hand so it swings with the arm
            // through the walk cycle - parented to the ghost's root it would slide along beside the
            // figure, which reads as an object being dragged rather than carried.
            //
            // Metres, in the hand's own frame. Just past the wrist so the key sits in the fist
            // rather than inside it. This is the one place to tune a ghost's grip.
            replayer.carryAnchor = MakeGhostAnchor(body.transform, ghost.transform, "MiddleHand.R",
                                                   "Carry", new Vector3(0f, -0.03f, 0.04f));

            // AND WHERE EVERYTHING ELSE IT CARRIES RIDES - a belt line across the hips.
            //
            // A ghost used to hide every item but the equipped one, which made those items
            // UNTAKEABLE as well as unseen, because CarryableItem.IsAvailable reads `visible`. A
            // past self holding pin and key with the pin out put the key somewhere the living player
            // could neither see nor reach. Wearing them is what makes "who has the key" answerable
            // and the answer reachable, and it needs no HUD to say it.
            //
            // The HIPS rather than the hand, because a stowed object should not swing with the arm;
            // rather than the ghost root, because at the root it slides beside the figure. The
            // 0.09 forward clears the body so an object sits ON the belt rather than in the pelvis,
            // and 0.13 apart is enough that two 0.22-scaled escape objects do not intersect.
            replayer.stowAnchor = MakeGhostAnchor(body.transform, ghost.transform, "Hips",
                                                  "Stow", Vector3.zero);
            replayer.stowLocalOrigin = new Vector3(0f, 0f, 0.09f);
            replayer.stowStep = new Vector3(0.13f, 0f, 0f);

            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(ghost, prefabPath);
            Object.DestroyImmediate(ghost);


            return prefabAsset.GetComponent<GhostReplayer>();
        }

        // ONE material across every submesh, where the opaque version cloned six (Skin, Shirt,
        // Pants, Hair, Eyes, Socks). Keeping them tinted apart would have survived the fresnel and
        // handed back the very thing the change removes: a rim that still says shirt-here,
        // trousers-there is a legible person, just a see-through one. The array still has to be
        // the submesh count long, so the same material is handed to every slot.
        //
        // The old Assets/Materials/Ghost*.mat are left on disk and simply unreferenced - they are
        // the FBX's own colours and cost nothing, and going back means pointing at them again.
        private static Material[] GhostMaterials(Material[] source)
        {
            Material faint = GhostFaintMaterial();
            var result = new Material[source.Length];
            for (int i = 0; i < source.Length; i++) result[i] = faint;
            return result;
        }

        // Tuning lives here rather than in the .shader's defaults, for the same reason every other
        // number in this project does: the shader is the mechanism, SceneBuilder is the value.
        private static Material GhostFaintMaterial()
        {
            string path = $"{MaterialsDir}/GhostFaint.mat";
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>($"{ShadersDir}/GhostFaint.shader");
            if (shader == null)
            {
                Debug.LogError($"[SceneBuilder] GhostFaint.shader missing from {ShadersDir}");
                return MakeColorMaterial("GhostFaintFallback", new Color(0.7f, 0.76f, 0.86f));
            }

            // GhostFaint.mat already exists from the era of the six faint primitives, on URP/Lit.
            // Reassigning the shader on the existing asset keeps its GUID, so nothing that
            // referenced it has to be found and repointed.
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            // The asset arrives carrying _SURFACE_TYPE_TRANSPARENT from its URP/Lit days. Swapping
            // the shader does not drop it - it just moves to m_InvalidKeywords and sits in the
            // diff forever. This shader declares no keywords at all, so the set is emptied.
            mat.shaderKeywords = new string[0];

            // Cool and desaturated, not white: at white the rim reads as a highlight on the wall
            // panelling behind it, which is the one colour in the room it must not be confused for.
            // Written through .linear - the project renders linear and SetColor takes the value
            // as-is, so an sRGB triple passed straight in comes out pale.
            mat.SetColor("_BaseColor", ((Color)new Color32(178, 194, 220, 255)).linear);
            mat.SetColor("_RimColor", ((Color)new Color32(224, 237, 255, 255)).linear);
            mat.SetFloat("_RimPower", 2.2f);
            mat.SetFloat("_RimAlpha", 0.75f);
            mat.SetFloat("_CoreAlpha", 0.06f);
            mat.SetFloat("_BottomFade", 0.35f);
            mat.SetFloat("_SoftFade", 0.12f);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Three states, no transitions, because GhostReplayer scrubs each of them by hand - it
        // pins animator.speed at 0 and writes the normalized time itself. See the note there for
        // why the walk has to advance with distance rather than with the clock.
        private static RuntimeAnimatorController GhostAnimatorController()
        {
            string path = $"{PrefabsDir}/GhostAnimator.controller";
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);
            if (controller == null)
                controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);

            var machine = controller.layers[0].stateMachine;
            // Rebuilt from scratch each time, since adding is not idempotent and a retried build
            // would otherwise stack duplicate states - the same failure ConfigureAmbientOcclusion
            // guards against with the SSAO feature.
            foreach (var child in machine.states) machine.RemoveState(child.state);

            AddGhostState(machine, "Walk", "Man_Walk", true);
            AddGhostState(machine, "Idle", "Man_Idle", false);
            AddGhostState(machine, "Swing", "Man_SwordSlash", false);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void AddGhostState(UnityEditor.Animations.AnimatorStateMachine machine,
                                          string stateName, string clipSuffix, bool isDefault)
        {
            AnimationClip clip = null;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(GhostModelPath))
            {
                var candidate = o as AnimationClip;
                if (candidate == null) continue;

                // LoadAllAssetsAtPath hands back the editor's hidden "__preview__" copies of every
                // clip alongside the real ones, and they sort first. Binding a state to one leaves
                // an animator that works in the editor and has nothing to play in a build - it
                // looked completely correct until the states were dumped and read.
                if (candidate.name.StartsWith("__preview__")) continue;

                // Clips arrive named "HumanArmature|Man_Walk", so match the tail rather than the
                // whole string - the armature prefix is the exporter's, not ours.
                if (candidate.name.EndsWith(clipSuffix)) { clip = candidate; break; }
            }
            if (clip == null) Debug.LogWarning($"[SceneBuilder] ghost clip '{clipSuffix}' not found");

            var state = machine.AddState(stateName);
            state.motion = clip;
            state.writeDefaultValues = true;
            if (isDefault) machine.defaultState = state;
        }
    }
}
