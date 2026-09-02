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
    // THE KEY, ITS LOCK, AND THE DOORWAYS THEY OPEN: the gold key model and every number measured
    // off it, the keyhole slot, the panel gate, and the big room the gates sit in.
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {

        // One key shape, used three times over: lying on the floor once its balloon bursts, seen
        // through the skin of the balloon that holds it, and mounted on Room2's lock so the thing
        // on the wall says what it wants. Laid out in the XY plane facing -Z.
        private const string GoldKeyPath = FurnitureDir + "/gold_key.glb";

        // Measured off the glb at scale 1, logged on every build so a re-export that moves them is
        // caught rather than discovered: the key runs 8.00 along Y with the BOW at +Y and the bit
        // at -Y, and its shaft turns about the line x=0, z=-0.03.
        private const float KeyModelLength = 8.0f;
        private const float KeyModelTipY = -7.493f;
        // Where the bow stops being a ring and becomes shaft. This is the waterline: everything
        // below it goes into the lock, everything above it stays out where the player can see it.
        private const float KeyModelBowJunctionY = -2.3f;
        private const float KeyModelShaftAxisZ = -0.03f;

        // 0.20m overall, which is what the five primitives it replaces measured. Not a coincidence
        // and not a free choice: Room2's puzzle is spotting the key THROUGH a balloon from across
        // the room, and that was play-tested at this size. A prop key rather than a real 6cm one.
        private const float KeyLength = 0.20f;
        private const float KeyScale = KeyLength / KeyModelLength;
        // Distance from the bow junction to the tip - i.e. how much key there is to push in.
        private const float KeyInsertTravel = (KeyModelBowJunctionY - KeyModelTipY) * KeyScale;

        // How the key hangs when it is NOT in the lock: bow up, bit down, turned off-axis. Only the
        // resting pose - CarryableItem.InsertInto zeroes the root's rotation, which is exactly the
        // frame the insert-and-turn animation wants, and ReturnToOrigin puts this back.
        private static readonly Quaternion KeyRestRotation = Quaternion.Euler(90f, 35f, 0f);

        // The gold key model, laid into its parent so that +Z is "into the lock" and a roll about
        // local Z is "turn the key". Everything downstream - the socket, the insertion, the turn -
        // depends on this frame, so it is established once here rather than at each call site.
        private static GameObject BuildKeyModel(Transform parent, float scaleMul, Material overrideMat,
                                                bool centreOnParent = false)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(GoldKeyPath);
            if (source == null)
            {
                Debug.LogError($"[SceneBuilder] Key model missing at {GoldKeyPath}");
                return null;
            }

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            model.name = "KeyModel";

            float scale = KeyScale * scaleMul;
            model.transform.localScale = Vector3.one * scale;

            // Two rotations, composed in this order and NOT written as one Euler: Unity evaluates
            // Euler as Y*X*Z, so a single triple would apply the roll before the lay-down and put
            // the bit back where it started.
            //   -90 about X  lays the key down, bow toward -Z (out at the player), bit into +Z.
            //   +90 about Z  rolls it so the flat of the bit stands VERTICAL, matching the
            //                keyhole's vertical slot. A key entering a slot sideways is the wrong
            //                key, and the slot is the one part of the lock that says which way up.
            model.transform.localRotation = Quaternion.AngleAxis(90f, Vector3.forward)
                                          * Quaternion.AngleAxis(-90f, Vector3.right);

            // Put the shaft's own axis on the parent's Z axis. Without this the turn is a wobble
            // about a line 0.75mm off the shaft - small, but it is the difference between a key
            // turning and a key being waggled.
            Vector3 axisPoint = new Vector3(0f, 0f, KeyModelShaftAxisZ) * scale;
            model.transform.localPosition = -(model.transform.localRotation * axisPoint);

            // The origin left by the line above is the BOW, which is what the lock wants - it is the
            // point that seats on the plate face, and CarryableItem.InsertInto drops the root exactly
            // there. It is wrong for the balloon: the key hangs off the origin by 0.09 and ends up
            // shouldered against one side of a 0.24-radius sphere instead of suspended in it, which
            // reads as a key stuck to the skin rather than one floating inside. Measured and
            // subtracted rather than hardcoded, so it survives the key being rescaled.
            if (centreOnParent)
            {
                Renderer[] centred = model.GetComponentsInChildren<Renderer>();
                Bounds local = new Bounds(parent.InverseTransformPoint(centred[0].bounds.center), Vector3.zero);
                foreach (Renderer r in centred)
                {
                    local.Encapsulate(parent.InverseTransformPoint(r.bounds.min));
                    local.Encapsulate(parent.InverseTransformPoint(r.bounds.max));
                }
                model.transform.localPosition -= local.center;
            }

            if (overrideMat != null)
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = overrideMat;
                    r.sharedMaterials = mats;
                }

            // Logged because every constant above is a measurement off this file, and a re-export
            // that moves them should show up in the build log rather than as a key sticking out of a
            // wall. (The nightstand used to be read the same way and no longer needs to be - it is
            // built from authored numbers now, not measured off a mesh.)
            if (scaleMul >= 1f)
            {
                Renderer[] rs = model.GetComponentsInChildren<Renderer>();
                Bounds b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                // Longest axis, not Z: the loose key's root carries KeyRestRotation, so which world
                // axis the length lands on depends on where this was called from.
                float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                Debug.Log($"[SceneBuilder] Key model size={b.size} longest={longest:0.000} (expects {KeyLength:0.000})");
            }

            return model;
        }

        // A CLONE of the glb's own Gold, because glb materials are sub-assets regenerated on every
        // reimport and anything written to them is lost - the same trap DisableBakedOcclusion works
        // around for the bed. `tint` scales the base colour: 1 for the loose key, dark for the copy
        // inside a balloon, which is read through a pink translucent skin and washes out at gold.
        //
        // THE ONE VALUE CHANGED IS metallicFactor, 1.0 -> 0.3, and it is not a style preference.
        // A fully metallic surface has NO diffuse term: every photon it shows is a reflection of
        // its surroundings. Reflections here come from a baked probe, and whatever this room's
        // probes are handing a metal it is not the white box they were baked in - the key rendered
        // BLACK with a thin gold rim (the rim being the only direct specular), floating in the
        // middle of a bright white room. Verified it was the metalness and not the lighting: with
        // occlusionTexture_strength forced to 0 nothing changed, and at 0.3 the key came back gold.
        //
        // This departs from "keep the glTF materials these ship with". That rule exists to stop a
        // hand-rolled URP/Lit stand-in dropping a model's metallic/roughness MAPS - and this model
        // has no textures at all, only factors, so there is nothing to drop. It is also the more
        // robust setting for this room: a key with a diffuse term reads as gold wherever it is put,
        // where a mirror only reads as gold where the probe happens to be right.
        private static Material KeyMaterial(string assetName, float tint) =>
            KeyMaterial(assetName, Color.white * tint);

        // tint is now a COLOUR rather than a brightness, which is what lets a red and a blue key be the
        // same object as the gold one. It is still applied by SCALING the glb's own baseColorFactor
        // rather than by assigning a chosen triple - so the model keeps its shading and the result stays
        // correct whichever colour space the glTF shader reads that property in. A red channel above 1
        // is deliberate and is why: scaling a dark gold by 1.0 would give a dark red, and the key has to
        // stay bright enough to be spotted through a balloon from across the room.
        private static Material KeyMaterial(string assetName, Color tint)
        {
            Material gold = null;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(GoldKeyPath))
                if (o is Material m && !m.name.StartsWith("__preview__")) { gold = m; break; }

            if (gold == null)
            {
                Debug.LogWarning($"[SceneBuilder] gold_key.glb has no material - {assetName} left flat");
                return MakeColorMaterial(assetName, new Color(0.85f, 0.68f, 0.24f) * tint);
            }

            string path = $"{MaterialsDir}/{assetName}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(gold);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = gold.shader;
            mat.CopyPropertiesFromMaterial(gold);
            mat.shaderKeywords = gold.shaderKeywords;

            mat.SetFloat("metallicFactor", 0.3f);
            const string baseColor = "baseColorFactor";
            if (tint != Color.white && mat.HasProperty(baseColor))
                mat.SetColor(baseColor, gold.GetColor(baseColor) * tint);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // The hole a key goes into: a round seat for the shaft with a narrow slot under it.
        // Two flat decals sitting on the plate face rather than a cut - the plate is a 0.09m cube
        // and boring a hole through it would show the wall behind.
        private static void BuildKeyholeSlot(Transform parent, Vector3 centre, Material mat)
        {
            GameObject seat = Prim(PrimitiveType.Cylinder, "KeyholeSeat", parent, centre,
                new Vector3(0.055f, 0.002f, 0.055f), mat, removeCollider: true);
            seat.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            Prim(PrimitiveType.Cube, "KeyholeSlot", parent,
                new Vector3(centre.x, centre.y - 0.037f, centre.z),
                new Vector3(0.022f, 0.055f, 0.004f), mat, removeCollider: true);
        }

        // Room2's balloons, pooled rather than spawned. The pool is what makes a balloon's id mean
        // the same thing in every iteration, which is what a ghost's recorded pops refer to.
        private static (BalloonField, CarryableItem[]) BuildBalloons(Transform parent)
        {
            const int balloonCount = 70;
            const int fieldSeed = 20260810;

            GameObject root = new GameObject("BalloonField");
            root.transform.SetParent(parent, false);

            // Translucent, so whatever is inside a balloon shows through it as a shape. That is
            // the whole point: the key balloon is now findable by looking rather than by bursting
            // seventy of them and hoping, which is what made the search luck before.
            Material pink = MakeTranslucentMaterial("BalloonPink", new Color(0.98f, 0.44f, 0.68f, 0.62f), 0.72f);
            Material knotMat = MakeColorMaterial("BalloonKnot", new Color(0.76f, 0.28f, 0.5f));

            // Bouncy and slippery. Combine on Maximum so a balloon still bounces off the floor and
            // the walls, which are plain matte colliders with no bounce of their own.
            PhysicsMaterial rubber = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>($"{MaterialsDir}/BalloonRubber.physicMaterial");
            if (rubber == null)
            {
                rubber = new PhysicsMaterial("BalloonRubber");
                AssetDatabase.CreateAsset(rubber, $"{MaterialsDir}/BalloonRubber.physicMaterial");
            }
            rubber.bounciness = 0.55f;
            rubber.dynamicFriction = 0.28f;
            rubber.staticFriction = 0.28f;
            rubber.bounceCombine = PhysicsMaterialCombine.Maximum;
            rubber.frictionCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(rubber);

            // THREE keys, in three balloons, from the same fixed seed as the spawn points - so each key
            // is in the same balloon in every run, which is what makes "I know where the blue one is"
            // worth having. Distinct by construction rather than by luck: drawing three times from 70
            // collides about one run in twenty-four, and a collision would silently put two keys in one
            // balloon and leave a door with no key anywhere.
            var keyIndices = new System.Collections.Generic.List<int>();
            System.Random keyRng = new System.Random(fieldSeed);
            while (keyIndices.Count < Room2Keys.Length)
            {
                int candidate = keyRng.Next(balloonCount);
                if (!keyIndices.Contains(candidate)) keyIndices.Add(candidate);
            }

            System.Random pitchRng = new System.Random(fieldSeed + 1);

            // Their own layer, so the player's controller can exclude them outright and there is
            // never any contact to be lifted by. Set on the root only - the collider lives there,
            // and the children are visuals.
            int balloonLayer = EnsureLayer(BalloonLayerName);

            Balloon[] balloons = new Balloon[balloonCount];
            for (int i = 0; i < balloonCount; i++)
            {
                GameObject go = new GameObject($"Balloon_{i}");
                go.transform.SetParent(root.transform, false);
                go.layer = balloonLayer;

                Prim(PrimitiveType.Sphere, "Body", go.transform, Vector3.zero,
                    new Vector3(0.48f, 0.58f, 0.48f), pink, removeCollider: true);
                Prim(PrimitiveType.Cube, "Knot", go.transform, new Vector3(0f, -0.3f, 0f),
                    new Vector3(0.06f, 0.07f, 0.06f), knotMat, removeCollider: true);

                SphereCollider col = go.AddComponent<SphereCollider>();
                col.radius = 0.27f;
                col.sharedMaterial = rubber;

                Rigidbody rb = go.AddComponent<Rigidbody>();
                rb.mass = 0.04f;
                // Gravity off: Balloon.FixedUpdate applies its own much gentler fall instead. Unity
                // has no per-body gravity scale, and the alternative - damping a full 9.81 down to
                // a drift - takes so much damping that the balloons stop bouncing and start
                // behaving like they are underwater. Low damping plus a small pull is light AND
                // lively; heavy damping is only slow.
                rb.useGravity = false;
                rb.linearDamping = 1.1f;
                rb.angularDamping = 1.2f;
                // Parked until the field releases them. Built live, seventy spheres would start the
                // scene interpenetrating at the origin - which is in Room1, beside the bed.
                rb.isKinematic = true;
                col.enabled = false;

                Balloon balloon = go.AddComponent<Balloon>();
                balloon.id = i;

                // The key inside, visible through the skin of whichever balloon has one. A child of the
                // balloon, so Balloon.SetInPlay hides and shows it along with everything else and it
                // vanishes the moment the balloon bursts. Hung off its own child so it keeps the bow-up
                // resting pose: BuildKeyModel lays the key along +Z for the lock, and a key floating
                // horizontally inside a balloon reads as debris.
                //
                // In the KEY'S OWN COLOUR, which is what makes the search a search for a particular
                // key rather than for "a key". The balloon's own reference is wired below, once the
                // key objects exist.
                int keySlot = keyIndices.IndexOf(i);
                if (keySlot >= 0)
                {
                    GameObject keyVisual = new GameObject("KeyVisual");
                    keyVisual.transform.SetParent(go.transform, false);
                    keyVisual.transform.localRotation = KeyRestRotation;
                    BuildKeyModel(keyVisual.transform, 0.85f,
                                  KeyMaterial(Room2Keys[keySlot].materialName + "InBalloon",
                                              Room2Keys[keySlot].metal * 0.36f),
                                  centreOnParent: true);
                }
                balloon.audioSource = MakeSource(go.transform, "PopAudio", 1f, 0.8f);
                // A fixed detune per balloon, from the field's own seed. One clip across seventy
                // balloons reads as a machine gun; a balloon keeping the same voice every
                // iteration is one more thing about the room that stays put.
                balloon.audioSource.pitch = 0.86f + (float)pitchRng.NextDouble() * 0.3f;
                balloon.popClip = LoadClip(SfxDir, "sfx_balloon_pop");

                balloons[i] = balloon;
            }

            // THE THREE KEYS. Each is the same object as the one key that was here before - same model,
            // same scale, same hand pose, same seated-orientation-is-identity trick - differing only by
            // its KeySpec. Built in a loop precisely so they cannot drift apart from each other.
            //
            // Their origins are spread along the room's mid-line rather than stacked, because origin is
            // where the loop puts a key back at the top of every iteration: three keys sharing one
            // origin would be three keys inside each other, and `ReturnAllToOrigin` would do it every
            // sixty seconds. Pocketed rather than held on pickup, so taking one does not knock the pin
            // out of the hand that got it.
            var keyItems = new CarryableItem[Room2Keys.Length];
            for (int k = 0; k < Room2Keys.Length; k++)
            {
                KeySpec spec = Room2Keys[k];

                GameObject keyRoot = new GameObject(spec.materialName);
                keyRoot.transform.SetParent(root.transform, false);
                keyRoot.transform.position = new Vector3((k - 1) * 0.5f, 0.06f, RoomPitch);
                // The resting pose lives on the ROOT, not on the model, and that is load-bearing:
                // InsertInto zeroes the root's rotation, which is precisely the frame KeyLock's
                // insert-and-turn works in, and ReturnToOrigin restores this at the top of the loop.
                keyRoot.transform.localRotation = KeyRestRotation;

                BuildKeyModel(keyRoot.transform, 1f, KeyMaterial(spec.materialName, spec.metal));

                BoxCollider keyTrigger = keyRoot.AddComponent<BoxCollider>();
                keyTrigger.isTrigger = true;
                keyTrigger.size = new Vector3(0.9f, 0.9f, 0.9f);

                CarryableItem keyItem = keyRoot.AddComponent<CarryableItem>();
                keyItem.itemId = spec.itemId;
                keyItem.displayName = "KEY";
                keyItem.icon = KeyIcon();
                // One silhouette for all three, tinted. Three drawn shapes would be three things to
                // learn where the colour is already the whole message.
                keyItem.iconTint = spec.display;
                // In the hand the key points the way it goes into a lock - teeth forward, bit vertical
                // - because the root's identity rotation IS the seated orientation (see BuildKeyModel).
                // Held nose-first means walking up to the lock and pressing E needs no mental rotation.
                // Angled up slightly and further from the eye than the pin, which is a short stub where
                // this is 0.20m long and would otherwise cross the middle of the screen.
                keyItem.handLocalPosition = HandPoseFor(KeyScale);
                keyItem.handLocalEuler = new Vector3(-12f, -14f, 0f);
                // LIES FLAT WHEN PUT DOWN. The root's built pose is KeyRestRotation - bow up,
                // standing on its blade - which is the pose of a key left somewhere to be FOUND, not
                // of one that has been dropped. A key stood on end where it fell reads as placed on
                // purpose. This is the same quarter turn BalloonField gives a key coming out of a
                // burst balloon, now stated once on the object instead of at each place that puts
                // one down.
                keyItem.restRoll = 90f;
                keyItem.audioSource = MakeSource(keyRoot.transform, "PickupAudio", 1f, 0.9f);
                keyItem.pickupClip = LoadClip(SfxDir, "sfx_item_pickup");

                keyItems[k] = keyItem;
                // The balloon holding this key learns which one it is. Done here rather than in the
                // balloon loop above because the key objects do not exist yet at that point.
                balloons[keyIndices[k]].heldKey = keyItem;
            }

            BalloonField field = root.AddComponent<BalloonField>();
            field.balloons = balloons;
            field.seed = fieldSeed;
            field.roomCenterZ = RoomPitch;

            // Park the pool now rather than waiting for the first iteration to do it. Built objects
            // sit at their parent's origin, which for these is the middle of Room1.
            field.ComputeSpawnPoints();
            field.ResetField();

            return (field, keyItems);
        }

        // A GATE: TWO LEAVES OF WALL THAT SLIDE APART WHILE A PAD IS HELD.
        //
        // Mechanically this is two `Door`s. That is not a shortcut - `Door` already TRACKS its pads
        // rather than latching (see its own note on why Room1's door works that way), which is
        // exactly the behaviour asked for here: step on, the wall opens; step off, it shuts. Writing
        // a second component for it would be a second answer to "is this open" for the same reason.
        //
        // **SO NOBODY WALKS THROUGH THEIR OWN GATE.** The pad is in this room and the opening is in
        // its wall, and one person cannot be on both. Every one of the four rooms around room3-1 is
        // reachable only while a PAST SELF is standing on the pad - which is the cycle's premise
        // stated in its first room, the way Room3's two pads state cycle 1's.
        //
        // The leaves live in the pocket between this room's wall and the next room's, the same
        // 0.1m cavity an ordinary door slides in, and each slides outward by its own width so the
        // opening clears completely. The cavity is capped by a liner everywhere the leaves do not
        // sweep - without it the gap between the two rooms' walls is open to the skybox at both
        // ends, which is the fault `BuildDoorPocketFill` exists to fix for the ordinary doors.
        // WHERE A WALL'S GATE IS, and both rooms that share the wall have to agree - the opening is
        // cut out of each of their walls separately, so this is the one place the answer lives.
        //
        // **THE GATE IS ALWAYS WHOLE CELLS AND ALWAYS CENTRED; ONLY HOW MANY DEPENDS ON THE WALL.**
        // Room3-1's east and west walls are 10.5 across, six cells, so the middle PAIR straddles the
        // centre and the gate is two leaves opening to their own sides. The north and south walls are
        // 8.75, FIVE cells - there is no middle pair, so the gate is the middle CELL, one leaf, and it
        // slides one way. A two-cell gate on an odd wall was built first and is what this replaces:
        // it could only sit half a cell off centre, which read as a mistake rather than as a wall.
        //
        // A leaf is never part of a cell. A half-width panel would carry a groove down the middle of
        // a cell where the wall has none, which is the one thing that would give a shut gate away.
        private static void GateSpan(float wallWidth, out float openMin, out float openMax,
                                     out float leafTravel, out int leaves)
        {
            // The same rounding `BuildPanelWall` does, so the cells this lands on are the cells that
            // are actually built.
            int cols = Mathf.Max(2, Mathf.RoundToInt(wallWidth / GridCellWidth));
            float cellWidth = wallWidth / cols;

            leaves = cols % 2 == 0 ? 2 : 1;
            float halfSpan = leaves * cellWidth / 2f;
            openMin = -halfSpan;
            openMax = halfSpan;
            // One cell either way. A split pair only has to clear its own half; a single leaf only
            // has its own width to vacate. Both park exactly over the cell next door.
            leafTravel = cellWidth;
        }

        private static Rect GateCutout(float wallWidth)
        {
            GateSpan(wallWidth, out float openMin, out float openMax, out _, out _);
            return Rect.MinMaxRect(openMin, 0f, openMax, GateHeight);
        }

        // A GATE: A PIECE OF THE WALL THAT LEAVES WHILE A PAD IS HELD.
        //
        // **THE LEAVES ARE WALL, not door slabs, and that is the whole specification.** Same material,
        // same 0.025 thickness, same depth off the face, same groove inset, and split into one quad
        // per grid cell so the row line across the opening lands where the wall's own does. Shut,
        // there is nothing to see: no frame, no reveal, no lamp. A player who has not stood on the pad
        // has no way to know a gate is there.
        //
        // **EACH LEAF CARRIES ITS OWN BACKING, and leaving it out is what made the first version show
        // the room next door.** The cutout takes the wall's backing away along with its panels - so
        // the 0.05 groove around a leaf had nothing behind it, and the sightline ran groove, cavity,
        // the neighbour's own cutout, daylight. A panel is not a wall; a panel PLUS the dark plate
        // behind its grooves is. So the plate travels with it, sized to the full cell with no groove
        // inset, which is exactly the area the grooves expose.
        //
        // Mechanically it is one `Door` per leaf. `Door` already TRACKS its pads rather than latching
        // (see its note on Room1); what it did not have was the push-in phase a flush panel needs
        // before it can travel sideways, and that is a field on `Door` now rather than a second
        // component, so "is this open" still has one answer.
        // **BUILT ONCE PER SIDE OF THE WALL, and both sides are needed.** A gate lives in a wall two
        // rooms share, and each of them has its own panelled face - so a gate built on one side only
        // leaves the OTHER room looking at a bare hole where its grid should be. Play found it exactly
        // that way. The two calls take the same pads, so the two faces move as one wall.
        //
        // They fit because a leaf is thin. Each retracts to its OWN side of the 0.1m cavity rather
        // than to the middle of it: near leaf 0.129-0.169 back from its face, far leaf the mirror of
        // that, 12mm between them.
        //
        // `withCavityLiner` belongs to the first call only. The liner fills one volume and building it
        // twice is two coincident slabs fighting for the same pixels.
        private static Door[] BuildPanelGate(Transform parent, string name,
                                             Vector3 wallCentreAtBase, Vector3 rightDir, Vector3 inward,
                                             float wallWidth, Material panelMat, Material grooveMat,
                                             FloorButton[] pads, bool withCavityLiner = true,
                                             bool withAudio = true)
        {
            GateSpan(wallWidth, out float openMin, out float openMax, out float leafTravel, out int leaves);

            float leafWidth = (openMax - openMin) / leaves;
            float openCentre = (openMin + openMax) / 2f;
            float groove = GridLineThickness;

            Vector3 outward = -inward;
            Vector3 depthAxis = new Vector3(Mathf.Abs(inward.x), Mathf.Abs(inward.y), Mathf.Abs(inward.z));
            Vector3 widthAxis = new Vector3(Mathf.Abs(rightDir.x), Mathf.Abs(rightDir.y), Mathf.Abs(rightDir.z));

            // THE WHOLE LEAF HAS TO FIT IN THE CAVITY, which is 0.1 deep, so its backing is a thin
            // plate rather than the wall's own 0.1 slab. It only has to stop a sightline down a 0.05
            // groove, not hold a building up.
            const float leafBackingDepth = 0.015f;
            // JUST INSIDE THE CAVITY, not centred in it. Centred was right while only one side had a
            // gate; with a leaf coming from each face they have to pass each other, and the cavity is
            // 0.1 deep against two leaves of 0.04. 4mm past the wall's own backing puts this one at
            // 0.129-0.169 and its opposite number at 0.181-0.221.
            float pushDistance = WallDepth + 0.004f;

            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);

            AudioSource audio = MakeSource(root.transform, "GateAudio", spatialBlend: 1f, volume: 0.7f);
            audio.transform.localPosition = wallCentreAtBase + rightDir * openCentre
                                          + Vector3.up * (GateHeight / 2f);
            // **ONE DOORWAY MAKES ONE SOUND, AND FOR A WHILE IT MADE TWO** (2026-09-02, by request:
            // room3-1's doors are louder than everything else in the game).
            //
            // Every doorway in room3-1 is built TWICE - once from room3-1 and once from the room on
            // the far side, `Gate3_1_South` and `Gate3_2S_North` being the same opening. They are
            // driven by the same pad, so they open together, and each had its own source playing the
            // same clip from 0.1m away. Two identical waveforms in sync do not average, they ADD:
            // that is +6 dB exactly, on the one door in the game that is built as a pair.
            //
            // Per source these gates were 0.7 against an ordinary door's 1.0 - QUIETER, which is why
            // turning the number down was the wrong instinct and why it is back at 0.7. The far side
            // is simply silent, which is the rule the leaves either side of this line already follow:
            // two copies of the same slide half a frame apart is a flam.
            AudioClip openClip = withAudio ? LoadClip(SfxDir, "sfx_door_open") : null;

            var doors = new Door[leaves];
            for (int i = 0; i < leaves; i++)
            {
                float cellAlong = openMin + i * leafWidth;
                float cellEnd = cellAlong + leafWidth;
                float cellCentre = (cellAlong + cellEnd) / 2f;
                // Two leaves open to their own sides; a single leaf has only one way to go.
                float direction = leaves == 2 ? (i == 0 ? -1f : 1f) : 1f;

                GameObject leafRoot = new GameObject($"Leaf_{i}");
                leafRoot.transform.SetParent(root.transform, false);
                // On the opening's centre, so `Door.doorwayCentre` - read off this, and what decides
                // whether the player is standing in the way - lands in the gap rather than on a leaf.
                leafRoot.transform.localPosition = wallCentreAtBase + rightDir * openCentre
                                                 + Vector3.up * (GateHeight / 2f);

                // ONE QUAD PER GRID CELL, laid out exactly as `BuildPanelWall` does it.
                for (int row = 0; row * GridCellHeight < GateHeight - 0.001f; row++)
                {
                    float bottom = row * GridCellHeight;
                    float top = Mathf.Min(bottom + GridCellHeight, GateHeight);
                    float upCentre = (bottom + top) / 2f;

                    // Chamfered like every other panel in the building, and that consistency is the
                    // reason rather than the look on its own: a gate leaf is meant to read as a
                    // piece of wall until it moves, and a leaf whose panels had sharp rims beside a
                    // wall whose panels did not would announce every door in the game.
                    ChamferedPanel($"Panel_{row}", leafRoot.transform,
                        rightDir * (cellCentre - openCentre)
                            + Vector3.up * (upCentre - GateHeight / 2f)
                            - inward * (GrooveDepth / 2f),
                        leafWidth - groove, top - bottom - groove, GrooveDepth, inward, panelMat);
                }

                // THE BACKING, which is what a groove has at the bottom of it everywhere else in the
                // building. Full cell, no inset, so every slot around and between this leaf's panels
                // has the same near-black behind it that the rest of the wall does.
                Prim(PrimitiveType.Cube, "Backing", leafRoot.transform,
                    rightDir * (cellCentre - openCentre)
                        - inward * (GrooveDepth + leafBackingDepth / 2f),
                    widthAxis * leafWidth + Vector3.up * GateHeight + depthAxis * leafBackingDepth,
                    grooveMat, removeCollider: true);

                // COLLISION IS ONE BOX, THICKER THAN THE PANELS ARE. The opening is cut out of both
                // rooms' wall collision, so a shut gate is the only thing between them - and a 25mm
                // collider is a thin thing to trust a CharacterController against. Built without the
                // groove inset too, so there is no 50mm slot to squeeze through at the seam.
                GameObject block = new GameObject("Solid");
                block.transform.SetParent(leafRoot.transform, false);
                block.transform.localPosition = rightDir * (cellCentre - openCentre)
                                              - inward * ((GrooveDepth + leafBackingDepth) / 2f);
                BoxCollider blockCollider = block.AddComponent<BoxCollider>();
                // As deep as the leaf assembly rather than a door slab's 0.06, so the two sides'
                // colliders do not overlap in the cavity. There are two of them in series anyway, and
                // `CharacterController.Move` sweeps rather than teleports, so neither can be tunnelled.
                blockCollider.size = widthAxis * leafWidth + Vector3.up * GateHeight
                                   + depthAxis * (GrooveDepth + leafBackingDepth);

                // Baked into the probes despite moving, because shut it is wall - see WallWhileShut.
                leafRoot.AddComponent<WallWhileShut>();

                Door door = leafRoot.AddComponent<Door>();
                door.doorPanel = leafRoot.transform;
                door.pushInOffset = outward * pushDistance;
                door.openLocalOffset = rightDir * (direction * leafTravel);
                door.requiredFloorButtons = pads;
                door.openDuration = 1.4f;
                // **SLOW OPEN, SNAP SHUT, and it is the same asymmetry the corridor block has.** The
                // gate is not being closed, it is being let go of: the pad is not held, so the wall
                // is a wall again. 0.3s is also short enough that the 1.18m from an east or west pad
                // cannot be crossed in time, which is what makes "nobody goes through their own pad"
                // true rather than merely intended - see `Door.closeDuration`.
                door.closeDuration = 0.3f;
                // AND IT DOES NOT WAIT FOR ANYONE. See `Door.standsOffForPlayer`: everywhere else in
                // the building a door refuses to shut on the player, and here that reprieve was a way
                // through your own gate and a way to hold it open by loitering in it.
                door.standsOffForPlayer = false;
                door.audioSource = audio;
                // One clip for the pair - two copies of the same slide half a frame apart is a flam.
                door.openClip = i == 0 ? openClip : null;
                door.doorwayClearance = (openMax - openMin) / 2f + 0.6f;
                doors[i] = door;
            }

            // THE CAVITY LINER. Without it the 0.1m gap between the two rooms' walls is open to the
            // skybox at both ends - the fault `BuildDoorPocketFill` fixes for the ordinary doors. It
            // fills everywhere the leaves do NOT go; where they park it has to stay clear.
            if (!withCavityLiner) return doors;

            float clearMin = leaves == 2 ? openMin - leafTravel : openMin;
            float clearMax = openMax + leafTravel;
            Rect cavity = Rect.MinMaxRect(-wallWidth / 2f - WallDepth, 0f,
                                           wallWidth / 2f + WallDepth, RoomHeight);
            Rect swept = Rect.MinMaxRect(clearMin, 0f, clearMax, GateHeight);

            GameObject liner = new GameObject(name + "_CavityLiner");
            liner.transform.SetParent(root.transform, false);
            int part = 0;
            foreach (Rect piece in SubtractRect(cavity, swept))
            {
                if (piece.width <= 0.001f || piece.height <= 0.001f) continue;
                Prim(PrimitiveType.Cube, $"Fill_{part++}", liner.transform,
                    wallCentreAtBase + rightDir * piece.center.x + Vector3.up * piece.center.y
                        + outward * (WallDepth + DoorPocketDepth / 2f),
                    widthAxis * piece.width + Vector3.up * piece.height
                        + depthAxis * DoorPocketDepth,
                    grooveMat, removeCollider: true);
            }

            return doors;
        }

        // THE CORRIDOR NORTH OUT OF ROOM3-1, AND THE SLAB THAT COMES DOWN IN IT.
        //
        // Not a gate. The other three walls of room3-1 open as panels and are meant not to be found;
        // this one is a hole you can see down, with a mechanism in it you are meant to see coming.
        // Both walls it passes through keep the gate-sized cutout and neither gets leaves - the
        // barrier partway along is the only thing that ever blocks the way.
        //
        // **THE CORRIDOR IS ONE CELL WIDE**, the same 1.75 the north wall's gate span is, so its mouth
        // lands on the wall grid exactly and there are no sliver panels beside it. Twenty-two metres
        // of it at that width is a tube, which is the intention: it is somewhere you commit to.
        //
        // The pad that holds the slab up is in room3-1, at the near end. **One person cannot hold it
        // and walk this**, which is the whole of the room - and unlike the gates, being wrong about it
        // is not merely inconvenient. See `CrushingBarrier`.
        // A ROOM THAT IS NOT THE STANDARD SHELL. Room3-2N is twice as wide, twice as deep and THREE
        // times as tall as every other room in the building - the climbing puzzle needs the height and
        // the panel stairs need the floor.
        //
        // **THE GRID CELL DOES NOT CHANGE, ONLY THE COUNT.** 17.5 across is ten cells of 1.75 where a
        // normal wall is five, 21 deep is twelve where a normal one is six, and 16.2234 tall is twelve
        // rows of 1.3519 where a normal wall is four. Every number is an exact multiple, so the
        // panelling reads as the same wall continued rather than as a different wall - which is the
        // whole reason the size is a multiple in the first place.
        //
        // Written as its own builder rather than as parameters on `BuildEmptyRoom`, because that one
        // is not merely sized by constants - `BuildSlab` lays its floor across a whole `RoomPitch` so
        // neighbouring rooms meet under their shared divider, which is a rule about the CHAIN and not
        // about this room. Threading a size through it would put a room-sized hole in that rule.
        private static Transform BuildBigRoom(Transform parent, string name, Vector3 at,
                                              float width, float depth, float height,
                                              Material floorMat, Material grooveMat,
                                              Material panelMat, Material fixtureMat,
                                              Rect southCutout, Rect northCutout,
                                              Rect floorHole, Rect ceilingHole,
                                              // THE EAST WALL CAN BE HOLED TOO, since 2026-08-31:
                                              // room3-2N's is where the cable car comes through at
                                              // the end of the game. Defaulted so the one caller
                                              // that does not want one says nothing.
                                              Rect eastCutout = default)
        {
            GameObject rootGO = new GameObject(name + "_Root");
            rootGO.transform.SetParent(parent, false);
            rootGO.transform.localPosition = at;

            GameObject roomGO = new GameObject(name);
            roomGO.transform.SetParent(rootGO.transform, false);
            Transform t = roomGO.transform;

            // Overrunning the interior by the wall build-up at every edge, the same reason
            // `BuildPanelWall` overruns its structure: two slabs that merely abut leave a hairline the
            // grazing angles find.
            float slabX = width + 2f * WallDepth;
            float slabZ = depth + 2f * WallDepth;

            // ~~BUILT IN PIECES~~ ONE SLAB AGAIN. It was cut into rectangles for a while so that
            // coloured blocks could come up through it; those went with the levers that drove them
            // (2026-08-21), and a floor with nothing passing through it should be one surface with no
            // seams in it to find.
            // **HOLED, BOTH OF THEM, SINCE 2026-08-30.** The floor opens onto cycle 4 - a whole
            // room's worth, not a hatch - and the ceiling carries the ladder shaft up to room3-0.
            // `BuildSlabRect` subtracts the rect and lays the remainder as pieces, which is what
            // every other holed slab in the building already does.
            Rect slab = Rect.MinMaxRect(-slabX / 2f, -slabZ / 2f, slabX / 2f, slabZ / 2f);
            BuildSlabRect(t, "Floor", -WallThickness / 2f, slab, floorMat, floorHole);
            BuildSlabRect(t, "Ceiling", height + WallThickness / 2f, slab, CeilingMaterial(),
                          ceilingHole);

            // Whole walls, unholed. They carried a hole per coloured step until 2026-08-21; the
            // `panelHoles` argument stays on `BuildPanelWall` because gates still want it.
            // **THE NORTH WALL'S OPENING IS NOT AT FLOOR LEVEL**, and it is the only one in the
            // building that is not: it is the way onto deck B, two storeys up. `BuildPanelWall` cuts
            // a rect in the wall's own frame with y measured from the room's floor, so the height
            // costs nothing here - but nothing else has ever asked it for one, which is why
            // `AssertWalkable` is pointed at it in `BuildCycleThreeShell`.
            BuildPanelWall(t, "Wall_North", new Vector3(0f, 0f, depth / 2f),
                Vector3.right, Vector3.back, width, grooveMat, panelMat, northCutout, height);
            BuildPanelWall(t, "Wall_South", new Vector3(0f, 0f, -depth / 2f),
                Vector3.right, Vector3.forward, width, grooveMat, panelMat, southCutout, height);
            BuildPanelWall(t, "Wall_West", new Vector3(-width / 2f, 0f, 0f),
                Vector3.forward, Vector3.right, depth, grooveMat, panelMat, Rect.zero, height);
            BuildPanelWall(t, "Wall_East", new Vector3(width / 2f, 0f, 0f),
                Vector3.forward, Vector3.left, depth, grooveMat, panelMat, eastCutout, height);

            BuildTallRoomLights(t, name, width, depth, height, fixtureMat);
            BuildMezzanines(t, width, depth, floorMat, panelMat);

            // Sized to the room rather than to `RoomWidth`/`RoomDepth`, and centred at half its own
            // height. A probe left at the standard size would capture a box a fraction of this one and
            // reflect it onto every surface in here.
            BuildReflectionProbe(t, name, 0f, sizeOverride: new Vector3(width, height, depth),
                                 yCenter: height * 0.5f);

            return t;
        }

        // LIGHTING A ROOM THREE STOREYS TALL, which the standard fixture grid cannot do.
        //
        // `BuildCeilingLights` puts four spots at fixed offsets with `range` 11 and `intensity` 10.5,
        // derived for a 5.4m ceiling. At 16.2m the range does not even REACH the floor, and inverse
        // square says the floor would get a ninth of the light if it did. So this scales both: the
        // grid spreads with the room, the range covers the diagonal, and the intensity goes up by the
        // square of the height ratio - which is the same derivation the 10.5 itself came from.
        //
        // **These numbers are derived, not seen.** The 10.5 was found by eye and then re-derived once
        // when the ceiling moved; this re-derives it again over a much bigger jump, and a jump that
        // big is exactly where an inverse-square approximation stops being one. Expect to retune.
        private static void BuildTallRoomLights(Transform parent, string roomName,
                                                float width, float depth, float height,
                                                Material emissiveMat)
        {
            GameObject root = new GameObject(roomName + "_CeilingLights");
            root.transform.SetParent(parent, false);

            // **BACK TO THE STANDARD 2x2, FROM 3x3** (2026-08-28, by request: the decks were too
            // bright).
            //
            // The nine were justified by a thing that no longer exists - "this is a room the player
            // has to read a route across from the far end of a CCTV feed" - and the feeds were deleted
            // with the staircase they were watching. What is left is a room with two decks in it, read
            // by standing on them.
            //
            // **THE COUNT IS NOT THE WHOLE OF WHY IT WAS BRIGHT UP THERE, and this is only the half
            // that was asked for.** The intensity below is multiplied by the SQUARE of the height
            // ratio - nine times - to get light down to a floor three storeys away. Deck B is 5.4m
            // under the ceiling, which is an ordinary room's height, so a fixture over it runs nine
            // times brighter than the number this building was tuned at. Halving the count takes out
            // the fill between the cones; it does not touch what is directly overhead. `TODO.md`.
            float[] xs = { -width / 4f, width / 4f };
            float[] zs = { -depth / 4f, depth / 4f };

            const float panelSize = 1.4f, panelThickness = 0.04f;
            float heightRatio = height / RoomHeight;

            int index = 0;
            foreach (float x in xs)
                foreach (float z in zs)
                {
                    GameObject fixture = new GameObject($"Fixture_{index++}");
                    fixture.transform.SetParent(root.transform, false);
                    fixture.transform.localPosition = new Vector3(x, height, z);

                    Prim(PrimitiveType.Cube, "Panel", fixture.transform,
                        new Vector3(0f, -panelThickness / 2f, 0f),
                        new Vector3(panelSize, panelThickness, panelSize),
                        emissiveMat, removeCollider: true);

                    GameObject lightGO = new GameObject("Light");
                    lightGO.transform.SetParent(fixture.transform, false);
                    lightGO.transform.localPosition = new Vector3(0f, -panelThickness, 0f);
                    lightGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                    Light light = lightGO.AddComponent<Light>();
                    light.type = LightType.Spot;
                    light.spotAngle = CeilingSpotAngle;
                    light.innerSpotAngle = 45f;
                    // Far enough to reach the floor with the cone's edge, not just its axis. Taken
                    // from the fixtures' own spacing, so it followed the grid from 3x3 to 2x2 without
                    // having to be retuned.
                    light.range = Mathf.Sqrt(height * height + (width / 4f) * (width / 4f)) * 1.15f;
                    // The same inverse-square move that took 9 to 10.5 when the ceiling rose 8%.
                    light.intensity = CeilingLightIntensity * heightRatio * heightRatio;
                    light.color = new Color(0.99f, 0.99f, 1f);
                    // NO SHADOWS AT ALL in here. Every additional light's shadow shares one atlas, and
                    // several casters in one room would take the whole of it from the rest of the cycle.
                    light.shadows = LightShadows.None;
                    light.renderMode = LightRenderMode.ForcePixel;
                }
        }
    }
}
