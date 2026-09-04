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
    // CYCLE 3'S LIGHT AND WHAT IT DRIVES: the mirrors and their rack, the emitter, the call
    // conduit, the beam lift, the receivers, and the north corridor the beam crosses - plus the
    // floor buttons and pad doors the rest of the building runs on.
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {

        // **~~The four spots under the decks~~ GONE 2026-08-28, by request.** They were four
        // fixtures at a flat intensity of 6, on the reasoning that "a deck is a ceiling for whatever
        // is under it". True, and they were adding to the same complaint the 3x3 overhead grid was:
        // the upper storeys were too bright. A deck's underside reading as shade is what tells you
        // there is a floor over your head, and this room's problem was never too little light.
        //
        // ==================================================== CYCLE 3'S BEAM, AND THE MIRRORS THAT BEND IT
        //
        // A laser leaves room3-2E and has to arrive in room3-2N. Nothing between the two is a switch:
        // the light is bent by mirrors, a mirror only works while somebody is holding it, and nobody
        // can hold two (CLAUDE.md §4). **One mirror is one past self.**
        //
        // Why it is this and not the coloured levers it replaced: a lever REMEMBERS a decision, so a
        // past self who threw one wrongly goes on throwing it wrongly for the rest of the cycle, and
        // there is no way to make a remembered decision both meaningful and harmless. A mirror
        // remembers nothing - a ghost holding one is a body in a place, which `RecordedFrame` already
        // stores exactly. The full argument is in `Mirror`.
        //
        // Everything here lives in ONE HORIZONTAL PLANE at `BeamHeight`, and that is forced by the
        // recording format rather than chosen for looks: a timeline has `position`, `yaw` and
        // `signals` in it and no pitch anywhere, so a mirror that could tilt would replay at the wrong
        // angle in a ghost's hands.

        private const string MirrorModel = FurnitureDir + "/mirror_trensum.glb";

        // A SUPPLY of five, sharing one id, exactly as the three pins share `"Tool"`. A recorded "took
        // a Mirror" has to be satisfiable by whichever one is going spare, and `CarryEvent`'s
        // `instanceName` is what keeps track of WHICH - see CLAUDE.md §1.4.
        // The FIRST pane's name, and the stem the rest are numbered off. Each mirror's id is its own
        // object name now - see the assignment in `BuildMirror`.
        private const string MirrorItemId = "Mirror";

        // THE ONE PANE THAT LEAVES THE PLANE. Named rather than numbered because it is not one of the
        // rack's five and must never be mistaken for one - by a reader or by a ghost, which is why it
        // carries its own id.
        // THE SOCKET, IN TWO SIZES, AND THEY DO DIFFERENT JOBS.
        //
        // `SocketFace` is the grey disc: what the player FINDS, from the far end of a room seventeen
        // metres across and three storeys tall. It wants to be big.
        //
        // `SocketBore` is the notch cut in the middle of it: what the player AIMS AT. It wants to be
        // small, because a hole only reads as drilled while it is much smaller than the plate it is
        // drilled in - at 0.34 against a 0.44 collar the two were nearly the same circle, which is
        // why the last version read as a dark disc rather than as a hole.
        //
        // **NEITHER IS `LaserReceiver.radius`, and that is deliberate.** The hit is judged on 0.7m,
        // which is generous on purpose: a mirror is aimed by turning your body and the law of
        // reflection doubles every wobble, so at twenty metres one degree of hand is 70cm of travel
        // and a target measured in centimetres is one nobody could hold. **The cost is that a beam can
        // land visibly outside the notch and still count** - worth knowing before anybody "fixes" it
        // by tightening the tolerance to match the picture. `TODO.md`.
        private const float SocketFace = 1.15f;
        private const float SocketBore = 0.20f;
        // How deep the bore is. Deep relative to its own WIDTH is what reads as a hole - at 0.20
        // across, 0.13 down is two thirds of a diameter, so the wall of the bore is visible from any
        // angle a player approaches it at rather than only dead ahead.
        private const float SocketDepth = 0.13f;

        // **~~THE RISER PANE AND ITS 45 DEGREES~~ GONE 2026-08-30**, with the ceiling call two days
        // before it. What it did is worth keeping written down, because the arithmetic is the kind
        // that gets rediscovered: a pane tilted up by t turns a level beam through 2t, so 45 and only
        // 45 sends a level beam straight up. If anything ever wants a beam to leave this plane again,
        // that is the number and `Mirror.facePitch` is the field.
        private const int MirrorCount = 5;

        // Chest height. Under the 2.7038 gate opening by a mile, so the beam passes through every gate
        // in the cycle, and above the bed in room3-1 so it crosses that room without being interrupted
        // by the furniture. One constant: the emitter, the receiver and every mirror take it from
        // here, so the beam cannot miss a mirror by being on a different plane from it.
        private const float BeamHeight = 1.2f;

        // Where along its own wall the emitter sits. Centred.
        //
        // **THE EMITTER IS ON ROOM3-2E'S SOUTH WALL AND FIRES NORTH** (2026-08-22, by request; it was
        // on the east wall firing west). That is not a cosmetic move - it changes the puzzle, and for
        // the better:
        //
        // - **The first mirror now lives in room3-2E.** Firing north, the light runs up its own room
        //   and stops at the far wall; getting it out through the gate, which is in the WEST wall,
        //   takes a turn made inside this room. The route was two mirrors long with five on the floor.
        //   It is three now, and room3-2E stops being a wall with a gun on it.
        // - **The lane through room3-1 becomes the player's choice** rather than a constant here. The
        //   west-bound leg can leave at any z the gate's opening allows - the middle PAIR of cells,
        //   so ±1.75 of the wall's centre.
        //
        // **And that choice is narrower than it looks, because of the bed.** The beam clears it on its
        // own (0.89 tall against a 1.2 lane), but the corner mirror in room3-1 has to be STOOD at, on
        // the corridor's axis, and a person holding a mirror half a metre in front of them needs rather
        // more room than a beam. The bed is 1.26 x 2.05 and sits 0.70 NORTH of centre, so it reaches
        // z +1.72 and eats the northern half of the band outright. What is left is roughly z -1.75 to
        // -0.7: about a metre of usable lane, on the south side, with the bed visibly explaining why.
        // Both figures are read off the build (`Cycle 3 bed:` in the log) rather than off a plan.
        private const float BeamLane = 0f;

        // ONE MIRROR, scattered on a floor or held in both hands.
        //
        // **EVERYTHING ABOUT THIS MODEL IS MEASURED, AND EACH THING THAT WAS ASSUMED INSTEAD WAS
        // WRONG IN PLAY.** `mirror_trensum.glb` is an IKEA clamp-on vanity mirror converted from an
        // FBX. Three separate traps, in the order they were found:
        //
        // 1. **It is Z-UP**, not Y-up - foot at z 0, head at z 0.229.
        // 2. **The glass is not on a model axis.** The head is TILTED on its clamp, the way a
        //    dressing-table mirror is, so assuming the silvered normal was the model's +Y left every
        //    mirror in the room a few degrees turned. The give-away was in the dump from the start:
        //    the pane's own bounds are (0.165, 0.0437, 0.1596), and 44mm is far too thick for a sheet
        //    of glass unless it is lying at an angle inside its own box. About 15 degrees of it.
        // 3. **A pane's vertex normals do not say which side of the frame it is on.** Averaging them
        //    gives the axis; the SIGN has to come from somewhere else, and getting it backwards
        //    silvered the side facing the holder. Play reported it exactly that way - "the front of
        //    the mirror is looking at me". The other pane settles it: the two are back to back, so
        //    the vector from one to the other IS the first one's outward direction.
        //
        // And then the tilt has to be taken OUT rather than lived with, because it pulls two ways at
        // once: the optics need the glass vertical (the beam is horizontal), and the eye needs the
        // stand vertical (it is a thing standing on a floor). Aligning either one leans the other by
        // fifteen degrees, which is what "make it stand up straight" was about. **So the head is
        // rotated back on its own clamp** - which is what the clamp is for - and both come out
        // upright.
        private static CarryableItem BuildMirror(Transform parent, string name, Vector3 floorAt,
                                                 float yaw, Sprite icon, float facePitch = 0f)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = floorAt;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            // The face carries everything visible and is what `Mirror` re-aims every frame off the
            // HOLDER. The root follows a hand - a bone, for a ghost - and must not be believed.
            GameObject face = new GameObject("Face");
            face.transform.SetParent(root.transform, false);

            (GameObject model, Bounds bounds) = PlaceModelLocal(
                MirrorModel, face.transform, "Model", Vector3.zero, Quaternion.identity, MirrorHeight);
            if (model == null) return null;

            Renderer front = null, back = null, clamp = null;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.StartsWith("CLAMP")) clamp = r;
                if (!r.name.StartsWith("MIRROR")) continue;
                if (front == null) front = r;
                else if (back == null) back = r;
            }

            if (front == null || back == null)
            {
                Debug.LogError($"[SceneBuilder] {name}: expected two `MIRROR` meshes in {MirrorModel}, "
                             + "found " + (front == null ? "none" : "one")
                             + ". Run Iteration Room/Dump Furniture Models.");
                return null;
            }

            MeshFilter frontMesh = front.GetComponent<MeshFilter>();
            if (frontMesh == null || frontMesh.sharedMesh == null)
            {
                Debug.LogError($"[SceneBuilder] {name}: the `MIRROR` mesh has no geometry.");
                return null;
            }

            // WHICH WAY THE GLASS POINTS - **the THIN AXIS of the mesh's own bounds**, sign from the
            // other pane.
            //
            // **It was the average of the vertex normals, and that was wrong for a reason worth
            // keeping**: a pane modelled as a SOLID - front, back and a rim - has a normal for every
            // direction and they cancel, so the average is a near-zero vector pointing nowhere in
            // particular. The magnitude check passed anyway (it is not exactly zero) and the direction
            // was noise, which is why every mirror in the room came out at some arbitrary angle. Play
            // called it "the disc looks like it can rotate", which is precisely what a garbage
            // orientation looks like.
            //
            // A flat disc has one short axis and it is the normal. That is a fact about the shape
            // rather than about how somebody chose to weld the mesh.
            Vector3 ms = frontMesh.sharedMesh.bounds.size;
            Vector3 thin = ms.x <= ms.y && ms.x <= ms.z ? Vector3.right
                         : ms.y <= ms.z ? Vector3.up : Vector3.forward;

            Vector3 glassNormal = face.transform.InverseTransformDirection(
                front.transform.TransformDirection(thin)).normalized;

            // The two panes are back to back, so the vector from one to the other IS the first one's
            // outward direction. Anywhere a sign has to be chosen, find a second feature that decides
            // it rather than picking and waiting to be told.
            Vector3 outward = face.transform.InverseTransformDirection(
                front.bounds.center - back.bounds.center);
            if (Vector3.Dot(outward, glassNormal) < 0f) glassNormal = -glassNormal;

            // The glass is round, so its largest local dimension IS its diameter.
            float radius = 0.5f * Mathf.Max(ms.x, Mathf.Max(ms.y, ms.z)) * front.transform.lossyScale.x;

            // STAND IT UP FIRST. The model's own up - its +Z, which is what the FBX conversion left
            // behind - becomes +Y, and the HORIZONTAL part of the glass normal becomes +Z. Written as
            // which axis becomes which rather than as three numbers, because the numbers would be a
            // lie the moment the model is replaced.
            Vector3 modelUp = Vector3.forward;
            Vector3 flatNormal = Vector3.ProjectOnPlane(glassNormal, modelUp);
            if (flatNormal.sqrMagnitude < 1e-6f) flatNormal = Vector3.right;
            model.transform.localRotation =
                Quaternion.Inverse(Quaternion.LookRotation(flatNormal.normalized, modelUp));

            // THEN TAKE THE TILT OUT OF THE HEAD, on the clamp it tilts on. What turns with the glass
            // is the frame and both panes; the arm, the stem and the foot stay where they are, which
            // is what keeps the thing standing on its base.
            //
            // **THEY ARE TURNED ONE BY ONE ABOUT A SHARED POINT, NOT REPARENTED UNDER A COMMON PIVOT,
            // and the first version did the second and silently did nothing.** A model placed with
            // `PrefabUtility.InstantiatePrefab` is a prefab INSTANCE, and Unity refuses to restructure
            // one - `SetParent` across it is dropped without an exception. The build cheerfully
            // reported "levelled on the clamp: yes" for a pivot that had collected zero nodes, which
            // is what a log that measures the intention instead of the result is worth.
            //
            // Rotating each node about the same world point by the same amount is exactly what a
            // common parent would have done, and needs no restructuring at all.
            var tilting = new System.Collections.Generic.List<Transform>();
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                if (!(t.name.StartsWith("FRAME") || t.name.StartsWith("MIRROR"))) continue;
                // Only the topmost of each - a pane's mesh child carries the same name as its node.
                if (t.parent != null && (t.parent.name.StartsWith("FRAME")
                                         || t.parent.name.StartsWith("MIRROR"))) continue;
                tilting.Add(t);
            }

            Vector3 clampAt = clamp != null ? clamp.bounds.center : front.bounds.center;

            // Re-measured after the stand went upright, because that is the angle that is left.
            // `FromToRotation` rather than an euler: it carries its own sign, and a sign written by
            // hand here is a coin toss that only shows up in a screenshot.
            Vector3 nNow = face.transform.InverseTransformDirection(
                front.transform.TransformDirection(thin)).normalized;
            if (Vector3.Dot(nNow, Vector3.forward) < 0f) nNow = -nNow;

            float tilt = Mathf.Asin(Mathf.Clamp(nNow.y, -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 nLevel = new Vector3(nNow.x, 0f, nNow.z);
            if (tilting.Count > 0 && nLevel.sqrMagnitude > 1e-6f)
            {
                Quaternion level = Quaternion.FromToRotation(nNow, nLevel.normalized);
                level.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 0.01f && angle < 359.99f)
                {
                    Vector3 axisWorld = face.transform.TransformDirection(axis);
                    foreach (Transform t in tilting) t.RotateAround(clampAt, axisWorld, angle);
                }
            }

            // **MEASURED AFTER, not before.** The line under this used to print the angle the stand-up
            // left behind and stop there, which says nothing at all about whether the correction
            // worked - and it did not, for a while, with the log cheerfully reporting the fault it
            // was supposed to catch.
            Vector3 after = face.transform.InverseTransformDirection(
                front.transform.TransformDirection(thin)).normalized;
            if (Vector3.Dot(after, Vector3.forward) < 0f) after = -after;
            float residual = Mathf.Asin(Mathf.Clamp(after.y, -1f, 1f)) * Mathf.Rad2Deg;

            if (name.EndsWith("Mirror"))
                Debug.Log($"[SceneBuilder] {name}: glass thin axis {thin}, stand-up left the disc "
                        + $"{tilt:0.##}° off vertical, {tilting.Count} node(s) turned on the clamp, "
                        + $"**residual {residual:0.##}°**.");

            // THE DISC'S CENTRE BECOMES THE OBJECT'S ORIGIN, re-measured after both turns. Everything
            // downstream - the carry pose, `Mirror.Centre`, the beam, the prompt - then talks about the
            // glass rather than about a pivot that happens to be under a foot.
            Vector3 centreInFace = face.transform.InverseTransformPoint(front.bounds.center);
            model.transform.localPosition -= centreInFace;

            // **AND THE OBJECT NOW STANDS ON THE FLOOR.** `CarryableItem.floorY` is how far the ORIGIN
            // rests above the floor, and this origin is the middle of the glass - so it is the height
            // of the glass above the mirror's own foot, measured rather than guessed. Without it a
            // dropped mirror sank to its waist.
            bounds = MeasuredBounds(model);
            float glassAboveFoot = root.transform.position.y - bounds.min.y;
            root.transform.localPosition += Vector3.up * glassAboveFoot;

            // **THE BACK IS BLANKED, and that is the single-sided rule made visible.** The model ships
            // with glass on both sides; a beam that arrives at the back of this one stops dead, so the
            // two faces must not look alike or the rule is a trick.
            back.sharedMaterial = MakeColorMaterial("MirrorBack", new Color(0.13f, 0.13f, 0.15f));

            BoxCollider reach = root.AddComponent<BoxCollider>();
            reach.isTrigger = true;
            reach.size = Vector3.one * 1.1f;

            CarryableItem item = root.AddComponent<CarryableItem>();
            // ONE ID PER PANE. Five mirrors sharing one id made a SUPPLY, and a ghost replaying
            // "took a Mirror" would then take whichever was going spare - which for a puzzle made of
            // people standing in a line is the route quietly rearranging itself. It matters more now
            // that the panes are not interchangeable: one of them sends the beam to the ceiling, and
            // a ghost handed THAT one where a flat one was recorded breaks the chain. Same reasoning
            // as the pool props; see `BuildFloatingProps`.
            item.itemId = name;
            item.displayName = "MIRROR";
            // `Mirror.Sync` puts the glass in front of the holder's BODY, not at the hand anchor - so
            // `HeldItemClearance` must not freeze the view on a contact measured at the anchor. See
            // `CarryableItem.posesItself`.
            item.posesItself = true;
            item.icon = icon;
            item.floorY = glassAboveFoot;
            // **THE PROMPT HANGS OFF THE GLASS, NOT OFF THE ROOT, and that is how you take one off a
            // past self.** A carried item is parented to its holder's hand - a wrist bone, for a ghost
            // - while `Mirror` puts the glass out in front of their body. Left on the root, the aim
            // test and the prompt disc sat at the wrist and the thing you were looking at was a metre
            // away: play found it as "taking the mirror off a ghost does not work". The reach volume
            // follows the same point, from `Mirror.Sync`.
            item.hintAnchor = face.transform;
            item.handLocalPosition = new Vector3(0f, -0.34f, 0.62f);
            item.restsUpright = true;
            item.audioSource = MakeSource(root.transform, "PickupAudio", 1f, 0.85f);
            item.pickupClip = LoadClip(SfxDir, "sfx_item_pickup");

            Mirror mirror = root.AddComponent<Mirror>();
            mirror.face = face.transform;
            mirror.radius = radius;
            mirror.frontRenderer = front;
            mirror.backRenderer = back;
            mirror.reach = reach;
            // ABOVE THE HOLDER'S FEET, not a world height. `BeamHeight` is how high the emitter
            // fires above its own floor, so a holder standing on any floor in the cycle presents the
            // glass exactly where a beam crossing that floor is - which on the ground floor is the
            // height this used to be pinned to, and on deck A is five metres higher.
            mirror.carryHeight = BeamHeight;
            mirror.facePitchDegrees = facePitch;

            // **THE DISC LEANS; THE STAND DOES NOT.**
            //
            // **TURNED ABOUT A SHARED POINT, NEVER REPARENTED - the same rule the stand-up correction
            // above had to learn, and the first version of this broke it and looked like something
            // else entirely.** `model` is a prefab INSTANCE, so `SetParent` across it is dropped
            // without an exception: the panes and the frame did not move an inch. What DID move was
            // the empty pivot they were supposed to hang under, and `Mirror.Normal` read that - so the
            // optics tilted 45 degrees while the object stood up straight, and the mirror rendered a
            // reflection of a plane it was not on. Play reported it as "the frame is not rotated, only
            // the mirror inside it is", which is exactly what a 45-degree reflection painted onto an
            // upright pane looks like.
            //
            // `tilting` is already the right set and was built for the same job one correction
            // earlier: the topmost `FRAME` and `MIRROR` nodes and nothing else. Measured off
            // `mirror_trensum.glb` rather than assumed, in model units:
            //   FRAME        y 0.117 - 0.252    the rim, 0.138 across
            //   MIRROR 2     y 0.119 - 0.248    glass
            //   MIRROR 2.001 y 0.122 - 0.251    glass, the other face
            //   C / CLAMP / FOOT / BASE / BOLT  y 0 - 0.198   the stand, which must NOT move
            //   ring         y 0.005            **NOT the rim** - a 24mm ring lying on the base plate
            //   IMG-1872     y 0.005            a flat label on the base
            // `ring` is the one a name would get wrong, which is why the bounds are written down.
            //
            // The turn happens about the GLASS CENTRE, which is `face`'s own origin by construction -
            // the model was slid to put it there. That point is within a millimetre of the clamp's
            // centre (0.184 against 0.185), so the head turns on the hinge a real one turns on.
            if (Mathf.Abs(facePitch) > 0.01f)
            {
                // An EMPTY marker, and it is only the optics. It carries no children - the visual
                // parts cannot be reparented under it - and exists so `Mirror.Normal` has a transform
                // to read that is driven by the same number as the geometry below.
                GameObject glassPivot = new GameObject("GlassTilt");
                glassPivot.transform.SetParent(face.transform, false);
                glassPivot.transform.localRotation = Quaternion.Euler(-facePitch, 0f, 0f);
                mirror.glass = glassPivot.transform;

                // And the geometry, turned to match. `-facePitch` about the face's own +X is the same
                // rotation the marker just took: both send the outward normal up by `facePitch`.
                Vector3 hinge = face.transform.position;
                Vector3 pitchAxis = face.transform.right;
                foreach (Transform t in tilting) t.RotateAround(hinge, pitchAxis, -facePitch);

                // **MEASURED OFF THE PANE, NOT COUNTED OFF THE LIST.** A log that says how many nodes
                // it meant to turn is what let the reparent ship; this reads the front pane's actual
                // world normal back and reports the angle it really makes with the horizontal, so a
                // rotation that did not happen prints 0.
                Vector3 paneNormal = front.transform.TransformDirection(thin).normalized;
                float panePitch = 90f - Vector3.Angle(paneNormal, Vector3.up);
                Debug.Log($"[SceneBuilder] {name}: head turned on the hinge - {tilting.Count} node(s), "
                        + $"pane now **{Mathf.Abs(panePitch):0.#}deg** off vertical against "
                        + $"{facePitch:0.#} asked for. Expects 3 nodes (two panes and the frame); the "
                        + "stand is not among them.");
            }

            BuildMirrorReflection(root.transform, mirror, front);

            return item;
        }

        // WHAT THE GLASS SHOWS. A camera standing behind the mirror looking back, and a shader that
        // samples what it drew by SCREEN position - see `MirrorReflection` for the four rules that
        // keep it affordable and `MirrorGlass.shader` for why screen space rather than UVs.
        private static void BuildMirrorReflection(Transform root, Mirror mirror, Renderer glass)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>($"{ShadersDir}/MirrorGlass.shader");
            if (shader == null)
            {
                Debug.LogError($"[SceneBuilder] MirrorGlass.shader missing from {ShadersDir} - "
                             + "the mirrors will show whatever the model shipped with.");
                return;
            }

            // **THE TINT IS WHAT ENDS THE TUNNEL**, and it is a value rather than a shader default
            // for the reason every number in this project is (CLAUDE.md §2). Slightly cool and a
            // quarter down from white: real glass loses about that much per bounce, and two mirrors
            // facing each other then fade out on their own instead of needing a cap.
            Material glassMat = MakeColorMaterial("MirrorGlass", new Color(0.76f, 0.78f, 0.82f));
            glassMat.shader = shader;
            glassMat.SetColor("_Tint", new Color(0.76f, 0.78f, 0.82f));
            glass.sharedMaterial = glassMat;

            GameObject rig = new GameObject("ReflectionCamera");
            rig.transform.SetParent(root, false);

            Camera cam = rig.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            // The building, not the horizon. Room3-2N is the deepest thing a mirror can be pointed
            // down and it is 21m.
            cam.farClipPlane = 40f;
            // The WHOLE player, never the headless one they are looking out of - the entire point of
            // a mirror here is seeing yourself. The shadow body goes too: the world body already casts
            // here, and both would put two shadows under one person.
            cam.cullingMask &= ~(1 << EnsureLayer(PlayerBodyViewLayer));
            cam.cullingMask &= ~(1 << EnsureLayer(PlayerBodyShadowLayer));

            // Same trim as the CCTV feeds, and for the same reason: a reflection of a white room does
            // not need the pipeline the game needs, and shadow maps were the expensive part.
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.antialiasing = AntialiasingMode.None;
            data.dithering = false;
            data.stopNaN = false;

            MirrorReflection reflection = root.gameObject.AddComponent<MirrorReflection>();
            reflection.mirror = mirror;
            reflection.glass = glass;
            reflection.reflectionCamera = cam;
        }

        // Big enough to be a thing carried in both hands and to be an easy target for a beam twenty
        // metres away. At this height the glass comes out about half a metre across.
        private const float MirrorHeight = 1.0f;

        // WHERE THE FIVE LIVE. **Scattered across room3-2W's floor** (2026-08-21, by request) rather
        // than racked in a row on a wall. A rack says "take one of these five"; five of them standing
        // about at odd angles says "somebody left these here", which is what every other room in this
        // building says about its contents.
        //
        // Placed clear of the walk in from the east gate, so getting into the room is not a matter of
        // stepping over the puzzle.
        private static CarryableItem[] BuildMirrorRack(Transform room, Material propMat)
        {
            GameObject rack = new GameObject("Mirrors");
            rack.transform.SetParent(room, false);

            var spots = new (float x, float z, float yaw)[]
            {
                (-2.6f,  3.7f,  40f),
                ( 1.6f,  4.3f, 205f),
                (-3.1f, -2.2f, 120f),
                ( 2.2f, -3.9f, 290f),
                (-0.4f,  0.9f, 165f),
            };

            // Drawn once and shared. `SaveSprite` writes a PNG and reimports it, so a glyph made
            // inside the loop is the same file written five times over.
            Sprite icon = MirrorIcon();

            var mirrors = new CarryableItem[MirrorCount];
            for (int i = 0; i < MirrorCount && i < spots.Length; i++)
                mirrors[i] = BuildMirror(rack.transform, i == 0 ? "Mirror" : $"Mirror_{i}",
                    new Vector3(spots[i].x, 0f, spots[i].z), spots[i].yaw, icon);

            return mirrors;
        }

        // THE SOURCE. On room3-2E's SOUTH wall, firing north up its own room - so the first mirror of
        // the route has to be held in here, and the way out through the west gate is a turn somebody
        // makes rather than a line somebody drew.
        private static LaserBeam BuildLaserEmitter(Transform room, Material propMat)
        {
            GameObject root = new GameObject("LaserEmitter");
            root.transform.SetParent(room, false);
            root.transform.localPosition = new Vector3(BeamLane, BeamHeight, -RoomDepth / 2f + 0.2f);
            // Local +Z points the way the beam goes: north, up its own room, at nothing in particular
            // until somebody stands in it with a mirror.
            root.transform.localRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

            // **DARK, AND THAT IS THE WHOLE OF IT** (simplified 2026-08-28, by request: it had got
            // "조잡" - cluttered).
            //
            // The one thing this object needed was to stop being white: built out of `propMat` it was
            // a pale box against a pale wall in a pale room, and the only part you could find was the
            // 6mm aperture. A dark housing fixes that completely.
            //
            // **Everything ELSE that was added along with the dark - heat-sink fins, a hazard band, a
            // recessed throat, a flare at the mouth - was solving a problem nobody had**, and three
            // of the four introduced one: the band shared a plane with the housing's front face and
            // the flare's radius exactly matched the barrel's, which is what play saw as a black
            // wedge across the muzzle. A box, a barrel and a lit aperture say "this fires something"
            // without any of that.
            Material shell = MakeColorMaterial("LaserShell", new Color(0.12f, 0.12f, 0.14f));
            SetSmoothness(shell, 0.55f);

            Prim(PrimitiveType.Cube, "Housing", root.transform, new Vector3(0f, 0f, -0.16f),
                new Vector3(0.42f, 0.42f, 0.44f), shell);
            Prim(PrimitiveType.Cylinder, "Barrel", root.transform, new Vector3(0f, 0f, 0.10f),
                new Vector3(0.13f, 0.09f, 0.13f), shell)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // The lit mouth. Past 1.0 on purpose: `ConfigureVolume` puts the bloom threshold at 1.2
            // precisely so this building's near-white walls do NOT bloom, so a thing that should has
            // to be given a value that clears it.
            Material glow = MakeEmissiveMaterial("LaserGlow", new Color(1f, 0.28f, 0.22f), 6f);
            Prim(PrimitiveType.Cylinder, "Aperture", root.transform, new Vector3(0f, 0f, 0.192f),
                new Vector3(0.075f, 0.006f, 0.075f), glow, removeCollider: true)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            GameObject muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(root.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, 0f, 0.2f);

            LaserBeam beam = root.AddComponent<LaserBeam>();
            beam.muzzle = muzzle.transform;

            // ONE LINE RENDERER PER POSSIBLE SEGMENT, built here rather than pooled at runtime so
            // the scene says what it contains. Unlit: a beam is a light source, not a lit surface.
            //
            // **ONE PLAIN RED LINE, and it went two passes and came back** (2026-08-28). A hot white
            // core inside a red halo is what a laser in air actually looks like, and in this room it
            // was worse: the thing has to be findable at a glance across seventeen metres of white,
            // and what makes it findable is being RED - which a white-cored beam is not, and a wide
            // soft one has an edge instead of a colour.
            Material beamMat = MakeColorMaterial("LaserBeamLine", new Color(1f, 0.22f, 0.16f));
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit != null) beamMat.shader = unlit;

            var lines = new LineRenderer[beam.maxBounces + 1];
            for (int i = 0; i < lines.Length; i++)
            {
                GameObject seg = new GameObject($"Segment_{i:00}");
                seg.transform.SetParent(root.transform, false);
                LineRenderer line = seg.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.widthMultiplier = 0.03f;
                line.numCapVertices = 2;
                line.sharedMaterial = beamMat;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.enabled = false;
                lines[i] = line;
            }
            beam.segments = lines;

            // WHERE IT LANDS. Two spheres for the same reason the beam is two lines: a hot point and
            // the scatter around it. This is the thing the whole aiming loop is watching move, so it
            // has to be findable across a room three storeys tall.
            GameObject dot = Prim(PrimitiveType.Sphere, "TerminalDot", root.transform,
                Vector3.zero, Vector3.one * 0.09f, glow, removeCollider: true);
            beam.terminalDot = dot.transform;

            beam.audioSource = MakeSource(root.transform, "LaserAudio", spatialBlend: 1f, volume: 0.6f);
            beam.hitClip = LoadClip(SfxDir, "sfx_switch_on");
            return beam;
        }

        // WHAT IT HAS TO REACH. On room3-2N's west wall for now, so the shortest route needs one turn
        // in room3-1 and one more inside the big room - two mirrors of the five.
        //
        // **WHAT IT DOES IS UNDECIDED, AND THAT IS DELIBERATE.** `LaserReceiver.Lit` is the whole
        // interface; whether the beam runs a machine or opens the way out of the cycle is a design
        // decision that has not been taken, and taking it in code before it is taken in play is what
        // put a CCTV system in this cycle before there was a puzzle for it to watch.
        // **A LINE PAINTED ON THE WALL AND THE FLOOR, FROM A CALL PLATE TO THE LIFT IT WORKS**
        // (2026-08-28, by request).
        //
        // Two plates on opposite walls and two lifts elsewhere in the room is four possible pairings
        // and no way to tell which is real - the only way to learn it was to fire the beam and see
        // what moved, which makes the room a guess rather than a puzzle. A line answers it before
        // anything is tried, and answers it from across the room.
        //
        // **PAINT, NOT CONDUIT.** It was a run of 75mm tube that lit up while its plate was live,
        // which is a fine idea and the wrong object: a pipe standing off a floor is a THING IN THE
        // ROOM, and the room already has enough of those - it read as another mechanism to work out
        // rather than as a label on the two that exist. Paint is flat, is obviously not machinery,
        // and is what a real plant marks a circuit with.
        //
        // **FLAT ENOUGH TO BE 2D, WITHOUT BEING A DECAL.** There is no decal system here and one is
        // not worth building for two lines, so these are boxes 8mm thick lying against the surface -
        // thin enough to have no visible side, offset just clear of it so nothing z-fights. The rule
        // this project keeps relearning is that a face laid EXACTLY on another face flickers, and the
        // offset is the whole of avoiding it.
        //
        // **ORTHOGONAL, IN THREE RUNS: down the wall, along X, along Z.** Not a straight line between
        // the two points - a stripe slanting across a floor reads as an annotation somebody drew,
        // where one that turns square corners reads as a marking that belongs to the building. It is
        // also easier to follow: the eye tracks a right angle and loses a diagonal among the wall
        // grid's own lines.
        private static void BuildCallConduit(Transform room, string name, Vector3 plateAt,
                                             Vector3 inward, Vector2 toXZ)
        {
            const float ink = 0.055f;      // how wide the line is painted
            const float leaf = 0.008f;     // how thick the paint is
            const float clear = 0.006f;    // how far it stands off the surface it is painted on

            GameObject root = new GameObject(name);
            root.transform.SetParent(room, false);

            Material paint = MakeColorMaterial("MarkingPaint", new Color(0.09f, 0.09f, 0.10f));
            SetSmoothness(paint, 0.18f);

            void Bar(string barName, Vector3 at, Vector3 size) =>
                Prim(PrimitiveType.Cube, barName, root.transform, at, size, paint,
                     removeCollider: true);

            // ON THE WALL: from just under the plate down to the floor. Placed off the PLATE rather
            // than off a wall coordinate - the plate is mounted a known distance proud of the wall it
            // is bolted to, so this cannot drift if the fixture moves.
            float wallX = plateAt.x - inward.x * (0.13f - clear);
            float wallZ = plateAt.z - inward.z * (0.13f - clear);
            float top = plateAt.y - 0.80f;

            Bar("Wall", new Vector3(wallX, (top + clear) / 2f, wallZ),
                new Vector3(Mathf.Abs(inward.x) > 0.5f ? leaf : ink, top - clear,
                            Mathf.Abs(inward.z) > 0.5f ? leaf : ink));

            // ON THE FLOOR: out from the wall, then along to the lift. The corner is where the two
            // runs overlap, which is also what stops a hairline gap showing at the turn.
            float fromX = wallX + inward.x * 0.02f;
            float fromZ = wallZ + inward.z * 0.02f;

            Bar("Floor_X", new Vector3((fromX + toXZ.x) / 2f, clear, fromZ),
                new Vector3(Mathf.Abs(toXZ.x - fromX) + ink, leaf, ink));
            Bar("Floor_Z", new Vector3(toXZ.x, clear, (fromZ + toXZ.y) / 2f),
                new Vector3(ink, leaf, Mathf.Abs(toXZ.y - fromZ) + ink));
        }

        // A DECK ON A SHAFT, PULLED DOWN BY THE BEAM AND RISING WHEN IT GOES. See `BeamLift` for why
        // it runs that way round rather than the obvious one.
        //
        // **WHERE IT STANDS IS DECIDED BY DECK A'S EDGE, not chosen.** Deck A is an L two cells deep
        // down the west wall, so its inner edge is at `-width/2 + 2 * GridCellWidth`; the shaft is
        // butted against that edge on the open side, and the panel at rest is flush with the deck's
        // surface. Anywhere else and the ride ends beside the deck rather than on it.
        // ONE STOREY OF LIFT. Both of room3-2N's are this, and the only things that differ are where
        // the shaft stands, how big the deck is, and which two surfaces it runs between - so they are
        // arguments and there is one description of what a lift IS.
        //
        // `lowerSurface` and `upperSurface` are the FLOORS it joins, not slab positions: the panel's
        // top face lands flush with each. Everything about the slab's own thickness is dealt with
        // here, so a caller never has to think about it.
        private static BeamLift BuildBeamLift(Transform room, string name, Vector2 shaftXZ, float pad,
                                              float lowerSurface, float upperSurface,
                                              Material propMat, Material grooveMat,
                                              float? shaftFootY, params LaserReceiver[] calls)
        {
            const float thickness = 0.24f;      // thick enough to read as a floor rather than a card

            GameObject root = new GameObject(name);
            root.transform.SetParent(room, false);
            root.transform.localPosition = new Vector3(shaftXZ.x, 0f, shaftXZ.y);

            GameObject panel = new GameObject("Deck");
            panel.transform.SetParent(root.transform, false);

            Prim(PrimitiveType.Cube, "Slab", panel.transform, Vector3.zero,
                new Vector3(pad, thickness, pad), propMat);
            // A dark rim, so the edge of a floor several metres up is visible from on top of it.
            // There is no parapet anywhere in this room by decision, which makes the edge itself the
            // only warning there is.
            //
            // **ALL FOUR SIDES** (2026-08-28, by request). The west band was left off because that is
            // the side butted against the deck, and a band there marks an edge you cannot fall off -
            // which was reasoning about the DECK'S geometry from inside a builder that knows nothing
            // about it, and is wrong for the second lift anyway. Three sides read as an unfinished
            // object rather than as a considered omission, which is what play reported.
            const float rim = 0.06f;
            void Rim(string rimName, Vector3 at, Vector3 size) =>
                Prim(PrimitiveType.Cube, rimName, panel.transform, at, size, grooveMat,
                    removeCollider: true);

            Rim("RimN", new Vector3(0f, thickness / 2f, pad / 2f - rim / 2f),
                new Vector3(pad, 0.02f, rim));
            Rim("RimS", new Vector3(0f, thickness / 2f, -pad / 2f + rim / 2f),
                new Vector3(pad, 0.02f, rim));
            Rim("RimE", new Vector3(pad / 2f - rim / 2f, thickness / 2f, 0f),
                new Vector3(rim, 0.02f, pad));
            Rim("RimW", new Vector3(-pad / 2f + rim / 2f, thickness / 2f, 0f),
                new Vector3(rim, 0.02f, pad));

            // **AND THE COLUMN THAT HOLDS IT UP.** Play reported the deck as floating - which it was,
            // a slab travelling through air with no mechanism under it, and that reads as something
            // broken rather than as a lift.
            //
            // Glass, for two reasons that pull the same way: a solid column standing in a room three
            // storeys tall would become the thing the eye goes to, and the whole point of deck B's
            // void is being able to look down through it. `BeamLift.StretchShaft` scales this to span
            // the floor and the deck's underside every frame, so it is measured off the lift rather
            // than animated beside it.
            //
            // Its collider stays: the column is a real object standing in the room, and a player at
            // the bottom of a raised shaft should meet it rather than walk through it.
            Material tube = MakeTranslucentMaterial("LiftShaftGlass",
                new Color(0.62f, 0.70f, 0.76f, 0.20f), 0.92f);
            GameObject shaft = Prim(PrimitiveType.Cylinder, "Shaft", root.transform,
                Vector3.zero, new Vector3(pad * 0.28f, 1f, pad * 0.28f), tube);

            // A collar at the foot, so the column lands ON something rather than stopping at a plane.
            float footY = shaftFootY ?? lowerSurface - LiftRestingStep;
            Prim(PrimitiveType.Cylinder, "ShaftCollar", root.transform,
                new Vector3(0f, footY + 0.06f, 0f),
                new Vector3(pad * 0.42f, 0.06f, pad * 0.42f), grooveMat, removeCollider: true);

            // WHO IS ON IT. Inset by a third of a metre all round, so somebody standing on the deck
            // beside the panel at either end is not dragged along with it; two metres tall, because
            // what is being asked is "are your feet on this", and a standing capsule's bounds are.
            GameObject rideGO = new GameObject("Rider");
            rideGO.transform.SetParent(panel.transform, false);
            BoxCollider ride = rideGO.AddComponent<BoxCollider>();
            ride.isTrigger = true;
            ride.center = new Vector3(0f, thickness / 2f + 1.0f, 0f);
            ride.size = new Vector3(pad - 0.6f, 2.0f, pad - 0.6f);

            BeamLift lift = root.AddComponent<BeamLift>();
            lift.panel = panel.transform;
            lift.calls = calls;
            lift.shaft = shaft.transform;
            // The floor the column stands on. Usually a resting step below where the deck rests -
            // but the second lift is asked to carry its column PAST the deck it starts from and down
            // to the ground, so the caller can say where the floor is.
            lift.shaftBaseY = shaftFootY ?? lowerSurface - LiftRestingStep;
            lift.PanelHalfThickness = thickness / 2f;
            // Half a slab under each surface, so the TOP FACE lands flush with the floor it is joining
            // at either end. Getting this off by the thickness is a step at the top and a lip at the
            // bottom, and both read as the lift being broken rather than as a number being wrong.
            lift.raisedY = upperSurface - thickness / 2f;
            lift.loweredY = lowerSurface - thickness / 2f;
            lift.speed = 2.2f;
            lift.rideVolume = ride;
            lift.audioSource = MakeSource(root.transform, "LiftAudio", spatialBlend: 1f, volume: 0.5f);
            // The same clip the crushing barrier moves on - the two are the same event, a slab
            // that travels, and this building has one sound for that.
            lift.moveClip = LoadClip(SfxDir, "sfx_door_open");

            // Starts where the loop will always put it back.
            lift.ResetLift();

            Debug.Log($"[SceneBuilder] Cycle 3 {name}: {pad:0.##}m deck at local "
                    + $"({shaftXZ.x:0.##}, {shaftXZ.y:0.##}), surfaces {lowerSurface:0.##} -> "
                    + $"{upperSurface:0.##} ({upperSurface - lowerSurface:0.##}m of travel) at "
                    + $"{lift.speed:0.#}m/s, {calls.Length} call(s) - any one lowers it.");
            return lift;
        }

        // **A CALL PLATE IN THE CEILING**, which is where one has to be if the riser pane is the only
        // thing that can reach it: a pane tilted 45 degrees turns a level beam through 90, and a
        // vertical beam lands on the ceiling. Face down, so `Covers` is asked about a point directly
        // under it.
        private static LaserReceiver BuildCeilingCall(Transform room, string name, Vector3 localPos,
                                                      Material propMat)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(room, false);
            root.transform.localPosition = localPos;

            // The same pale plate with a small bore drilled in it that the wall sockets have - one
            // kind of object, one look, whichever surface it is bolted to.
            Material bezel = MakeColorMaterial("LaserBezel", new Color(0.58f, 0.59f, 0.61f));
            SetSmoothness(bezel, 0.62f);

            Prim(PrimitiveType.Cube, "Plate", root.transform, new Vector3(0f, 0.06f, 0f),
                new Vector3(1.5f, 0.14f, 1.5f), bezel);
            MakeRing(root.transform, "Face", new Vector3(0f, -0.055f, 0f), Vector3.down,
                SocketFace / 2f, SocketBore / 2f, SocketDepth,
                MakeColorMaterial("LaserCollar", new Color(0.74f, 0.75f, 0.77f)));

            Prim(PrimitiveType.Cylinder, "BoreFloor", root.transform,
                new Vector3(0f, -0.055f + SocketDepth + 0.008f, 0f),
                new Vector3(SocketBore * 0.99f, 0.008f, SocketBore * 0.99f),
                MakeColorMaterial("LaserBore", new Color(0.05f, 0.05f, 0.055f)), removeCollider: true);

            Material lampMat = MakeEmissiveMaterial("LaserReceiverLamp", Color.white, 1f);
            GameObject lens = Prim(PrimitiveType.Cylinder, "Lens", root.transform,
                new Vector3(0f, -0.055f + SocketDepth - 0.004f, 0f),
                new Vector3(SocketBore * 0.80f, 0.006f, SocketBore * 0.80f), lampMat,
                removeCollider: true);

            LaserReceiver call = root.AddComponent<LaserReceiver>();
            call.lamp = lens.GetComponent<Renderer>();
            call.offColour = new Color(0.30f, 0.055f, 0.045f);
            call.radius = 0.7f;
            call.audioSource = MakeSource(root.transform, "ReceiverAudio",
                                          spatialBlend: 1f, volume: 0.8f);
            call.onClip = LoadClip(SfxDir, "sfx_switch_on");
            return call;
        }

        // A PLATE ON A WALL. `inward` is the way it faces, which is also the axis its body is thin
        // on - so the same builder makes a west plate and an east one without a second copy of it.
        private static LaserReceiver BuildLaserReceiver(Transform room, string name, Vector3 localPos,
                                                       Vector3 inward, Material propMat)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(room, false);
            root.transform.localPosition = localPos;
            // **A PLATE WITH A SMALL HOLE IN THE MIDDLE OF IT, AND THE BEAM GOES IN THE HOLE**
            // (2026-08-28, by request).
            //
            // This has now been three things. White on a white wall - invisible. Then a dark bezel
            // round a dark bore - "온통 검은색이여서 레이저를 넣는 구멍으로 안보여", all black, so it
            // does not read as somewhere you put a laser. Both failures are the same failure: **a
            // hole is read from the CONTRAST between its rim and its inside, and each version had
            // only one of the two.**
            //
            // So: a pale steel plate, and a small dark bore drilled in the centre of it. The plate is
            // what you find from across the room; the bore is what you aim at; and a hole a good deal
            // smaller than the plate is what makes it look drilled rather than painted on.
            Material bezel = MakeColorMaterial("LaserBezel", new Color(0.58f, 0.59f, 0.61f));
            SetSmoothness(bezel, 0.62f);

            Prim(PrimitiveType.Cube, "Plate", root.transform, -inward * 0.06f,
                new Vector3(0.14f, 1.5f, 1.5f), bezel);

            Quaternion facing = Quaternion.LookRotation(inward) * Quaternion.Euler(90f, 0f, 0f);

            // **THE GREY DISC, WITH A REAL HOLE IN IT.** A `Cylinder` primitive here is what made the
            // last version's notch invisible - a solid disc hides whatever is behind it, and no
            // arrangement of primitives puts a hole in one. `MakeRing` is an annulus with the bore's
            // wall included, so what you look into is geometry rather than a darker circle.
            GameObject face = MakeRing(root.transform, "Face", inward * 0.055f, inward,
                SocketFace / 2f, SocketBore / 2f, SocketDepth,
                MakeColorMaterial("LaserCollar", new Color(0.74f, 0.75f, 0.77f)));

            // THE BOTTOM OF THE BORE, seen down the hole. Dark, and deliberately not black: a
            // zero-value surface takes no light at all and reads as a hole cut in the SCREEN, with
            // nothing for the eye to place in space.
            GameObject well = Prim(PrimitiveType.Cylinder, "BoreFloor", root.transform,
                inward * (0.055f - SocketDepth - 0.008f),
                new Vector3(SocketBore * 0.99f, 0.008f, SocketBore * 0.99f),
                MakeColorMaterial("LaserBore", new Color(0.05f, 0.05f, 0.055f)), removeCollider: true);
            well.transform.localRotation = facing;

            // And the lens sitting on it, which is the part that lights.
            Material lampMat = MakeEmissiveMaterial("LaserReceiverLamp", Color.white, 1f);
            GameObject lamp = Prim(PrimitiveType.Cylinder, "Lens", root.transform,
                inward * (0.055f - SocketDepth + 0.004f),
                new Vector3(SocketBore * 0.80f, 0.006f, SocketBore * 0.80f), lampMat,
                removeCollider: true);
            lamp.transform.localRotation = facing;

            LaserReceiver receiver = root.AddComponent<LaserReceiver>();
            receiver.lamp = lamp.GetComponent<Renderer>();
            // A DARK RED LENS WHEN IT IS OFF, not a grey one. The default here is a neutral charcoal,
            // which inside a dark bore is indistinguishable from the bore - so the socket had no
            // pupil. A red glass that is merely unlit says both what the fixture is FOR and that it
            // is currently not receiving.
            receiver.offColour = new Color(0.30f, 0.055f, 0.045f);
            receiver.radius = 0.7f;
            receiver.audioSource = MakeSource(root.transform, "ReceiverAudio", spatialBlend: 1f, volume: 0.8f);
            receiver.onClip = LoadClip(SfxDir, "sfx_switch_on");
            return receiver;
        }

        private static CrushingBarrier BuildNorthCorridor(Transform parent, Material floorMat,
                                                          Material grooveMat, Material panelMat,
                                                          FloorButton pad)
        {
            GateSpan(RoomWidth, out float openMin, out float openMax, out _, out _);
            float halfWidth = (openMax - openMin) / 2f;

            // **THE WALLS STAND 20mm WIDER THAN THE OPENING, AND THAT IS THE FLICKER FIX.**
            //
            // Measured rather than reasoned, after three wrong guesses - `CycleThreeDiagnostics`
            // opens the built scene and looks for renderers with a face in common. Eight of the twelve
            // pairs it found were this one: room3-1's north wall panel beside the opening ends at
            // x 20.80, and the corridor's side-wall BACKING has its outer face at x 20.80 as well.
            // Full height, both jambs, exactly coplanar.
            //
            // It is not a coincidence of placement, it is a coincidence of CONSTANTS. A panel is inset
            // from its cell boundary by `GridLineThickness / 2` = 0.025, and a backing starts
            // `GrooveDepth` = 0.025 behind the wall face. The two numbers were equal, so the two
            // planes landed on each other, and no amount of moving things along Z would separate them.
            //
            // **THE TWO ARE NO LONGER EQUAL** - `GrooveDepth` went to 0.015 on 2026-09-01 for reasons
            // that have nothing to do with this - so the coincidence is gone by accident. The fix
            // below is what actually holds, and this paragraph is kept because the NEXT time two
            // unrelated constants land on each other it will be the fastest way to recognise it.
            //
            // Widening the WALLS broke it, and the corridor stood 20mm wider than its mouth for a day.
            //
            // **IT IS BACK TO ZERO, AND THE 20mm WAS COSTING SOMETHING AT THE OTHER END** (2026-08-21).
            // Two things happened after that fix. The walls were inset along Z until they touch
            // neither room's wall at all, which is what actually separates them from room3-1's
            // panelling - the sentence above about Z is wrong, and it was written before the inset
            // existed. And room3-2N grew a staircase whose steps are wall panels with a metre of tail
            // behind them: the step in the cell next to the mouth reaches 25mm PAST the mouth's edge,
            // into the back of this corridor's west wall.
            //
            // Those two pull opposite ways on the same number. Every 1mm of clearance moves this wall
            // 1mm further west and buries 1mm more of it inside that step - at 25mm the blue step
            // would come through the corridor wall and be visible from inside the corridor. At zero,
            // the wall's face sits 25mm proud of the step's edge and hides it, which is the margin the
            // step's own groove already provides. Keep it at zero unless a scan says otherwise.
            const float wallClearance = 0f;
            float wallHalfWidth = halfWidth + wallClearance;

            // Between the two rooms' inner faces. Both ends are derived, so moving either room moves
            // the corridor with it rather than leaving a gap nobody notices until they fall through.
            // **THE CORRIDOR'S WALLS TOUCH NEITHER ROOM'S WALL, and that took three goes to get to.**
            //
            // `BuildPanelWall` extends its backing and collision `WallDepth` past each end so
            // perpendicular walls bury each other's corners. The corridor runs along Z, so its overrun
            // goes north and south into whatever is there. Built flush to room3-1's inner face it
            // stood 12.5cm INSIDE the room - two black pillars. Inset by one `WallDepth` it stopped
            // protruding but its front face landed on z 113.75, which is exactly the plane of room3-1's
            // own wall panels: coplanar, overlapping, and torn-looking from every angle.
            //
            // Inset by TWO, it starts where room3-1's wall build-up ends. The two ABUT and never
            // share a volume, which is the only arrangement of the three with nothing to fight over -
            // and it is what play asked for in as many words: stop the structures overlapping.
            //
            // Everything else follows: the room beyond moves out by the same amount at its end, and
            // the floor, the shaft and the block still span the whole way between the inner faces.
            float zStart = RoomDepth / 2f;                       // room3-1's inner face
            float wallStart = zStart + 2f * WallDepth;           // clear of room3-1's wall entirely
            float wallEnd = wallStart + CorridorRun;
            float zEnd = wallEnd + 2f * WallDepth;               // room3-2N's inner face
            float zMid = (zStart + zEnd) / 2f;                   // also the walls' midpoint
            float run = zEnd - zStart;

            GameObject root = new GameObject("Room3_1_NorthCorridor");
            root.transform.SetParent(parent, false);

            // Floor and ceiling run the full width including the wall build-up, so the side walls sit
            // ON them rather than beside them.
            float slabWidth = 2f * wallHalfWidth + 2f * WallDepth;

            // **THE FLOOR STOPS WHERE THE ROOMS' FLOORS DO, and getting that wrong was the last
            // flicker.** `BuildSlab` lays a room's floor across its whole `RoomPitch` so neighbouring
            // rooms meet exactly under their shared divider - so room3-1's floor already reaches
            // 5.425, well past its own inner face at 5.25. A corridor floor starting at the inner face
            // double-laid the first 175mm with both top faces on y=0, which is two coplanar surfaces
            // fighting for every pixel right where the player walks in. The big room's floor overruns
            // its interior by `WallDepth` and did the same at the far end.
            //
            // Abutting rather than overlapping is the house style here - see `BuildSlab`, where
            // adjacent rooms' floors meet at the pitch boundary and nothing overlaps at all.
            float floorStart = RoomPitch / 2f;          // where room3-1's own floor slab ends
            float floorEnd = zEnd - WallDepth;          // where room3-2N's begins
            Prim(PrimitiveType.Cube, "Floor", root.transform,
                new Vector3(0f, -WallThickness / 2f, (floorStart + floorEnd) / 2f),
                new Vector3(slabWidth, WallThickness, floorEnd - floorStart), floorMat);

            // NO CEILING OVER THE CORRIDOR. The block that fills it IS the ceiling while it is down,
            // and while it is up the underside of that block is what the player walks beneath - which
            // is the whole image. A slab here would be a lid over a lift shaft and the block would
            // have nowhere to go; the void's own top, built with the barrier below, closes it in.

            // Panelled like every other wall in the building, at the corridor's own height so the grid
            // reads as the same grid one row shorter.
            // **PANELLED THE FULL HEIGHT OF THE SHAFT, not just of the walk.** The corridor has no
            // ceiling of its own - the block is its ceiling - so anyone standing in it with the block
            // raised is looking up past the walking height into the space the block came out of. Built
            // only as high as the corridor, those walls stopped at 2.70 and the shaft's own structure
            // showed above them as a black band down both sides. Play saw it exactly that way.
            //
            // Panelling all the way up also means the shaft needs no separate sides: the wall's own
            // backing and collision are the enclosure, so there is nothing behind the panels to see.
            float shaftTop = GateHeight + CorridorShaftHeight;
            BuildPanelWall(root.transform, "Wall_West", new Vector3(-wallHalfWidth, 0f, zMid),
                Vector3.forward, Vector3.right, CorridorRun, grooveMat, panelMat, Rect.zero, shaftTop);
            BuildPanelWall(root.transform, "Wall_East", new Vector3(wallHalfWidth, 0f, zMid),
                Vector3.forward, Vector3.left, CorridorRun, grooveMat, panelMat, Rect.zero, shaftTop);

            // ~~JAMB TRIM~~ REMOVED. Thin dark fillers were added here on the theory that the dark
            // beside the mouth was a slot opening onto an unlit corridor. It was not - it was the wall
            // overrun above, standing in the room. With that inset, the 25mm either side of the
            // opening is room3-1's OWN groove, backed by its own wall backing 25mm behind, exactly
            // like every other groove on that wall. Nothing to fill.

            // --- the fill ----------------------------------------------------------------------
            //
            // **THE CORRIDOR IS SOLID, AND THE PAD HOLLOWS IT OUT.** Not a slab partway along - the
            // whole twenty-two metres is one block, and holding the pad lifts the entire thing into a
            // void of exactly its own height above. What the player walks through is the hole it
            // leaves; what is over their head the whole way is the block, held up by a foot at the
            // near end that is not theirs.
            //
            // Letting go does not close a door. It fills the corridor back in, everywhere at once,
            // and anywhere inside it is inside the block.
            //
            // The lift is `GateHeight`, so the corridor and the void above it stack to 5.42 - within
            // 2mm of `RoomHeight`, which is not a coincidence worth engineering around but is worth
            // knowing: this whole assembly is one storey tall, like everything else here.
            //
            // **THE 60mm ON TOP IS THE LAST OF THE FLICKER, and it is the most literal answer to what
            // play described** ("after the structure has finished rising, the upper part flickers").
            // At `GateHeight + 0.02` the block's underside came to rest at exactly `GateHeight` - and
            // `GateHeight` is where the wall's cutout stops, so the wall backing's own bottom face is
            // on that plane too. A 25mm-deep strip across the full width of the mouth, near-black
            // backing against white block, sharing a plane, right at the top edge of the opening a
            // player walks under and looks up at.
            //
            // Raised 40mm clear of it instead. The mouth's headroom is unchanged - that is the wall's
            // cutout, not the block - and the top still lands inside `Shaft_Top` without touching
            // either of its faces.
            float lift = GateHeight + 0.06f;

            GameObject barrierRoot = new GameObject("NorthBarrier");
            barrierRoot.transform.SetParent(root.transform, false);
            barrierRoot.transform.localPosition = new Vector3(0f, 0f, zMid);

            // JUST THE TOP OF THE SHAFT. Its sides are the corridor's own panelled walls, which run
            // the full height, and **its two ENDS are the rooms' own walls** - the cutout at each end
            // stops at `GateHeight`, so above the corridor's mouth both walls are intact and already
            // close the shaft.
            //
            // **END CAPS WERE BUILT HERE AND WERE A BUG.** They were placed half the run plus half a
            // wall from the corridor's midpoint, which lands 5cm PAST each room's inner face - so each
            // one was a 2m x 2.7m white slab stuck to the INSIDE of a room's wall, above the corridor
            // mouth, with no grid on it. Play saw it exactly: the panels above the opening did not line
            // up and the dark edging round it was the wrong thickness, because none of it was the wall.
            //
            // White, not the near-black every other cavity in this building is: a groove or a door
            // pocket is meant to read as a dark line, and this is a surface the player looks straight
            // up at from inside the corridor. It is a ceiling and takes the ceiling's material.
            // Same trim at the south end as the floor gets, and for the same reason: room3-1's ceiling
            // slab sits at exactly this height and reaches 5.425. The north end needs none - room3-2N
            // is three storeys tall, so its ceiling is nowhere near this one.
            float shaftStart = RoomPitch / 2f - zMid;
            // 30mm short at the north end, the same as the block below it and for the same reason:
            // room3-2N's inner face is a panel plane, and a lid that reaches it shares that plane.
            float shaftEnd = run / 2f - 0.03f;
            Prim(PrimitiveType.Cube, "Shaft_Top", barrierRoot.transform,
                new Vector3(0f, GateHeight + CorridorShaftHeight + WallThickness / 2f,
                            (shaftStart + shaftEnd) / 2f),
                new Vector3(slabWidth, WallThickness, shaftEnd - shaftStart), CeilingMaterial());

            // AUTHORED FILLED, which is the pose it rests in - every plinth and lid in this project is
            // authored in its resting pose so the scene can be read without pressing Play.
            //
            // **AND WHAT IT LOOKS LIKE FILLED IS THE NORTH WALL.** The block is white, not the
            // near-black the void around it is, and its south end carries the wall's own panel grid on
            // the wall's own plane - one cell wide, two rows, inset half a groove, with the dark
            // backing plate behind them that every groove in this building has. Stand in room3-1 with
            // the pad untouched and there is no corridor: there is a wall, and it is the same wall as
            // the other three. The same specification the gates are built to, on a leaf that happens
            // to be twenty-two metres deep.
            GameObject slab = new GameObject("Fill");
            slab.transform.SetParent(barrierRoot.transform, false);
            slab.transform.localPosition = Vector3.zero;

            const float faceBackingDepth = 0.015f;
            // **NOTHING ON THIS BLOCK IS EXACTLY AS WIDE AS THE CORRIDOR, and that is what stopped the
            // flicker.** At exactly 1.75 its sides were the same plane as the corridor's wall panels,
            // and two coplanar faces fight for the same pixels from every angle. The clearances are
            // asymmetric on purpose: the body is 2mm clear each side and the face backing only 1mm, so
            // no slot is ever left uncovered wide enough to see the lit wall behind the groove - which
            // is what read as a thicker black border with a bright edge to it.
            // **20mm EACH SIDE, UP FROM 2mm, AND THE OLD NUMBER WAS THE FLICKER.** Two parallel faces
            // 2mm apart running twenty-three metres are within the depth buffer's precision at that
            // distance - at 20m with a 0.05 near plane a 24-bit buffer resolves about half a
            // millimetre, so 2mm is four times nothing and the pair shimmer, worse the more the camera
            // moves. It is not a coplanarity bug, which a hair of clearance fixes; it is a RESOLUTION
            // one, and the only fix is a gap the buffer can tell apart. 20mm reads as the mechanical
            // clearance a sliding block would have anyway.
            const float bodyClearance = 0.04f;    // 20mm each side
            // The face keeps its 1mm. It is 15mm deep and sits at the mouth, so it has no long
            // parallel run to shimmer down - and a wider gap here lets the lit wall show through the
            // groove as a bright edge.
            const float faceClearance = 0.002f;
            // Sunk below the floor rather than resting on it, for the same reason: a bottom face at
            // y=0 is the floor slab's top face. Buried, there is no shared plane and no gap either.
            const float sink = 0.02f;
            float faceZ = zStart - zMid;                       // the block's south end, in its own frame
            float grooveGap = GridLineThickness;

            // **THE FACE IS ONLY DRAWN WHILE THE BLOCK IS SHUT, and that is a flicker fix measured
            // rather than guessed** (2026-08-21, by `CycleThreeDiagnostics` scanning the OPENED pose -
            // the first scan that had ever looked at this cycle with anything moved).
            //
            // The block lifts by its own height, so this two-row grid arrives exactly on the wall's
            // own panels in rows 2 and 3 of the same cell: same plane, same width, overlapping. A
            // whole cell of coplanar panelling directly above the corridor mouth, in the one place a
            // player standing in room3-1 is looking.
            //
            // **It cannot be fixed by moving anything.** The shaft is exactly one block tall, so a
            // taller face rises out through the top of it; a face set back far enough to clear the
            // wall is a face visibly recessed while shut, which is the one thing it exists not to be.
            // What is left is the observation that this grid has no job once the block moves: it is
            // the wall while the wall is a wall, and after that it is a lid on a lift. Switched off at
            // the first millimetre of travel, so the change happens under the clunk.
            var faceRenderers = new System.Collections.Generic.List<Renderer>();

            for (int row = 0; row * GridCellHeight < GateHeight - 0.001f; row++)
            {
                float bottom = row * GridCellHeight;
                float top = Mathf.Min(bottom + GridCellHeight, GateHeight);
                // THE ONE THING THAT IS EXACTLY ON THE GRID. These are what room3-1 sees when the
                // corridor is shut, so they are the wall's own panels - cell width less one groove,
                // row height less one groove - and no clearance is applied to them.
                faceRenderers.Add(Prim(PrimitiveType.Cube, $"FacePanel_{row}", slab.transform,
                    new Vector3(0f, (bottom + top) / 2f, faceZ + GrooveDepth / 2f),
                    new Vector3(2f * halfWidth - grooveGap, top - bottom - grooveGap, GrooveDepth),
                    panelMat, removeCollider: true).GetComponent<Renderer>());
            }

            faceRenderers.Add(Prim(PrimitiveType.Cube, "FaceBacking", slab.transform,
                new Vector3(0f, (GateHeight - sink) / 2f, faceZ + GrooveDepth + faceBackingDepth / 2f),
                new Vector3(2f * halfWidth - faceClearance, GateHeight + sink, faceBackingDepth),
                grooveMat, removeCollider: true).GetComponent<Renderer>());

            // The mass itself, starting where the face finishes so nothing is coplanar with anything.
            //
            // **AND STOPPING 60mm SHORT AT THE NORTH END**, which is the same rule at the other end and
            // was also found by scanning the opened pose: raised, the block's top reaches into
            // `Shaft_Top` and the two shared their north face exactly.
            //
            // 60 rather than 30 because the lid stops 30 short as well, and shortening both by the
            // same amount moved the shared plane rather than removing it - which is the entire lesson
            // of this corridor, made once more in one line. Both ends land inside room3-2N's own wall
            // build-up, so nothing can see either gap while the block is down.
            float bodyStart = faceZ + GrooveDepth + faceBackingDepth;
            float bodyEnd = zEnd - zMid - 0.06f;
            Prim(PrimitiveType.Cube, "Body", slab.transform,
                new Vector3(0f, (GateHeight - sink) / 2f, (bodyStart + bodyEnd) / 2f),
                new Vector3(2f * halfWidth - bodyClearance, GateHeight + sink, bodyEnd - bodyStart),
                panelMat);

            // **THE CORRIDOR HAD NO REFLECTION PROBE, AND TWENTY-THREE METRES OF 0.85-SMOOTH WHITE
            // PANELLING IS ALMOST ENTIRELY WHAT IT REFLECTS** (2026-08-21; play reported the ceiling
            // "flickering, and worse the more I move", once the block was fully up).
            //
            // Every other space in the building has one and this one fell between two rooms: room3-1's
            // probe is a box around room3-1 and room3-2N's is a box around room3-2N, and the corridor
            // is in neither. **Box projection does not politely give up outside its box - it
            // EXTRAPOLATES**, so every surface in here was sampling room3-1's cubemap re-projected up
            // to twenty metres past where it was captured. That smears, and it swims as the camera
            // moves, which is what "flickers when I move" looks like on a surface with no texture of
            // its own to anchor it.
            //
            // It only shows once the block is up because until then there is no corridor to stand in -
            // which is also why no earlier scan of this cycle could have found it.
            //
            // Sized to the walk plus its shaft and centred on the run, so the box is the space rather
            // than a room next to it.
            BuildReflectionProbe(root.transform, "Room3_1_Corridor", zMid,
                                 sizeOverride: new Vector3(slabWidth, shaftTop, run),
                                 yCenter: shaftTop * 0.5f);

            AudioSource audio = MakeSource(barrierRoot.transform, "BarrierAudio",
                                           spatialBlend: 1f, volume: 0.95f);
            audio.transform.localPosition = new Vector3(0f, GateHeight / 2f, 0f);

            // BAKED INTO THE PROBES, and said out loud rather than left to luck. `MovesDuringPlay`
            // happens not to list `CrushingBarrier`, so this would be baked anyway - but its face IS
            // room3-1's north wall while it is down, and a wall missing from the reflections is the
            // mark that gives a concealed opening away (see WallWhileShut). Marking it means adding
            // `CrushingBarrier` to that list later cannot quietly undo this.
            slab.AddComponent<WallWhileShut>();

            CrushingBarrier barrier = barrierRoot.AddComponent<CrushingBarrier>();
            barrier.slab = slab.transform;
            barrier.openLocalOffset = new Vector3(0f, lift, 0f);
            barrier.pads = new[] { pad };
            barrier.audioSource = audio;
            // **SLOW UP, FAST DOWN, and the asymmetry is the whole feel of it.** 0.9s read as the
            // block being flicked aside; twenty-two metres of it should look like it weighs what it
            // weighs, and 2.6s is a lift rather than a flinch. The fall stays at 0.45 on purpose - it
            // is not closing, it is being let go of, and the thing you spent nine seconds earning
            // should take half a second to lose.
            barrier.openDuration = 2.6f;
            barrier.closeDuration = 0.45f;
            barrier.faceRenderers = faceRenderers.ToArray();
            barrier.moveClip = LoadClip(SfxDir, "sfx_door_open");
            // The heaviest thing in the SFX folder. There is no crunch in it and there does not need
            // to be - the room is a facility, and what it does to you is switch you off.
            barrier.crushClip = LoadClip(SfxDir, "sfx_power_down");
            // THE WHOLE CORRIDOR IS THE KILL VOLUME, because the whole corridor is the block. There is
            // no safe corner to stand in: the only safe place is out of it at one end or the other.
            barrier.killHalfExtents = new Vector3(halfWidth, GateHeight, run / 2f);

            return barrier;
        }

        private static FloorButton BuildFloorButton(Transform parent, Material mat, string name, Vector3 position)
        {
            // Root at unit scale so the activation volume is defined in clean world units;
            // the flattened cylinder mesh lives on an unscaled-collider visual child instead.
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;

            // Unity's cylinder is 1 unit across, so the X/Z scale is the diameter.
            const float padRadius = 0.35f;
            GameObject visual = Prim(PrimitiveType.Cylinder, "Visual", root.transform, Vector3.zero,
                new Vector3(padRadius * 2f, 0.03f, padRadius * 2f), mat, removeCollider: true);

            // No collider: FloorButton is a logical volume tested against the player's centre, not
            // a physics trigger. Its radius is derived from the visible pad here so the hit area
            // and the thing you can see can't drift apart - a hair proud of the edge, no more.
            FloorButton fb = root.AddComponent<FloorButton>();
            fb.activationRadius = padRadius + 0.05f;
            fb.buttonRenderer = visual.GetComponent<Renderer>();

            // **THE CLUNK IS WIRED HERE, WHERE THE PAD IS MADE, AND THAT IS THE POINT.** It used to be
            // wired in `BuildAudio` from a HAND-WRITTEN LIST of pads, and the comment beside that list
            // already recorded this exact bug happening to the doors: cycle 2's seven were left off
            // it and opened in silence for weeks, because a door that makes no sound looks exactly
            // like a door. Cycle 3's four pads were then left off the same list, and play reported it
            // as "the floor pad sound is gone".
            //
            // A list of every instance of a thing, maintained by hand, is a list that will be wrong.
            // The fix is not a better list - it is that there is no list: every pad in the game gets
            // its source and its clips from the one function that can make one.
            //
            // Positional and parented to the pad, which is the entire point of it. The door lamp only
            // reports the condition to someone looking at the door; the clunk reaches you wherever you
            // are, so hearing a ghost step onto a pad behind you is how the puzzle tells you the way
            // is open - and in room3-1, where four pads open four different walls, it is how you learn
            // which is which without turning round.
            fb.audioSource = MakeSource(root.transform, "FloorButtonAudio", 1f, 0.9f);
            fb.pressClip = LoadClip(SfxDir, "sfx_floor_button_press");
            fb.releaseClip = LoadClip(SfxDir, "sfx_floor_button_release");
            return fb;
        }

        // The slab, its pocket, and the lamp above it - everything both doors have in common. What
        // differs is only what unlocks them: Room1's is a button wired to a floor pad, Room2's is a
        // key lock fed by whatever a balloon gave up.
        // yaw turns the whole door, which is how a door in a SIDE wall costs nothing: the slab, the
        // pocket offset, the lamp and the slide are all measured in this root's local frame, so
        // rotating the root puts every one of them on a different wall with no second copy of the
        // measurements. 0 is a north wall, 90 the east, -90 the west.
        //
        // wallHalfExtent is how far the wall is from the room centre along the door's own normal -
        // RoomDepth/2 for the end walls this was written for, RoomWidth/2 for a side wall. It used to
        // be hardcoded, which is the only thing that made this Z-only.
        private static (Door door, DoorIndicator indicator, float wallInnerZ) BuildDoorShell(
            Transform parent, string name, float roomCenterZ, Material mat,
            float wallHalfExtent = RoomDepth / 2f, float yaw = 0f)
        {
            GameObject doorRoot = new GameObject(name);
            doorRoot.transform.SetParent(parent, false);
            // The root carries the room offset, so every measurement below stays in the same local
            // frame it was tuned in back when there was only one door. The offset is in the PARENT's
            // frame and the rotation is the root's own, so the two do not interfere.
            doorRoot.transform.localPosition = new Vector3(0f, 0f, roomCenterZ);
            doorRoot.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            // The slab lives in the pocket between this room's wall and the next room's, so sliding
            // it sideways tucks it inside the wall build-up rather than dragging it across the
            // panelling. wallInnerZ is the room surface; the pocket starts WallDepth behind that.
            float wallInnerZ = wallHalfExtent;
            float slabZ = wallInnerZ + WallDepth + DoorPocketDepth / 2f;

            // The slab KEEPS its collider. The doorway is cut out of both walls' collision, so the
            // closed door is the only thing standing between the two rooms - built without one, the
            // player just walks through it and the puzzle is bypassable. Once open the slab sits
            // entirely behind the wall's own collision, so it never blocks the opening it cleared.
            GameObject panel = Prim(PrimitiveType.Cube, "DoorPanel", doorRoot.transform,
                new Vector3(0f, DoorHeight / 2f, slabZ),
                new Vector3(DoorWidth, DoorHeight, DoorThickness), mat);

            // A single lamp block split down the middle: red half on the left, green on the right,
            // only ever one of them lit. Both halves share one emissive material and are driven
            // apart by DoorIndicator through property blocks.
            Material lampMat = MakeEmissiveMaterial("IndicatorLamp", Color.white, 1f);
            const float lampWidth = 0.34f, lampHeight = 0.11f, lampDepth = 0.04f;
            float lampY = DoorHeight + 0.28f;
            // Proud of the wall surface by half its depth, so it sits on the panelling.
            float lampZ = wallInnerZ - lampDepth / 2f;

            GameObject lampRoot = new GameObject("DoorIndicator");
            lampRoot.transform.SetParent(doorRoot.transform, false);

            GameObject redHalf = Prim(PrimitiveType.Cube, "RedHalf", lampRoot.transform,
                new Vector3(-lampWidth / 4f, lampY, lampZ),
                new Vector3(lampWidth / 2f, lampHeight, lampDepth), lampMat, removeCollider: true);
            GameObject greenHalf = Prim(PrimitiveType.Cube, "GreenHalf", lampRoot.transform,
                new Vector3(lampWidth / 4f, lampY, lampZ),
                new Vector3(lampWidth / 2f, lampHeight, lampDepth), lampMat, removeCollider: true);

            Door door = doorRoot.AddComponent<Door>();
            // Slides right (+X as seen from inside the room) by its own width, so it clears the
            // opening exactly.
            door.openLocalOffset = new Vector3(DoorWidth, 0f, 0f);
            door.doorPanel = panel.transform;

            // **THE DOORWAY AS A SWITCHABLE HOLE** (2026-09-04, by request: occlude while the door
            // is shut). See `Door.portal` for why a leaf cannot simply be an occluder - it moves,
            // and baked occlusion is static.
            //
            // Sized to the opening and spanning the whole divider - near wall, pocket, far wall - so
            // closing it separates the two rooms into different cells rather than leaving a slot
            // through the middle of one. `OcclusionPortal` exposes only `open` to script, so the box
            // is written through `SerializedObject`, which is how the Inspector does it too.
            GameObject portalGO = new GameObject("DoorwayPortal");
            portalGO.transform.SetParent(doorRoot.transform, false);
            OcclusionPortal portal = portalGO.AddComponent<OcclusionPortal>();
            var portalSO = new SerializedObject(portal);
            portalSO.FindProperty("m_Center").vector3Value =
                new Vector3(0f, DoorHeight / 2f, wallInnerZ + WallDepth + DoorPocketDepth / 2f);
            portalSO.FindProperty("m_Size").vector3Value =
                new Vector3(DoorWidth, DoorHeight, DoorPocketDepth + 2f * WallDepth);
            // Authored SHUT, because a door is. If the portal started open, the first frame of every
            // run would draw the whole corridor before anything asked the door about it.
            portalSO.FindProperty("m_Open").boolValue = false;
            portalSO.ApplyModifiedPropertiesWithoutUndo();
            door.portal = portal;

            DoorIndicator indicator = lampRoot.AddComponent<DoorIndicator>();
            indicator.redHalf = redHalf.GetComponent<Renderer>();
            indicator.greenHalf = greenHalf.GetComponent<Renderer>();
            indicator.door = door;

            return (door, indicator, wallInnerZ);
        }

        // A powered door with no control of its own, held open while every pad handed to it is
        // held. Room1 passes one pad, Room3 two.
        //
        // There was a DoorButton on the wall beside Room1's until a play-test found nobody could
        // locate it - testers held the pad, walked to the door and expected it to open. See Door's
        // own note for why they were right and what removing the button buys. Note the wall beside
        // these doors is bare, which is part of the point: there is no affordance left to mislead.
        // yaw / wallHalfExtent pass straight through to `BuildDoorShell`, which has always taken them -
        // this only ever hid them. The ring needs both: its three corner doors sit in a WEST wall
        // (yaw 270) whose half-extent is `RoomWidth/2`, not the `RoomDepth/2` a north wall has.
        private static Door BuildPadDoor(Transform parent, string name, float roomCenterZ,
                                         FloorButton[] pads, Material mat,
                                         float yaw = 0f, float wallHalfExtent = RoomDepth / 2f)
        {
            (Door door, DoorIndicator indicator, float _) =
                BuildDoorShell(parent, name, roomCenterZ, mat, wallHalfExtent, yaw);

            // The lamp tracks the condition, not the door, so it goes green the moment the pads are
            // all held. With no button anywhere it is the room's only readout for that, which makes
            // it load-bearing rather than decorative - and in Room3 it is the only way to tell "one
            // of my past selves has arrived" from "both have".
            indicator.requiredFloorButtons = pads;
            door.requiredFloorButtons = pads;

            return door;
        }

        // A keyed door on the north wall of the room centred at roomCenterZ - no pad and no condition
        // to hold open, and deliberately not a button: the shape of the thing on the wall is the
        // puzzle telling you what it wants. Called three times now, once per coloured key, at three
        // different joins in the corridor - identical every time except the wall it ends up in and
        // the key it wants, which is the point: three doors that behave the same and differ only by
        // colour is what makes the colour readable as the rule.
        private static (Door, KeyLock) BuildKeyDoor(Transform parent, string name, float roomCenterZ, Material mat, KeySpec spec)
        {
            (Door door, DoorIndicator indicator, float wallInnerZ) =
                BuildDoorShell(parent, name, roomCenterZ, mat);
            return (door, AttachKeyLock(door, indicator, wallInnerZ, mat, spec));
        }

        // The lock beside a door, and the colour that says which key it wants.
        //
        // Factored out of BuildKeyDoor when Room2 grew from one keyed door to three. Every one of them
        // is the same fixture at a different colour, and the colour comes from the same KeySpec the key
        // itself is built from - so a lock cannot end up wanting a key that does not match the plate
        // beside it. That was the whole risk in this feature: the colours are the only instructions the
        // player gets, and two places deciding them independently is how they come apart.
        private static KeyLock AttachKeyLock(Door door, DoorIndicator indicator, float wallInnerZ,
                                             Material mat, KeySpec spec)
        {
            GameObject lockRoot = new GameObject("KeyLock");
            lockRoot.transform.SetParent(door.transform, false);
            lockRoot.transform.localPosition = new Vector3(-GridCellWidth, GridCellHeight * 1.5f, wallInnerZ - 0.06f);

            // Taller and narrower than the door button next door: across a room the two have to
            // read as different kinds of thing rather than as the same switch twice.
            //
            // IN THE KEY'S COLOUR, and this is the primary signal - it is the thing the player is
            // standing in front of with a key in hand. The band on the door says the same thing from
            // across the room; this says it at arm's length.
            Material plateMat = MakeColorMaterial(spec.materialName + "Plate", spec.display);
            SetSmoothness(plateMat, 0.45f);
            GameObject plate = Prim(PrimitiveType.Cube, "Visual", lockRoot.transform, Vector3.zero,
                new Vector3(0.2f, 0.3f, 0.09f), plateMat, removeCollider: true);

            // A plain keyhole, NOT the key silhouette this used to carry. The etching said what the
            // lock wanted, which was the right idea while nothing could ever be put in it - but the
            // key is now literally inserted here, and an inserted key sitting on top of an engraved
            // one reads as two keys. A keyhole says the same thing and leaves the socket empty.
            Material slotMat = MakeColorMaterial("KeySlot", new Color(0.06f, 0.06f, 0.07f));
            BuildKeyholeSlot(lockRoot.transform, new Vector3(0f, -0.03f, -0.047f), slotMat);

            // Where an accepted key ends up: SEATED, not parked on the surface. The key used to be
            // laid flat against the plate like a sticker, which is what "insert" meant when the
            // key was five primitives. It is a real key now and it goes IN.
            //
            // The plate is a 0.09-deep cube, so its face is at z -0.045. Putting the bow junction
            // exactly on that face leaves the bow standing proud where it can be seen and everything
            // from the shaft down inside the plate - and behind the plate is 0.015 of air and then
            // 0.125 of wall, so the 0.13 of shaft and bit has somewhere to be and is hidden the
            // whole way. Nothing is clipped and nothing reaches Room3 (the tip stops 0.10 short).
            const float plateFaceZ = -0.045f;
            float bowJunction = -KeyModelBowJunctionY * KeyScale;
            GameObject socket = new GameObject("KeySocket");
            socket.transform.SetParent(lockRoot.transform, false);
            // Y matches the keyhole seat below, so the shaft runs into the hole rather than past it.
            socket.transform.localPosition = new Vector3(0f, -0.03f, plateFaceZ - bowJunction);

            // **A WIDER REACH THAN THE LOCK IS BIG** (2026-08-29, by request: the E range was tight).
            //
            // 0.8 x 0.8 was about the size of the plate itself, so the player had to stand almost
            // exactly in front of it - and this is a fixture you walk up to holding a key, from
            // whatever angle the door happens to be on. The volume is what says "close enough to
            // touch"; being ON SCREEN is what stops it firing behind your back, and that half is
            // `PlayerLookup.InView` rather than a small box (CLAUDE.md 1.2). So the box can be
            // generous without the press becoming loose.
            BoxCollider trigger = lockRoot.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(2.2f, 2.4f, 2.0f);

            KeyLock keyLock = lockRoot.AddComponent<KeyLock>();
            keyLock.door = door;
            keyLock.keySocket = socket.transform;
            // How far back the key is presented before it slides in - exactly the length of key
            // that ends up buried, so it starts with its tip at the hole rather than at a distance
            // someone picked.
            keyLock.insertTravel = KeyInsertTravel;
            keyLock.lockRenderer = plate.GetComponent<Renderer>();
            // THE ONE THING THAT MAKES A DOOR PICKY. Both the player's gate (hand.Holding) and a
            // ghost's hand-over (ItemRegistry.FindSocket, keyed on AcceptedItemId) read this, so the
            // matching is enforced in one place for living and dead alike.
            keyLock.requiredItemId = spec.itemId;
            // The lamp over this door reports "you are carrying the key" the way the other one
            // reports "the pad is held".
            if (indicator != null) indicator.keyLock = keyLock;

            // NO BAND ON THE DOOR. There was one, in the same colour, on the reasoning that the plate is
            // 0.2m wide and the far side of the room is where the player decides which key to look for.
            // It went because the plate turned out to be enough on its own and the band was not free: a
            // whole slab wearing a colour reads as a coloured DOOR, which invites the idea that the door
            // itself is the thing that changes, where the lock is what actually differs. One coloured
            // fixture beside three identical doors says that more precisely than three coloured doors.
            return keyLock;
        }

        private static (GameObject, FirstPersonController, PlayerRecorder, CameraShaker, PlayerHand) BuildPlayer(Transform spawn, GhostInteractable[] ghostInteractables)
        {
            GameObject player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = spawn.position;
            player.transform.rotation = spawn.rotation;

            CharacterController cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            // **RAISED FROM UNITY'S 0.3 SO CYCLE 3'S STAIRS ARE WALKED UP, NOT JUMPED** (2026-08-21).
            //
            // The first attempt at making a grid-row step climbable was a bigger jump - 6 to 7.9,
            // 0.90m to 1.56m - and play called it awkward, which it is: the player floats, and every
            // room in the game inherits it. This does the opposite. The step comes down to half a row
            // (0.651m to its top) and the controller simply walks up it.
            //
            // **AND IT CANNOT REACH ANYTHING A JUMP COULD NOT ALREADY.** 0.72 is below the 0.90m the
            // jump has always cleared, so nothing in cycles 1 and 2 becomes reachable that was not
            // reachable before - which is exactly what the 1.56m jump could not promise. The two
            // existing things that lean on this value both still hold: the chess board is thinner than
            // this and is still walked over, and balloons are excluded from the controller entirely
            // (see `excludeLayers` below) so nobody rides one to the ceiling.
            cc.stepOffset = PlayerStepOffset;

            // ~~THE PLAYER CAST A SHADOW~~ REMOVED 2026-08-17, by request.
            //
            // It was the ghosts' own body model parented to the player with every renderer set to
            // `ShadowCastingMode.ShadowsOnly` - never drawn, so it could not be seen from inside or
            // clip the near plane, and it put the same silhouette on the floor that a past self does.
            //
            // Recorded rather than just deleted because the idea is an obvious one to have again, and
            // because the part that is NOT obvious is what it costs: nothing in this building casts a
            // shadow unless a light is told to, and only Room1's fixtures were. Restoring it means
            // restoring `castShadows: true` across the rooms as well, which is a shadow map per room.

            // The controller does not collide with balloons at all. They are still solid to each
            // other, to the floor and to the walls - this excludes exactly one pair. Without it the
            // player can stand on one, and the moment a balloon under a standing player takes any
            // upward push at all, stepOffset walks them up onto it and they ride it to the ceiling.
            // FirstPersonController pushes them from an overlap query instead of by contact.
            int balloonLayer = EnsureLayer(BalloonLayerName);
            cc.excludeLayers = 1 << balloonLayer;

            // A rig sits between the player and the camera purely so CameraShaker has somewhere to
            // write. FirstPersonController rewrites the camera's own localPosition and euler angles
            // every frame, so a shake applied to the camera itself would be wiped instantly.
            GameObject rigGO = new GameObject("CameraRig");
            rigGO.transform.SetParent(player.transform, false);
            CameraShaker shaker = rigGO.AddComponent<CameraShaker>();

            GameObject camGO = new GameObject("PlayerCamera");
            camGO.transform.SetParent(rigGO.transform, false);
            camGO.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            Camera cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
            cam.tag = "MainCamera";

            // **THE PLAYER SEES THEIR OWN BODY AND NOT THE ONE THE MIRRORS SEE.** Two bodies, two
            // layers, and each camera picks one - see `BuildPlayerBody`. This one takes the headless
            // one, because it is standing inside the other one's skull.
            BuildPlayerBody(player.transform);
            cam.cullingMask &= ~(1 << EnsureLayer(PlayerBodyWorldLayer));

            // Unity's default near plane of 0.3 is the same as the controller's radius, and that
            // does not work: what pokes through a wall is the near plane's CORNER, not its centre,
            // and at fov 60 that corner reaches 0.463m (0.532m ultrawide) while the capsule stops
            // the camera 0.3m away - less once skinWidth lets the wall penetrate the capsule. The
            // whole wall build-up is only WallDepth (0.125m) thick, so the frustum clipped clean
            // through it: stand against a wall or the door, look sideways, and you saw the far side
            // of the room. At 0.05 the corner reaches 0.077m, comfortably inside the standoff.
            cam.nearClipPlane = 0.05f;
            // **500, NOT 100** (2026-09-03). It was pulled in from Unity's default 1000 to keep depth
            // precision where the scene actually was - "~22m across both rooms" - and that sentence
            // is the whole problem: the game is not two rooms any more. The ending's ride is 216.8m
            // of path up the outside of a building four cycles tall, so everything past 100m was
            // clipped and the player saw SKY through the far half of it. Play reported it as the
            // distant rooms not rendering.
            //
            // The SSAO reasoning it was set for still holds - the panels stand `GrooveDepth` proud
            // of their backing and SSAO resolves those grooves off the depth buffer - but it costs
            // far less than the comment implies on a reversed-Z float depth buffer, which is what
            // every platform this ships to uses: precision there is dominated by the NEAR plane,
            // and that is untouched at 0.05.
            cam.farClipPlane = 500f;

            // Opt the camera into the volume stack - URP cameras ignore post-processing otherwise.
            UniversalAdditionalCameraData camData = camGO.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            FirstPersonController fpc = player.AddComponent<FirstPersonController>();
            fpc.playerCamera = cam;
            fpc.pushLayers = 1 << balloonLayer;
            // 2D: these are the player's own feet, not something across the room. Fired off the same
            // step phase the head bob is drawn from, so the sound and the movement are one event.
            fpc.footstepSource = MakeSource(player.transform, "FootstepAudio", 0f, 1f);
            // Three, cycled by step number rather than picked at random - a random order re-uses the
            // same clip twice in a row often enough to be heard doing it.
            fpc.footstepClips = new[]
            {
                LoadClip(SfxDir, "sfx_footstep_1"),
                LoadClip(SfxDir, "sfx_footstep_2"),
                LoadClip(SfxDir, "sfx_footstep_3"),
            };
            // AND THE SAME THREE FOR WATER, swapped in while the player is in room2-5's pool. Generated
            // rather than sourced, like the chop - see MakeWadeClip for why a splash clip and a
            // footstep clip are both the wrong sound for this.
            //
            // ON THE PLAYER, so it is built by `Build` and NOT by `RebuildCycleTwo`: a cycle rebuilt on
            // its own gets the pool without the sound of walking in it, and the fallback in `Footstep`
            // is what makes that quiet rather than silent. Same standing hazard as the door audio.
            fpc.wadeClips = new[]
            {
                MakeWadeClip("sfx_wade_1", 20260819),
                MakeWadeClip("sfx_wade_2", 20260820),
                MakeWadeClip("sfx_wade_3", 20260821),
            };

            PlayerRecorder recorder = player.AddComponent<PlayerRecorder>();
            recorder.interactables = ghostInteractables;

            // Parented under the camera rather than the player, so a carried item rides the view -
            // including through the wake-up, where WakeUpSequence poses the camera directly and
            // anything hung off the body would swing independently of where you are looking.
            GameObject handAnchor = new GameObject("HandAnchor");
            handAnchor.transform.SetParent(camGO.transform, false);

            PlayerHand hand = player.AddComponent<PlayerHand>();
            hand.holdAnchor = handAnchor.transform;
            // HOW FAR IN FRONT A PUT-DOWN LANDS, and it is written from here into BOTH the player's
            // hand and the ghost prefab (see BuildGhostPrefab). It was a component default on each
            // and they were not the same act: the player threw the object a metre forward and a past
            // self let it go at its own feet, which room3-2N's ledges turned into "the player drops
            // it off the deck and every ghost afterwards leaves it on the lip".
            hand.dropAhead = DropAheadDistance;
            // The same eye the controller uses, so the visibility walk-back inside `PutDownPoint`
            // asks from the same height whether it is the player's camera or a past self's head.
            hand.eyeHeight = fpc.standingEyeHeight;
            // A put-down stops at a wall rather than going through it. Balloons excluded for the
            // same reason the controller excludes them - an object put down in Room2 would otherwise
            // be stopped short by whatever balloon happened to be drifting past.
            hand.dropBlockers = ~(1 << balloonLayer);

            // Stops a held object reaching through a wall the player is standing against. Balloons
            // are excluded from the cast for the same reason the controller excludes them: the item
            // would otherwise shy away from every balloon in Room2 as the player walked through them.
            HeldItemClearance clearance = handAnchor.AddComponent<HeldItemClearance>();
            clearance.anchor = handAnchor.transform;
            clearance.eye = camGO.transform;
            clearance.hand = hand;
            clearance.ignoreRoot = player.transform;
            clearance.blockers = ~(1 << balloonLayer);

            // ~~A FIRST-PERSON HAND UNDER THE HELD OBJECT~~ **BUILT AND REMOVED 2026-08-24, after
            // play, by request: it hurt the game more than it helped.**
            //
            // It was DavidFischer's rigged FPS hands on `PlayerBodyView`, parented here so
            // `HeldItemClearance` pulled it in with whatever it was holding, closing per object from
            // the object's own thinnest dimension. None of that was the problem - it worked. The
            // problem is what a hand DOES to a first-person game: it fills the corner of the frame
            // permanently, and this is a game about looking at rooms.
            //
            // **The reasoning that argued FOR it still stands and is why this note is here**: held
            // objects are their true size, so a metre of glass genuinely fills the view, and a hand
            // is what explains that rather than leaving it a prop stuck to the camera. If it ever
            // comes back, `git log` has the whole of it - the measured-not-written rig orientation,
            // the 100x bone-space trap, and the per-object closure. What it does NOT solve is the
            // objection above.
            // Takes and surrenders go into the same timeline the frames do, so a past self can
            // repeat them. Only items flagged ghostCarryable are written.
            hand.recorder = recorder;

            BalloonTool tool = player.AddComponent<BalloonTool>();
            tool.playerCamera = cam;
            tool.hand = hand;
            tool.recorder = recorder;
            // 2D: this is the player's own arm, not something across the room. The clip slot is
            // left empty - there is no swing sound generated yet, and borrowing the bed's sheet
            // rustle for it would be worse than silence. BalloonTool guards on null.
            tool.audioSource = MakeSource(player.transform, "ToolAudio", 0f, 0.5f);

            return (player, fpc, recorder, shaker, hand);
        }
    }
}
