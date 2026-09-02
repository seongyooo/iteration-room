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
    // THE THINGS BOLTED TO A WALL OR STANDING ON A FLOOR that are neither a room nor a puzzle: the
    // light switch, the taps and the mixer, the water tank, the bucket and the stand that catches
    // it, and the wall panel display.
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {

        // A WALL-MOUNTED SWITCH, wired to one ceiling fixture. `localPosition` is the point on the
        // wall it mounts flush against; `yaw` turns it to face into the room - 90 for a west wall
        // (faces +X), -90 for an east wall (faces -X). Everything under the holder is authored with
        // +Z pointing into the room, so no builder below has to know which wall it is on.
        private static LightSwitch BuildLightSwitch(Transform parent, string name, Vector3 localPosition,
                                                     float yaw, Light light, Renderer panel,
                                                     Material litFixtureMat, Material darkFixtureMat)
        {
            GameObject holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPosition;
            holder.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            // BUILT, NOT IMPORTED, and for the same reason the nightstand is: there is nothing to
            // animate in the model. `old_light_switch.glb` merges its plate and its lever into ONE
            // mesh, so "flip the switch" could only ever tilt the whole wall plate - which had to be
            // held down to 6 degrees to stop it reading as the plate coming off the wall, and at 6
            // degrees it reads as nothing at all. Two boxes and an empty give a lever that moves on
            // its own, and every number below is a number rather than a measurement off someone
            // else's topology.
            Material plateMat = MakeColorMaterial("SwitchPlate", new Color(0.90f, 0.90f, 0.88f));
            Material leverMat = MakeColorMaterial("SwitchLever", new Color(0.80f, 0.79f, 0.76f));

            // THE PLATE'S SIZE WAS SET BY HAND IN THE EDITOR and read back off the scene, which is why
            // these are not round numbers - 200 x 320 x 25mm, a good deal larger than the 90 x 150 this
            // started at. A domestic plate is correct and was too small to find across a dark room,
            // which is the only thing this fixture has to do before it is pressed.
            const float plateW = 0.19954f, plateH = 0.31926f, plateT = 0.0252f;
            Prim(PrimitiveType.Cube, "Plate", holder.transform, new Vector3(0f, 0f, plateT / 2f),
                new Vector3(plateW, plateH, plateT), plateMat, removeCollider: true);

            // THE LEVER TURNS ABOUT THIS, and the pivot is an empty rather than the lever itself so
            // the tilt happens at the plate face - a box rotated about its own centre would sink half
            // its body into the plate on the way over. Sat exactly on the plate's front face, so the
            // lever swings across it rather than through it.
            GameObject pivot = new GameObject("LeverPivot");
            pivot.transform.SetParent(holder.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0f, plateT);
            // AUTHORED IN THE OFF POSITION, tilted half of the throw. `LightSwitch` reads this pose as
            // "off" in Awake and takes the whole `flipAngle` from it, so the two ends come out
            // symmetrical about the plate - a lever that sat flat when off would not read as a toggle
            // at all, it would read as a switch with nothing in it.
            pivot.transform.localRotation = Quaternion.Euler(-15f, 0f, 0f);

            // Hung BELOW the pivot, so the pivot is the hinge at the top of the throw. Also measured
            // off the placed switch rather than chosen.
            Prim(PrimitiveType.Cube, "Lever", pivot.transform, new Vector3(0f, -0.0116f, 0.0104f),
                new Vector3(0.04545f, 0.08116f, 0.03246f), leverMat, removeCollider: true);

            // WHERE THE E DISC HANGS. Its own object, because the prompt has to sit ON the switch and
            // the lever pivot is at the plate's face - anchored there the disc drew low, under the
            // fixture it was labelling. Level with the plate's centre and clear of its face.
            GameObject anchor = new GameObject("HintAnchor");
            anchor.transform.SetParent(holder.transform, false);
            anchor.transform.localPosition = new Vector3(0f, 0f, plateT + 0.06f);

            // REACH, and it was far too tight: 0.6 x 0.6 x 0.5 centred ON the wall put most of the
            // volume inside the wall, so the half in front of it was about 250mm deep and the player
            // had to stand against the switch to press it. Pushed out into the room and made roughly
            // a person's reach - the volume is tested against the player's own bounds, so this is
            // "close enough to touch it", not "close enough to see it".
            BoxCollider trigger = holder.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0f, 0.85f);
            trigger.size = new Vector3(1.7f, 2.4f, 1.8f);

            LightSwitch lightSwitch = holder.AddComponent<LightSwitch>();
            lightSwitch.switchVisual = pivot.transform;
            lightSwitch.hintAnchor = anchor.transform;
            lightSwitch.controlledLight = light;
            lightSwitch.controlledPanel = panel;
            lightSwitch.litMaterial = litFixtureMat;
            lightSwitch.darkMaterial = darkFixtureMat;
            // Positional, and that is the point rather than a detail: the room is dark and the switches
            // are on two walls, so the click is how you know WHICH one a past self just threw.
            lightSwitch.audioSource = MakeSource(holder.transform, "SwitchAudio", spatialBlend: 1f, volume: 0.8f);
            lightSwitch.onClip = LoadClip(SfxDir, "sfx_switch_on");
            lightSwitch.offClip = LoadClip(SfxDir, "sfx_switch_off");

            return lightSwitch;
        }

        // ROOM2-2'S TWO TAPS, one on the west wall and one standing on the floor. Press E at either to
        // turn it: the water shows, and pressing again shuts it off.
        //
        // NOT YET A GhostInteractable, so a past self cannot turn one on for you the way it can a
        // light switch. That is the next thing this wants, not an oversight to leave unsaid.
        //
        // `a_water_tap.glb` IS NO LONGER USED and nothing loads it. It was a floor-to-ceiling pipe with
        // a small hook two metres up, which at any scale that fits the room read as a thin column
        // standing in a corner - play reported it as "a water column in the middle" and, separately, as
        // the tap being missing. Those were the same observation twice, and no placement fixes a model
        // that has no body and no handle.
        private static WaterTap[] BuildWaterTap(Transform parent)
        {
            // BOTH PLACED BY HAND IN THE EDITOR AND READ BACK OFF THE SCENE, which is why none of these
            // is a round number. What was thrown away in the transfer is a degree or two of X tilt on
            // each - free-rotating with the gizmo picks that up, and a tap leaning off a wall is a
            // slip rather than a decision.
            //
            // The mixer hangs off the west wall. Its pivot sits well behind the wall face because the
            // model's body is authored ~0.09 in front of its own origin, and at this scale that is
            // more than half a metre.
            WaterTap wall = BuildWallMixer(parent, "WallTap", new Vector3(-4.311f, 1.979f, -1.600f), yaw: 90f);

            // TWO MORE, ON THE WALLS THAT HAD NONE. Same fixture, mirrored onto the east wall and hung
            // on the north one, each `WallMixerReach` in front of its own wall face so its stand sits
            // where the water lands (see the stands in BuildCycleTwoShell).
            //
            // FOUR TAPS IS A DIFFICULTY CHANGE, not decoration: the tank takes four bucketloads and
            // there are four buckets, so the room can now in principle be filled by four hands at
            // once. One player still cannot - they can only carry one bucket at a time - which is the
            // gap the past selves fill, and widening it is what makes room2-2 land sooner in a run
            // rather than easier within an iteration.
            //
            // The south wall is left bare on purpose: it carries the doorway out and, since the tank
            // moved beside it, the only other thing in the room worth looking at.
            WaterTap east = BuildWallMixer(parent, "EastTap", new Vector3(4.311f, 1.979f, 1.600f), yaw: 270f);
            WaterTap north = BuildWallMixer(parent, "NorthTap", new Vector3(-2.400f, 1.979f, 5.186f), yaw: 180f);

            // ~~And the standing one, out on the floor~~ REPLACED 2026-08-15 by a fourth wall mixer, on
            // the south wall. `boiling_water_tap` was a gooseneck standing in the middle of the floor,
            // and it was the odd one out in three ways at once: it was the only fixture in the room the
            // player could walk into, the only one whose stand was not against a wall (so the walking
            // line between the taps ran through it), and the only one that read as a free-standing
            // appliance rather than as the building's plumbing.
            //
            // One per wall now. The south wall carries the doorway and the tank, so this hangs on its
            // far side - the +X half, which the tank leaves clear.
            //
            // What is lost with it is a turning HANDLE: that import kept its parts as separate nodes
            // (`Water Knob_5` and the rest) while the mixer is one merged mesh, so the mixers animate
            // through a lever cut out of their own geometry instead. `BuildOneTap` still takes
            // `handleNode` and `spoutHeightFraction` for whatever model wants them next.
            WaterTap south = BuildWallMixer(parent, "SouthTap", new Vector3(2.500f, 1.979f, -5.186f), yaw: 0f);

            // ORDER IS A WIRE FORMAT. `taps[0]` and `taps[1]` are what the two original stands are
            // wired to, and the array's order is also its order in `ghostInteractables` - so the two
            // added ones stay APPENDED (CLAUDE.md §1.6) rather than slotted in beside the mixer they
            // are copies of, and the south mixer takes over slot 1 from the floor tap it replaced.
            return new[] { wall, south, east, north };
        }

        // THE WALL MIXER, and there are three of them now. Every number here was measured once against
        // the west one and is shared rather than re-typed, so a change to the spout nudge or the lever
        // split lands on all three instead of on whichever copy someone remembers.
        private static WaterTap BuildWallMixer(Transform parent, string name, Vector3 localPosition, float yaw)
        {
            return BuildOneTap(parent, name, $"{FurnitureDir}/modern_faucet_high_poly.glb",
                localPosition, yaw: yaw, scale: 6.4296f,
                spoutHeightFraction: 0f,
                // Nudged by hand after looking at it: the measured outlet is the lowest, front-most
                // point of the bounds, and on this model that lands a little low and a little proud of
                // where the water actually leaves. Applied to the SPOUT rather than to the flow root,
                // which matters - the root carries the puddle too, and moving that up would float the
                // spill ten centimetres above the floor.
                //
                // In the HOLDER's own frame, which is what lets one set of numbers serve three walls:
                // `yaw` turns the holder, and the nudge turns with it.
                spoutNudge: new Vector3(0f, 0.101f, -0.107f),
                handleNode: null,
                // This model has no handle NODE, so one is cut out of its mesh: above y = 0.04 the
                // geometry is the lever and nothing else. The hinge is at the back of that cluster.
                leverSplitY: 0.040f,
                leverPivotLocal: new Vector3(0f, 0.040f, 0.060f));
        }

        // CUTS A LEVER OFF A MERGED MESH, so a tap whose handle is welded to its body can still be
        // turned.
        //
        // `modern_faucet_high_poly` arrives as one node with one 39,000-vertex mesh: there is no handle
        // to animate, and turning the whole fixture swings it off the wall. But the lever is not mixed
        // INTO the body, it just shares a mesh with it - the vertices above the spout's dome are a
        // separate cluster in space (checked before writing this: above y = 0.04 the geometry narrows
        // to a band at z 0.045..0.156, which is the lever and nothing else). So the split is a plane.
        //
        // BY TRIANGLE CENTROID, not by vertex: classifying vertices would tear any triangle that
        // straddles the cut and leave a hole. A whole triangle goes one way or the other, so the two
        // meshes tile exactly as the original did, and the seam sits inside the body's dome where it
        // cannot be seen.
        private static (Mesh body, Mesh lever) SplitMeshAtHeight(Mesh source, string assetPrefix, float splitY)
        {
            string bodyPath = GeneratedDir + "/" + assetPrefix + "_Body.mesh";
            string leverPath = GeneratedDir + "/" + assetPrefix + "_Lever.mesh";
            Mesh cachedBody = AssetDatabase.LoadAssetAtPath<Mesh>(bodyPath);
            Mesh cachedLever = AssetDatabase.LoadAssetAtPath<Mesh>(leverPath);
            if (cachedBody != null && cachedLever != null) return (cachedBody, cachedLever);

            Vector3[] verts = source.vertices;
            Vector3[] norms = source.normals;
            Vector2[] uvs = source.uv;
            int[] tris = source.triangles;

            var bodyTris = new System.Collections.Generic.List<int>();
            var leverTris = new System.Collections.Generic.List<int>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                float cy = (verts[tris[i]].y + verts[tris[i + 1]].y + verts[tris[i + 2]].y) / 3f;
                var target = cy > splitY ? leverTris : bodyTris;
                target.Add(tris[i]); target.Add(tris[i + 1]); target.Add(tris[i + 2]);
            }

            // BOTH KEEP THE FULL VERTEX ARRAY. Compacting it would mean remapping every index for a
            // saving that does not matter here - the mesh is written once at build time and the unused
            // vertices are never submitted, because no triangle references them.
            Mesh body = new Mesh { name = assetPrefix + "_Body", indexFormat = source.indexFormat };
            body.vertices = verts; body.normals = norms; body.uv = uvs;
            body.SetTriangles(bodyTris, 0);
            body.RecalculateBounds();

            Mesh lever = new Mesh { name = assetPrefix + "_Lever", indexFormat = source.indexFormat };
            lever.vertices = verts; lever.normals = norms; lever.uv = uvs;
            lever.SetTriangles(leverTris, 0);
            lever.RecalculateBounds();

            if (!Directory.Exists(GeneratedDir)) Directory.CreateDirectory(GeneratedDir);
            AssetDatabase.CreateAsset(body, bodyPath);
            AssetDatabase.CreateAsset(lever, leverPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[SceneBuilder] {assetPrefix}: lever split at y={splitY:0.###} - "
                    + $"{bodyTris.Count / 3} body triangles, {leverTris.Count / 3} lever triangles.");
            return (AssetDatabase.LoadAssetAtPath<Mesh>(bodyPath), AssetDatabase.LoadAssetAtPath<Mesh>(leverPath));
        }

        // A GENERATED MESH HAS TO LIVE SOMEWHERE, and the water shipped once without knowing it.
        //
        // A `Mesh` built at edit time and assigned straight to a scene object is GONE when the scene is
        // reloaded - the reference serialises, the mesh does not, and the object comes back with
        // `MeshFilter.sharedMesh == null`. It looks like the water simply failing to render, which is
        // exactly how it was found. `RingShardMesh` and `BevelledPrismMesh` already write to
        // `GeneratedDir` for this reason; this is the same thing with the build step factored out, so
        // the next generated mesh cannot repeat it.
        //
        // ALSO A CACHE. Rebuilt only when the asset is missing, so a rebuild does not re-author four
        // meshes it already has - and the name carries the tap, since the two streams differ by seed.
        private static Mesh SaveGeneratedMesh(string assetName, System.Func<Mesh> build)
        {
            string path = GeneratedDir + "/" + assetName + ".mesh";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            Mesh mesh = build();
            mesh.name = assetName;
            if (!Directory.Exists(GeneratedDir)) Directory.CreateDirectory(GeneratedDir);
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        // ROOM2-2'S TANK: a glass column that has to be filled to a mark before its door will open.
        //
        // THE NUMBERS ARE THE PUZZLE, so they live here rather than on the component (CLAUDE.md §2).
        // With `fillPerTapPerSecond` at 0.009 and a target of 0.75:
        //
        //   one tap  -> 0.75 / 0.009      = 83 seconds. LONGER THAN THE LOOP. Cannot be done.
        //   two taps -> 0.75 / 0.018      = 42 seconds. Comfortably inside it, with time to walk.
        //
        // So the room cannot be solved by one person however well they play it, and is easy the moment
        // a past self is running the other tap. That gap IS the design - see WaterTank.
        //
        // The 42 seconds assumes both taps open at the start of the iteration, which the player cannot
        // do alone: they are at opposite ends of the room. What they can do is open one, let the
        // iteration end, and arrive next time to find that self already opening it.
        private static WaterTank BuildWaterTank(Transform parent, Material propMat)
        {
            // WIDER AND SHORTER THAN IT WAS (2.2m tall, 0.45 radius). A narrow column that height
            // reads as a pipe rather than as a vessel, and it put the target mark at 1.77m - above
            // a 1.6m eye, so the one thing in the room the player is aiming at was sighted upward at
            // arm's length. Squat and broad, the waterline is a surface seen from above rather than
            // an edge seen side-on, and the mark lands at chest height.
            const float innerHeight = 1.45f;
            const float radius = 0.72f;
            const float targetLevel = 0.75f;
            // The plinth's top, and where the water therefore starts. Named because `WaterTank` needs
            // the same number to put its column in the right place - see WaterTank.baseLocalY.
            const float baseY = 0.12f;
            // How far the plinth oversails the glass. Named because the PLINTH is the widest part of
            // this object, so it - not the glass - is what has to clear the doorway below.
            const float baseSpread = 2.3f;
            const float baseRadius = radius * baseSpread / 2f;

            GameObject tank = new GameObject("WaterTank");
            tank.transform.SetParent(parent, false);

            // BESIDE THE WAY OUT, ON ITS RIGHT - not in the middle of the floor, where it stood until
            // 2026-08-15. A vessel this wide in the centre of the room is something to walk around on
            // every trip between the two taps, and it put the thing the player is filling behind them
            // for most of the errand.
            //
            // DERIVED FROM THE DOORWAY rather than typed: room2-2 leaves by its SOUTH wall (`Door2_2`
            // is built at yaw 180), and a player walking at that door has -X on their right. So the
            // tank sits half a doorway plus its own PLINTH off the centre line, and that plinth in
            // from the wall, with a hand's clearance on both - which keeps the door's swept width
            // clear whatever `DoorWidth` or the tank's own size later become.
            const float clearance = 0.4f;
            tank.transform.localPosition = new Vector3(
                -(DoorWidth / 2f + baseRadius + clearance),
                0f,
                -(RoomDepth / 2f - baseRadius - clearance));

            // A plinth, so the glass is not growing out of the floor and the waterline starts at a
            // height the player can read without crouching.
            Prim(PrimitiveType.Cylinder, "Base", tank.transform, new Vector3(0f, 0.06f, 0f),
                new Vector3(baseRadius * 2f, 0.06f, baseRadius * 2f), propMat, removeCollider: true);

            // THE GLASS. Translucent rather than the water shader: this is a container and has to read
            // as a hard surface with the water clearly INSIDE it, so it wants no refraction of its own
            // fighting the column's.
            Material glassMat = MakeTranslucentMaterial("TankGlass", new Color(0.86f, 0.92f, 0.95f, 0.16f), 0.94f);
            // OPEN AT THE TOP AND HOLLOW, which a primitive cylinder cannot be: it is capped at both
            // ends and its collider is a capsule filling the whole volume. As a tank that made it a
            // solid lump with a lid - water was poured into a closed vessel and the inside was
            // somewhere that did not exist.
            //
            // The Y scale is no longer halved: a primitive cylinder is two units tall and
            // `WaterMeshes.Tube` is one.
            GameObject glass = new GameObject("Glass");
            glass.transform.SetParent(tank.transform, false);
            glass.transform.localPosition = new Vector3(0f, baseY + innerHeight / 2f, 0f);
            glass.transform.localScale = new Vector3(radius * 2f, innerHeight, radius * 2f);
            // Wall thickness is a fraction of the UNIT radius (0.5), so 0.045 comes out at 6.5cm of
            // glass at this size - thick enough for the rim to be a visible edge from standing height.
            Mesh tube = SaveGeneratedMesh("TankGlassTube",
                () => WaterMeshes.Tube(segments: 48, wallThickness: 0.045f));
            glass.AddComponent<MeshFilter>().sharedMesh = tube;
            glass.AddComponent<MeshRenderer>().sharedMaterial = glassMat;

            // SOLID WALL, EMPTY MIDDLE. Still a collider, unlike most props here - this is a real
            // object standing in a room people walk through, and walking through a glass tank would
            // say it is not really there - but the collider is now the WALL rather than the volume, so
            // the inside is a place. Non-convex, which is legal because nothing ever moves it.
            MeshCollider shell = glass.AddComponent<MeshCollider>();
            shell.sharedMesh = tube;
            shell.convex = false;

            // THE WATER, inside the glass and a little narrower so the two surfaces never z-fight.
            Material waterMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WaterStream.mat");
            GameObject body = new GameObject("Water");
            body.transform.SetParent(tank.transform, false);
            body.transform.localPosition = new Vector3(0f, baseY, 0f);
            body.transform.localScale = new Vector3(radius * 1.94f, 0.0001f, radius * 1.94f);
            body.AddComponent<MeshFilter>().sharedMesh = PrimitiveMesh(PrimitiveType.Cylinder);
            body.AddComponent<MeshRenderer>().sharedMaterial = waterMat;

            // THE MARK the player is filling to. Without it the target is invisible and the puzzle is
            // "keep going and hope" - which is not a puzzle, it is a wait.
            //
            // RED, AND DRAWN ON THE GLASS. It was yellow and stood 4cm proud of the tube, which made
            // it a shelf bolted round the tank rather than a line marked on it - and yellow against a
            // pale blue column is the same warm-on-cool contrast the ceiling fixtures already own, so
            // it read as lighting rather than as instruction. Red belongs to nothing else in this
            // building, and a band that hugs the glass reads as a graduation on the vessel.
            //
            // Emission is higher than the yellow's for the same apparent brightness: red is the
            // dimmest primary to the eye, and matching a number here would not match what is seen.
            // A BAND, NOT A DISC. A primitive cylinder has a top and a bottom face, so however thin it
            // was scaled it stayed a plate bolted round the tank - and from above the whole lid of it
            // was in view. `WaterMeshes.Band` is that cylinder's WALL and nothing else: a line drawn
            // round the vessel, which is what a graduation is.
            Material markMat = MakeEmissiveMaterial("TankMark", new Color(1f, 0.16f, 0.12f), 2.4f);
            GameObject mark = new GameObject("TargetMark");
            mark.transform.SetParent(tank.transform, false);
            mark.transform.localPosition = new Vector3(0f, baseY + innerHeight * targetLevel, 0f);
            // Barely proud of the glass (radius * 2 is the tube itself). The band mesh is one unit
            // tall, so the Y scale IS the height of the line in metres.
            mark.transform.localScale = new Vector3(radius * 2.045f, 0.038f, radius * 2.045f);
            mark.AddComponent<MeshFilter>().sharedMesh =
                SaveGeneratedMesh("TankMarkBand", () => WaterMeshes.Band(segments: 48));
            mark.AddComponent<MeshRenderer>().sharedMaterial = markMat;

            // WHAT THE PLAYER IS AIMING AT: the mark, which is the thing they are actually looking at
            // when they carry a bucket over. The tank's own transform is on the floor - see
            // WaterTank.aimAnchor.
            GameObject aim = new GameObject("Aim");
            aim.transform.SetParent(tank.transform, false);
            aim.transform.localPosition = new Vector3(0f, baseY + innerHeight * targetLevel, 0f);

            WaterTank comp = tank.AddComponent<WaterTank>();
            comp.aimAnchor = aim.transform;
            comp.waterBody = body.transform;
            comp.innerHeight = innerHeight;
            comp.targetLevel = targetLevel;
            // The plinth the glass stands on. Without it the column fills from the tank's ORIGIN,
            // which is the floor, and the waterline sits this far below the mark it is being
            // compared against - see WaterTank.baseLocalY.
            comp.baseLocalY = baseY;

            // WHERE A POUR IS RECORDED. Not a socket: pouring hands nothing over, so there is no
            // custody change for `CarryKind.Surrender` to carry, and without this a past self walks
            // to the tank with a full bucket and stands there. See PourPoint.
            PourPoint pour = tank.AddComponent<PourPoint>();
            pour.tank = comp;
            pour.bucketItemId = BucketItemId;
            comp.pourPoint = pour;
            // A fifth of the tank per bucket, against a target of 0.75 - so FOUR full bucketloads open
            // the door. Four is chosen against the minute rather than picked: a bucket takes 14s under
            // a tap and the round trip to the tank is a few seconds more, so one iteration delivers
            // about two. The room therefore needs a past self running the other tap and the other
            // bucket, which is the whole reason it is here.
            comp.bucketFraction = 0.2f;
            return comp;
        }

        // A PLACE TO STAND A BUCKET, under a tap, wired to the tap that fills it. See BucketStand for
        // why the spot is named rather than "anywhere under falling water".
        private static void BuildBucketStand(Transform parent, string name, WaterTap tap, Vector3 localPosition)
        {
            GameObject stand = new GameObject(name);
            stand.transform.SetParent(parent, false);
            stand.transform.localPosition = localPosition;

            // A shallow tray, so the spot is visibly A SPOT rather than a patch of floor the player is
            // supposed to guess at.
            Material trayMat = MakeColorMaterial("BucketTray", new Color(0.55f, 0.56f, 0.58f));
            Prim(PrimitiveType.Cylinder, "Tray", stand.transform, new Vector3(0f, 0.015f, 0f),
                new Vector3(0.62f, 0.015f, 0.62f), trayMat, removeCollider: true);

            GameObject seat = new GameObject("Seat");
            seat.transform.SetParent(stand.transform, false);
            seat.transform.localPosition = new Vector3(0f, 0.03f, 0f);

            // WHAT THE PLAYER IS AIMING AT - roughly where a bucket standing here would be, rather
            // than the tray it stands on. See BucketStand.aimAnchor: at floor level the target point
            // fell below the screen the moment the player walked up to it.
            GameObject aim = new GameObject("Aim");
            aim.transform.SetParent(stand.transform, false);
            aim.transform.localPosition = new Vector3(0f, 0.45f, 0f);

            // WHERE AN OVERFLOWING PAIL PUTS THE WATER. A full bucket under a running tap spills over
            // its rim - `Bucket.overflow` already draws that running down the staves - and until now
            // it reached the floor and stopped existing. Water does not do that.
            //
            // The same `SpreadingPuddle` the taps' own spill uses, so an overflow behaves like every
            // other loose water in the building: it grows while it is fed, stops when the tap does,
            // lingers and dries. It is on the STAND rather than on the bucket because a puddle is a
            // property of the FLOOR - a bucket that is picked up mid-overflow leaves the water behind,
            // which is what would happen.
            Material spillMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WaterStream.mat");
            GameObject spill = new GameObject("Overflow");
            spill.transform.SetParent(stand.transform, false);
            spill.transform.localPosition = new Vector3(0f, 0.008f, 0f);
            spill.AddComponent<MeshFilter>().sharedMesh = SaveGeneratedMesh(
                "WaterSpill_" + name + "_d20",
                // Shallower than a tap's own spill: this is what runs off a rim, not what a tap
                // pours straight at the floor.
                () => WaterMeshes.Spill(rings: 8, segments: 40, irregularity: 0.24f, depth: 0.020f,
                                        seed: name.GetHashCode() ^ 0x5177));
            spill.AddComponent<MeshRenderer>().sharedMaterial = spillMat;
            SpreadingPuddle spreading = spill.AddComponent<SpreadingPuddle>();
            // Slower and smaller than a tap's: a rim overflow is a trickle, and one that raced out to
            // three metres would say the bucket was the leak rather than the thing being filled.
            spreading.growthRate = 0.16f;
            spreading.maxRadius = 1.1f;

            BucketStand comp = stand.AddComponent<BucketStand>();
            comp.tap = tap;
            comp.seat = seat.transform;
            comp.aimAnchor = aim.transform;
            comp.bucketItemId = BucketItemId;
            comp.overflowPuddle = spreading;
        }

        // A MODEL'S BOUNDS IN SOME OTHER TRANSFORM'S FRAME, exactly.
        //
        // **`Renderer.bounds` IS WORLD SPACE**, and forgetting it is a mistake that hides, because
        // half of what comes back is still right: a SIZE cannot be changed by a translation, so
        // anything measured that way looks correct. A CENTRE or a MIN carries wherever in the
        // building the object happened to be built - and fed back in as a `localPosition` it displaces
        // the model by its own room's world position, scaled.
        //
        // That is what happened to the buckets, and all three of its symptoms looked like different
        // faults: room2-2 stands 43.4m along the corridor and one storey down, so every pail was
        // drawn 1.6m behind and a quarter of a metre above the object the game was carrying, seating
        // and dropping. Hence a bucket floating in mid-air, a bucket that "went somewhere else" when
        // stood on a perch it was in fact standing on, and a bucket that vanished when picked up -
        // 1.6m behind the hold anchor is 1.6m behind the camera.
        //
        // The eight corners of each MESH's own bounds are mapped, rather than the two corners of its
        // world box: re-boxing a world box in a rotated frame grows it, and this number decides how
        // big the object is. `BuildOneTap` measures in its holder's frame for the same reason and got
        // it right; this is that idea with the rotation case closed.
        private static Bounds ModelBounds(Transform frame, GameObject model)
        {
            bool any = false;
            Bounds local = new Bounds();

            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                Mesh mesh = r is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;

                Bounds mb = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3(
                        (c & 1) == 0 ? -1f : 1f,
                        (c & 2) == 0 ? -1f : 1f,
                        (c & 4) == 0 ? -1f : 1f));
                    Vector3 point = frame.InverseTransformPoint(r.transform.TransformPoint(corner));

                    if (any) local.Encapsulate(point);
                    else { local = new Bounds(point, Vector3.zero); any = true; }
                }
            }

            return local;
        }

        // ONE BUCKET: the model, the water it holds, and the overflow when it holds no more.
        //
        // MEASURED ONCE, AT IDENTITY, AND NEVER AGAIN. Every earlier version of this re-measured the
        // object after it had been scaled, rotated or given children, and every one of those
        // measurements was wrong in a different way:
        //
        //   - measured after the ROOT was scaled, the model's own scale came out divided by it, so the
        //     two cancelled and the bucket was the same size whatever the root said;
        //   - measured after the water and overflow cylinders existed, the bounds were THEIRS - a unit
        //     cylinder is 2 tall, so the "bucket" measured 2 units high and the water was sized to a
        //     bucket that does not exist. That is where the thin column came from.
        //
        // So the model is measured exactly once, while it is alone and untransformed, and every number
        // after that is arithmetic on that one measurement. Nothing is re-read.
        //
        // `tipDegrees` lays it on its side. Some of these are knocked over on purpose - four buckets
        // standing in a tidy row is a set of equipment issued to the player, and four lying about the
        // floor is a room somebody left in a hurry, which is the one this game is set in.
        private static void BuildBucket(Transform parent, string name, Vector3 localPosition,
                                        float yaw, float tipDegrees)
        {
            // SIZED IN TWO STEPS, and both are here rather than one combined number so each says what
            // it is for. The model is authored at 16.4 units, so it is first brought to a real bucket's
            // 300mm - the step a different bucket model would also need. `rootScale` is then the size
            // this GAME wants it at.
            const float wantedHeight = 0.30f;
            const float rootScale = 2.0f;

            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);

            // TWO OBJECTS BETWEEN THE ROOT AND EVERYTHING THE BUCKET IS MADE OF, so it can be tipped
            // over its own lip. `PourPivot` is placed AT the rim and is the thing that rotates;
            // `Body` cancels that placement, so the frame everything below it sits in is the root's
            // frame exactly as before - every measurement further down is unaffected, and
            // `Bucket.Apply` goes on positioning the water in the coordinates it always used.
            //
            // Rotating the root instead would tip the object the player's hand is holding, and the
            // hand pose is what decides where a carried thing sits on screen (CarryableItem.AttachTo).
            GameObject pivot = new GameObject("PourPivot");
            pivot.transform.SetParent(root.transform, false);
            GameObject bodyRoot = new GameObject("Body");
            bodyRoot.transform.SetParent(pivot.transform, false);

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>($"{FurnitureDir}/wooden_bucket.glb");
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source, bodyRoot.transform);
            model.name = "Visual";
            model.transform.localPosition = Vector3.zero;

            // THE ONE MEASUREMENT, AND IT IS TAKEN IN THE ROOT'S OWN FRAME. `Renderer.bounds` is
            // WORLD space - see ModelBounds for what taking it raw did to this object.
            Bounds raw = ModelBounds(root.transform, model);

            float modelScale = wantedHeight / Mathf.Max(0.0001f, raw.size.y);
            model.transform.localScale = Vector3.one * modelScale;

            // Everything below is that measurement times the scale - never a second look at the object.
            Vector3 size = raw.size * modelScale;
            // Loud, because this is the number every other number here is derived from and a silent
            // wrong one is what shipped. **The centre offset is the tell**: it is the model's own
            // middle in the ROOT's frame, so it belongs at zero, and anything else means the
            // measurement has picked up the world again (see ModelBounds).
            //
            // `lift` is whatever this model needs to stand on the root and is NOT expected to be any
            // particular value - `wooden_bucket` sits on its own origin, so it is zero. The comment
            // that used to say the model "hangs entirely below its origin" was itself an artifact of
            // the broken measurement: what it was describing was the storey, not the mesh.
            Debug.Log($"[SceneBuilder] {name}: size={size} "
                    + $"centre-offset={new Vector2(raw.center.x, raw.center.z) * modelScale} (expects 0,0) "
                    + $"lift={-raw.min.y * modelScale:0.000}");
            // STOOD ON THE ROOT and centred on it, whatever the mesh's own origin happens to be.
            //
            // This used to claim the model "hangs entirely below its own origin", which was never true
            // of this mesh - it was reading the STOREY. The bounds were world-space, so `min.y` was
            // seven metres down and `center.z` forty-three along, and the correction those produced
            // was mistaken for the model's own shape.
            model.transform.localPosition = new Vector3(
                -raw.center.x * modelScale, -raw.min.y * modelScale, -raw.center.z * modelScale);

            float outerRadius = Mathf.Min(size.x, size.z) * 0.5f;
            // The inside of a pail, as fractions of the outside: staves have thickness and the base is
            // not at the very bottom of the silhouette.
            float innerRadius = outerRadius * 0.84f;
            float innerBottom = size.y * 0.10f;
            float innerHeight = size.y * 0.80f;

            Material waterMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WaterStream.mat");

            GameObject water = new GameObject("Water");
            water.transform.SetParent(bodyRoot.transform, false);
            water.AddComponent<MeshFilter>().sharedMesh = PrimitiveMesh(PrimitiveType.Cylinder);
            water.AddComponent<MeshRenderer>().sharedMaterial = waterMat;

            // OVERFLOW: water going over the rim because there is nowhere else for it. A thin skirt
            // around the outside rather than a second stream - it is not falling from anywhere, it is
            // running down the staves.
            GameObject spill = new GameObject("Overflow");
            spill.transform.SetParent(bodyRoot.transform, false);
            spill.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            spill.transform.localScale = new Vector3(outerRadius * 2.06f, size.y * 0.5f, outerRadius * 2.06f);
            spill.AddComponent<MeshFilter>().sharedMesh = PrimitiveMesh(PrimitiveType.Cylinder);
            spill.AddComponent<MeshRenderer>().sharedMaterial = waterMat;
            spill.SetActive(false);

            // THE PIVOT'S PLACE: the front lip, at the top of the pail on the side it tips over. The
            // body hangs below and behind it, so a rotation about the pivot's own X swings the bucket
            // up and over that edge - which is what pouring looks like - rather than rolling it about
            // its middle.
            pivot.transform.localPosition = new Vector3(0f, size.y, outerRadius);
            bodyRoot.transform.localPosition = -pivot.transform.localPosition;

            // THE WATER LEAVING IT. Off until there is some; positioned in world space by `Bucket`
            // while it pours, because it has to hang straight down from a lip that is riding a hand.
            // One shared mesh across all four buckets - `SaveGeneratedMesh` serves the second and
            // later calls from the asset the first one wrote.
            GameObject pourStream = new GameObject("PourStream");
            pourStream.transform.SetParent(root.transform, false);
            pourStream.AddComponent<MeshFilter>().sharedMesh = SaveGeneratedMesh(
                "WaterStream_BucketPour",
                () => WaterMeshes.Stream(rings: 16, segments: 16, bottomScale: 0.78f,
                                         irregularity: 0.13f, seed: 20260815));
            pourStream.AddComponent<MeshRenderer>().sharedMaterial = waterMat;
            pourStream.SetActive(false);

            BoxCollider reach = root.AddComponent<BoxCollider>();
            reach.isTrigger = true;
            reach.center = new Vector3(0f, size.y / 2f, 0f);
            reach.size = new Vector3(size.x + 0.35f, size.y + 0.35f, size.z + 0.35f);

            // A SOLID BODY, like every other carryable: a bucket you can walk through is not in the
            // room. Switched off while it is in the hand by `CarryableItem.blocker`.
            GameObject solid = new GameObject("Blocker");
            solid.transform.SetParent(root.transform, false);
            BoxCollider block = solid.AddComponent<BoxCollider>();
            block.center = new Vector3(0f, size.y / 2f, 0f);
            block.size = size;

            CarryableItem item = root.AddComponent<CarryableItem>();
            item.blocker = block;
            item.itemId = BucketItemId;
            // **~~Weighable~~ NOT ANY MORE** (2026-08-29, by request: the bucket leaves the weighing
            // room). It read 1.8 empty and 12.0 full, which was the room2-2 puzzle's knowledge paying
            // off two doors away and is a connection worth mourning.
            //
            // It is also why room2-7 was never single-solution. **A partly filled pail is a
            // CONTINUOUS weight** - `Weighable` reads `Bucket.Level`, which is anything at all while
            // a bucket stands under a running tap - so a player who knew the numbers could dial in
            // any total the scale asked for. Every other object in that room is a fixed value; this
            // was the one that made the uniqueness proof a caveat instead of a proof. Off the pan,
            // it is a proof again. See the weight table.
            item.displayName = "BUCKET";
            item.icon = BucketIcon();
            item.floorY = 0f;
            item.handLocalPosition = HandPoseFor(size.y * rootScale);
            // The ROOT's scale, which is this object's world size - not the model child's. Passing the
            // child's put a fraction-of-a-millimetre bucket in the hand.
            item.handLocalScale = Vector3.one * rootScale;
            // STANDS ON ITS BASE WHEN IT IS PUT DOWN, whatever pose it was BUILT in. Two of these are
            // knocked over on purpose, and without this putting one down restored that built pose -
            // on its side, floating, because the lift a tipped bucket needs is part of its origin
            // POSITION and a drop restores only the rotation. See CarryableItem.restsUpright.
            item.restsUpright = true;
            item.audioSource = MakeSource(root.transform, "PickupAudio", 1f, 0.85f);
            item.pickupClip = LoadClip(SfxDir, "sfx_item_pickup");

            Bucket bucket = root.AddComponent<Bucket>();
            // ~~The scale reads the water through the bucket~~ - it does not read this pail at all
            // any more (see the `Weighable` note above). The bucket still owns how full it is, which
            // is what the tank and the pour need; nothing weighs it.
            // TWICE THE OLD RATE, by request: play found the tank the bottleneck of room2-2, and a
            // wait that is the slowest thing in a sixty-second loop is a wait the player spends
            // standing still. The number lives here rather than on the component for the reason every
            // tuned value does (CLAUDE.md §2) - it was taking the component's own default, which is
            // how it stayed unexamined.
            // HALVED, 2026-08-19: play reported that even with past selves running the taps there
            // was always dead time before the room could be left. Seven seconds under a tap is seven
            // seconds of STANDING THERE - one object in the hand means the bucket cannot be left to
            // fill while its carrier does something else, so every second of it is a second of the
            // sixty spent watching.
            //
            // The room's LENGTH is unchanged: it is still four bucketloads into the tank
            // (`bucketFraction` 0.2 against a 0.75 mark), so it still cannot be finished by one pair
            // of hands. What is gone is the waiting, which was never the interesting part - the
            // interesting part is that there are two taps and one of you.
            bucket.fillSeconds = 3.5f;
            bucket.waterBody = water.transform;
            bucket.innerBottom = innerBottom;
            bucket.innerHeight = innerHeight;
            bucket.innerRadius = innerRadius;
            bucket.overflow = spill;
            bucket.pourPivot = pivot.transform;
            bucket.pourStream = pourStream.transform;
            // In METRES, like every other size here - the stream mesh is a unit column, so this is
            // the width it comes out at. Taken off the pail's own mouth (which is `outerRadius` at
            // model scale, so twice that in the world) rather than picked, so a different bucket
            // pours a stream in proportion to itself.
            bucket.streamWidth = outerRadius * rootScale * 0.30f;
            bucket.audioSource = MakeSource(root.transform, "PourAudio", spatialBlend: 1f, volume: 0.75f);
            bucket.pourClip = LoadClip(SfxDir, "sfx_water_splash_1");

            // POSE AND SIZE LAST, once every measurement is safely in hand.
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(tipDegrees, yaw, 0f);
            root.transform.localScale = Vector3.one * rootScale;
            // Knocked over, and lifted onto its side so it rests on the rim rather than sinking in.
            if (!Mathf.Approximately(tipDegrees, 0f))
                root.transform.localPosition = localPosition + new Vector3(0f, outerRadius * rootScale, 0f);
        }

        // The shared mesh behind a primitive, without leaving the throwaway GameObject in the scene.
        private static Mesh PrimitiveMesh(PrimitiveType type)
        {
            GameObject temp = GameObject.CreatePrimitive(type);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
            return mesh;
        }

        // ~~PROPS DROPPED IN ROOM2-2 TO BE ARRANGED BY HAND~~ REMOVED 2026-08-15, by request: the
        // pipe kit, the gear clock and the valve are out of the room, along with `PlaceProp`, which
        // had no other caller.
        //
        // They were three imports with no job, parked at placeholder positions until somebody designed
        // something for them. Nobody did, and an unplaced prop is not neutral - it is scenery the
        // player reads as a fixture and tries to use. The room's own fixtures (two taps, four buckets,
        // the tank) are all operable, so a valve that is only a decoration teaches exactly the wrong
        // thing about what E is for.
        //
        // The `.glb` files are still in `Assets/ArtAssets/Furniture` and still listed in
        // `docs/asset-licences.md`; nothing but the placing is gone, so putting one back is a call to
        // whatever places it.

        // ONE TAP: the model, the water it lets out, and the volume that answers E.
        //
        // `localPosition` is where the fixture sits and `yaw` turns it to face into the room. The
        // SPOUT IS MEASURED rather than passed in - each of these models has its outlet somewhere
        // different, and a hand-written offset is a number that silently stops being true the day the
        // model is swapped. The water falls from the front-bottom of the model's own bounds, which is
        // where a spout is on both of them.
        private static WaterTap BuildOneTap(Transform parent, string name, string modelPath,
                                            Vector3 localPosition, float yaw, float scale,
                                            float spoutHeightFraction, Vector3 spoutNudge,
                                            string handleNode,
                                            float leverSplitY = -999f,
                                            Vector3 leverPivotLocal = default)
        {
            GameObject holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPosition;
            holder.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source, holder.transform);
            model.name = "Visual";
            model.transform.localPosition = Vector3.zero;
            // ITS OWN IMPORT ROTATION IS LEFT ALONE, and forcing identity here was a real bug: both of
            // these glTF imports carry (270, 0, 0) on their root to bring a Z-up export into Unity's
            // Y-up, so overwriting it laid each tap on its back. The holder's yaw is what aims the
            // fixture; the model's own rotation is what makes it stand up at all.
            model.transform.localScale = Vector3.one * scale;

            // Measured in the HOLDER's frame, so the answer comes back in the same space the stream is
            // built in and no rotation has to be undone afterwards.
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            Bounds local = new Bounds(holder.transform.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
            foreach (Renderer r in renderers)
            {
                local.Encapsulate(holder.transform.InverseTransformPoint(r.bounds.min));
                local.Encapsulate(holder.transform.InverseTransformPoint(r.bounds.max));
            }

            // THE OUTLET. Across and front-to-back it can be measured - a spout is centred on its
            // fixture and points into the room. Its HEIGHT cannot: under a wall mixer the outlet is the
            // lowest thing on the model, and on a gooseneck it is near the top with the whole column
            // below it. That one number is the caller's to state.
            Vector3 spout = new Vector3(
                local.center.x,
                Mathf.Lerp(local.min.y, local.max.y, spoutHeightFraction),
                local.max.z - 0.03f) + spoutNudge;

            Material streamMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WaterStream.mat");

            // ONE ROOT FOR THE WHOLE FLOW - but NOT one switch: see WaterFlow. The stream stops with
            // the valve and the puddle it left behind dries in its own time, which one SetActive
            // cannot express.
            GameObject flow = new GameObject("WaterFlow");
            flow.transform.SetParent(holder.transform, false);

            // The floor, in the holder's own frame - below it by however high the fixture is mounted -
            // and how far the water has to fall to reach it.
            float floorY = -localPosition.y;
            float drop = spout.y - floorY;

            // THE FALL. A generated tube rather than a primitive cylinder: a cylinder is one segment
            // tall, so there is nothing down its length to taper or to break, and a stream of constant
            // width is the clearest possible statement that nothing is moving. See WaterMeshes.Stream.
            //
            // Width follows the tap, and the mesh is unit height so the drop is the Y scale.
            float streamWidth = Mathf.Max(0.05f, local.size.x * 0.10f);
            GameObject stream = new GameObject("Stream");
            stream.transform.SetParent(flow.transform, false);
            stream.transform.localPosition = new Vector3(spout.x, floorY + drop / 2f, spout.z);
            stream.transform.localScale = new Vector3(streamWidth, drop, streamWidth);
            stream.AddComponent<MeshFilter>().sharedMesh = SaveGeneratedMesh(
                "WaterStream_" + name,
                () => WaterMeshes.Stream(rings: 26, segments: 20, bottomScale: 0.62f,
                                         irregularity: 0.10f, seed: name.GetHashCode()));
            stream.AddComponent<MeshRenderer>().sharedMaterial = streamMat;

            // THE RUN, ON THE STREAM ITSELF, so it starts and stops with the water for free - the
            // source is under what `WaterFlow` switches, and nothing has to remember to stop it.
            //
            // LOST ONCE ALREADY, when the stream was rebuilt from a primitive into a generated mesh:
            // the audio lived on the object that was replaced, and a silent tap looks exactly like a
            // working one. It is attached here, next to the object it belongs to, for that reason.
            // Volume came back DOWN to 0.5 once the two AudioListeners were sorted out. It had been
            // pushed to 1 while the sound was not arriving at all, which was never a volume problem -
            // turning a broken thing up is how it ends up too loud the moment it is fixed.
            //
            // AND DOWN AGAIN TO 0.24, because the room grew. 0.5 was set against TWO taps; there are
            // four now and past selves leave them running, so late in a run every one of them can be
            // going at once - four copies of a loop that was tuned to be audible on its own. The
            // rolloff below is what keeps a running tap findable, so this is the number to spend,
            // not the range.
            AudioSource runSource = MakeSource(stream.transform, "RunAudio", spatialBlend: 1f,
                                               volume: 0.24f, loop: true);
            runSource.clip = LoadClip(SfxDir, "sfx_water_run");
            // Started by WaterFlow when the tap is opened, not by this flag - see WaterFlow.runSource.
            runSource.playOnAwake = false;
            // ROLLOFF, NOT JUST VOLUME. Unity's default logarithmic curve had this inaudible a couple
            // of metres out however loud it was set. Linear over a room's width means it is loud at the
            // tap and still clearly there from the doorway - which is the job, because a tap a past
            // self left running has to be findable by ear.
            runSource.rolloffMode = AudioRolloffMode.Linear;
            runSource.minDistance = 1.5f;
            runSource.maxDistance = 18f;

            // THE SPILL. Flat, and NOT A CIRCLE - see WaterMeshes.Spill. The ripple surface that used
            // to be here is gone: play called the moving water less natural than still water, which is
            // correct. Standing water in a sealed room does not undulate; what moves on it is the
            // highlight, and that now comes from the shader's noise rather than from the mesh.
            //
            // A millimetre off the floor, since two coplanar surfaces fight over the same depth.
            GameObject puddle = new GameObject("Spill");
            puddle.transform.SetParent(flow.transform, false);
            puddle.transform.localPosition = new Vector3(spout.x, floorY + 0.008f, spout.z);
            // THE DEPTH IS IN THE ASSET NAME, and it has to be: `SaveGeneratedMesh` serves from cache,
            // so changing a shape parameter without changing the name gets you the OLD mesh back and
            // the change silently does nothing. `BevelledPrismMesh` puts its chamfer in the name for
            // exactly this reason.
            puddle.AddComponent<MeshFilter>().sharedMesh = SaveGeneratedMesh(
                "WaterSpill_" + name + "_d45",
                // 45mm of standing water. Deep enough that the rim is a visible waterline from
                // standing height, which is the whole point of giving it a depth at all.
                () => WaterMeshes.Spill(rings: 8, segments: 40, irregularity: 0.22f, depth: 0.045f,
                                        seed: name.GetHashCode() ^ 77));
            puddle.AddComponent<MeshRenderer>().sharedMaterial = streamMat;
            SpreadingPuddle spreading = puddle.AddComponent<SpreadingPuddle>();

            // AND THE SPLASH WHEN IT IS WALKED THROUGH, on the spill rather than on the player: the
            // puddle is the only thing that knows how far it has spread, so it is the only thing that
            // can answer "is someone standing in me". Lost in the same rewrite as the run above.
            PuddleSplash splash = puddle.AddComponent<PuddleSplash>();
            splash.audioSource = MakeSource(puddle.transform, "SplashAudio", spatialBlend: 1f, volume: 0.7f);
            splash.splashClips = new[]
            {
                LoadClip(SfxDir, "sfx_water_splash_1"),
                LoadClip(SfxDir, "sfx_water_splash_2"),
                LoadClip(SfxDir, "sfx_water_splash_3"),
            };

            // DROPLETS, AND THEY ARE THE ONE SIMULATED THING IN THIS GAME. The exception is safe for
            // exactly the reason the no-physics rule exists: that rule is about objects the LOOP HAS
            // TO PUT BACK, and a settle that lands differently each iteration would break "a past self
            // does what you did". Spray is not put back, carried, recorded or interacted with.
            //
            // What it buys is the arc. A droplet thrown up and turned over by gravity is the thing no
            // scrolling texture can fake, and it is what says the water has weight.
            GameObject sprayGO = new GameObject("Spray");
            sprayGO.transform.SetParent(flow.transform, false);
            sprayGO.transform.localPosition = new Vector3(spout.x, floorY + 0.02f, spout.z);
            ParticleSystem spray = sprayGO.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule sprayMain = spray.main;
            sprayMain.loop = true;
            sprayMain.startLifetime = new ParticleSystem.MinMaxCurve(0.30f, 0.95f);
            // FASTER AND FURTHER, because play could not see it at all. Water hitting a hard floor at
            // this height throws a long way sideways, and the first pass was so slow and short-lived
            // that the droplets never cleared the stream they came out of.
            sprayMain.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 4.2f);
            // A WIDE RANGE OF SIZES, not one size jittered. Real spray is a few big slow drops among a
            // lot of fine fast ones, and a narrow range is what makes particles read as a "system".
            sprayMain.startSize = new ParticleSystem.MinMaxCurve(0.014f, 0.075f);
            sprayMain.gravityModifier = 1f;
            // Nearly opaque. Droplets are small and moving fast, and at low alpha against a white wall
            // they were invisible however many there were - the count was never the problem.
            sprayMain.startColor = new ParticleSystem.MinMaxGradient(new Color(0.90f, 0.95f, 1f, 0.92f));
            sprayMain.simulationSpace = ParticleSystemSimulationSpace.World;
            sprayMain.maxParticles = 600;

            ParticleSystem.EmissionModule sprayEmit = spray.emission;
            sprayEmit.rateOverTime = 150f;
            // Bursts on top of the steady rate, so the spray gusts instead of ticking over - the
            // regularity of a constant emitter is the tell.
            sprayEmit.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, new ParticleSystem.MinMaxCurve(10f, 26f), 1000, 0.15f),
            });

            // A shallow skirt rather than a cone from a point: the water arrives as a column and leaves
            // sideways, so droplets start at the stream's edge and go outward from there.
            ParticleSystem.ShapeModule sprayShape = spray.shape;
            sprayShape.shapeType = ParticleSystemShapeType.Cone;
            // Flatter, so the droplets go OUT rather than up: a narrow cone fires them back through
            // the falling stream, where they are lost against it.
            sprayShape.angle = 84f;
            sprayShape.radius = streamWidth * 0.6f;
            sprayShape.rotation = new Vector3(-90f, 0f, 0f);

            ParticleSystem.SizeOverLifetimeModule spraySize = spray.sizeOverLifetime;
            spraySize.enabled = true;
            spraySize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f));

            ParticleSystemRenderer sprayRend = sprayGO.GetComponent<ParticleSystemRenderer>();
            sprayRend.sharedMaterial = streamMat;
            sprayRend.renderMode = ParticleSystemRenderMode.Billboard;
            sprayRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sprayRend.receiveShadows = false;

            // WHAT THE TAP ACTUALLY SWITCHES. The stream and its impact go the instant the valve shuts;
            // the spill is handed to `SpreadingPuddle` and dries in its own time. The flow root itself
            // stays ACTIVE - switching it off would take the drying puddle with it, which is exactly
            // the behaviour this replaced.
            WaterFlow waterFlow = flow.AddComponent<WaterFlow>();
            waterFlow.falling = new[] { stream, sprayGO };
            waterFlow.puddle = spreading;
            waterFlow.runSource = runSource;
            // What a bucket standing under the tap changes - see WaterFlow.SetCatch. The spray is
            // "floor only" because it is water bouncing off a floor; caught in a bucket there is no
            // bounce, and the fall simply ends at the rim.
            waterFlow.stream = stream.transform;
            waterFlow.floorOnly = new[] { sprayGO };
            waterFlow.spoutLocalY = spout.y;
            waterFlow.floorLocalY = floorY;

            // WHERE THE E DISC HANGS, and where the press is answered from. On the fixture rather than
            // at the holder's origin, which for the wall tap is inside the wall.
            GameObject anchor = new GameObject("HintAnchor");
            anchor.transform.SetParent(holder.transform, false);
            anchor.transform.localPosition = local.center;

            GameObject interact = new GameObject(name + "Interact");
            interact.transform.SetParent(holder.transform, false);
            interact.transform.localPosition = local.center;
            BoxCollider trigger = interact.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            // Reach, not sight: tested against the player's own bounds, and it was tight enough before
            // that the player had to stand against the fixture for E to answer at all.
            trigger.center = new Vector3(0f, 0f, 0.8f);
            trigger.size = new Vector3(1.8f, 2.6f, 2.0f);

            WaterTap waterTap = interact.AddComponent<WaterTap>();
            waterTap.tapVisual = model.transform;
            waterTap.hintAnchor = anchor.transform;
            waterTap.waterFlow = waterFlow;
            // The valve itself - a short knock at the tap, separate from the run. It has to live
            // OUTSIDE the flow root, or the sound for shutting the water off would be switched off
            // by the same press that asks for it.
            waterTap.audioSource = MakeSource(interact.transform, "TapAudio", spatialBlend: 1f, volume: 0.7f);
            waterTap.openClip = LoadClip(SfxDir, "sfx_tap_open");
            waterTap.closeClip = LoadClip(SfxDir, "sfx_tap_close");

            // THE HANDLE, and the axis it turns about is MEASURED rather than assumed. A knob turns on
            // the spindle it is mounted on, which points away from the body of the tap - but which of
            // the node's own axes that is depends entirely on how the model was authored, and guessing
            // it wrong makes the handle scythe through the fixture instead of turning on it.
            // NO HANDLE NODE, SO ONE IS CUT OUT OF THE MESH. See SplitMeshAtHeight - the lever is a
            // separate cluster of geometry that merely shares a mesh with the body, and a plane through
            // it separates the two cleanly.
            if (string.IsNullOrEmpty(handleNode) && leverSplitY > -900f)
            {
                MeshFilter big = null;
                foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>())
                    if (mf.sharedMesh != null && (big == null || mf.sharedMesh.vertexCount > big.sharedMesh.vertexCount))
                        big = mf;

                if (big != null)
                {
                    (Mesh bodyMesh, Mesh leverMesh) = SplitMeshAtHeight(big.sharedMesh, name + "Faucet", leverSplitY);
                    Material shared = big.GetComponent<MeshRenderer>().sharedMaterial;
                    big.sharedMesh = bodyMesh;

                    // The pivot goes at the BASE of the lever, in the mesh's own space, and the lever
                    // is offset back by the same amount - so it renders exactly where it always did
                    // and turning the pivot swings it about its hinge rather than about the origin.
                    GameObject pivotGO = new GameObject("LeverPivot");
                    pivotGO.transform.SetParent(big.transform, false);
                    pivotGO.transform.localPosition = leverPivotLocal;

                    GameObject leverGO = new GameObject("Lever");
                    leverGO.transform.SetParent(pivotGO.transform, false);
                    leverGO.transform.localPosition = -leverPivotLocal;
                    leverGO.AddComponent<MeshFilter>().sharedMesh = leverMesh;
                    leverGO.AddComponent<MeshRenderer>().sharedMaterial = shared;

                    waterTap.handle = pivotGO.transform;
                    // A mixer lever goes UP and DOWN, which is a turn about the axis running across
                    // the fixture - the mesh's own X.
                    waterTap.handleAxis = Vector3.right;
                    waterTap.handleAngle = -28f;
                }
            }
            else if (!string.IsNullOrEmpty(handleNode))
            {
                Transform knob = FindDeep(model.transform, handleNode);
                if (knob == null)
                {
                    Debug.LogWarning($"[SceneBuilder] {name}: no '{handleNode}' node - handle will not turn.");
                }
                else
                {
                    waterTap.handle = knob;
                    // Outward, horizontally, from the fixture's centre line to the knob.
                    Vector3 outward = knob.position - holder.transform.TransformPoint(
                        new Vector3(local.center.x, knob.localPosition.y, local.center.z));
                    outward.y = 0f;
                    // Degenerate when the knob sits on the centre line - fall back to the column's own
                    // up, which is the other axis a tap handle ever turns about.
                    Vector3 worldAxis = outward.sqrMagnitude > 1e-6f ? outward.normalized : holder.transform.up;
                    waterTap.handleAxis = knob.InverseTransformDirection(worldAxis);
                }
            }

            return waterTap;
        }

        // CYCLE 2'S BUILDING, one storey below cycle 1's and running back the other way.
        //
        // THE Y OFFSET IS ON THE ROOT, and that is what made a second storey cheap. Every builder in
        // this file places its output with `localPosition` under the parent it is handed, so a parent
        // moved down carries all of them with it - `BuildRoomShell`, `BuildCeilingLights` and
        // `BuildReflectionProbe` needed no Y parameter and no change at all. The alternative was
        // threading a storey height through five signatures and every call site.
        //
        // A SIBLING OF "Room", not a child, and that is load-bearing rather than tidiness: the wall
        // panel gather in `Build` walks everything under `Room` and takes anything parented to a
        // "*_Panels" node. Built inside it, cycle 2's panels would silently join cycle 1's `ERROR`
        // glitch - a cycle failing in a room the player has not reached yet. Outside it, the two sets
        // are separate for free and each cycle gets its own display.
        //
        // Cycle 2 has ONE room and no puzzles yet. Its rooms are sealed on both walls for now; the
        // switchback means the next one goes at 4 * RoomPitch and this room grows a SOUTH doorway when
        // it does.
        // One cycle's wall panels, as the thing that boots them, flares them and finally fails them.
        private static WallPanelDisplay MakeWallPanelDisplay(string name, Renderer[] panels,
                                                             Texture2D testCard, Texture2D staticNoise)
        {
            GameObject go = new GameObject(name);
            WallPanelDisplay display = go.AddComponent<WallPanelDisplay>();
            display.panels = panels;
            display.offColor = WallPanelColor;
            display.onColor = PanelLitColor;   // must equal PanelWhite's albedo - see PanelLitColor
            display.testCard = testCard;
            display.staticNoise = staticNoise;
            return display;
        }
    }
}
