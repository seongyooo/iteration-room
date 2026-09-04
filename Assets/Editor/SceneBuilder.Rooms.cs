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
    // ROOM SHELLS AND THE GEOMETRY SHARED BETWEEN THEM - the ring room, the slide and the path a
    // ride takes down it, the empty room, the hatch assertions, and the flat panel every sign in
    // the building is printed on.
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {

        // ONE ROOM OF THE RING: a parent carrying its world position and facing, with the room built
        // at local zero inside it.
        //
        // **A parent per room rather than a parent per leg**, which is what makes the whole ring
        // reuse builders that only ever take a `zCenter`. `BuildRoomShell`, `BuildCeilingLights` and
        // `BuildReflectionProbe` know nothing about X or rotation; handed a rotated, positioned parent
        // and a zCenter of zero, every one of them lands correctly with no new parameters. The
        // alternative was threading an X and a yaw through five signatures and every call site.
        //
        // Travel is always local -Z, so every room's NORTH wall is where you came in and its SOUTH
        // wall is where you leave - except at a corner, where you leave through the WEST wall.
        // **West is always the way on**, either to the next room or, mid-leg, onto the core.
        private static Transform BuildRingRoom(Transform parent, string name, float x, float z, float yaw,
                                               Material floorMat, Material grooveMat, Material panelMat,
                                               Rect south, Rect north, Rect west,
                                               Rect ceilingHole = default)
        {
            GameObject holder = new GameObject(name + "_Root");
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = new Vector3(x, 0f, z);
            holder.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            BuildRoomShell(holder.transform, name, 0f, floorMat, grooveMat, panelMat,
                south, north, west, Rect.zero, Rect.zero, ceilingHole);

            return holder.transform;
        }

        // A floor or ceiling piece of arbitrary footprint, optionally holed.
        //
        // `BuildSlab` is the room-shaped version and is left exactly as it was, so every standard
        // shell goes on emitting the single cube it always emitted. This one exists because the tree
        // hall is not a room shape: a 9m bay and a 21m run at two different ceiling heights.
        private static void BuildSlabRect(Transform t, string name, float yCenter, Rect xz,
                                          Material mat, Rect hole = default)
        {
            var parts = SubtractRect(xz, hole);

            if (parts.Count == 1)
            {
                Prim(PrimitiveType.Cube, name, t, new Vector3(xz.center.x, yCenter, xz.center.y),
                     new Vector3(xz.width, WallThickness, xz.height), mat);
                return;
            }

            int piece = 0;
            foreach (Rect part in parts)
            {
                if (part.width <= 0.001f || part.height <= 0.001f) continue;
                Prim(PrimitiveType.Cube, $"{name}_{++piece}", t,
                     new Vector3(part.center.x, yCenter, part.center.y),
                     new Vector3(part.width, WallThickness, part.height), mat);
            }
        }


        // ROOM2-5'S SLIDE - and this one is not scenery.
        //
        // It furnished room2-6 until 2026-08-19, when it was dragged onto the tree hall's far ledge
        // by hand and set against the south wall with its chute going THROUGH it. That makes it the
        // way into the room beyond, and the only way: see BuildSlideRoom for what is on the other
        // side and SlideRide for what riding it does.
        //
        // THE POSE IS A READ-BACK, NOT A FIT. The old `BuildPlayground` derived its scale from the
        // room's door lanes, which is the right rule for scenery standing in an empty shell and the
        // wrong one for a prop that has to line up with a hole in a wall. This one was placed by eye
        // and measured afterwards - see SlideLocalPosition for the three things those numbers mean.
        //
        private static void BuildSlide(Transform hall)
        {
            string path = PlayDir + "/slide_playground.glb";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null)
            {
                Debug.LogError($"[SceneBuilder] {path} is missing - room2-5 keeps the hole in its "
                             + "wall and loses the only thing leading through it.");
                return;
            }

            ShrinkModelTextures(path, 1024);

            GameObject root = new GameObject("Room2_5_Slide");
            root.transform.SetParent(hall, false);
            root.transform.localPosition = SlideLocalPosition;

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            model.name = "Slide";

            // WRITTEN ONTO THE IMPORTED INSTANCE, which is the one case where that is the right thing
            // to do. The usual rule is a wrapper - a glTF root's own rotation is load-bearing and
            // overwriting it makes the model answer a rotation with a translation. What is written
            // here is that node's OWN pose, read back off the editor with the correction already in
            // it, so it reproduces the hand placement exactly instead of composing with it.
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(SlideLocalEuler);
            model.transform.localScale = Vector3.one * SlideLocalScale;

            // SOLID: a slide you walk through is a poster, the stairs are how the head of the chute is
            // reached at all, and the ride below is measured by raycasting these very colliders. Mesh
            // colliders on the parts as they are - convex would swallow the space under the deck and
            // fill in the chute itself.
            int solid = 0;
            foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                solid++;
            }

            Vector3[] ride = SampleRidePath(hall, model.transform);
            if (ride == null) return;

            // THE TRIGGER IS THE HEAD OF THE CHUTE, not the stairs and not the deck. The stairs' top
            // step is a metre east of it, so climbing does not commit you to anything; stepping off
            // that step onto the chute's platform does, which is what getting on a slide is.
            //
            // Tall enough to catch a standing player's capsule from the feet up - the ride starts on
            // ENTRY, so a volume that only reached the knees would be walked over between frames.
            const float triggerHeight = 2.2f;
            GameObject rideGO = new GameObject("Room2_5_SlideRide");
            rideGO.transform.SetParent(hall, false);

            // **THE WHOLE CHUTE ANSWERS, NOT JUST ITS TOP** (2026-08-29, by request: getting on
            // halfway down should work).
            //
            // One box at `ride[0]` meant the only way onto the slide was over the lip at the head of
            // it - step onto the middle of the chute, which is a solid surface a player can simply
            // walk onto, and nothing happened at all.
            //
            // **A BOX PER SAMPLE RATHER THAN ONE BIG ONE.** The chute DESCENDS, so a single box round
            // the whole run would be metres tall and would catch anybody walking underneath it. These
            // follow the surface instead. They can be axis-aligned and unrotated because the chute is
            // a straight line in XZ with only its height changing - which is a fact about this slide,
            // and the reason it is safe to say so here rather than in `SlideRide`.
            //
            // **ONLY THE CHUTE.** `SlideRideSamples` of the path is chute and the rest is the plunge
            // into the water; a trigger over the plunge would restart a ride on somebody standing in
            // the pool where the last one dropped them.
            int chutePoints = Mathf.Min(SlideRideSamples, ride.Length);
            float step = chutePoints > 1
                ? Vector3.Distance(ride[0], ride[chutePoints - 1]) / (chutePoints - 1) : 1f;

            int boxes = 0;
            for (int i = 0; i < chutePoints; i += 3)
            {
                BoxCollider box = rideGO.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = new Vector3(ride[i].x, ride[i].y + triggerHeight / 2f, ride[i].z);
                // Deep enough to overlap its neighbours: a gap between two of these is a stretch of
                // chute that does nothing, which is the bug being fixed.
                box.size = new Vector3(1.0f, triggerHeight, Mathf.Max(1.0f, step * 4.2f));
                boxes++;
            }

            // The path is in the hall's frame and this object sits at the hall's origin, so what the
            // component stores and what was measured are the same numbers.
            SlideRide rideComp = rideGO.AddComponent<SlideRide>();
            rideComp.path = ride;

            Debug.Log($"[SceneBuilder] Slide: {solid} solid part(s); ride of {ride.Length} points, "
                    + $"{ride[0].y:0.00}m down to {ride[ride.Length - 1].y:0.00}m over "
                    + $"{Mathf.Abs(SlideRideLanding.y - SlideRideHead.y):0.00}m. "
                    + $"{boxes} entry box(es) along {chutePoints} chute point(s) - one would mean the "
                    + "slide can only be joined at the top again.");
        }

        // THE RIDE, MEASURED OFF THE CHUTE RATHER THAN DESCRIBED.
        //
        // A downward ray at every step along the line the rider takes, so each height in the path is
        // whatever is actually underneath it: the chute's surface while there is chute, the landing
        // room's floor once there is not. The only numbers written down are where the line starts,
        // where it leaves the hall and where it stops.
        //
        // IT FAILS THE BUILD IF THE CHUTE IS NO LONGER UNDER IT, because that is not a visible fault:
        // the ride would still run and the player would still arrive in the room, and the only
        // symptom is a rider gliding through the air a foot above the slide.
        private static Vector3[] SampleRidePath(Transform hall, Transform slide)
        {
            // Colliders built this frame are not in the physics scene until it is told about them -
            // the same line `AssertWalkable` opens with, and for the same reason.
            Physics.SyncTransforms();

            // WHERE THE RAY STARTS, and it is a narrower window than it looks - and now TWO windows,
            // one per leg, because the two legs are no longer under the same ceiling.
            //
            // LEG 0, the chute, is still entirely inside the hall: has to clear the head of the chute
            // at 2.8m and stay UNDER the hall's own ceiling - the first version dropped from 6m, which
            // is fine for the 17.5m hall but lands on the roof of a same-level room the run-out used
            // to cross, and the ride came out sloping UP to 5.51m.
            //
            // LEG 1, the plunge, is inside room2-5's landing room, which sits BELOW the hall with its
            // ceiling at the top of the mouth - so a ray dropped from hall height would hit that
            // ceiling before it ever reached the floor beneath it. Starting just under it instead
            // keeps the ray inside the room it is actually meant to be measuring.
            const float probeFromHall = 4.5f;
            float probeFromRoom = SlideRoomFloorY + RoomHeight - 0.05f;
            // Generous rather than derived from either probe height: leg 0 drops a few metres to the
            // hall's own floor, leg 1 drops the better part of a storey to room2-5's - one constant
            // that comfortably clears both beats two numbers each tied to a probe height that used to
            // double as "how far below the floor is safe to still call a hit".
            const float probeMaxDistance = 12f;
            var points = new System.Collections.Generic.List<Vector3>();
            int onChute = 0, missed = 0;

            // The chute, then the plunge. Two passes rather than one because they are two different
            // things: leg 0 is measured off the slide, and leg 1 is a fall to a floor that is measured
            // only to know where it ENDS.
            //
            // LEG 0 TAKES THE SLIDE'S OWN SURFACE, not the first thing under the line. A single
            // `Raycast` takes the highest hit, and once the landing room came up to meet the mouth its
            // CEILING SLAB is the highest thing under the last two samples - so the measured path
            // climbed from the chute at 0.53m onto the roof of the room at 1.80m and the ride carried
            // the player over the top of the level, outside it, before dropping them in. It played
            // exactly as it reads. `RaycastAll` and prefer the slide.
            float chuteExitY = 0f;
            for (int leg = 0; leg < 2; leg++)
            {
                Vector2 from = leg == 0 ? SlideRideHead : SlideRideFoot;
                Vector2 to = leg == 0 ? SlideRideFoot : SlideRideLanding;
                int steps = leg == 0 ? SlideRideSamples : SlideRidePlungeSamples;
                float probeFrom = leg == 0 ? probeFromHall : probeFromRoom;

                for (int i = leg == 0 ? 0 : 1; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    Vector2 xz = Vector2.Lerp(from, to, t);
                    Vector3 at = hall.TransformPoint(new Vector3(xz.x, probeFrom, xz.y));

                    RaycastHit[] hits = Physics.RaycastAll(at, hall.TransformDirection(Vector3.down),
                                                           probeMaxDistance, ~0,
                                                           QueryTriggerInteraction.Ignore);
                    bool found = false;
                    float surfaceY = 0f;
                    float best = float.NegativeInfinity;
                    bool bestIsChute = false;

                    foreach (RaycastHit hit in hits)
                    {
                        float y = hall.InverseTransformPoint(hit.point).y;
                        bool isChute = slide != null && hit.transform.IsChildOf(slide);
                        // On leg 0 the slide wins outright, whatever is above it. Everywhere else the
                        // highest surface wins, which is the floor of whichever room the ray is in.
                        bool better = !found
                                   || (leg == 0 && isChute && !bestIsChute)
                                   || (y > best && (leg != 0 || isChute == bestIsChute));
                        if (!better) continue;

                        found = true;
                        surfaceY = y;
                        best = y;
                        bestIsChute = isChute;
                    }

                    if (found)
                    {
                        if (leg == 0)
                        {
                            if (bestIsChute) { onChute++; chuteExitY = surfaceY; }
                            // ON THE CHUTE OR NOWHERE. A leg-0 sample that found something else has
                            // found a surface the rider must not be put on - it is what happened with
                            // the landing room's ceiling - so the height is HELD at the last known one
                            // rather than stepped up onto it. The `onChute` count below is what says
                            // so out loud.
                            else if (points.Count > 0) surfaceY = points[points.Count - 1].y;
                        }
                    }
                    else
                    {
                        // Nothing under the ride at all. Carried at the last known height rather than
                        // dropped to zero, which would put a step in the path instead of a warning.
                        missed++;
                        surfaceY = points.Count > 0 ? points[points.Count - 1].y : 0f;
                    }

                    // LEG 1 IS AN ARC, NOT A FLOOR. Every sample of it raycasts the same floor, so
                    // taking the surface directly gave a path that stood at the mouth and then fell
                    // four metres straight down between two waypoints - a drop with a slide attached
                    // to it. The height is interpolated from the chute's exit down to the floor on a
                    // SQUARED curve instead: shallow where it leaves the chute, steepening as it goes,
                    // which is the shape of something thrown rather than something released. The
                    // raycast still decides where it ends and still clamps it - nothing may pass
                    // through the floor.
                    if (leg == 1)
                    {
                        float arc = Mathf.Lerp(chuteExitY, surfaceY, t * t);
                        surfaceY = Mathf.Max(surfaceY, arc);
                    }

                    points.Add(new Vector3(xz.x, surfaceY, xz.y));
                }
            }

            if (points.Count < 2)
            {
                Debug.LogError("[SceneBuilder] the slide's ride path came out empty.");
                return null;
            }

            if (onChute < SlideRideSamples / 2)
                Debug.LogError($"[SceneBuilder] the slide's ride path only touches the chute at "
                             + $"{onChute} of {SlideRideSamples + 1} samples - it has moved out from "
                             + "under the line in SlideRideHead/SlideRideFoot.");

            if (missed > 0)
                Debug.LogError($"[SceneBuilder] the slide's ride path has {missed} sample(s) with "
                             + "nothing underneath - the run-out is off the landing room's floor.");

            // A SLIDE GOES DOWN. Anything the sampler found ABOVE the head it started at is something
            // it was never meant to be measuring - a ceiling, a rail, the underside of a deck - and
            // the symptom is a ride that carries the player upward through the room. See probeFrom
            // for the version of this that actually happened.
            foreach (Vector3 point in points)
            {
                if (point.y <= points[0].y + 0.05f) continue;
                Debug.LogError($"[SceneBuilder] the slide's ride path climbs to {point.y:0.00}m, "
                             + $"above the {points[0].y:0.00}m it starts at - it is measuring "
                             + "something that is not the chute.");
                break;
            }

            return points.ToArray();
        }

        // WHAT THE SLIDE LANDS IN. Room2-5's own room, south of the tree hall's west end, reached by
        // riding the slide through the hall's south wall and left through a door back into the hall.
        //
        // BUILT HERE RATHER THAN THROUGH `BuildRingRoom` for one reason: its north wall needs TWO
        // openings - the chute's mouth and the doorway back - and `BuildPanelWall` takes one cutout.
        // Everything else is the standard helpers at the standard sizes, so it is the same floor, the
        // same panelling, the same lighting and the same probe as every other room in the building.
        //
        // NO PUZZLE, NO CARRYABLE, NO SIGNAL: it touches neither the loop nor the reset. What goes in
        // it is a later decision, and until then it is a room that exists.
        private static (Transform room, Door door, PoolDrain drain, Valve[] valves)
            BuildPoolRoom(Transform parent, Material floorMat, Material grooveMat, Material panelMat,
                          Material propMat, Material fixtureMat)
        {
            GameObject rootGO = new GameObject("Room2_6_Root");
            rootGO.transform.SetParent(parent, false);
            // BELOW THE HALL, BY EXACTLY WHAT PUTS ITS CEILING AT THE TOP OF THE MOUTH. It sat beside
            // the hall at the hall's own floor height, then a full `StoreyDrop` under it, and neither
            // connected: see `SlideRoomFloorY` for why the depth is derived from the opening now
            // rather than picked.
            rootGO.transform.localPosition =
                new Vector3(SlideRoomCentreX, SlideRoomFloorY, SlideRoomCentreZ);

            GameObject roomGO = new GameObject("Room2_6");
            roomGO.transform.SetParent(rootGO.transform, false);
            Transform t = roomGO.transform;

            // THE FLOOR IS CUT FOR THE DRAIN. `BuildSlab` subtracts one Rect, which is why the hole is
            // square and the grate is what makes it read as a drain.
            BuildSlab(t, "Floor", -WallThickness / 2f, 0f, floorMat,
                      new Rect(-DrainSize / 2f, -DrainSize / 2f, DrainSize, DrainSize));
            BuildSlab(t, "Ceiling", RoomHeight + WallThickness / 2f, 0f, CeilingMaterial(), default);

            // THE NORTH WALL IS THE SHARED ONE, and its one opening sits AGAINST THE CEILING - the top
            // 1.7m of the wall, which is now the same 1.7m band the hall's own opening covers, because
            // the room's height below the hall is derived from exactly that (`SlideRoomFloorY`). The
            // two cut the same rectangle out of two walls a pocket apart, so they are one hole with a
            // short tunnel in it. Same X as the hall's opening, shifted into this room's frame by the
            // one offset below rather than written down twice.
            //
            // NO DOOR YET, and the wall is back to ONE span rather than two because of it: the old
            // "way back" doorway assumed the room on the other side was at the same height, and it no
            // longer is. Its doorway, its pocket and the walkability asserts that checked it are gone
            // with the floor-level entrance - `SlideRoomDoorX`/`SlideRoomWallSplitX` are unused until
            // the exit is designed for a room that is now a storey down instead of next door.
            float mouthFrom = TreeHallWestFace - SlideRoomCentreX;
            float mouthTo = SlideMouthEast - SlideRoomCentreX;

            BuildPanelWall(t, "Wall_North", new Vector3(0f, 0f, RoomDepth / 2f),
                Vector3.right, Vector3.back, RoomWidth, grooveMat, panelMat,
                Rect.MinMaxRect(mouthFrom, RoomHeight - SlideMouthHeight, mouthTo, RoomHeight));

            // THE TUNNEL BETWEEN THE TWO WALLS. The hall's wall and this one are a door pocket apart,
            // and with a hole cut through both that gap is open on all four sides - looking through the
            // mouth at any angle but straight ahead you see along a slot with the sky at the end of it.
            // Thin pieces line it, so the hole reads as a hole through a thick wall, which is what it is.
            //
            // EVERY PIECE IS SIZED TO WHAT IS ACTUALLY OPEN, and the first version was not: it spanned
            // the whole pocket and so lay ON TOP of two slabs that already reach into it, which is two
            // pairs of coplanar faces and the flicker play reported at the mouth. What reaches in:
            //   - `BuildSlab` runs to the room PITCH, not its depth, so this room's ceiling already
            //     covers half a pocket past its own north wall;
            //   - the walls themselves have bodies - the hall's south wall hangs a `WallDepth` south of
            //     its face, which is the north end of the gap.
            // So the genuinely open span is from this room's wall face to the hall's, and the head only
            // has to close what the ceiling slab does not reach. Both are derived below rather than
            // measured, so a change to the pocket or the wall depth cannot re-open the slot.
            //
            // NOT a `BuildPanelWall`: these are the reveals of an opening rather than a surface of the
            // room, and panelling them would put grooves on strips nobody stands square to.
            float pocket = 2f * WallDepth + DoorPocketDepth;
            // FROM THE BACK OF ONE WALL TO THE BACK OF THE OTHER, which is a much narrower span than
            // the one between their FACES - 0.1m rather than 0.35 - and getting that wrong is what
            // play saw flickering at the mouth twice.
            //
            // **A WALL'S BACKING SLAB SITS BEHIND ITS FACE, OUTSIDE THE ROOM**: `BuildPanelWall` puts
            // it at `GrooveDepth + WallThickness/2` back from the face it is given, so the wall's body
            // is in the pocket rather than in the room. Two of them, one per side, eat 0.25 of the
            // 0.35, and anything lining the pocket across the whole of it is laid straight through
            // both - coplanar faces, and the flicker moves with the camera. Measured off the built
            // scene rather than reasoned about, after reasoning about it got it wrong once.
            float wallBody = GrooveDepth + WallThickness;
            float gapFrom = RoomDepth / 2f + wallBody;                        // back of this room's wall
            float gapTo = RoomDepth / 2f + pocket - wallBody;                 // back of the hall's
            float ceilingReach = RoomPitch / 2f;                   // how far the ceiling slab overruns
            float mouthMid = (mouthFrom + mouthTo) / 2f, mouthSpan = mouthTo - mouthFrom;
            // The sill is level with the hall's floor, which in this room's frame is `-SlideRoomFloorY`
            // - the one conversion between the two storeys.
            float sillY = -SlideRoomFloorY;

            GameObject tunnel = new GameObject("MouthTunnel");
            tunnel.transform.SetParent(t, false);

            if (gapTo - ceilingReach > 0.001f)
                Prim(PrimitiveType.Cube, "Head", tunnel.transform,
                     new Vector3(mouthMid, RoomHeight + WallThickness / 2f,
                                 (ceilingReach + gapTo) / 2f),
                     new Vector3(mouthSpan + WallThickness * 2f, WallThickness,
                                 gapTo - ceilingReach), floorMat);

            Prim(PrimitiveType.Cube, "Sill", tunnel.transform,
                 new Vector3(mouthMid, sillY - WallThickness / 2f, (gapFrom + gapTo) / 2f),
                 new Vector3(mouthSpan + WallThickness * 2f, WallThickness, gapTo - gapFrom), floorMat);

            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? mouthFrom - WallThickness / 2f : mouthTo + WallThickness / 2f;
                Prim(PrimitiveType.Cube, side == 0 ? "Reveal_West" : "Reveal_East", tunnel.transform,
                     new Vector3(x, (sillY + RoomHeight) / 2f, (gapFrom + gapTo) / 2f),
                     new Vector3(WallThickness, RoomHeight - sillY, gapTo - gapFrom), floorMat);
            }

            // THE WAY ON IS THE SOUTH WALL - straight ahead of the slide. The chute comes through the
            // north wall heading south, so a rider lands facing this door: the room says where it goes
            // before the player has turned round.
            BuildPanelWall(t, "Wall_South", new Vector3(0f, 0f, -RoomDepth / 2f),
                Vector3.right, Vector3.forward, RoomWidth, grooveMat, panelMat,
                new Rect(-DoorWidth / 2f, 0f, DoorWidth, DoorHeight));
            BuildPanelWall(t, "Wall_West", new Vector3(-RoomWidth / 2f, 0f, 0f),
                Vector3.forward, Vector3.right, RoomDepth, grooveMat, panelMat, Rect.zero);
            BuildPanelWall(t, "Wall_East", new Vector3(RoomWidth / 2f, 0f, 0f),
                Vector3.forward, Vector3.left, RoomDepth, grooveMat, panelMat, Rect.zero);

            BuildCeilingLights(t, "Room2_6", 0f, fixtureMat, castShadows: true);
            BuildReflectionProbe(t, "Room2_6", 0f);

            // AND IT IS FULL OF WATER, which is what the slide lands in.
            WaterPool pool = BuildWaterPool(t);

            // --- the door ---------------------------------------------------------------------------
            // **NOT CAPPED.** It was, for the one build where there was nothing on the other side, and
            // `capFarSide` does exactly what it says: it puts a SOLID WALL across the far mouth of the
            // pocket. Room2-7 was built behind this door and the cap was left on, so the room existed,
            // was lit, was probed - and was invisible and unreachable behind a wall in the doorway.
            //
            // The rule: `capFarSide` is true only where the walk ENDS. Every join with a room through
            // it is false.
            Door door = BuildPadDoor(t, "Door2_6", 0f, new FloorButton[0], propMat, yaw: 180f);
            BuildDoorPocketFill(t, "Pocket2_6", 0f, grooveMat, capFarSide: false, yaw: 180f);

            // --- the three valves -------------------------------------------------------------------
            // ONE PER WALL THAT HAS NO DOOR, centred, at a height a standing player turns a wheel at.
            // Spread as far apart as this room allows on purpose: six turns is more than one iteration
            // holds, and what makes that interesting rather than tedious is that the three of them
            // cannot be reached by one person in one minute. A past self at the east wheel is the
            // room being solved.
            var valves = new Valve[3];
            valves[0] = BuildValve(t, "Valve2_6_North", new Vector3(0f, ValveHeight, RoomDepth / 2f), 180f);
            valves[1] = BuildValve(t, "Valve2_6_East", new Vector3(RoomWidth / 2f, ValveHeight, 0f), 270f);
            valves[2] = BuildValve(t, "Valve2_6_West", new Vector3(-RoomWidth / 2f, ValveHeight, 0f), 90f);

            // --- the drain, and the rule ------------------------------------------------------------
            PoolDrain drain = BuildDrain(t, pool, valves, floorMat, propMat);
            door.condition = drain;

            return (t, door, drain, valves);
        }

        // ROOM2-7: THE WEIGHING ROOM, through room2-6's door.
        //
        // ONE ROOM, ONE SCALE, ONE NUMBER. Everything the player has learned to carry is worth
        // something here and they are told none of it - see the weight constants for why the numbers
        // are what they are, and `WeighScale` for why the room is a measuring instrument before it is
        // a lock.
        //
        // BUILT LIKE ROOM2-6 rather than through `BuildRingRoom`: it is off the ring entirely, hung
        // south of the pool at the pool's own depth, so it takes its parent's frame and its own
        // lights and probe. Its NORTH wall carries the doorway, which is the same opening room2-6's
        // south wall has - one door, two rooms, and the pocket between them belongs to room2-6.
        // A PLAIN SHELL, one room further on, so room2-7's door has somewhere to lead. Empty by
        // design and by admission: it is where cycle 2 continues, and what goes in it is the next
        // decision rather than something to guess at now. Its own door is the capped end of the walk.
        // `doorwayNorth` is the original one-doorway form and stays for every caller that has one.
        // The four explicit cutouts are for a room whose openings are not doorways at all - room3-1
        // has a gate in all four walls (see BuildPanelGate) - and they are separate arguments rather
        // than a replacement so that a room asking for the ordinary thing still says so in one word.
        private static Transform BuildEmptyRoom(Transform parent, string name, Vector3 at,
                                                Material floorMat, Material grooveMat,
                                                Material panelMat, Material fixtureMat,
                                                bool doorwayNorth, Rect floorHole = default,
                                                Rect ceilingHole = default,
                                                Rect northCutout = default, Rect southCutout = default,
                                                Rect westCutout = default, Rect eastCutout = default)
        {
            GameObject rootGO = new GameObject(name + "_Root");
            rootGO.transform.SetParent(parent, false);
            rootGO.transform.localPosition = at;

            GameObject roomGO = new GameObject(name);
            roomGO.transform.SetParent(rootGO.transform, false);
            Transform t = roomGO.transform;

            Rect doorway = new Rect(-DoorWidth / 2f, 0f, DoorWidth, DoorHeight);

            BuildSlab(t, "Floor", -WallThickness / 2f, 0f, floorMat, floorHole);
            BuildSlab(t, "Ceiling", RoomHeight + WallThickness / 2f, 0f, CeilingMaterial(), ceilingHole);

            BuildPanelWall(t, "Wall_North", new Vector3(0f, 0f, RoomDepth / 2f),
                Vector3.right, Vector3.back, RoomWidth, grooveMat, panelMat,
                doorwayNorth ? doorway : northCutout);
            BuildPanelWall(t, "Wall_South", new Vector3(0f, 0f, -RoomDepth / 2f),
                Vector3.right, Vector3.forward, RoomWidth, grooveMat, panelMat, southCutout);
            BuildPanelWall(t, "Wall_West", new Vector3(-RoomWidth / 2f, 0f, 0f),
                Vector3.forward, Vector3.right, RoomDepth, grooveMat, panelMat, westCutout);
            BuildPanelWall(t, "Wall_East", new Vector3(RoomWidth / 2f, 0f, 0f),
                Vector3.forward, Vector3.left, RoomDepth, grooveMat, panelMat, eastCutout);

            BuildCeilingLights(t, name, 0f, fixtureMat, castShadows: true);
            BuildReflectionProbe(t, name, 0f);
            return t;
        }

        // ROOM2-0: THE LAST ROOM OF CYCLE 2, AND THE ONE THAT BREAKS IT.
        //
        // The same white cell as every other, and the same argument cycle 1's room4 makes for that:
        // the room the player finally gets out into looks exactly like the ones they have been trying
        // to get out of. What makes it the end is that nothing is in it until they walk in.
        //
        // FOUR PEDESTALS ON ONE MOVER, not four movers. `RewardPlinth` raises a single Transform, and
        // the four are children of it - so they come up together as one machine rather than as four
        // props that happen to agree, and `MovesDuringPlay` (which walks UP from each renderer) finds
        // the plinth above all of them without any of them being named.
        //
        // WHAT EACH ONE WANTS is the count of the object drawn on its side: five axes, three ducks,
        // four buckets, two beach balls. The number is never written down anywhere in the building
        // and cannot be - it is the player's own memory of four rooms they have already crossed, which
        // is the only kind of key a time loop can ask for that a past self cannot fetch for them.
        //
        // THE ORDER OF THE ROW IS NOT THE ORDER OF THE ANSWERS (5, 3, 4, 2 left to right). A row that
        // read 2, 3, 4, 5 would be solvable by noticing that it counts.
        // CYCLE 3'S FIRST ROOM, AND SO FAR ITS ONLY ONE.
        //
        // A bed, a chest with nothing in it, and four gas emitters - which is exactly what room2-1 was
        // on the day cycle 2 was started, and deliberately so: what a cycle opens with is a person
        // waking up in a cell, and that is the same sentence every time. The chest is the cycle 2
        // build (`BuildDresser`, two opening bays and a cube on top) with `withBilliards` off, because
        // what goes in a drawer belongs to a puzzle that has not been designed.
        //
        // NO DOORWAY, NO DOOR, NO CONDITION. The room is sealed, `Cycle.finalRoom` is null and the
        // cycle cannot be finished - all three are supported states that cycle 2 shipped in for weeks
        // (see `AssembleCycleTwo`). The loop simply keeps iterating here, which is the honest state of
        // a cycle with no puzzles in it.
        private static (Transform root, Transform bedSpawn, ParticleSystem[] gas, Transform room,
                        GhostInteractable[] signals, CrushingBarrier northBarrier, BeamLift[] lifts)
            BuildCycleThreeShell(Material floorMat, Material grooveMat, Material panelMat,
                                 Material propMat)
        {
            // ITS OWN FIXTURE MATERIAL, like cycle 2's. `CeilingFixture` is one emissive material
            // shared by every room in a cycle, and `ChessReward` dims cycle 1's through a property
            // block - a third cycle borrowing either would be a third cycle that goes dark when a
            // board on another storey is finished.
            Material fixtureMat = MakeEmissiveMaterial("CeilingFixtureCycle3", Color.white, CeilingPanelEmission);

            GameObject root = new GameObject("Room_Cycle3");
            // AXIS-ALIGNED, unlike cycle 2. That cycle turns 180 about its own bed so its hall can be
            // tall without switchbacking under cycle 1; cycle 3 has one room and nothing to avoid, so
            // it is left square to the world and the next room it grows can decide for itself.
            root.transform.position = new Vector3(CycleThreeX, CycleThreeFloorY, CycleThreeZ);

            // THE HOLE THE PLAYER ARRIVES THROUGH. Its lid is NOT here - it lives on the join in the
            // core scene with the floor lid above it, exactly as cycle 1's does, because one
            // `CycleExit` drives both and a reference across a scene boundary comes back null. What
            // this room owns is the absence.
            // A GATE IN ALL FOUR WALLS, and no door anywhere in the room.
            //
            // A gate is measured off the wall's own grid rather than off the room - see `GateSpan`.
            // TWO CUTOUTS, NOT ONE. The north and south walls are 8.75 across and the east and west
            // 10.5, which is five cells against six - and `GateSpan` places the opening on the grid,
            // so the two are not the same rect. Both sides of a shared wall take the SAME one, which
            // is why it is computed from the wall's width rather than written twice.
            Rect gateNS = GateCutout(RoomWidth);
            Rect gateEW = GateCutout(RoomDepth);

            Transform r1 = BuildEmptyRoom(root.transform, "Room3_1", Vector3.zero,
                                          floorMat, grooveMat, panelMat, fixtureMat,
                                          doorwayNorth: false, ceilingHole: CycleTwoExitHole,
                                          northCutout: gateNS, southCutout: gateNS,
                                          westCutout: gateEW, eastCutout: gateEW);

            // THE FOUR ROOMS THE GATES OPEN ONTO. Empty shells for now - what goes in them is the
            // puzzle, and this is the way in.
            //
            // Each carries the matching cutout on the wall it shares with room3-1 and nothing else,
            // so a room is sealed except through its own gate. Their walls run the same way room3-1's
            // do (they are not turned to face it): a room to the east is still 8.75 across and 10.5
            // deep, which is what makes `RoomPitchX` the room's WIDTH plus the shared build-up.
            // TWICE ACROSS, TWICE DEEP, THREE TIMES TALL - ten cells, twelve cells, twelve rows,
            // every one an exact multiple of the grid the rest of the building uses.
            //
            const float bigWidth = 2f * RoomWidth;      // 17.5, ten cells
            const float bigDepth = 2f * RoomDepth;      // 21.0, twelve cells
            const float bigHeight = 3f * RoomHeight;    // 16.2234, twelve rows

            // **THE ROOM IS OFFSET HALF A CELL, and the corridor is not.** Room3-1's north wall has an
            // ODD cell count, so its middle cell is centred on the building's axis and the corridor
            // sits on it exactly. This room's south wall has an EVEN one - ten cells - so that axis
            // falls on a cell BOUNDARY here, and a centred opening straddled two cells and left a
            // half-panel either side. Play saw it immediately.
            //
            // Moving the ROOM rather than the corridor is what fixes it: shifted by half a cell, the
            // building's axis lands on the middle of one of this wall's cells and the mouth is exactly
            // one whole panel. A full cell would have changed nothing - the parity is what matters,
            // not the distance. The corridor stays where it is, so it is still centred in room3-1 and
            // merely enters this room half a cell off its centreline, which nothing can see.
            const float bigOffsetX = GridCellWidth / 2f;
            float mouthLocalX = -bigOffsetX;
            float halfMouth = gateNS.width / 2f;
            Rect corridorMouth = Rect.MinMaxRect(mouthLocalX - halfMouth, 0f,
                                                  mouthLocalX + halfMouth, GateHeight);

            // PLANNED BEFORE THE ROOM IS BUILT, because the walls have to be holed where the steps
            // go and the steps have to land in those holes. One plan, read twice.
            // TWO HOLES IN THIS ROOM, and they are its two ways on. The ceiling carries the ladder
            // shaft up to room3-0; the floor is room4-1's whole ceiling and opens at the break.
            Rect ladderHole = LadderShaftHole(LadderShaftXZ.x, LadderShaftXZ.y);

            Transform rN = BuildBigRoom(root.transform, "Room3_2N",
                           new Vector3(bigOffsetX, 0f, NorthRoomZ),
                           bigWidth, bigDepth, bigHeight,
                           floorMat, grooveMat, panelMat, fixtureMat, corridorMouth, Rect.zero,
                           CycleFourHole, ladderHole,
                           // AND A HOLE IN THE EAST WALL, plugged with slabs that slide out of it at
                           // the very end - see `BuildBreach`. It is cut at build time rather than
                           // made at runtime because a wall in this project is a generated mesh with
                           // a chamfered rim, not a scaled cube, and there is no cutting one later.
                           BreachCutout());
            Transform rS = BuildEmptyRoom(root.transform, "Room3_2S", new Vector3(0f, 0f, -RoomPitch),
                           floorMat, grooveMat, panelMat, fixtureMat,
                           doorwayNorth: false, northCutout: gateNS);
            Transform rE = BuildEmptyRoom(root.transform, "Room3_2E", new Vector3(RoomPitchX, 0f, 0f),
                           floorMat, grooveMat, panelMat, fixtureMat,
                           doorwayNorth: false, westCutout: gateEW);
            Transform rW = BuildEmptyRoom(root.transform, "Room3_2W", new Vector3(-RoomPitchX, 0f, 0f),
                           floorMat, grooveMat, panelMat, fixtureMat,
                           doorwayNorth: false, eastCutout: gateEW);

            // THE BED, and the point the loop teleports to at the top of every iteration.
            (_, Transform spawn) = BuildBed(r1, propMat, floorY: CycleThreeFloorY, zCentre: CycleThreeZ,
                                            yaw: 0f, xCentre: CycleThreeX);

            // Cycle 2's chest, in cycle 2's place, with cycle 2's lamp - and empty. Both bays are
            // wired as `GhostInteractable`s by `BuildDresser` whatever is in them, which is what makes
            // putting something in one later a one-line change rather than a signal-bit problem
            // (CLAUDE.md SS1.6).
            Drawer[] drawers = BuildDresser(r1, "Dresser3_1", new Vector3(-0.95f, 0f, 1.35f),
                                            yaw: 0f, withLamp: true);

            ParticleSystem[] gas = BuildGasEmitters(r1, "Room3_1_Gas", 0f);

            // ONE PAD PER DIRECTION, each on the axis of the wall it opens and 3.2m out from the
            // centre - clear of the bed, which sits in the middle of the room, and near enough to its
            // own gate that stepping on it and watching that wall open is one glance rather than a
            // hunt. The pads are otherwise identical and unlabelled: which one opens which wall is
            // meant to be learned by standing on one.
            Material padMat = MakeColorMaterial("Room3PadWhite", new Color(0.85f, 0.85f, 0.86f));
            const float padOut = 3.2f;
            var pads = new[]
            {
                BuildFloorButton(r1, padMat, "Pad3_1_North", new Vector3(0f, 0f, padOut)),
                BuildFloorButton(r1, padMat, "Pad3_1_South", new Vector3(0f, 0f, -padOut)),
                BuildFloorButton(r1, padMat, "Pad3_1_East", new Vector3(padOut, 0f, 0f)),
                BuildFloorButton(r1, padMat, "Pad3_1_West", new Vector3(-padOut, 0f, 0f)),
            };

            // EACH GATE TAKES ONE PAD, not all four. `Door.requiredFloorButtons` wants EVERY pad in
            // the array held at once - which is Room3's puzzle - and here the four walls are four
            // separate offers, so each array is one pad long.
            // **NORTH HAS NO GATE.** Its cutout is a permanent opening - the mouth of the corridor -
            // and pad 0 drives the slab partway down it instead. The other three walls are gates that
            // are not meant to be found; this one is a hole you can see down, with a mechanism in it
            // you are meant to see coming.
            CrushingBarrier northBarrier =
                BuildNorthCorridor(root.transform, floorMat, grooveMat, panelMat, pads[0]);

            // ROOM3-1'S FACE OF EACH GATE, and then the far room's face of the same gate. Both take
            // the same pad, so one wall opens; the far side skips the cavity liner because the near
            // side has already built it.
            BuildPanelGate(r1, "Gate3_1_South", new Vector3(0f, 0f, -RoomDepth / 2f),
                           Vector3.right, Vector3.forward, RoomWidth, panelMat, grooveMat,
                           new[] { pads[1] });
            BuildPanelGate(rS, "Gate3_2S_North", new Vector3(0f, 0f, RoomDepth / 2f),
                           Vector3.right, Vector3.back, RoomWidth, panelMat, grooveMat,
                           new[] { pads[1] }, withCavityLiner: false, withAudio: false);

            BuildPanelGate(r1, "Gate3_1_East", new Vector3(RoomWidth / 2f, 0f, 0f),
                           Vector3.forward, Vector3.left, RoomDepth, panelMat, grooveMat,
                           new[] { pads[2] });
            BuildPanelGate(rE, "Gate3_2E_West", new Vector3(-RoomWidth / 2f, 0f, 0f),
                           Vector3.forward, Vector3.right, RoomDepth, panelMat, grooveMat,
                           new[] { pads[2] }, withCavityLiner: false, withAudio: false);

            BuildPanelGate(r1, "Gate3_1_West", new Vector3(-RoomWidth / 2f, 0f, 0f),
                           Vector3.forward, Vector3.right, RoomDepth, panelMat, grooveMat,
                           new[] { pads[3] });
            BuildPanelGate(rW, "Gate3_2W_East", new Vector3(RoomWidth / 2f, 0f, 0f),
                           Vector3.forward, Vector3.left, RoomDepth, panelMat, grooveMat,
                           new[] { pads[3] }, withCavityLiner: false, withAudio: false);

            // EVERY GATE PROVED CLEAR, which is the one thing this room can get silently wrong. The
            // opening is cut out of two walls' collision and two liners are built into the cavity
            // beside it, and any of the four rects being a hair off leaves a gate you can see through
            // and not walk through. `AssertWalkable` ignores the leaves themselves (they carry
            // `Door`), so what it is really testing is everything AROUND them.
            // THROUGH THE OPENING, WHICH IS NOT THE MIDDLE OF THE WALL on the five-cell north and
            // south - a probe down the wall's centreline there would sweep half a metre of solid
            // panel and report a blockage that is the wall doing its job.
            float nsCentre = gateNS.center.x, ewCentre = gateEW.center.x;
            // The corridor end to end, which also proves its mouth and its far doorway. The slab is
            // ignored like any `Door`-shaped obstruction would not be - it is NOT a Door, so it has to
            // be authored raised for this to pass, and it is authored SHUT. Probing the two halves
            // separately is what tests the geometry rather than the mechanism.
            // §CYCLE 3'S BEAM. The emitter, the five mirrors and the one thing the light has to
            // reach. None of it costs a signal bit - a mirror is a `CarryableItem`, so a past self
            // holding one is a `CarryEvent` and a recorded position, both of which already exist.
            LaserBeam beam = BuildLaserEmitter(rE, propMat);
            CarryableItem[] mirrors = BuildMirrorRack(rW, propMat);
            // **A HAND-OVER OF A MIRROR IS WORTH REPRODUCING**, and until 2026-08-30 no ghost could
            // do one: `GhostReplayer.Eligible` allowed a take off another past self only for objects
            // with a SOCKET, and a mirror has none. So a player who took a pane off a ghost and stood
            // in its place had that take refused in every later iteration - the one thing this cycle
            // is about, silently not accumulating. See `CarryableItem.ghostHandover`.
            // ~~`ghostHandover = true`~~ NOT NEEDED SINCE 2026-09-01: every object can be passed
            // between past selves, so the mirrors no longer have to ask for it. See
            // `GhostReplayer.Eligible`, which records why the exception is what showed the rule was
            // wrong.
            // THE WEST PLATE, which is where the beam has had to arrive since 2026-08-21.
            LaserReceiver receiver = BuildLaserReceiver(rN, "LaserReceiver",
                new Vector3(-bigWidth / 2f + 0.18f, BeamHeight, 5f), Vector3.right, propMat);
            // **AND ONE DIRECTLY OPPOSITE IT** (2026-08-28, by request). Same height, same z, the
            // other wall - so one pane held between them answers either by turning round, and which
            // storey the player is buying is a decision made with their body rather than with a
            // different object. Neither plate can be lit by the same beam as the other: each is a
            // solid that stops the light, so a beam has one of them or the other and never both.
            LaserReceiver receiverEast = BuildLaserReceiver(rN, "LaserReceiver_East",
                new Vector3(bigWidth / 2f - 0.18f, BeamHeight, 5f), Vector3.left, propMat);

            // §ROOM3-2N'S GROUND FLOOR. The beam and the lifts are what the room's HEIGHT is for;
            // this is what its floor is for, and it costs no signal bit - a block is a
            // `CarryableItem`, so a past self carrying one is a `CarryEvent` and a recorded
            // position, both of which already exist.
            BedlamCube bedlam = BuildBedlamCube(rN, bigWidth, bigDepth, mouthLocalX, propMat);
            // ROOM3-0, ON TOP OF THIS ROOM. Cycle 3 can be finished from here on: `AssembleCycleThree`
            // finds this sequence and hands it to the `Cycle`, and `Build` gives it the way down.
            (Transform r0, FinalRoomSequence finalRoom) = BuildBreakRoomThree(
                root.transform, floorMat, grooveMat, panelMat, propMat, fixtureMat);

            // THE LADDER, IN ROOM3-2S, AND THE SPOT IT GOES IN, ON DECK B. Two rooms apart on
            // purpose: what this cycle charges for is trips.
            CarryableItem ladder = BuildLadder(rS, propMat);
            LadderMount ladderMount = BuildLadderMount(rN, propMat, r0.Find("Dismounts"));

            // THE SIGN THAT SAYS WHAT IS MISSING. On deck B's wall beside the shaft, and it is the
            // only thing in the room that explains the hole in the ceiling - a pictogram of a ladder
            // over a spot with no ladder in it is a sentence.
            // ON THE WEST WALL, beside the shaft. The shaft is at the north end of deck B's west
            // strip and the south wall is seventeen metres away - a sign that far from the thing it
            // is about is a sign about nothing. Yawed to face east, into the room.
            BuildLadderSign(rN, new Vector3(-bigWidth / 2f + 0.06f, DeckBSurfaceY + 1.7f,
                                            LadderShaftXZ.y),
                            Quaternion.Euler(0f, -90f, 0f));

            // THE LADDER SHAFT: room3-2N's ceiling, one service void, room3-0's floor. The tube's
            // walls are what stop the gap between two storeys being visible through the hole.
            GameObject shaft = new GameObject("LadderShaft");
            shaft.transform.SetParent(rN, false);
            shaft.transform.localPosition = new Vector3(LadderShaftXZ.x, bigHeight + WallThickness,
                                                        LadderShaftXZ.y);
            float outer = GridCellWidth + 2f * WallThickness;
            float halfCell = GridCellWidth / 2f;
            Prim(PrimitiveType.Cube, "Shaft_West", shaft.transform,
                new Vector3(-halfCell - WallThickness / 2f, LadderVoid / 2f, 0f),
                new Vector3(WallThickness, LadderVoid, outer), panelMat);
            Prim(PrimitiveType.Cube, "Shaft_East", shaft.transform,
                new Vector3(halfCell + WallThickness / 2f, LadderVoid / 2f, 0f),
                new Vector3(WallThickness, LadderVoid, outer), panelMat);
            Prim(PrimitiveType.Cube, "Shaft_South", shaft.transform,
                new Vector3(0f, LadderVoid / 2f, -halfCell - WallThickness / 2f),
                new Vector3(GridCellWidth, LadderVoid, WallThickness), panelMat);
            Prim(PrimitiveType.Cube, "Shaft_North", shaft.transform,
                new Vector3(0f, LadderVoid / 2f, halfCell + WallThickness / 2f),
                new Vector3(GridCellWidth, LadderVoid, WallThickness), panelMat);

            // THE ROUTE AS NUMBERS, because "does the light have anywhere to go" is exactly the kind
            // of thing that is obvious in a plan and wrong in a build. Every figure here is read off
            // the objects rather than restated from the constants that placed them.
            Mirror firstMirror = mirrors.Length > 0 && mirrors[0] != null
                ? mirrors[0].GetComponent<Mirror>() : null;
            // THE BED'S FOOTPRINT, because the beam's lane is placed to miss it and "does it" is
            // not answerable from a plan. The corner mirror has to be STOOD at, on the corridor's
            // axis, so whether the lane can be centred is a question about this box.
            Transform bedT = r1.Find("Bed") ?? r1;
            Bounds bedBox = MeasuredBounds(bedT.gameObject);
            Debug.Log($"[SceneBuilder] Cycle 3 bed: centre {bedBox.center}, size {bedBox.size}");

            Debug.Log($"[SceneBuilder] Cycle 3 beam: emitter at {beam.muzzle.position}, "
                    + $"firing {beam.muzzle.forward}, plane y={CycleThreeFloorY + BeamHeight:0.###}, "
                    + $"wall offset {BeamLane:0.##}, west leg must leave within "
                    + $"+-{GateCutout(RoomDepth).width / 2f:0.##} of the gate's centre. "
                    + $"{mirrors.Length} mirrors, glass {(firstMirror != null ? firstMirror.radius * 2f : 0f):0.###}m "
                    + $"across. Receiver at {receiver.transform.position}, target radius "
                    + $"{receiver.radius:0.##}m.");

            // **THE CCTV SYSTEM IS GONE** (2026-08-28, by request), and with it the only asset in the
            // project that could not be sold: `cctv_camera.glb` is CC-BY-NC-4.0. Three cameras in
            // room3-2N and three screens on room3-2S's walls, watching a coloured staircase that was
            // deleted in 2026-08-21 - the feeds had outlived their subject by a week and were being
            // kept because they worked, not because anything needed them.
            //
            // WHAT ROOM3-2S IS FOR NOW: the pane that leaves the horizontal plane starts here. That
            // keeps the four-rooms-four-jobs split the screens were bought to make -
            //
            //     room3-2E  the source          room3-2W  the flat mirrors
            //     room3-2S  the riser           room3-2N  where the light has to arrive
            //
            // - and it turns the control room from somewhere you LOOK into somewhere you FETCH from,
            // which is a better use of a room in a game whose currency is trips.
            //
            // **IT IS CARRIED, NOT INSTALLED.** A pane bolted to room3-2S's wall would send the beam
            // into a 5.4m ceiling and nothing else; carried, it goes wherever the player takes it,
            // and the room that wants a beam going up is the 16.2m one down the corridor.
            // §THE LIFT, AND THE FIRST THING `LaserReceiver.Lit` HAS EVER DRIVEN (2026-08-28, by
            // request). Room3-2N is three storeys tall with two decks and no way onto either; this is
            // the way onto deck A, and it is worked entirely by light.
            //
            // **THE LOW CALL IS THE PLATE THAT WAS ALREADY THERE.** The beam has had a destination
            // since 2026-08-21 and nothing at the far end of it - `Lit` was deliberately left driving
            // nothing until somebody had bounced the light by hand. That has happened, so the plate
            // gets its consumer rather than a second plate being built beside it.
            // **~~THE CEILING CALL~~ GONE 2026-08-30, by request.** It was a plate on room3-2N's
            // ceiling that only the riser pane could reach, and it drove lift A from above so the
            // ride was not one-way. What replaced the reason for it is the LADDER: the way up out of
            // deck B is a thing you carry, not a thing you light.
            //
            // Lift A keeps the west plate, so it can still be called from the ground floor by a past
            // self holding a mirror - which is what brings it DOWN with somebody on it. Nothing about
            // the ride is lost; what is gone is the second way to ask for it.

            // **LIFT 1: THE FLOOR TO DECK A.** Butted against deck A's inner edge - deck A is an L two
            // cells deep down the west wall, so its edge is at `-width/2 + 2 * GridCellWidth` and
            // anywhere else lands the ride BESIDE the deck instead of on it.
            BeamLift liftA = BuildBeamLift(rN, "BeamLift_A",
                new Vector2(DeckALiftX, DeckALiftZ), DeckALiftPad,
                LiftRestingStep, DeckARows * GridCellHeight,
                propMat, grooveMat, shaftFootY: null, receiver);

            // **LIFT 2: DECK A TO DECK B, AND IT RISES THROUGH THE HOLE THAT WAS ALREADY THERE.**
            //
            // Deck B has a 1.75 x 3.5 void cut clean out of it, put there in 2026-08-21 so that
            // standing on deck B you look through onto deck A and past deck A's open side to the floor
            // - three storeys in one glance. A shaft between the two decks has to come up through
            // SOMETHING, and a hole whose whole purpose is to be looked down is the one place in this
            // room where a rising slab is not an intrusion. So the shaft is the void, and no geometry
            // had to be cut for it.
            //
            // **WHERE INSIDE THE VOID IS FORCED, not chosen.** The panel is pushed to the void's NORTH
            // end so that at the top its north edge meets deck B's slab and you step off across.
            // Centred, it would arrive surrounded by hole on the south and 7.5cm gaps elsewhere -
            // standing on a platform you cannot get off. Its footprint also has to have deck A UNDER
            // it at the bottom, and deck A only reaches this x through its north strip (z >= 7).
            BeamLift liftB = BuildBeamLift(rN, "BeamLift_B",
                new Vector2(DeckBLiftX, DeckBVoidMaxZ - DeckBLiftPad / 2f), DeckBLiftPad,
                DeckARows * GridCellHeight + LiftRestingStep, DeckBRows * GridCellHeight,
                // **ITS COLUMN GOES ALL THE WAY DOWN TO THE GROUND, THROUGH DECK A** (2026-08-28, by
                // request). It stood on deck A, which is honest engineering and reads as a lift with
                // no visible reason to exist - a machine that begins one storey up begs the question
                // of what holds THAT up. Taken to the floor it becomes the room's one full-height
                // object, and standing under deck A you can see the thing that serves the storey
                // above you. `PlanMezzanines` cuts deck A open for it.
                propMat, grooveMat, shaftFootY: 0f, receiverEast);

            // AND THE LINES THAT SAY WHICH PLATE WORKS WHICH LIFT. Painted on the wall under each
            // plate and along the floor to the lift it calls - the east one has to cross most of a
            // 17.5m room to reach lift B, which is exactly why the mapping needed saying.
            //
            // The ceiling call gets none: it drives lift A, whose line is already drawn, and a second
            // stripe to the same slab would suggest a second machine.
            BuildCallConduit(rN, "Marking_A",
                new Vector3(-bigWidth / 2f + 0.18f, BeamHeight, 5f), Vector3.right,
                new Vector2(DeckALiftX, DeckALiftZ));
            BuildCallConduit(rN, "Marking_B",
                new Vector3(bigWidth / 2f - 0.18f, BeamHeight, 5f), Vector3.left,
                new Vector2(DeckBLiftX, DeckBVoidMaxZ - DeckBLiftPad / 2f));

            BeamLift[] lifts = { liftA, liftB };

            // **~~THE 45-DEGREE RISER PANE~~ GONE 2026-08-30, by request**, and it had already lost
            // its job. Its one purpose was to take the beam out of the horizontal plane and up to the
            // ceiling call, and that plate went two days earlier when the ladder replaced it as the
            // way off deck B. A pane that can only reach a target nothing has is a pane that teaches
            // the player a trick with nothing to use it on.
            //
            // Room3-2S keeps its job and it is a better one: it is where the LADDER is fetched from.
            // The four-rooms-four-jobs split the cycle is built on is unchanged -
            //
            //     room3-2E  the source          room3-2W  the flat mirrors
            //     room3-2S  the ladder          room3-2N  where the light has to arrive

            AssertWalkable(r1, "room3-1 north corridor mouth",
                new Vector3(nsCentre, 0f, RoomDepth / 2f - 1.2f), new Vector3(nsCentre, 0f, RoomDepth / 2f + 4f));
            AssertWalkable(r1, "room3-1 south gate",
                new Vector3(nsCentre, 0f, -RoomDepth / 2f + 1.2f), new Vector3(nsCentre, 0f, -RoomDepth / 2f - 1.2f));
            AssertWalkable(r1, "room3-1 east gate",
                new Vector3(RoomWidth / 2f - 1.2f, 0f, ewCentre), new Vector3(RoomWidth / 2f + 1.2f, 0f, ewCentre));
            AssertWalkable(r1, "room3-1 west gate",
                new Vector3(-RoomWidth / 2f + 1.2f, 0f, ewCentre), new Vector3(-RoomWidth / 2f - 1.2f, 0f, ewCentre));

            Debug.Log($"[SceneBuilder] Cycle 3: Room3_1 at ({CycleThreeX:0.##}, {CycleThreeFloorY:0.###}, "
                    + $"{CycleThreeZ:0.##}), 4 pads: 3 gates ({gateEW.width:0.##} E/W, "
                    + $"{gateNS.width:0.##} S) and a {CorridorRun:0.##}m "
                    + $"filled corridor north into Room3_2N ({2f * RoomWidth:0.##}x{2f * RoomDepth:0.##}"
                    + $"x{3f * RoomHeight:0.###})");
            // THE TWO DRAWER BAYS ARE THE WHOLE SIGNAL ARRAY, and they are in it before anything is
            // in THEM - the same order cycle 2's chest was wired in, and for the reason CLAUDE.md
            // SS1.6 gives: an entry's index IS its bit, so appending one later is the one change this
            // array makes awkward. Numbered from zero, which is legal because every ghost is destroyed
            // at a cycle boundary and no surviving timeline refers to another cycle's bits.
            // THE DRAWERS FIRST, THEN THE FOUR PADS, and the order is the wire format. An entry's
            // index IS its bit in `RecordedFrame.signals` (CLAUDE.md SS1.6), so this array is
            // append-only - a pad inserted ahead of the drawers would make every timeline recorded
            // before it replay the wrong fixture. Six of the thirty-two bits are spent.
            //
            // The pads MUST be in here or the whole room does not work: a `FloorButton` outside the
            // signal array is a pad no past self ever stands on, and every gate in this room is
            // opened by a past self.
            // Drawers, then pads, then levers - APPEND ONLY, because an entry's index is its bit in
            // `RecordedFrame.signals` (CLAUDE.md SS1.6). Nine of the thirty-two are spent.
            //
            // **THE LEVERS MUST BE IN HERE or room3-2N cannot be climbed at all.** A `GhostInteractable`
            // outside this array is a fixture no past self ever operates, and every step in that room
            // is held up by a past self.
            var signals = new GhostInteractable[drawers.Length + pads.Length];
            for (int i = 0; i < drawers.Length; i++) signals[i] = drawers[i];
            for (int i = 0; i < pads.Length; i++) signals[drawers.Length + i] = pads[i];

            return (root.transform, spawn, gas, r1, signals, northBarrier, lifts);
        }

        // THE LID HAS TO SIT ON THE HOLE IT FILLS, and nothing else in the build checks that.
        // `CycleThreeX`/`Z` are read off room2-0 by hand (see their comment), so this is the line
        // that notices when room2-0 moves and they do not.
        //
        // **IT COMPARED THE HATCH WITH THE CONSTANTS THAT PLACED THE HATCH** until 2026-09-04, which
        // is a value against itself: it could not fail, and it did not - it passed while the lid sat
        // 0.30m off its hole and play found the gap instead. An assert has to compare two things that
        // came from DIFFERENT places or it is decoration. The hole comes from room2-0, so room2-0 is
        // what the hatch is now measured against.
        //
        // Room2-0's own origin IS the hole's centre, because `CycleTwoExitHole` is cut on the room's
        // local origin rather than offset like cycle 1's - so its transform is the thing to compare.
        private static void AssertUnderHatch(Transform hatch, Transform roomTwoZero)
        {
            if (hatch == null) return;
            if (roomTwoZero == null)
            {
                Debug.LogError("[SceneBuilder] AssertUnderHatch found no 'Room2_0' to measure the "
                    + "cycle 2 hatch against, so the check that the lid covers its hole did not run. "
                    + "If that room was renamed, rename it here too.");
                return;
            }

            Vector3 lid = hatch.position, hole = roomTwoZero.position;
            float dx = Mathf.Abs(lid.x - hole.x), dz = Mathf.Abs(lid.z - hole.z);
            // Well under the clearance a visible gap needs: the opening is 1.75m square, so 5cm of
            // slip is invisible and 30cm was not.
            if (dx < 0.05f && dz < 0.05f) return;

            Debug.LogError($"[SceneBuilder] Cycle 2's floor hatch does not cover its hole. The lid is "
                + $"at ({lid.x:0.###}, {lid.z:0.###}) and room2-0's hole is at "
                + $"({hole.x:0.###}, {hole.z:0.###}) - out by ({dx:0.###}, {dz:0.###})m, which leaves "
                + $"that much of the opening uncovered and drops cycle 3's bed off the shaft. "
                + $"CycleThreeX/Z are ({CycleThreeX:0.###}, {CycleThreeZ:0.###}) and are what place "
                + "the lid; set them to room2-0's centre.");
        }

        private static (Transform room, FinalRoomSequence final, CycleExit exit) BuildBreakRoom(
            Transform parent, Vector3 at, Material floorMat, Material grooveMat, Material panelMat,
            Material propMat, Material fixtureMat)
        {
            Transform t = BuildEmptyRoom(parent, "Room2_0", at, floorMat, grooveMat, panelMat,
                                         fixtureMat, doorwayNorth: true, floorHole: CycleTwoExitHole);

            // --- the four pedestals -----------------------------------------------------------------
            GameObject rack = new GameObject("Room2_0_Pedestals");
            rack.transform.SetParent(t, false);

            // AUTHORED RAISED and sunk in `RewardPlinth.Awake`, like every other plinth in the game:
            // a scene whose only props are invisible cannot be checked without pressing Play.
            RewardPlinth risen = rack.AddComponent<RewardPlinth>();
            risen.plinth = rack.transform;
            risen.riseHeight = PedestalHeight + 0.06f;
            risen.riseSeconds = 2.4f;

            // ALL FOUR RIMS ARE THE SAME COLOUR, and that is the design rather than an omission.
            //
            // The first build gave each pedestal an accent of its own, and play reported the obvious
            // thing: the rim colour and the ball that goes in it disagree. Billiard balls are a
            // standard set - 2 blue, 3 red, 4 purple, 5 orange - so there are only two ways to stop
            // them disagreeing, and MATCHING them is the one that costs the puzzle. A purple rim over
            // a purple ball turns "how many buckets are there" into "find the purple one", and the
            // pictogram on the side becomes decoration.
            //
            // Identical neutral rims say nothing instead. Every one of them lights the same way for
            // every ball, because `offerItemIds` is the whole family - so the row answers a press and
            // never a question, and the only thing in the room that distinguishes one pedestal from
            // another is the object drawn on it.
            Color rim = new Color(0.78f, 0.79f, 0.82f);

            // **THREE, DOWN FROM FOUR** (2026-08-29, by request: the beach ball's pedestal is gone,
            // the bucket, the axe and the duck stay).
            //
            // Every entry here is a WHOLE ROUND TRIP - one hand, sixty seconds, and a ball that has
            // to be found in the chest and carried to room2-0 - so the count is the room's length
            // rather than its difficulty. Cycle 2 measured at 22 iterations against cycle 1's 9, and
            // this is one of the two places that number is set (the other is room2-7's latch, see
            // `TODO.md`). Nothing about the puzzle changes: the chest still holds nine balls, three
            // of which are wanted, so reading the pictogram is still the whole of it.
            //
            // `wants.Length` drives the pedestals, the slots and the completion count, so removing a
            // row is the entire change.
            var wants = new (string name, string ball, Sprite icon)[]
            {
                ("Axe",    BilliardIdPrefix + "5", AxeIcon()),
                ("Duck",   BilliardIdPrefix + "3", DuckIcon()),
                ("Bucket", BilliardIdPrefix + "4", FullBucketIcon()),
            };

            string[] family = BilliardIds();
            var slots = new FinalSlot[wants.Length];

            for (int i = 0; i < wants.Length; i++)
            {
                float x = (i - (wants.Length - 1) / 2f) * PedestalPitch;

                GameObject unit = new GameObject("Pedestal_" + wants[i].name);
                unit.transform.SetParent(rack.transform, false);
                unit.transform.localPosition = new Vector3(x, 0f, PedestalRowZ);

                // THE BODY STOPS SHORT OF THE TOP, and the socket cap makes up the difference. The
                // cap is where the bowl is cut, so the material the bowl is cut OUT of has to be the
                // cap and not the body - a box primitive cannot have a hole in it.
                //
                // Its collider goes on the unit instead, at full height, so the pedestal is still one
                // solid object to walk into. Rising through somebody standing exactly on it is not
                // reachable: the rise starts as they clear a doorway eight metres away.
                Prim(PrimitiveType.Cube, "Body", unit.transform,
                     new Vector3(0f, (PedestalHeight - SocketCapThickness) / 2f, 0f),
                     new Vector3(PedestalWidth, PedestalHeight - SocketCapThickness, PedestalDepth),
                     propMat, removeCollider: true);

                BoxCollider solid = unit.AddComponent<BoxCollider>();
                solid.center = new Vector3(0f, PedestalHeight / 2f, 0f);
                solid.size = new Vector3(PedestalWidth, PedestalHeight, PedestalDepth);

                // A HEMISPHERE CUT TO THE BALL'S OWN RADIUS, 2026-08-20 by request. It was a flat
                // disc, and a flat disc says "something round belongs here" where this room has to
                // say "a BILLIARD BALL belongs here" - a bowl a ball drops half into is a shape that
                // can be for nothing else. `SlotShape.Dish` builds the cap, the bowl and the lit ring
                // round its mouth as one thing; see BallSocketMesh.
                //
                // All four are identical, which is exactly right: they take the same KIND of object
                // and differ only in WHICH one, and that difference is carried by the pictogram on
                // the side and by nothing else in the room (see `rim` above).
                slots[i] = BuildFinalSlot(unit.transform, "Slot", SlotShape.Dish,
                    new Vector3(0f, PedestalHeight, 0f), SocketBowlRadius * 2f, rim,
                    reachCentre: new Vector3(0f, -0.55f, 0.42f),
                    reachSize: new Vector3(1.30f, 2.60f, 2.00f),
                    dishPlateHalf: new Vector2(PedestalWidth / 2f, PedestalDepth / 2f));
                slots[i].acceptedItemId = wants[i].ball;
                // ...AND IT WILL TAKE A PRESS FOR ANY OF THEM. Without this the rim would light for
                // the right ball and stay dark for the other eight, which hands the player the answer
                // for the cost of carrying each ball past each pedestal. See FinalSlot.offerItemIds.
                slots[i].offerItemIds = family;
                // The sound of no. Nothing in this project is a buzzer, so the switch's off-click is
                // borrowed - short, mechanical, and audibly not the sound an object landing makes.
                slots[i].refuseClip = LoadClip(SfxDir, "sfx_switch_off");

                BuildPedestalSign(unit.transform, wants[i].name, wants[i].icon);
            }

            // --- the way in, and the way down --------------------------------------------------------
            //
            // "The player got through the last door", which is what raises the rack. A latch, cleared
            // by the loop at the top of an iteration, so each run has to arrive for itself. Its
            // `door` is wired at the call site - the door is room2-7's and is built later.
            GameObject escapeGO = new GameObject("EscapeTrigger_Cycle2");
            escapeGO.transform.SetParent(t, false);
            escapeGO.transform.localPosition = new Vector3(0f, 0f, RoomDepth / 2f);
            EscapeTrigger escape = escapeGO.AddComponent<EscapeTrigger>();
            escape.halfWidth = DoorWidth / 2f + 0.05f;

            // **THE HATCH IS NOT BUILT HERE ANY MORE, since cycle 3 exists.** It was, while there was
            // nothing underneath: a lid in this room's floor and a capped shaft under it, all owned by
            // room2-0 because there was no storey for it to join TO.
            //
            // There is one now, and a `CycleExit` drives BOTH lids - the floor above and the ceiling
            // below - so it cannot live in either cycle's scene: a reference across a scene boundary
            // comes back null and Unity does not say so. It moves to `CycleJoin_2_3` in the core
            // scene, which is where cycle 1's has always been and for exactly this reason. See Build.
            CycleExit exit = null;

            GameObject seqGO = new GameObject("BreakSequence");
            seqGO.transform.SetParent(t, false);
            FinalRoomSequence sequence = seqGO.AddComponent<FinalRoomSequence>();
            sequence.console = risen;
            sequence.slots = slots;
            // AND EACH RECESS POINTS BACK, which it did not for the first two builds of this room.
            // `FinalSlot.Live` is `sequence != null && sequence.Active`, so four null sequences meant
            // four recesses that were never live: no prompt, and E did nothing at any of them. The
            // room looked finished and could not be finished. Cycle 1's builder has always had this
            // line; this one was written without it, which is why `CycleBinding` now re-establishes
            // it at runtime as well.
            foreach (FinalSlot slot in slots) if (slot != null) slot.sequence = sequence;
            sequence.arrival = escape;
            // `wayOut` is wired in `Build` along with the hatch itself, and `CycleBinding` writes it
            // again at runtime from `wayOuts`.
            //
            // **AND THE BREAK NO LONGER OPENS THE FLOOR**, which it did for exactly as long as there
            // was nothing under it. `LoopManager.CrossToNextCycle` owns the hatch now: it wakes cycle
            // 3, repoints the gas at it and THEN opens the way down, because a player must not be able
            // to drop into a storey that is still asleep.
            sequence.opensWayOutOnBreak = false;
            sequence.breakDuration = 10f;

            return (t, sequence, exit);
        }

        // ONE OBJECT DRAWN ON THE FRONT OF A PEDESTAL, and it is the whole of what the pedestal says.
        //
        // No word, no number, no rim colour that means anything - the four accents differ so the row
        // is four objects rather than one repeated, and none of them is a clue. A pictogram is the
        // only instrument in this building that can ask "how many of these are there" without also
        // answering it.
        //
        // FACING THE DOOR. A canvas's forward must match the direction the VIEWER is looking rather
        // than point at them (see MakeWallFace), and a player reading this is walking south - so yaw
        // 180, the same as the sign over room2-7's south door.
        private static void BuildPedestalSign(Transform unit, string name, Sprite icon)
        {
            CanvasGroup face = MakeWallFace(unit, "Sign",
                new Vector3(0f, PedestalHeight * 0.55f, PedestalDepth / 2f + 0.02f),
                Quaternion.Euler(0f, 180f, 0f),
                f => MakeWallIcon(f, name, icon, Vector2.zero, 1280f,
                                  new Color(0.20f, 0.20f, 0.23f, 0.88f)),
                worldWidth: 0.78f, withPlate: false, authoredHeight: 1600f);

            // MakeWallFace authors every sign at alpha 0, because the ones it was built for are faded
            // in and retired by `PanelMessage`. These never go: they are true for as long as the room
            // is unsolved.
            if (face != null) face.alpha = 1f;
        }

        private static (Transform room, WeighScale scale)
            BuildWeighRoom(Transform parent, Material floorMat, Material grooveMat, Material panelMat,
                           Material propMat, Material fixtureMat)
        {
            GameObject rootGO = new GameObject("Room2_7_Root");
            rootGO.transform.SetParent(parent, false);
            rootGO.transform.localPosition = new Vector3(SlideRoomCentreX, SlideRoomFloorY,
                                                          SlideRoomCentreZ - RoomPitch);

            GameObject roomGO = new GameObject("Room2_7");
            roomGO.transform.SetParent(rootGO.transform, false);
            Transform t = roomGO.transform;

            BuildSlab(t, "Floor", -WallThickness / 2f, 0f, floorMat, default);
            BuildSlab(t, "Ceiling", RoomHeight + WallThickness / 2f, 0f, CeilingMaterial(), default);

            BuildPanelWall(t, "Wall_North", new Vector3(0f, 0f, RoomDepth / 2f),
                Vector3.right, Vector3.back, RoomWidth, grooveMat, panelMat,
                new Rect(-DoorWidth / 2f, 0f, DoorWidth, DoorHeight));
            // THE SOUTH WALL IS CUT FOR THE WAY ON, and it was NOT - which is why room2-7 appeared to
            // have neither a door nor a sign. `BuildPadDoor` places a door slab and `MakeWallFace`
            // hangs the target number over it, and both were doing exactly that INSIDE a solid wall:
            // the door had no opening to slide across and the number was behind the panelling.
            //
            // The lesson generalises past this room: a door and its opening are two separate calls in
            // two different places, and nothing in the build checks that a door has a hole. If a door
            // is invisible, look for the Rect before looking at the door.
            BuildPanelWall(t, "Wall_South", new Vector3(0f, 0f, -RoomDepth / 2f),
                Vector3.right, Vector3.forward, RoomWidth, grooveMat, panelMat,
                new Rect(-DoorWidth / 2f, 0f, DoorWidth, DoorHeight));
            BuildPanelWall(t, "Wall_West", new Vector3(-RoomWidth / 2f, 0f, 0f),
                Vector3.forward, Vector3.right, RoomDepth, grooveMat, panelMat, Rect.zero);
            BuildPanelWall(t, "Wall_East", new Vector3(RoomWidth / 2f, 0f, 0f),
                Vector3.forward, Vector3.left, RoomDepth, grooveMat, panelMat, Rect.zero);

            BuildCeilingLights(t, "Room2_7", 0f, fixtureMat, castShadows: true);
            BuildReflectionProbe(t, "Room2_7", 0f);

            WeighScale scale = BuildWeighScale(t, propMat);
            BuildWeighPictogram(t, scale);
            return (t, scale);
        }

        // A FLAT PANEL WHERE THE MODEL HAD ITS OWN NUMBER.
        //
        // **THE SCALE'S DIGITS ARE GEOMETRY, NOT A TEXTURE**, and that took looking at the file to
        // find: `weigh_scale.glb` has no textures at all - four flat-colour materials - and its green
        // display is a single connected surface of 45 vertices and 54 triangles, 0.1 deep, with a
        // fixed number embossed into it. A first pass rebuilt that mesh's UVs and drew the live
        // reading onto it, which mapped a picture of digits across a surface that IS digits: play
        // reported it as the panel overlapping the number behind it, and that is exactly what it was.
        //
        // So the submesh is REPLACED rather than re-mapped. The model's fixed number goes with it -
        // asked for, and unavoidable anyway, since it is the same triangles - and what stands in its
        // place is a quad across the same footprint, at the same outer face, with clean 0..1 UVs. The
        // display is still the model's own display part, in the model's own place; it is simply a
        // surface something can be drawn on now.
        //
        // DOUBLE-SIDED, as eight vertices rather than four. Which way the panel's outer face points
        // is decided by the model's import rotation, and a single-sided quad that guessed wrong would
        // be invisible from above with nothing to say why.
        //
        // Saved as an asset, like every other generated mesh here: a `Mesh` built at edit time and
        // assigned straight to a scene object does not survive the scene being reloaded.
        private static Mesh FlatPanelMesh(Mesh source, string assetName)
        {
            if (source == null) return null;

            Bounds b = source.bounds;
            return SaveGeneratedMesh(assetName + "_panel", () =>
            {
                // The two widest axes are the face; the third is its thickness.
                int thin = b.size.x <= b.size.y && b.size.x <= b.size.z ? 0
                         : b.size.y <= b.size.z ? 1 : 2;
                int across = thin == 0 ? 2 : 0;
                int up = thin == 1 ? 2 : 1;

                // At the OUTER face rather than the middle: the digits were embossed toward the top of
                // the scale, so that is the side anything replacing them has to be on.
                float at = b.max[thin];

                Vector3 Corner(float u, float v)
                {
                    Vector3 c = Vector3.zero;
                    c[across] = Mathf.Lerp(b.min[across], b.max[across], u);
                    c[up] = Mathf.Lerp(b.min[up], b.max[up], v);
                    c[thin] = at;
                    return c;
                }

                Vector3 normal = Vector3.zero;
                normal[thin] = 1f;

                var verts = new Vector3[8];
                var norms = new Vector3[8];
                var uvs = new Vector2[8];
                var tris = new int[12];

                for (int side = 0; side < 2; side++)
                {
                    int o = side * 4;
                    verts[o + 0] = Corner(0f, 0f);
                    verts[o + 1] = Corner(1f, 0f);
                    verts[o + 2] = Corner(1f, 1f);
                    verts[o + 3] = Corner(0f, 1f);

                    for (int i = 0; i < 4; i++) norms[o + i] = side == 0 ? normal : -normal;

                    // **THE BACK FACE IS MIRRORED, AND IT IS THE ONE BEING LOOKED AT.**
                    //
                    // Two sides of a quad see the same texture from opposite directions, so whichever
                    // one the player is on, one of them reads back to front. Play saw exactly that -
                    // the number as if in a mirror - which also settles which face is up: the panel's
                    // outward normal points AWAY from the player, so it is the back set they see.
                    //
                    // Giving the two sides opposite U fixes it for both at once, which is better than
                    // flipping the whole thing and hoping: read from above or from underneath, the
                    // number runs the right way.
                    bool mirrored = side == 1;
                    float u0 = mirrored ? 1f : 0f, u1 = mirrored ? 0f : 1f;
                    uvs[o + 0] = new Vector2(u0, 0f);
                    uvs[o + 1] = new Vector2(u1, 0f);
                    uvs[o + 2] = new Vector2(u1, 1f);
                    uvs[o + 3] = new Vector2(u0, 1f);
                }

                // Front wound one way, back the other, so both faces are lit and drawn correctly.
                int[] front = { 0, 2, 1, 0, 3, 2 };
                int[] back = { 4, 5, 6, 4, 6, 7 };
                System.Array.Copy(front, 0, tris, 0, 6);
                System.Array.Copy(back, 0, tris, 6, 6);

                var mesh = new Mesh { name = assetName + "_panel" };
                mesh.vertices = verts;
                mesh.normals = norms;
                mesh.uv = uvs;
                mesh.triangles = tris;
                mesh.RecalculateBounds();
                return mesh;
            });
        }

        // AN UNLIT SURFACE. Used for the scale's display, which carries its own brightness in its
        // texture and must not be shaded by the room - a lit panel goes grey in a shadow, and an LCD
        // that dims when you lean over it is a painted one.
        private static Material MakeUnlitMaterial(string name, Color colour)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) unlit = OpaqueShader();

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(unlit);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = unlit;
            mat.color = colour;
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
