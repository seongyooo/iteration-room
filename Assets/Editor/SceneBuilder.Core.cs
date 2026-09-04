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
    // THE BUILDING ITSELF. `BuildCore` and everything structural under it: the plinth housing, the
    // sleeping gas, the exit shaft, a cycle's exit, room shells, slabs and panel walls.
    // **`BuildPanelWall` overruns its own length by `WallDepth` at each end** - work out where that
    // lands before building one (CLAUDE.md 3).
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {


        // THE MACHINE THE RING IS BUILT AROUND.
        //
        // A sealed casing the player never enters, seen only through four windows. Inset from the
        // rooms' inner faces rather than flush with them, which does two things: it avoids two
        // coplanar walls fighting over the same plane, and it leaves a shallow chamber so the core
        // reads as a thing standing in a shaft rather than as a solid block of building.
        //
        // Deliberately plain at this stage. What the core DOES - clamps releasing, an axle turning,
        // shards pushed out through hatches - belongs with the puzzles that drive it, and a casing
        // that already moved would be machinery with nothing operating it.
        // Returns the AXLE, not the casing - the axle is the only part anything else drives.
        private static Transform BuildCore(Transform parent, Vector3 centre, float span, Material propMat)
        {
            GameObject core = new GameObject("Core");
            core.transform.SetParent(parent, false);
            core.transform.localPosition = centre;

            Material casing = MakeColorMaterial("CoreCasing", new Color(0.17f, 0.18f, 0.21f));
            SetSmoothness(casing, 0.55f);

            // Solid, and the only thing between the player and the core's insides.
            Prim(PrimitiveType.Cube, "Casing", core.transform,
                new Vector3(0f, RoomHeight / 2f, 0f),
                new Vector3(span, RoomHeight, span), casing);

            // Something to look at through the glass: a lit column standing in the casing's mouth on
            // each of the four window faces. Emissive rather than lit, so it reads through glass in a
            // room whose own lights are behind the viewer.
            // THE AXLE, which is what room4's ratchet turns. The rotors hang off it rather than off
            // the casing, so eight notches of cranking is eight visible eighths of a turn seen
            // through four different windows - the core reacting rather than a number going up.
            GameObject axle = new GameObject("CoreAxle");
            axle.transform.SetParent(core.transform, false);

            Material glow = MakeEmissiveMaterial("CoreGlow", new Color(0.45f, 0.75f, 1f), 2.6f);
            float face = span / 2f;
            var faces = new[]
            {
                new Vector3( face, 0f, 0f), new Vector3(-face, 0f, 0f),
                new Vector3(0f, 0f,  face), new Vector3(0f, 0f, -face),
            };
            for (int i = 0; i < faces.Length; i++)
            {
                Vector3 inward = -faces[i].normalized * 0.28f;
                Prim(PrimitiveType.Cylinder, $"CoreRotor_{i}", axle.transform,
                    faces[i] + inward + Vector3.up * (GridCellHeight * 2f),
                    new Vector3(0.55f, GridCellHeight * 0.9f, 0.55f), glow, removeCollider: true);
            }

            return axle.transform;
        }

        // Returns `rooms` in ring order - room1 first, room0 last - because room0's console needs
        // things that do not exist yet when the shell is built: this cycle's wall panels are gathered
        // FROM the shell, and the shaker and the hand belong to the player, who is built later.
        // Handing the transforms back and finishing room0 at the call site is what keeps that
        // ordering honest rather than shuffling half of `Build` around it.
        private static (Transform root, Transform bedSpawn, ParticleSystem[] gas,
                        Door[] doors, RoomCondition[] conditions, GhostInteractable[] signals,
                        Transform[] rooms, Transform[] loweredRooms,
                        FinalRoomSequence finalRoom, CycleExit wayOut)
            BuildCycleTwoShell(
            Material floorMat, Material grooveMat, Material panelMat, Material propMat,
            Transform ghostParent)
        {
            GameObject root = new GameObject("Room_Cycle2");
            // TURNED ROUND ABOUT THE BED. See CycleTwoYaw for why. The pivot is folded into the
            // position - a child at local z maps to world `2 * CycleTwoFirstRoomZ - z`, so the bed
            // room at local `CycleTwoFirstRoomZ` lands on itself and stays under the exit shaft
            // while every other room swings out past the end of cycle 1.
            root.transform.position = new Vector3(0f, -StoreyDrop, 2f * CycleTwoFirstRoomZ);
            root.transform.rotation = CycleTwoYaw;

            Rect doorway = new Rect(-DoorWidth / 2f, 0f, DoorWidth, DoorHeight);

            // THE RING. Eight rooms around the perimeter of a 3x3 block with the core in the middle,
            // turning WEST three times - at room3, room5 and room7, which are its corners.
            //
            // Room1 keeps the position it already had, so the drop from `room1-0` lands exactly where
            // it did. Everything else is walked out from there: `RoomPitch` along a leg, `CornerPitch`
            // across a turn, and a quarter turn of yaw at each corner.
            //
            // **Only four rooms share a wall with the core** - 2, 4, 6 and 0, the mid-leg ones. The
            // corners touch it diagonally and cannot see in at all. That is not a limitation to work
            // around; it is the rhythm the layout hands over for free: one glass room and one corner
            // room per leg.
            float ringZ = CycleTwoFirstRoomZ;

            Transform r1 = BuildRingRoom(root.transform, "Room2_1", 0f, ringZ, 0f,
                floorMat, grooveMat, panelMat, doorway, Rect.zero, Rect.zero, CycleExitHoleFromBelow);
            Transform r2 = BuildRingRoom(root.transform, "Room2_2", 0f, ringZ - RoomPitch, 0f,
                floorMat, grooveMat, panelMat, doorway, doorway, Rect.zero);
            // ~~r3, r4 and r5~~ MERGED 2026-08-16 into one space, `BuildTreeHall`. They were already
            // a straight line along this leg, so the ring's shape is unchanged and both neighbours -
            // r2 through the bay's north wall, r6 through the run's - meet exactly the walls they
            // always met. The hall is built further down, once `fixtureMat` exists.
            float legTwoZ = ringZ - 2f * RoomPitch;

            // LEG 3 MOVED NORTH BY 0.875m, which is the whole price of the hall being one width.
            //
            // It used to sit at `legTwoZ + CornerPitch`, the corner spacing between a room presenting
            // its width and one presenting its depth. That put r6's doorway in a wall 0.875m south of
            // r2's, and a hall joining both had to step between the two - the step the soffit covered.
            // Putting r6 on the SAME Z as r2 lets one straight wall carry both openings, and a corner
            // room is free to sit wherever its own door needs it to.
            // LEG 3 IS GONE, 2026-08-19 by request: the old room2-6, room2-7 and room2-0 are deleted
            // and the SLIDE'S LANDING ROOM is room2-6 now, with the weighing room behind it as room2-7
            // and the console room as room2-0. The walk is room2-1, room2-2, the tree hall - and then
            // down the chute into the pool, which is a way on that no door provides.
            //
            // The two numbers survive because the CORE is still measured off them (below): the shaft
            // in the middle is placed by the four legs' inner faces, and leg 3's is still where the
            // building's west side is even with no rooms standing on it.
            float legThreeX = -CornerPitch - RoomPitch;
            float legThreeZ = ringZ - RoomPitch;                 // 43.4, level with r2

            // TWO SHELLS AND A HALL. The hall is not in this list because it is not a shell - it
            // builds its own floor, walls, lights and probes at two different ceiling heights - and
            // neither are room2-6, room2-7 and room2-0, which are built with the hall (the slide has
            // to be measured against room2-6's floor) and light and probe themselves for the same
            // reason.
            var rooms = new[] { r1, r2 };
            var names = new[] { "Room2_1", "Room2_2" };

            // THE WINDOWS ARE GONE, with the puzzles that looked through them. They were built as
            // translucent panes at 0.94 smoothness, and in a lit white room that reads as a MIRROR
            // rather than as a window - which is what play reported. The casing stays: the ring is
            // still wrapped around something, and cutting a window back in is one Rect.
            const float coreInset = 0.15f;
            // MEASURED OFF THE FOUR LEGS' INNER FACES, not derived from a pitch. The pitch formula it
            // replaces happened to agree while every leg sat at its corner spacing; leg 3 has moved
            // (above), and a formula that no longer describes the building is worse than no formula.
            float coreEastFace = -RoomWidth / 2f;                  // leg 1's west wall
            float coreWestFace = legThreeX + RoomWidth / 2f;       // leg 3's east wall
            float coreSouthFace = legTwoZ + TreeHallNorthFace;     // the hall's north wall
            float coreNorthFace = legThreeZ + RoomPitch - RoomWidth / 2f;   // leg 4's south wall
            float coreSpan = Mathf.Min(coreEastFace - coreWestFace,
                                       coreNorthFace - coreSouthFace) - 2f * coreInset;
            Vector3 coreCentre = new Vector3((coreEastFace + coreWestFace) / 2f, 0f,
                                             (coreNorthFace + coreSouthFace) / 2f);
            BuildCore(root.transform, coreCentre, coreSpan, propMat);

            // THE DOORS, each built under the room it leaves FROM and set in that room's exit wall.
            // Straight joins use the south wall (yaw 180 from the builder's default north); the three
            // corners use the west wall (yaw 270) and the room's narrower half-extent.
            // FIVE, NOT SEVEN. Door2_3, Door2_4 and Door2_5 were the joins between the three rooms
            // the hall absorbed; two of those joins no longer exist and the third - the way out onto
            // r6 - is built by `BuildTreeHall` itself, in the same wall at the same x, because the
            // door has to be positioned along a 21m wall rather than at a room's centre.
            var ringDoors = new Door[2];
            ringDoors[0] = BuildPadDoor(r1, "Door2_1", 0f, new FloorButton[0], propMat, yaw: 180f);
            ringDoors[1] = BuildPadDoor(r2, "Door2_2", 0f, new FloorButton[0], propMat, yaw: 180f);

            // EVERY DOOR WHOSE ROOM HAS NO PUZZLE YET STANDS OPEN, and this flag is the to-do list kept
            // in the scene - it comes off as each room is designed. Door2_1 is the one that has come
            // off: its room is the four light switches, and it hangs on that condition instead.
            //
            // WITHOUT THIS THE RING IS NOT WALKABLE. A door with no condition, no pads and no flag asks
            // `FloorButton.AllActive` of an empty array, which deliberately refuses - so it never opens
            // and cycle 2 dead-ends at room2-2. That is the right default for a MIS-WIRED door and the
            // wrong one for a door whose room simply has not been built, which is exactly why the two
            // are different facts and this one is said out loud.
            // BOTH DOORS THE RING STILL HAS HANG ON A PUZZLE, so nothing is flagged here any more -
            // Door2_1 on the four switches, Door2_2 on the tank, both set below. The flag stays in
            // `Door` because it is the to-do list kept in the scene and the next unfinished room will
            // want it again.
            ringDoors[1].openUntilPuzzled = true;

            // One pocket per join, all uncapped: every one has a room through it.
            BuildDoorPocketFill(r1, "Pocket2_1", 0f, grooveMat, capFarSide: false, yaw: 180f);
            BuildDoorPocketFill(r2, "Pocket2_2", 0f, grooveMat, capFarSide: false, yaw: 180f);

            Material fixtureMat = MakeEmissiveMaterial("CeilingFixtureCycle2", Color.white, CeilingPanelEmission);
            Light[] room1Lights = null;
            Renderer[] room1Panels = null;
            for (int i = 0; i < rooms.Length; i++)
            {
                (Light[] lights, Renderer[] panels) =
                    BuildCeilingLights(rooms[i], names[i], 0f, fixtureMat, castShadows: true);
                if (i == 0) { room1Lights = lights; room1Panels = panels; }
                BuildReflectionProbe(rooms[i], names[i], 0f);
            }

            // THE TREE HALL, on this leg where r3, r4 and r5 used to be. It lights and probes itself
            // - see BuildTreeHall - which is why it sits outside the loop above rather than in it.
            (Transform hall, TreeTrunk treeTrunk, TreeFelled treeFelled,
             Transform poolRoom, Door poolDoor, PoolDrain poolDrain, Valve[] poolValves,
             Transform weighRoom, WeighScale weighScale,
             Transform breakRoom, FinalRoomSequence breakSequence, CycleExit cycleTwoExit) =
                BuildTreeHall(root.transform, legTwoZ, floorMat, grooveMat, panelMat, propMat,
                              fixtureMat);

            // ROOM2-1'S PUZZLE: four switches scattered on its sealed walls, each wired to one of
            // the four ceiling fixtures just built. All four dark at rest - see AllLightsOn for why
            // this reads as latched without the condition itself remembering anything, and
            // LightSwitch for why a flip is an event a ghost replays once rather than a level.
            // Dark through a SHARED material rather than `renderer.material`, which instantiates a
            // copy per panel at build time and leaves four orphans in the scene file - Unity says so
            // in a warning. The lit material is the one every other room's fixtures already wear, so
            // a switch coming on simply hands the panel back to it.
            Material fixtureOffMat = MakeEmissiveMaterial("CeilingFixtureCycle2Off", Color.white, 0f);
            foreach (Light l in room1Lights) l.enabled = false;
            foreach (Renderer p in room1Panels) p.sharedMaterial = fixtureOffMat;

            // Kept for the SECOND probe bake - see `BakeSwitchRoomLitProbe`. From here to the end of
            // the build this room is dark, so the ordinary bake photographs an unlit room and the
            // walls have no lit ceiling to reflect once the player switches it on.
            Room2OneLights = room1Lights;
            Room2OnePanels = room1Panels;
            Room2OneFixtureLit = fixtureMat;
            Room2OneFixtureOff = fixtureOffMat;

            // THE HEIGHT WAS PLACED BY HAND IN THE EDITOR AND READ BACK, which is why it is not round.
            // 1.2m is a light switch's real height and read as low and easy to miss in a dark room.
            //
            // THE SPREAD IS UNCHANGED, deliberately. Moving one switch out to 2.617 while placing it
            // said nothing about wanting all four moved - only the height was being judged.
            const float switchHeight = 1.851f;
            const float switchSpread = 1.8f;
            const float switchInset = RoomWidth / 2f;
            var lightSwitches = new LightSwitch[4];
            lightSwitches[0] = BuildLightSwitch(r1, "LightSwitch2_1_A",
                new Vector3(-switchInset, switchHeight, -switchSpread), 90f, room1Lights[0], room1Panels[0],
                fixtureMat, fixtureOffMat);
            lightSwitches[1] = BuildLightSwitch(r1, "LightSwitch2_1_B",
                new Vector3(-switchInset, switchHeight, switchSpread), 90f, room1Lights[1], room1Panels[1],
                fixtureMat, fixtureOffMat);
            lightSwitches[2] = BuildLightSwitch(r1, "LightSwitch2_1_C",
                new Vector3(switchInset, switchHeight, -switchSpread), -90f, room1Lights[2], room1Panels[2],
                fixtureMat, fixtureOffMat);
            lightSwitches[3] = BuildLightSwitch(r1, "LightSwitch2_1_D",
                new Vector3(switchInset, switchHeight, switchSpread), -90f, room1Lights[3], room1Panels[3],
                fixtureMat, fixtureOffMat);

            GameObject allLightsGO = new GameObject("AllLightsOn2_1");
            allLightsGO.transform.SetParent(r1, false);
            AllLightsOn allLightsOn = allLightsGO.AddComponent<AllLightsOn>();
            allLightsOn.switches = lightSwitches;

            // THE ROOM IS NOT DIMMED, and a `RoomDimmer` that multiplied every surface down was tried
            // and REMOVED on 2026-08-15. Recorded because the idea is an obvious one to have again.
            //
            // The problem it aimed at is real: this building is lit almost entirely by ambient standing
            // in for the bounce URP does not compute, ambient reaches every surface in the scene
            // equally, and so switching four fixtures off leaves the room only slightly less lit.
            //
            // Multiplying the surfaces down does not fix that, it breaks something else. These walls
            // read as white PAINT because of how brightly and evenly they are lit; take that away and
            // they stop looking like the same building with its lights off and start looking like a
            // different, darker material. Play called it at 0.16 and again at 0.38 - the second was
            // "about one light on", and it still read as wrong rather than as dark.
            //
            // If the room genuinely has to go dark later, the fix is in the LIGHTING rather than in the
            // surfaces: lower the global ambient and give the fixtures back the difference, which keeps
            // lit rooms looking as they do now and lets an unlit one actually fall away. That is a
            // change to numbers cycle 1 is tuned around, so it is not a small one.
            //
            // **AND THAT IS WHAT `RoomBlackout` DOES, 2026-09-04, by request - except DYNAMICALLY,
            // which is what makes it a small change after all.** The note above is right that ambient
            // is the dial and wrong that it has to be paid for globally: nothing is re-tuned, because
            // a LIT room keeps the authored values exactly. Only this room, only while its switches
            // are off, drops to a fifth of them - and it is the same component that puts them back.
            //
            // A fifth rather than nothing, because the switches are at 1.85m and this file already
            // calls them easy to miss in a dark room; at zero the puzzle loses its own pieces. See
            // `RoomBlackout` for why a global setting is safe for a local effect here - the short of
            // it is that the door below seals this room until the condition it watches is met, so
            // there is no moment when the player can see another room while it is applied.
            // Door2_1 is ringDoors[0] - the south door r1 built above, the only way out of this room.
            ringDoors[0].condition = allLightsOn;

            RoomBlackout blackout = allLightsGO.AddComponent<RoomBlackout>();
            blackout.litWhen = allLightsOn;
            blackout.darkFraction = Room2OneDarkFraction;
            // The authored ambient, handed over rather than left for the component to sample - see
            // `RoomBlackout.litSky`. The ending reads these settings expecting the values
            // `ApplyEnvironment` wrote, so a wrong restore here darkens the outside of the building.
            blackout.litSky = AmbientSky;
            blackout.litEquator = AmbientEquator;
            blackout.litGround = AmbientGround;
            // The room this blackout belongs to, so it can tell whether the player is in it - see
            // `RoomBlackout.roomCentre`. Slightly generous on every axis: the point is to answer
            // "is the player in this room", and a box that stopped exactly at the walls would let a
            // player standing against one fall outside their own room.
            blackout.roomCentre = r1.position + Vector3.up * (RoomHeight * 0.5f);
            blackout.roomSize = new Vector3(RoomWidth + 1f, RoomHeight + 1f, RoomDepth + 1f);

            // The same condition drives the probe swap, and it is captured here rather than looked
            // up at bake time for the same reason the fixtures are.
            Room2OneLitWhen = allLightsOn;

            // ROOM2-2'S TAPS. Not gating anything yet - see BuildWaterTap.
            WaterTap[] taps = BuildWaterTap(r2);

            // THE TANK, AND THE DOOR IT OPENS. Room2-2 is no longer a room you walk through.
            WaterTank tank = BuildWaterTank(r2, propMat);

            // A STAND UNDER EACH TAP, and four buckets to run between them and the tank. The stands
            // are placed where each tap's water actually lands, so "put the bucket under the tap"
            // means the thing it looks like.
            // Both placed by hand in the Editor and read back off the scene, which is why neither is a
            // round number: they sit exactly where each tap's water lands.
            // ONE PER WALL, each 1.151m in front of its own wall face - the reach measured off the
            // west mixer by hand, mirrored rather than re-eyeballed because the fixture is identical
            // on all four and so is the fall.
            BuildBucketStand(r2, "Stand_Wall", taps[0], new Vector3(-3.160f, 0f, -1.600f));
            BuildBucketStand(r2, "Stand_South", taps[1], new Vector3(2.500f, 0f, -4.035f));
            BuildBucketStand(r2, "Stand_East", taps[2], new Vector3(3.160f, 0f, 1.600f));
            BuildBucketStand(r2, "Stand_North", taps[3], new Vector3(-2.400f, 0f, 4.035f));

            // FOUR, which is the tank's four bucketloads - so the room can in principle be finished
            // in one pass if there were four pairs of hands, and cannot be with one. They share an id,
            // which ItemRegistry has supported since the pin drawer: an id can name a SUPPLY, and each
            // instance still obeys the one-object rule and returns to its own origin (CLAUDE.md §1.2).
            //
            // SCATTERED, AND TWO OF THEM KNOCKED OVER. Four buckets in a row is equipment issued to the
            // player; four lying about is a room somebody left in a hurry, which is the one this game
            // is set in. The positions dodge the tank, both stands and both doorways - a bucket in a
            // doorway is the first thing a player kicks and the last thing they find again.
            BuildBucket(r2, "Bucket_0", new Vector3(-2.70f, 0f, 2.95f), yaw: 34f, tipDegrees: 0f);
            BuildBucket(r2, "Bucket_1", new Vector3(1.85f, 0f, 3.55f), yaw: -68f, tipDegrees: 90f);
            // MOVED OUT OF THE NEW EAST STAND, which landed 0.53m from where this one used to lie -
            // close enough that the tray and the pail overlapped. A bucket sitting inside the spot it
            // is supposed to be carried TO is the one arrangement this room must not ship with.
            BuildBucket(r2, "Bucket_2", new Vector3(3.60f, 0f, 3.30f), yaw: 12f, tipDegrees: 0f);
            BuildBucket(r2, "Bucket_3", new Vector3(-3.45f, 0f, 0.85f), yaw: 121f, tipDegrees: 78f);
            // Door2_2 is ringDoors[1] - the way out of room2-2 - and its `openUntilPuzzled` comes off here,
            // which is what that flag is for: it is the to-do list kept in the scene, and a room with a
            // puzzle in it must not stand open.
            ringDoors[1].openUntilPuzzled = false;
            ringDoors[1].condition = tank;


            // THE BEDSIDE, LAID OUT EXACTLY AS CYCLE 1'S IS - same numbers, same builder calls.
            //
            // It was not, and the reason is the cycle turning round: `CycleTwoYaw` puts 180 degrees on
            // the whole cycle, so the same call that reads "bed against this wall" in cycle 1 came out
            // mirrored here - headboard the other way, chest on the other side. The two rooms are the
            // same room in the fiction and every iteration opens in one of them, so having them differ
            // is the kind of wrong that is felt without being noticed.
            //
            // A WRAPPER THAT CANCELS THE CYCLE'S TURN is what fixes it, rather than mirrored numbers.
            // Inside `BedFrame` the net rotation is identity again, so cycle 1's figures can be used
            // verbatim below and stay correct if the cycle's yaw ever changes.
            GameObject bedFrame = new GameObject("Room2_1_Bedside");
            bedFrame.transform.SetParent(r1, false);
            bedFrame.transform.localRotation = Quaternion.Inverse(CycleTwoYaw);

            // `zCentre` is a WORLD z, because `PlaceModel` measures world bounds - and r1's world z is
            // its local one, since the cycle's rotation pivots on this very room.
            (_, Transform spawn) = BuildBed(bedFrame.transform, propMat,
                                            floorY: -StoreyDrop, zCentre: ringZ, yaw: 0f);

            // ~~BuildNightstand~~ REPLACED 2026-08-15 by the chest of drawers, which is what this
            // bedside was asked for in the first place. The nightstand brought THREE pins with it and
            // cycle 2 has nothing to use a pin on.
            //
            // AND IT IS NO LONGER EMPTY, 2026-08-19: nine billiard balls across the two trays, which
            // are room2-0's keys. That is why the chest was built with two OPENING bays rather than
            // one and a shelf - see BuildDresser - and why both were wired as `GhostInteractable`s
            // from the first build, before there was anything to open them for.
            //
            // Cycle 1's own nightstand position, unchanged, now that the frame above makes it mean
            // the same thing here.
            Drawer[] dresserDrawers = BuildDresser(bedFrame.transform, "Dresser2_1",
                new Vector3(-0.95f, 0f, 1.35f), yaw: 0f, withLamp: true, withBilliards: true);

            // The last couple of metres up to each opening, from inside the room. Short on purpose:
            // see AssertWalkable for why this is not a path across the room.
            float outZ = RoomDepth / 2f - 0.7f, outX = RoomWidth / 2f - 0.7f;
            Vector3 nIn = new Vector3(0f, 0f, outZ - 2.2f), nOut = new Vector3(0f, 0f, outZ);
            Vector3 sIn = new Vector3(0f, 0f, -outZ + 2.2f), sOut = new Vector3(0f, 0f, -outZ);
            Vector3 wIn = new Vector3(-outX + 2.2f, 0f, 0f), wOut = new Vector3(-outX, 0f, 0f);

            AssertWalkable(rooms[0], "room2-1 south door", sIn, sOut);
            AssertWalkable(rooms[1], "room2-2 north door", nIn, nOut);
            AssertWalkable(rooms[1], "room2-2 south door", sIn, sOut);
            // ROOM2-6 IS ENTERED BY FALLING INTO IT, so there is no approach to walk. What is checked
            // instead is the way OUT: from the middle of the pool to its door, which is the walk the
            // drain has to leave possible.
            AssertWalkable(poolRoom, "room2-6 south door", sIn, sOut);
            AssertWalkable(weighRoom, "room2-7 north door", nIn, nOut);
            AssertWalkable(weighRoom, "room2-7 south door", sIn, sOut);
            AssertWalkable(breakRoom, "room2-0 north door", nIn, nOut);

            // THE HALL'S OWN CHECKS, in its own frame rather than a room's: both openings are in
            // the north wall now, one at x=0 and one at x=-20.825, twenty metres apart.
            //
            // THE THIRD IS THE POINT, and it is the only assert in this project that expects its walk
            // to FAIL: the near ledge to the far ledge, straight across the pit. If that ever comes
            // back walkable, the hole is not a hole and the tree is decoration.
            float hallOut = TreeHallNorthFace - 0.7f;
            AssertWalkable(hall, "tree hall entrance",
                new Vector3(TreeHallEntranceX, 0f, hallOut - 2.2f),
                new Vector3(TreeHallEntranceX, 0f, hallOut));
            AssertWalkable(hall, "tree hall exit",
                new Vector3(TreeHallExitX, 0f, hallOut - 2.2f),
                new Vector3(TreeHallExitX, 0f, hallOut));
            AssertNotWalkable(hall, "tree hall pit",
                new Vector3(TreePitEastEdge - 0.6f, 0f, 0f),
                new Vector3(TreePitWestEdge + 0.6f, 0f, 0f));

            ParticleSystem[] gas = BuildGasEmitters(r1, "Room2_1_Gas", 0f);

            // WHAT A GHOST CAN OPERATE IN CYCLE 2: room2-1's four switches at bits 0-3, room2-2's
            // FOUR taps at 4-7, and the tank's pour point at 8. **APPEND ONLY, NEVER REORDER** - an
            // entry's index IS its bit in RecordedFrame.signals, so moving one changes what every
            // recorded frame means.
            //
            // The two new taps are appended INSIDE the tap array (BuildWaterTap returns them last),
            // which keeps the original pair on bits 4 and 5. The pour point does shift from 6 to 8,
            // and that is safe for the reason the rule is really about: a timeline is runtime-only and
            // nothing serialises one, so a REBUILD has no surviving recordings to invalidate. What the
            // rule forbids is mutating the array while ghosts born against it are alive.
            //
            // The taps matter more than the switches here. A tap is the first thing in this game that
            // a past self can leave RUNNING while you are elsewhere, which is the only way anything
            // that takes longer than sixty seconds ever gets done.
            //
            // The pour is a signal for a reason that took a while to see: it is the only thing a past
            // self does in this room that hands nothing over, so no carry event describes it, and
            // without a bit of its own the tank could only ever be filled by the living player - see
            // PourPoint.
            var cycleTwoSignalList = new System.Collections.Generic.List<GhostInteractable>(lightSwitches);
            cycleTwoSignalList.AddRange(taps);
            if (tank.pourPoint != null) cycleTwoSignalList.Add(tank.pourPoint);
            // Room2-3's two drawers, at 9 and 10. Wired BEFORE anything is in them, deliberately: a
            // drawer with something in it that a past self cannot open is a room that quietly needs
            // the living player for an errand the loop was supposed to absorb, and appending a bit
            // later is the one change this array makes awkward.
            cycleTwoSignalList.AddRange(dresserDrawers);
            // THE TREE, at bit 11 - ONE bit for any number of choppers. Each ghost carries its own
            // timeline, so "this past self was swinging at this moment" is per-ghost already; the
            // bit says only that, and whether the swing lands is asked of that ghost's own hands at
            // replay (TreeTrunk.SetGhostSignal). Five simultaneous axes cost one bit, not five.
            if (treeTrunk != null) cycleTwoSignalList.Add(treeTrunk);
            // ROOM2-6'S THREE VALVES, at 12, 13 and 14. One bit each rather than one for the room,
            // because they are three separate things a past self can be doing - and the whole design
            // of that room is that six turns is more than one pair of hands fits in a minute, so a
            // ghost standing at the far wheel IS the solution. Appended, like everything else here.
            if (poolValves != null) cycleTwoSignalList.AddRange(poolValves);

            // The hall's exit joins the ring's four here rather than being wired separately: `Cycle`
            // shuts every door it is handed at the top of an iteration, and one left out of the list
            // is one that stays open into the next.
            //
            // IN WALK ORDER, which is what makes the LAST one the door into room0 - the one
            // `BuildFinalRoom` seals at the break. Appending the hall's exit on the end instead would
            // have been silently wrong there, and it is not the kind of wrong that shows up until
            // somebody finishes the cycle.
            // ROOM2-7'S DOOR, ONTO ROOM2-8. **NOT capped** - there is a room through it, and
            // `capFarSide` puts a solid wall across the pocket, which is exactly how room2-7 came to
            // be invisible behind room2-6's door for a build. The flag is a statement about where the
            // WALK ENDS, and the walk ends at room2-0 now - the room that breaks the cycle.
            Door weighDoor = BuildPadDoor(weighRoom, "Door2_7", 0f, new FloorButton[0], propMat,
                                          yaw: 180f);
            weighDoor.condition = weighScale;
            BuildDoorPocketFill(weighRoom, "Pocket2_7", 0f, grooveMat, capFarSide: false, yaw: 180f);

            // FOUR DOORS. The hall's own exit is gone with leg 3 - the tree opens no door, it opens
            // the only bridge across the pit (see BuildTreeHall) - and room2-7's is the LAST, which
            // makes it the one room2-0 seals at the break. That ordering is load-bearing and silently
            // so: `Cycle` shuts every door it is handed, and the break shuts this one in the player's
            // view. Appending anything after it would put the wrong door on that line.
            var doors = new[] { ringDoors[0], ringDoors[1], poolDoor, weighDoor };

            // ROOM2-0'S TWO REFERENCES BACK UP THE WALK, wired here because the room is built with
            // the hall and the door it hangs on is built with the ring. The trigger sits on room2-0's
            // own threshold and only counts a player who came through an OPEN door - so walking up to
            // a shut one arms nothing, and the scale is what opens it.
            if (breakSequence != null)
            {
                breakSequence.doorBehind = weighDoor;
                if (breakSequence.arrival != null) breakSequence.arrival.door = weighDoor;
            }

            // AND THE NEW ROOMS ARE IN THE ROOM LIST, appended rather than built into it: the loop
            // above lights and probes the shells, and these three did both for themselves when the
            // hall built them.
            var allRooms = new[] { r1, r2, poolRoom, weighRoom, breakRoom };

            // THE THREE HUNG A FURTHER STOREY DOWN. Named here, where the names still mean something,
            // rather than found by string at the far end: `AssembleCycleTwo` sets one floor base over
            // the whole cycle and these three do not share it - their floors are `SlideRoomFloorY`
            // below the rest of it, because the slide has to arrive somewhere lower than it leaves.
            var loweredRooms = new[] { poolRoom, weighRoom, breakRoom };

            return (root.transform, spawn, gas, doors,
                    new RoomCondition[] { allLightsOn, tank, treeFelled, poolDrain, weighScale },
                    cycleTwoSignalList.ToArray(), allRooms, loweredRooms, breakSequence,
                    cycleTwoExit);
        }

        // WHAT A RETRACTED PLINTH RETRACTS INTO.
        //
        // Every `RewardPlinth` is authored raised and sunk by `riseHeight` in Awake, so when it is
        // down it sits about a metre BELOW the floor. That was invisible for as long as there was
        // nothing under the floor to see it from. With a storey below, room1-0's console hangs a
        // metre out of room2-1's ceiling - the reported "cycle 1's structures are visible in cycle
        // 2's ceiling".
        //
        // Five sides, open at the top, so the plinth still rises straight out of it. From above
        // nothing changes: the floor slab is opaque and the housing is under it. From below it reads
        // as building services, which is what a retracting plinth would actually need.
        //
        // Only built where a room exists underneath. The other three plinths - Room3's, the cube
        // room's, and the chess reward - sink into nothing today, and will want this when cycle 2
        // grows rooms beneath them.
        private static void BuildPlinthHousing(Transform parent, string name, Vector3 centreXZ,
                                               float footprintX, float footprintZ, float depth, Material mat)
        {
            GameObject well = new GameObject(name);
            well.transform.SetParent(parent, false);
            // Hung from the underside of the floor slab, which is where its open top belongs.
            well.transform.localPosition = new Vector3(centreXZ.x, -WallThickness, centreXZ.z);

            float halfX = footprintX / 2f, halfZ = footprintZ / 2f;

            Prim(PrimitiveType.Cube, "Bottom", well.transform,
                new Vector3(0f, -depth - WallThickness / 2f, 0f),
                new Vector3(footprintX + 2f * WallThickness, WallThickness, footprintZ + 2f * WallThickness), mat);

            Prim(PrimitiveType.Cube, "Side_West", well.transform,
                new Vector3(-halfX - WallThickness / 2f, -depth / 2f, 0f),
                new Vector3(WallThickness, depth, footprintZ + 2f * WallThickness), mat);
            Prim(PrimitiveType.Cube, "Side_East", well.transform,
                new Vector3(halfX + WallThickness / 2f, -depth / 2f, 0f),
                new Vector3(WallThickness, depth, footprintZ + 2f * WallThickness), mat);
            Prim(PrimitiveType.Cube, "Side_South", well.transform,
                new Vector3(0f, -depth / 2f, -halfZ - WallThickness / 2f),
                new Vector3(footprintX, depth, WallThickness), mat);
            Prim(PrimitiveType.Cube, "Side_North", well.transform,
                new Vector3(0f, -depth / 2f, halfZ + WallThickness / 2f),
                new Vector3(footprintX, depth, WallThickness), mat);
        }

        // A SOFT ROUND BLOB, which is the whole of what a vapour particle needs to be. Generated
        // like every other texture here rather than sourced.
        //
        // The falloff is squared rather than linear: a linear ramp gives a disc with a visible rim,
        // and what has to be invisible is the EDGE of the sprite - a hundred overlapping billboards
        // only read as one cloud if none of them has a boundary you can find.
        private static Texture2D MakeSoftBlobTexture(string name, int size)
        {
            Color[] px = new Color[size * size];
            float half = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a *= a;
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            return WriteTexture(name, size, size, px, TextureWrapMode.Clamp, FilterMode.Bilinear);
        }

        // WHERE THE GAS COMES FROM: the top of every wall, all four sides, nowhere to stand that is
        // not under one.
        //
        // PARTICLES, after two simpler attempts failed for the same reason. A lit slot along each
        // wall left four near-black strips in a white room permanently, in exchange for a moment; a
        // translucent box that grew downward left a zero-height quad at the wall head that still
        // caught a specular highlight. Both were trying to fake a volume with a surface, and a
        // surface always has an edge you can find.
        //
        // What makes this read as vapour rather than as sprites is the NOISE module - without it a
        // hundred soft blobs drifting on straight lines look exactly like a hundred soft blobs. The
        // damping is the other half: the gas decelerates as it comes off the wall, so it pours in
        // and then hangs, which is what a heavier-than-air gas does in a sealed room.
        private static ParticleSystem[] BuildGasEmitters(Transform parent, string name, float roomCenterZ)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 0f, roomCenterZ);

            Texture2D blob = MakeSoftBlobTexture("GasBlob", 128);
            Material gasMat = MakeParticleMaterial("GasVapour", blob);

            float y = RoomHeight - 0.25f;
            float halfX = RoomWidth / 2f;
            float halfZ = RoomDepth / 2f;

            // Position, the direction it blows, and how wide the emitter slot is along the wall.
            var walls = new (Vector3 at, Vector3 inward, float run)[]
            {
                (new Vector3(0f, y, -halfZ + 0.3f), Vector3.forward, RoomWidth * 0.8f),
                (new Vector3(0f, y,  halfZ - 0.3f), Vector3.back,    RoomWidth * 0.8f),
                (new Vector3(-halfX + 0.3f, y, 0f), Vector3.right,   RoomDepth * 0.8f),
                (new Vector3( halfX - 0.3f, y, 0f), Vector3.left,    RoomDepth * 0.8f),
            };

            var systems = new System.Collections.Generic.List<ParticleSystem>();

            for (int i = 0; i < walls.Length; i++)
            {
                GameObject go = new GameObject($"GasEmitter_{i}");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = walls[i].at;
                // Aimed inward AND down. Gas coming straight off a wall reads as a fan; this is a
                // vent in a ceiling corner, so it should already be falling as it arrives.
                go.transform.localRotation = Quaternion.LookRotation(
                    (walls[i].inward + Vector3.down * 0.7f).normalized, Vector3.up);

                ParticleSystem ps = go.AddComponent<ParticleSystem>();
                ConfigureGasEmitter(ps, walls[i].run, gasMat);
                systems.Add(ps);
            }

            return systems.ToArray();
        }

        private static void ConfigureGasEmitter(ParticleSystem ps, float run, Material mat)
        {
            // Stopped, and stopped from the start: the room is not being gassed until it is.
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 12f;
            // LONG-LIVED and SLOW. Vapour that clears in two seconds is a puff of smoke; this has to
            // still be in the air while the player goes down on top of it.
            main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 11f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.22f, 0.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.7f, 3.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // Very low alpha per particle. The cloud is built out of overlap, not out of any one
            // billboard being visible - which is what stops it looking like sprites.
            main.startColor = new Color(0.95f, 0.965f, 0.98f, 0.085f);
            // Barely heavier than air. Enough that it settles rather than hangs at head height.
            main.gravityModifier = 0.014f;
            // WORLD, or the whole cloud would drag along with anything that moved the emitter.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 260;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 13f;

            // A slot along the wall rather than a point, so it arrives as a sheet.
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(run, 0.25f, 0.15f);
            shape.randomDirectionAmount = 0.12f;

            // It pours in and then hangs. Without the damping it crosses the room and piles up
            // against the far wall, which is a wind tunnel rather than a room filling.
            ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.055f;
            limit.limit = new ParticleSystem.MinMaxCurve(0.5f);

            // THE ONE THAT MAKES IT VAPOUR. Straight-line blobs read as sprites however soft they
            // are; a slow large-scale wobble reads as something moving through air.
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.55f);
            noise.frequency = 0.17f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.09f);
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.High;

            // Fade in over the first fifth, hold, fade out over the last third - so nothing ever
            // appears or vanishes, which is the other half of not looking like sprites.
            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.22f),
                    new GradientAlphaKey(0.85f, 0.62f),
                    new GradientAlphaKey(0f, 1f),
                });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);

            // Spreading as it goes, which is what a gas does and what hides the billboard edges.
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            var grow = new AnimationCurve();
            grow.AddKey(0f, 0.55f);
            grow.AddKey(1f, 1.45f);
            size.size = new ParticleSystem.MinMaxCurve(1f, grow);

            ParticleSystem.RotationOverLifetimeModule spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

            var psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.alignment = ParticleSystemRenderSpace.View;
            psr.sharedMaterial = mat;
            psr.sortMode = ParticleSystemSortMode.Distance;
            // Nothing this soft should be casting or catching a shadow.
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;
        }

        // A FLAT DECAL: an unlit, alpha-blended quad for something drawn ON a surface - the digits
        // scrawled on the wall and the readouts lying on the pads.
        //
        // Unlit rather than Lit, and that is the point rather than a saving. Ink on a wall has no
        // shading of its own; a Lit decal picks up the ceiling fixtures and reads as a sticker with a
        // sheen. It also sidesteps the trap that produced a black band along the wall head, where a
        // Lit transparent material kept its specular at zero alpha.
        //
        // Deliberately NOT the particle material next door, which is the same idea for a different
        // renderer: URP's particle shaders expect vertex streams a MeshRenderer does not supply.
        private static Material MakeDecalMaterial(string name, Texture2D map, Color tint)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetTexture("_BaseMap", map);
            mat.mainTexture = map;
            mat.SetColor("_BaseColor", tint);
            mat.color = tint;

            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Unlit and alpha-blended, NOT premultiplied. The translucent Lit material this replaces
        // preserved specular at zero alpha, which is exactly how an invisible box ended up as a
        // visible band along the wall head - see BuildGasEmitters. Unlit has no specular to preserve.
        private static Material MakeParticleMaterial(string name, Texture2D map)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetTexture("_BaseMap", map);
            mat.mainTexture = map;
            mat.SetColor("_BaseColor", Color.white);

            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ROOM2-1'S PUZZLE: five digits scrawled on the south wall, and five counting pads standing
        // one grid cell out from it, one in front of each.
        //
        // WHY THE WALL AND NOT A SIGN. The digits go on the panel grid itself, in the second row up -
        // reachable height, where somebody standing in this room would have written them. The grid is
        // what makes "one digit per cell, in order" legible without a single line of instruction: the
        // wall is already divided into five columns and the answer is one per column.
        //
        // WHY ONE CELL OUT. A pad hard against the wall would be read as part of the wall; a cell's
        // gap makes the pairing an arrangement in the room rather than a label. It is also far enough
        // that the digit above is still in view while standing on the pad, which matters because the
        // player will be counting.
        // WHAT THE PLAYER FALLS THROUGH between the two storeys: a square tube joining the hole in
        // one cycle's floor to the hole in the next one's ceiling, across the service void.
        //
        // Without it the void is open to the room - you would drop out of the floor into a roof space
        // with the plinth housing and the underside of the slab on show. With it, the drop reads as
        // going down a shaft, and the void either side stays sealed.
        //
        // Four walls and no cap at either end, because both ends are the holes it exists to connect.
        private static void BuildExitShaft(Transform parent, string name, float roomCenterZ, Material mat)
        {
            GameObject shaft = new GameObject(name);
            shaft.transform.SetParent(parent, false);
            // Hung from the underside of the floor it starts at, running down the whole void.
            shaft.transform.localPosition = new Vector3(0f, -WallThickness, roomCenterZ + CycleExitZ);

            float half = GridCellWidth / 2f;
            float outer = GridCellWidth + 2f * WallThickness;

            Prim(PrimitiveType.Cube, "Shaft_West", shaft.transform,
                new Vector3(-half - WallThickness / 2f, -ServiceVoid / 2f, 0f),
                new Vector3(WallThickness, ServiceVoid, outer), mat);
            Prim(PrimitiveType.Cube, "Shaft_East", shaft.transform,
                new Vector3(half + WallThickness / 2f, -ServiceVoid / 2f, 0f),
                new Vector3(WallThickness, ServiceVoid, outer), mat);
            Prim(PrimitiveType.Cube, "Shaft_South", shaft.transform,
                new Vector3(0f, -ServiceVoid / 2f, -half - WallThickness / 2f),
                new Vector3(GridCellWidth, ServiceVoid, WallThickness), mat);
            Prim(PrimitiveType.Cube, "Shaft_North", shaft.transform,
                new Vector3(0f, -ServiceVoid / 2f, half + WallThickness / 2f),
                new Vector3(GridCellWidth, ServiceVoid, WallThickness), mat);
        }

        // The lid over that hole: a slab of floor that slides aside when the console is full.
        //
        // Slides sideways INTO the surrounding floor slab, which is solid, so it is out of sight the
        // moment it is open - the same thing a door slab does inside its pocket, without needing a
        // pocket built for it. Authored closed, so the room can be looked at in the editor as the
        // player first meets it.
        private static CycleExit BuildCycleExit(Transform parent, float roomCenterZ, Material floorMat, Transform player)
        {
            GameObject go = new GameObject("CycleExit");
            go.transform.SetParent(parent, false);
            // At the floor plane of the room it belongs to, which is what `throughDrop` is measured
            // from - "the player has fallen well below the floor they were standing on".
            go.transform.localPosition = new Vector3(0f, 0f, roomCenterZ + CycleExitZ);

            // BOTH LIDS ARE FLUSH WITH THE SURFACE THEY FILL, and both retract into the service
            // void - the floor one drops as it slides, the ceiling one rises.
            //
            // The first version recessed each by 10mm to dodge a coplanar-face z-fight against the
            // slab it slid into. That worked and left a 10mm hatch outline in the ceiling of the room
            // below, lit differently from everything round it - which is exactly what a lid is not
            // supposed to look like when it is shut. Retracting out of the slab instead means there
            // is nothing to be coplanar with, so the lid can fill its hole exactly.
            GameObject upper = Prim(PrimitiveType.Cube, "Cover_Floor", go.transform,
                new Vector3(0f, -WallThickness / 2f, 0f),
                new Vector3(GridCellWidth, WallThickness, GridCellWidth), floorMat);

            float ceilingMid = -(ServiceVoid + 1.5f * WallThickness);
            // THE CEILING LID TAKES THE CEILING'S MATERIAL, which stopped being the floor's on
            // 2026-08-20 when the two were split (albedo 0.85/smoothness 0.3 against 1.0/0). Left on
            // `floorMat` it is a darker, glossier square sitting in a matte white ceiling - play saw
            // it as a patch of the wrong colour, which is exactly what it was.
            GameObject lower = Prim(PrimitiveType.Cube, "Cover_Ceiling", go.transform,
                new Vector3(0f, ceilingMid, 0f),
                new Vector3(GridCellWidth, WallThickness, GridCellWidth), CeilingMaterial());

            CycleExit exit = go.AddComponent<CycleExit>();
            exit.covers = new[] { upper.transform, lower.transform };
            // Sideways by its own width so the hole is fully clear, and out of its own slab by rather
            // more than the slab is thick so neither lid can be seen edge-on through the opening.
            exit.openOffsets = new[]
            {
                new Vector3(GridCellWidth, -0.16f, 0f),
                new Vector3(GridCellWidth,  0.16f, 0f),
            };
            exit.player = player;
            exit.audioSource = MakeSource(go.transform, "ExitAudio", spatialBlend: 1f, volume: 0.9f);
            exit.openClip = LoadClip(SfxDir, "sfx_door_open");
            exit.sealClip = LoadClip(SfxDir, "sfx_power_down");
            return exit;
        }

        private static void BuildShell(Transform parent, Material floorMat, Material grooveMat, Material panelMat)
        {
            // The doorway is cut out of the panelling as an exact rectangle, so panels frame the
            // door instead of the door having to fit a whole number of cells.
            Rect doorway = new Rect(-DoorWidth / 2f, 0f, DoorWidth, DoorHeight);

            // Six rooms in a line, each sharing a divider with the next: a room's north wall and its
            // neighbour's south wall face each other across the door pocket, each with the same
            // doorway cut out of its panelling, its backing and its collision, so the opening is a
            // real hole. Room1 is the only one with no doorway to the south, and ROOM4 THE ONLY ONE
            // WITH NONE TO THE NORTH - it is the end of the building, and there is deliberately
            // nothing past it to look at or walk to.
            //
            // Room2West and Room2East used to be a pair of side rooms turned ninety degrees off
            // Room2's east and west walls. They are ON THE CHAIN now, the same shape and orientation
            // as every other room here, between Room2 and Room3 - see Build() for why: a hub with
            // three doors home to the same room read as a walk back and forth between them rather
            // than as progress, and the fix was to let the coloured doors BE the corridor instead of
            // branching off it.
            BuildRoomShell(parent, "Room1", 0f, floorMat, grooveMat, panelMat, Rect.zero, doorway);
            BuildRoomShell(parent, "Room2", RoomPitch, floorMat, grooveMat, panelMat, doorway, doorway);
            BuildRoomShell(parent, "Room2West", 2f * RoomPitch, floorMat, grooveMat, panelMat, doorway, doorway);
            BuildRoomShell(parent, "Room2East", 3f * RoomPitch, floorMat, grooveMat, panelMat, doorway, doorway);
            BuildRoomShell(parent, "Room3", 4f * RoomPitch, floorMat, grooveMat, panelMat, doorway, doorway);
            // Identical to the others in every way, and that is the point rather than a saving:
            // the room the player finally gets out into is the same white cell they have been in
            // for the whole run.
            //
            // EXCEPT FOR THE HOLE IN ITS FLOOR. `room1-0` is the hinge: it closes cycle 1 and opens
            // onto cycle 2, and the way on is a grid cell of floor behind the console that slides
            // aside once the console is full. Its north wall stays solid - there is still nothing past
            // the end of the building, the way out is DOWN.
            BuildRoomShell(parent, "Room4", 5f * RoomPitch, floorMat, grooveMat, panelMat, doorway, Rect.zero,
                floorHole: CycleExitHole);

            // One pocket per join, all UNCAPPED: every one of them has a room through it, and a cap
            // would be a wall across the only way to the next. Room4 needs no pocket of its own - its
            // north wall has no doorway cut in it, so there is no cavity there to close, the same
            // reason the calibration room has none.
            BuildDoorPocketFill(parent, "DoorPocketFill_1", 0f, grooveMat, capFarSide: false);
            BuildDoorPocketFill(parent, "DoorPocketFill_2", RoomPitch, grooveMat, capFarSide: false);
            BuildDoorPocketFill(parent, "DoorPocketFill_3", 2f * RoomPitch, grooveMat, capFarSide: false);
            BuildDoorPocketFill(parent, "DoorPocketFill_4", 3f * RoomPitch, grooveMat, capFarSide: false);
            BuildDoorPocketFill(parent, "DoorPocketFill_5", 4f * RoomPitch, grooveMat, capFarSide: false);

            // A sealed copy of the same shell, well clear of the chain, used for nothing but the
            // mouse-sensitivity step before iteration 1. Rect.zero for both cutouts, so it has no
            // doorways at all - the player is meant to look around it, not leave it, and there is
            // nothing to leave through.
            //
            // Empty on purpose: no bed, no pad, no door, no lamp. What is being judged is how fast
            // the room goes round, and furniture is something to look AT rather than something that
            // shows motion. The panelling does that better than any prop - it is a regular grid, so
            // the grooves sweeping past give the eye an unambiguous read on speed.
            //
            // Far enough out that no slab or wall overrun can touch Room1: this spans Z -26.95 to
            // -16.45 against Room1's -5.25 to 5.25.
            Material fixtureMat = MakeEmissiveMaterial("CeilingFixture", Color.white, CeilingPanelEmission);

            // Shadows only in Room1. Every additional light's shadow shares one atlas, and the
            // rooms past the first hold nothing that casts a shadow worth the map: Room2 is
            // balloons, Room3 is two floor pads.
            BuildCeilingLights(parent, "Room1", 0f, fixtureMat, castShadows: true);
            BuildCeilingLights(parent, "Room2", RoomPitch, fixtureMat, castShadows: true);
            // Room2West's fixtures are kept: the chess board dims them and brings them back up as
            // its reward, so something downstream needs the references rather than just the room.
            (Room2WestLights, Room2WestPanels) =
                BuildCeilingLights(parent, "Room2West", 2f * RoomPitch, fixtureMat, castShadows: true);
            BuildCeilingLights(parent, "Room2East", 3f * RoomPitch, fixtureMat, castShadows: true);
            BuildCeilingLights(parent, "Room3", 4f * RoomPitch, fixtureMat, castShadows: true);
            BuildCeilingLights(parent, "Room4", 5f * RoomPitch, fixtureMat, castShadows: true);
            // Built after the lights, so the probes capture the rooms already lit.
            BuildReflectionProbe(parent, "Room1", 0f);
            BuildReflectionProbe(parent, "Room2", RoomPitch);
            BuildReflectionProbe(parent, "Room2West", 2f * RoomPitch);
            BuildReflectionProbe(parent, "Room2East", 3f * RoomPitch);
            BuildReflectionProbe(parent, "Room3", 4f * RoomPitch);
            BuildReflectionProbe(parent, "Room4", 5f * RoomPitch);
        }

        // Fills the cavity between the two rooms' walls, everywhere except the volume the door slab
        // actually sweeps through.
        //
        // Left as one open pocket, that cavity ran the full width of the building and exited to the
        // sky at both ends - standing in the doorway and looking sideways showed a 0.1m x 5m slot
        // straight to the outside, which is the "you can see through between the walls" report.
        // Capping it also gives the opening a proper reveal instead of a hollow slot at the jamb.
        //
        // Rotated by yaw for a door in a side wall, for the same reason BuildDoorShell is: the pocket
        // geometry is identical, only its axis differs, and a second copy of it would be a second place
        // for the "you can see between the walls" bug to come back. wallHalfExtent is the wall's
        // distance from the room centre along the door's normal, crossHalfWidth how far the fill runs
        // along the wall.
        private static void BuildDoorPocketFill(Transform parent, string name, float roomCenterZ, Material mat,
                                                bool capFarSide,
                                                float wallHalfExtent = RoomDepth / 2f,
                                                float crossHalfWidth = RoomWidth / 2f + WallDepth,
                                                float yaw = 0f)
        {
            GameObject fill = new GameObject(name);
            fill.transform.SetParent(parent, false);
            // The room offset and the rotation both live on the root now, so the measurements below
            // are relative and read the same at any yaw.
            fill.transform.localPosition = new Vector3(0f, 0f, roomCenterZ);
            fill.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            float pocketCenterZ = wallHalfExtent + WallDepth + DoorPocketDepth / 2f;

            // Matches the floor and ceiling slabs, so the fill reaches the outer face of the walls it
            // runs between and the divider is closed off at the same plane they are.
            float halfWidth = crossHalfWidth;
            Rect pocket = Rect.MinMaxRect(-halfWidth, 0f, halfWidth, RoomHeight);

            // The slab's full sweep: closed at -DoorWidth/2, open a further DoorWidth to the right.
            // The clearance keeps the fill's faces off the door's, which would otherwise be exactly
            // coincident planes.
            const float clearance = 0.01f;
            Rect doorSweep = Rect.MinMaxRect(
                -DoorWidth / 2f - clearance, 0f,
                DoorWidth / 2f + DoorWidth + clearance, DoorHeight + clearance);

            // No colliders: the walls either side of the doorway already carry the collision, and
            // the only way to reach this volume is through the opening the fill deliberately leaves.
            int piece = 0;
            foreach (Rect part in SubtractRect(pocket, doorSweep))
            {
                if (part.width <= 0.001f || part.height <= 0.001f) continue;

                Prim(PrimitiveType.Cube, $"Fill_{piece++}", fill.transform,
                    new Vector3(part.center.x, part.center.y, pocketCenterZ),
                    new Vector3(part.width, part.height, DoorPocketDepth),
                    mat, removeCollider: true);
            }

            // Seals the far mouth of the pocket where there is no next room to close it. Without
            // this the doorway is a hole to the outside once the door slides clear - the same
            // failure the pocket fill exists to stop, just at the other end of the building. It
            // KEEPS its collider: this is the end of the world for now, and the player must not be
            // able to walk out through the door they just unlocked.
            if (!capFarSide) return;

            Prim(PrimitiveType.Cube, "FarCap", fill.transform,
                new Vector3(0f, RoomHeight / 2f, pocketCenterZ + DoorPocketDepth / 2f + WallThickness / 2f),
                new Vector3(halfWidth * 2f, RoomHeight, WallThickness),
                mat);
        }

        // westCutout/eastCutout default to none and nothing in the chain uses them any more -
        // Room2West and Room2East were the one thing that did, back when they were side rooms turned
        // ninety degrees off Room2's walls. Left on `BuildPanelWall`, which has always taken a cutout
        // in wall-local coordinates and been indifferent to which axis the wall runs along, on the
        // reasoning that a wall with a doorway in an unusual side is cheap to keep able and expensive
        // to re-derive the day something needs it again.
        // floorHole / ceilingHole: a rectangle in ROOM-LOCAL XZ (x across the room, y of the Rect being
        // Z relative to zCenter) to leave out of that slab. Rect.zero means none, which is every room
        // but the two a cycle boundary passes through.
        //
        // The floor takes `floorMat` and the ceiling takes `CeilingMaterial()`, and the two are NOT
        // interchangeable: a floor is directly under the lights and a ceiling faces away from every
        // one of them, so the albedo that stops one clipping is the albedo that kills the other. A
        // pair of per-room overrides lived here for a day in 2026-08-20 while that was being settled;
        // the answer is in the two materials now, so a room does not get to disagree with it.
        private static void BuildRoomShell(Transform parent, string roomName, float zCenter, Material floorMat, Material grooveMat, Material panelMat, Rect southCutout, Rect northCutout, Rect westCutout = default, Rect eastCutout = default, Rect floorHole = default, Rect ceilingHole = default)
        {
            GameObject room = new GameObject(roomName);
            room.transform.SetParent(parent, false);
            Transform t = room.transform;

            float minX = -RoomWidth / 2f, maxX = RoomWidth / 2f;
            float minZ = zCenter - RoomDepth / 2f, maxZ = zCenter + RoomDepth / 2f;

            BuildSlab(t, "Floor", -WallThickness / 2f, zCenter, floorMat, floorHole);
            BuildSlab(t, "Ceiling", RoomHeight + WallThickness / 2f, zCenter, CeilingMaterial(), ceilingHole);

            BuildPanelWall(t, "Wall_South", new Vector3(0f, 0f, minZ), Vector3.right, Vector3.forward, RoomWidth, grooveMat, panelMat, southCutout);
            BuildPanelWall(t, "Wall_North", new Vector3(0f, 0f, maxZ), Vector3.right, Vector3.back, RoomWidth, grooveMat, panelMat, northCutout);
            BuildPanelWall(t, "Wall_West", new Vector3(minX, 0f, zCenter), Vector3.forward, Vector3.right, RoomDepth, grooveMat, panelMat, westCutout);
            BuildPanelWall(t, "Wall_East", new Vector3(maxX, 0f, zCenter), Vector3.forward, Vector3.left, RoomDepth, grooveMat, panelMat, eastCutout);
        }

        // A floor or a ceiling, with an optional hole in it.
        //
        // Slabs run the full room PITCH, not just the interior, so neighbouring rooms' floors meet
        // exactly under the divider. Sized to the interior they would leave an open gap in the doorway
        // threshold and the player would drop through it.
        //
        // THE HOLE REUSES `SubtractRect`, which is the whole reason a floor opening turned out to be
        // cheap. That function is pure Rect arithmetic - it has no idea its inputs are usually
        // along-a-wall and height - so handing it (x, z) instead cuts a floor exactly the way it cuts
        // panelling, collision and backing. Without a hole this emits one cube named `Floor` or
        // `Ceiling` exactly as before, which keeps every room that has no opening byte-identical.
        private static void BuildSlab(Transform t, string name, float yCenter, float zCenter, Material mat, Rect holeLocalXZ)
        {
            float halfX = RoomWidth / 2f + WallDepth;
            Rect slab = Rect.MinMaxRect(-halfX, zCenter - RoomPitch / 2f, halfX, zCenter + RoomPitch / 2f);

            // The hole arrives in room-local Z; the slab is in the room's own frame, where Z is
            // absolute. One offset reconciles them, and it is why the SAME Rect can be handed to two
            // rooms one storey apart and line up.
            Rect hole = holeLocalXZ.width > 0f && holeLocalXZ.height > 0f
                ? Rect.MinMaxRect(holeLocalXZ.xMin, zCenter + holeLocalXZ.yMin,
                                  holeLocalXZ.xMax, zCenter + holeLocalXZ.yMax)
                : new Rect();

            var parts = SubtractRect(slab, hole);

            if (parts.Count == 1)
            {
                Prim(PrimitiveType.Cube, name, t, new Vector3(0f, yCenter, zCenter),
                     new Vector3(slab.width, WallThickness, slab.height), mat);
                return;
            }

            for (int i = 0; i < parts.Count; i++)
            {
                Rect part = parts[i];
                Prim(PrimitiveType.Cube, $"{name}_{i + 1}", t,
                     new Vector3(part.center.x, yCenter, part.center.y),
                     new Vector3(part.width, WallThickness, part.height), mat);
            }
        }

        // Builds one wall as a recessed backing slab plus a grid of raised panels, so the seams are
        // actual grooves rather than a painted-on pattern.
        // faceCenterAtBase: the point on the finished panel surface at the wall's mid-width and floor level.
        // inward: unit normal pointing into the room.
        // cutout: a rectangle in wall-local (along-the-wall, height) coordinates to leave empty.
        // Rect.zero means none.
        // `wallHeight` defaults to `RoomHeight`, which is what every room in the game was until the
        // tree hall. It is a PARAMETER rather than a second constant because the height is a property
        // of the room being built, not of the building - see TreeHallHeight for the one room that
        // needs another value and why it cannot simply raise `RoomHeight` for everybody.
        // `panelHoles` opens the FACE only - the backing and the collision behind it stay whole. That
        // is a different thing from `cutout`, which is a hole all the way through and is what a doorway
        // is. Room3-2N's coloured steps need it: each one IS the wall panel at its cell, so the white
        // panel there must not exist - but the wall behind it must, or the room has a hole in it the
        // moment a step slides out.
        private static void BuildPanelWall(Transform parent, string name, Vector3 faceCenterAtBase, Vector3 rightDir, Vector3 inward, float wallWidth, Material backingMat, Material panelMat, Rect cutout, float wallHeight = RoomHeight, float baseHeight = 0f, Rect[] panelHoles = null)
        {
            Vector3 depthAxis = new Vector3(Mathf.Abs(inward.x), Mathf.Abs(inward.y), Mathf.Abs(inward.z));
            Vector3 widthAxis = new Vector3(Mathf.Abs(rightDir.x), Mathf.Abs(rightDir.y), Mathf.Abs(rightDir.z));

            // `baseHeight` lets a wall start ABOVE the floor, which is what a soffit is: the header
            // over the tree hall's mouth is a wall from 5.46m to 17.57m with open air beneath it.
            Rect wallRect = Rect.MinMaxRect(-wallWidth / 2f, baseHeight, wallWidth / 2f, wallHeight);

            // Backing and collision overrun both ends of the wall by its own depth, so adjacent
            // walls interpenetrate at the corners instead of merely abutting.
            //
            // Sized to the interior exactly, two perpendicular walls meet along a line and leave a
            // WallDepth-square column that neither one covers, running the full height of the room.
            // It never opens wide enough for a raycast to find - the void sits behind a zero-width
            // seam - but rendering two slabs that only touch produces pinhole cracks at the corner
            // from grazing angles. Note this is why thickening the walls would make it WORSE: the
            // void is WallDepth squared.
            //
            // The panel grid is left at the interior width, so the visible cell layout is unchanged
            // and the overrun is buried inside the adjacent wall.
            Rect structureRect = Rect.MinMaxRect(
                wallRect.xMin - WallDepth, wallRect.yMin, wallRect.xMax + WallDepth, wallRect.yMax);

            // The backing slab is cut by the same doorway rect as the panels. As one solid slab it
            // left the doorway a dead-end recess: you could walk through (collision was already
            // cut) but you were looking at wall.
            GameObject backing = new GameObject(name);
            backing.transform.SetParent(parent, false);
            int slab = 0;
            foreach (Rect part in SubtractRect(structureRect, cutout))
            {
                if (part.width <= 0.001f || part.height <= 0.001f) continue;

                Prim(PrimitiveType.Cube, $"Backing_{slab++}", backing.transform,
                    faceCenterAtBase + rightDir * part.center.x + Vector3.up * part.center.y
                        - inward * (GrooveDepth + WallThickness / 2f),
                    widthAxis * part.width + Vector3.up * part.height + depthAxis * WallThickness,
                    backingMat, removeCollider: true);
            }

            // Collision is built separately from invisible boxes rather than left on the backing,
            // for two reasons: the slab sits GrooveDepth too far back (the player would walk past
            // the panels and the camera's near plane would clip through the wall), and it has to be
            // cut around the doorway.
            GameObject collision = new GameObject(name + "_Collision");
            collision.transform.SetParent(parent, false);
            int solid = 0;
            foreach (Rect part in SubtractRect(structureRect, cutout))
            {
                if (part.width <= 0.001f || part.height <= 0.001f) continue;

                GameObject block = new GameObject($"Solid_{solid++}");
                block.transform.SetParent(collision.transform, false);
                block.transform.localPosition = faceCenterAtBase
                    + rightDir * part.center.x
                    + Vector3.up * part.center.y
                    - inward * (WallDepth / 2f);

                BoxCollider blockCollider = block.AddComponent<BoxCollider>();
                blockCollider.size = widthAxis * part.width + Vector3.up * part.height + depthAxis * WallDepth;
            }

            GameObject panels = new GameObject(name + "_Panels");
            panels.transform.SetParent(parent, false);

            float groove = GridLineThickness;
            // THE CELL WIDTH IS FITTED TO THE WALL, and this is the third version of it.
            //
            // Rounding the count and laying whole 1.75m cells left bare backing at the far end - a
            // black border down a 17.5m wall. Clamping the last cell instead covered the backing but
            // left a SLIVER panel against the corner, which play called out as a cut-off panel. Both
            // came from insisting the cell be exactly 1.75m on a wall that is not a multiple of it.
            //
            // So the count is rounded and the width divided out: the hall's 30.45m wall gets 17 cells
            // of 1.791m rather than 17 of 1.75 plus a 0.70m gap. Nobody can see 4cm; everybody can
            // see a sliver. **Every wall in cycle 1 is 8.75 or 10.5 across - 5 and 6 exact cells - so
            // this is arithmetically identical everywhere it was already exact.**
            int cols = Mathf.Max(1, Mathf.RoundToInt(wallWidth / GridCellWidth));
            float cellWidth = wallWidth / cols;

            int piece = 0;
            for (int col = 0; col < cols; col++)
            {
                float cellAlong = -wallWidth / 2f + col * cellWidth;
                float cellEnd = cellAlong + cellWidth;

                for (int row = 0; row * GridCellHeight < wallHeight - 0.001f; row++)
                {
                    // THE GRID STAYS ANCHORED TO THE FLOOR even when the wall does not start there,
                    // so a soffit's cells line up with the cells of the wall beneath it rather than
                    // restarting the rhythm at its own base. Rows entirely below `baseHeight` fall
                    // out through the zero-height check.
                    float bottom = Mathf.Max(row * GridCellHeight, baseHeight);
                    // The top row is a partial cell whenever the room height isn't a whole number
                    // of cells, so clamp it rather than letting panels poke through the ceiling.
                    float top = Mathf.Min(row * GridCellHeight + GridCellHeight, wallHeight);
                    if (top - bottom <= 0.001f) continue;

                    Rect panel = Rect.MinMaxRect(
                        cellAlong + groove / 2f, bottom + groove / 2f,
                        cellEnd - groove / 2f, top - groove / 2f);

                    foreach (Rect part in SubtractRects(SubtractRect(panel, cutout), panelHoles))
                    {
                        if (part.width <= 0.02f || part.height <= 0.02f) continue;

                        Vector3 pos = faceCenterAtBase
                            + rightDir * part.center.x
                            + Vector3.up * part.center.y
                            - inward * (GrooveDepth / 2f);
                        // **A CHAMFERED MESH, NOT A CUBE** - see `ChamferedPanelMesh` for what the
                        // rim buys and why it has to be built at true size rather than scaled.
                        // Collisionless either way: the backing slab behind these is what the player
                        // walks into, which is why the cube this replaces was made with
                        // `removeCollider: true`.
                        ChamferedPanel($"Panel_{piece++}", panels.transform, pos,
                                       part.width, part.height, GrooveDepth, inward, panelMat);
                    }
                }
            }
        }

        // The same subtraction applied over and over. Each hole is taken out of every piece the last
        // one left, so a wall face can carry any number of them - which is what a staircase made OF
        // wall panels needs, one hole per step.
        private static System.Collections.Generic.List<Rect> SubtractRects(
            System.Collections.Generic.List<Rect> parts, Rect[] holes)
        {
            if (holes == null || holes.Length == 0) return parts;

            foreach (Rect hole in holes)
            {
                var next = new System.Collections.Generic.List<Rect>();
                foreach (Rect part in parts) next.AddRange(SubtractRect(part, hole));
                parts = next;
            }
            return parts;
        }

        // Returns `panel` minus `hole` as up to four axis-aligned pieces (below, above, left,
        // right of the hole). Lets a doorway of any size be cut out of the panel grid without the
        // door having to line up with cell boundaries.
        private static System.Collections.Generic.List<Rect> SubtractRect(Rect panel, Rect hole)
        {
            var parts = new System.Collections.Generic.List<Rect>();

            bool overlaps = hole.width > 0f && hole.height > 0f && panel.Overlaps(hole);
            if (!overlaps)
            {
                parts.Add(panel);
                return parts;
            }

            float bandMin = Mathf.Max(panel.yMin, hole.yMin);
            float bandMax = Mathf.Min(panel.yMax, hole.yMax);

            if (hole.yMin > panel.yMin)
                parts.Add(Rect.MinMaxRect(panel.xMin, panel.yMin, panel.xMax, hole.yMin));
            if (hole.yMax < panel.yMax)
                parts.Add(Rect.MinMaxRect(panel.xMin, hole.yMax, panel.xMax, panel.yMax));
            if (hole.xMin > panel.xMin)
                parts.Add(Rect.MinMaxRect(panel.xMin, bandMin, hole.xMin, bandMax));
            if (hole.xMax < panel.xMax)
                parts.Add(Rect.MinMaxRect(hole.xMax, bandMin, panel.xMax, bandMax));

            return parts;
        }

        // floorY / zCentre are WORLD, and they have to be, which is the trap this signature exists to
        // close. `PlaceModel` measures the model's RENDERED bounds - which are world-space - and
        // corrects `localPosition` by the difference, so its `targetXZCenter` and `floorY` are world
        // heights whatever the parent is doing. Parenting a bed to a node carrying the room's offset
        // therefore does NOT move it: cycle 2's bed came out standing in Room1, on cycle 1's floor,
        // exactly where a bed asked for y=0 and z=0.7 belongs.
        //
        // The spawn point below is unaffected - it is placed with `localPosition` and inherits the
        // parent normally - which is why the player woke in the right room with no bed in it.
        // yaw turns the whole arrangement about the room centre - the bed, which end of the room it is
        // at, and which way the sleeper faces - rather than spinning the model on the spot. Cycle 2
        // uses 180 so its bed head is at the other end from cycle 1's.
        //
        // Applied as a SIGN on the layout rather than a rotation on the parent, because `PlaceModel`
        // corrects `localPosition` by a world-space delta: under a rotated parent that correction goes
        // in the wrong direction entirely. Anything placed with plain `localPosition` (the spawn point
        // here, the nightstand elsewhere) can use a rotated wrapper quite safely.
        // `xCentre`/`zCentre` are a WORLD position, because `PlaceModel` corrects a world-space
        // delta into a localPosition - so a caller whose room is not on the origin has to say where it
        // is. X was hard-coded to zero until 2026-08-20 and got away with it for two cycles, both of
        // which put their bed room on x=0; cycle 3's is twenty-one metres out, under room2-0, and the
        // bed would have been built in the middle of the tree hall's pit.
        // See the call in `BuildBed`. Kept separate because it is entirely about colliders and
        // reads nothing else the bed knows.
        private static void SplitBedCollider(GameObject bed)
        {
            BoxCollider whole = bed.GetComponent<BoxCollider>();
            if (whole == null)
            {
                Debug.LogWarning("[SceneBuilder] The bed has no box collider to split, so anything "
                               + "thrown onto it will fall through to the floor.");
                return;
            }

            // The bedding, by name. `UseSinglePillow` has already run, so what is measured here is
            // the bed as it will be played.
            Bounds? sheet = null;
            foreach (Renderer r in bed.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                if (r.sharedMaterial == null || !r.sharedMaterial.name.Contains("Sheet")) continue;
                sheet = sheet.HasValue ? Encapsulated(sheet.Value, r.bounds) : r.bounds;
            }

            if (!sheet.HasValue)
            {
                Debug.LogWarning("[SceneBuilder] No `Sheet` renderer on the bed, so its collider is "
                               + "left as one box - objects thrown onto it will rest above the "
                               + "bedding. Check the model's material names.");
                return;
            }

            Bounds full = whole.bounds;
            float top = sheet.Value.max.y;

            // **THE SURFACE IS THE TOP OF THE BEDDING, AND THE BEDDING IS NAMED.**
            //
            // Two rules were tried before this one and both were wrong, in opposite directions:
            //
            // 1. The `Sheet` alone. That is the FLAT sheet, whose top is 0.587 - under a duvet that
            //    heaps to 0.55 and pillows that reach 0.691. An object came to rest inside the
            //    bedding, which play reported as things disappearing into the bed.
            // 2. The highest thing over the MIDDLE of the bed, on the reasoning that a headboard
            //    stands at one end. It does not, as far as the BOUNDS are concerned: this model has
            //    one renderer for the whole frame, `Bed_BedFrame_0`, whose box runs the full length
            //    of the bed and whose top IS the headboard at 0.894. The rule put the surface back
            //    where the unsplit collider had it - the original bug, restored.
            //
            // Position cannot separate them, because the frame's box CONTAINS the headboard without
            // being it. The material can: the four soft things are `Matress`, `Sheet`, `Duvet` and
            // `Pillow*`, and their union is exactly the thing an object is put down on.
            //
            // The `enabled` test is load-bearing - `UseSinglePillow` has already run and DISABLED
            // one of the two pillows, and a hidden pillow must not vote on a surface nobody sees.
            foreach (Renderer r in bed.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled || r.sharedMaterial == null) continue;
                string mat = r.sharedMaterial.name;
                if (!mat.Contains("Matress") && !mat.Contains("Sheet")
                    && !mat.Contains("Duvet") && !mat.Contains("Pillow")) continue;
                top = Mathf.Max(top, r.bounds.max.y);
            }

            // **NEITHER BOX IS THE IMPORTED ONE ANY MORE, AND THAT IS THE FIX.**
            //
            // The mattress box was made by RESIZING the collider `PlaceModel` fitted - writing a new
            // `size.y` on it - and `size` is in the collider's OWN local space. This model is
            // imported at 0.00941 and rotated `Euler(-90, 180 + yaw, 0)`, so its local Y is not the
            // room's up at all; the -90 about X maps it onto a horizontal axis. Writing a height
            // into it therefore shortened the box along the bed's LENGTH, and left the ends of the
            // bed with no collider over them.
            //
            // Play found it exactly there: everything worked except throwing something at the PILLOW
            // end, where the object met no bed at all and fell to the floor under it. CLAUDE.md
            // records this trap twice already - the chess set and the ladder - and the rule it gives
            // is the one used below: measure in world space and build fresh, never write a local
            // number onto an imported transform.
            //
            // So the imported box goes, and two plain boxes replace it on unrotated, unscaled
            // children of the ROOM - not of the bed, whose scale would shrink them to a hundredth of
            // the size asked for. The bed never moves, so nothing is lost by hanging them next to it
            // rather than under it.
            Transform host = bed.transform.parent != null ? bed.transform.parent : bed.transform;
            Object.DestroyImmediate(whole);

            BoxCollider mattress = WorldBox(host, "BedSurface",
                new Vector3(full.center.x, (full.min.y + top) / 2f, full.center.z),
                new Vector3(full.size.x, top - full.min.y, full.size.z));

            // THE HEADBOARD, if anything stands above the bedding. Built in world space and then
            // put back into the bed's frame, because this model is imported at 0.0094 and rotated
            // and its local axes are not the room's - the same trap `BuildIntakeNotice` records.
            if (full.max.y <= top + 0.02f)
            {
                Debug.Log($"[SceneBuilder] The bed's collider stops at the bedding, y={top:0.000}: "
                        + $"surface box {Say(mattress.bounds)}. Nothing stands above it, so there "
                        + "is no headboard box.");
                return;
            }

            // **A STRIP AT THE PILLOW END, NOT A LID OVER THE WHOLE BED.** The frame's box is the
            // full length of the bed, so re-using it here would lay a solid slab across the whole
            // sleeping surface at head height - which is the bug this method exists to fix, moved
            // twenty centimetres up.
            //
            // Which end is the head is MEASURED, off the pillow: it is the one part of this model
            // that is unambiguously at one end of it. `HeadboardDepth` is the only authored number
            // in here, and it is authored because the model gives no way to ask where the board
            // stops and the frame starts - they are one renderer.
            bool alongX = full.size.x >= full.size.z;
            float bedMid = alongX ? full.center.x : full.center.z;
            float headAt = bedMid;
            foreach (Renderer r in bed.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled || r.sharedMaterial == null) continue;
                if (!r.sharedMaterial.name.Contains("Pillow")) continue;
                headAt = alongX ? r.bounds.center.x : r.bounds.center.z;
                break;
            }
            float side = headAt >= bedMid ? 1f : -1f;
            float outer = alongX ? (side > 0f ? full.max.x : full.min.x)
                                 : (side > 0f ? full.max.z : full.min.z);
            float centreAlong = outer - side * HeadboardDepth / 2f;

            BoxCollider board = WorldBox(host, "BedHeadboard",
                new Vector3(alongX ? centreAlong : full.center.x,
                            (top + full.max.y) / 2f,
                            alongX ? full.center.z : centreAlong),
                new Vector3(alongX ? HeadboardDepth : full.size.x,
                            full.max.y - top,
                            alongX ? full.size.z : HeadboardDepth));

            // **THE RESULT, IN WORLD METRES.** Both boxes were wrong in ways the numbers ABOVE could
            // not show - they were right, and what was built from them was not - so what is printed
            // is what the colliders actually came out as.
            Debug.Log($"[SceneBuilder] The bed's collider is split. Bedding at y={top:0.000}; "
                    + $"surface box {Say(mattress.bounds)}; headboard box {Say(board.bounds)}. "
                    + $"The bed's own bounds are {Say(full)}.");
        }

        // A BOX IN WORLD AXES, on a child that has none of the model's rotation or scale. Every
        // collider in `SplitBedCollider` goes through this, because a `BoxCollider`'s `size` and
        // `center` are read in its own local space and the bed's local space is neither upright nor
        // metric.
        private static BoxCollider WorldBox(Transform host, string name, Vector3 centre, Vector3 size)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(host, false);
            go.transform.position = centre;
            go.transform.rotation = Quaternion.identity;
            // AFTER the parenting, and against the parent's own scale, so the box is the size asked
            // for however the host is scaled.
            Vector3 hostScale = host.lossyScale;
            go.transform.localScale = new Vector3(
                Mathf.Approximately(hostScale.x, 0f) ? 1f : 1f / hostScale.x,
                Mathf.Approximately(hostScale.y, 0f) ? 1f : 1f / hostScale.y,
                Mathf.Approximately(hostScale.z, 0f) ? 1f : 1f / hostScale.z);

            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = size;
            return box;
        }

        private static string Say(Bounds b) =>
            $"y {b.min.y:0.000}..{b.max.y:0.000} x {b.min.x:0.00}..{b.max.x:0.00} "
          + $"z {b.min.z:0.00}..{b.max.z:0.00}";

        // How deep the headboard's collider is. See `SplitBedCollider` - the only number in there
        // that is authored rather than measured.
        private const float HeadboardDepth = 0.2f;

        private static Bounds Encapsulated(Bounds a, Bounds b)
        {
            a.Encapsulate(b);
            return a;
        }

        private static (Transform bed, Transform spawn) BuildBed(Transform parent, Material mat,
                                                                 float floorY = 0f, float zCentre = 0f,
                                                                 float yaw = 0f, float xCentre = 0f)
        {
            // Combined bed + bedside table model, which replaces the old separate Kenney bed,
            // drawer, lamp and cup. It is authored Z-up in centimetres, hence the -90 X rotation
            // and the 0.01 scale - without those it arrives 100x too big and lying on its side.
            // Chosen for the rumpled, slept-in bedding: the loop opens every iteration by waking up
            // here, so the bed has to look like someone just got out of it. The alternatives were
            // all neatly made (or, in old_bed's case, a bare frame with no mattress at all).
            // -90 X uprights it (authored Z-up), then 180 Y turns the headboard toward the door.
            // Keep the glTF materials it ships with - they carry metallic/roughness maps that a
            // hand-rolled URP/Lit stand-in would drop.
            // +1 at yaw 0, -1 at yaw 180. Rounded, so it is exactly a sign rather than a float that
            // happens to be near one.
            float facing = Mathf.Round(Mathf.Cos(yaw * Mathf.Deg2Rad));

            (GameObject bed, _) = PlaceModel($"{FurnitureDir}/messy_bed.glb", parent, "Bed",
                new Vector3(xCentre, 0f, zCentre + 0.7f * facing), floorY, 0.00941f, addBoxCollider: true,
                rotation: Quaternion.Euler(-90f, 180f + yaw, 0f));

            // `xCentre`, NOT ZERO. This shifts the pillow in WORLD space (see UseSinglePillow), so a
            // hard zero centres it on the world origin rather than on its own bed - and cycle 3's bed
            // is twenty-one metres out, under room2-0. Play found it as "room3-1's bed has no pillow";
            // it had one, in the middle of the tree hall's pit.
            //
            // The same fault `BuildBed` itself carried until the same day, for the same reason: two
            // cycles' bed rooms both sat on x = 0, so a world X hard-coded to zero was accidentally
            // right twice.
            UseSinglePillow(bed, keepName: "Pillow_2", hideName: "Pillow_1", centreX: xCentre);

            // **AND THE ONE BOX IS SPLIT IN TWO, OR THINGS LAND ON THIN AIR OVER IT.**
            //
            // `PlaceModel(addBoxCollider: true)` fits ONE box to the whole model, which for a bed
            // means a slab from the floor to the top of the HEADBOARD. `FallingItem.ProbeUnder`
            // casts down and takes whatever it hits, so an object thrown onto the bed came to rest
            // on the top of that box - about a third of a metre above the bedding, over the middle
            // of the mattress, held up by nothing. Play reported it as exactly that (2026-09-03).
            //
            // It is not a bug in the fall: the fall found the surface it was given. The surface was
            // wrong, and it was wrong because a bed is not a box - it is a low thing you put things
            // ON with a tall thing at one end.
            //
            // Measured off the model rather than typed. `Sheet` is the bedding, and its top IS the
            // surface a thrown object should find; anything standing above it is the headboard, and
            // that gets a box of its own so the player still cannot walk through it.
            SplitBedCollider(bed);

            // This model bakes contact shadows into its occlusion maps, which only looks right in
            // the exact pose it was authored in. Moving the pillow and removing its neighbour left
            // those shadows painted onto the bedding - a dark smear beside the pillow, and a hard
            // black band across the pillow itself. Switch baked occlusion off for both; the scene's
            // SSAO supplies real contact shadows that follow the geometry instead.
            DisableBakedOcclusion(bed, materialName: "Sheet", assetName: "BedSheet");
            DisableBakedOcclusion(bed, materialName: "Pillow_2", assetName: "BedPillow");

            // Hard against the foot of the bed, facing away from it down the room - you have just
            // got up, so the bed is behind you and the open floor is what you see.
            //
            // This sat at Z=-1.75 facing 180 (away from everything), which put the player 1.42m
            // past the foot of the bed with their back to it - so the wake-up sequence, which
            // poses the eye as if lying down and then sits you up, ended with the bed out of
            // frame entirely and you looking at the blank south wall. It never read as waking up
            // in the bed because you were nowhere near it.
            //
            // The player cannot be spawned *in* the bed: the model carries a full-volume
            // BoxCollider (Y 0..0.89) and the CharacterController would spawn interpenetrating it
            // and get shoved out on the first frame. The bed's foot is at Z=-0.33 and the
            // controller's radius is 0.3, so -0.7 is as close as it can stand without overlapping.
            // Keeping X=0 preserves the deliberate door/bed/spawn centre line.
            GameObject spawn = new GameObject("BedSpawnPoint");
            spawn.transform.SetParent(parent, false);
            spawn.transform.localPosition = new Vector3(0f, 0.05f, -0.7f * facing);
            spawn.transform.localRotation = Quaternion.Euler(0f, 180f + yaw, 0f);

            return (bed.transform, spawn.transform);
        }
    }
}
