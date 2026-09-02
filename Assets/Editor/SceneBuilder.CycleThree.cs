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
    // CYCLE 3, THE ONLY CYCLE WITH A SECOND STOREY: the decks and their mezzanines, the lift, how
    // the facility BREAKS, the room-sized exit, the ladder that is the game's only climbing motion,
    // cycle 4's unfinished cell, and the Bedlam cube that is the escape object.
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {

        // ==================================================== ROOM3-2N'S SECOND STOREY, AND ITS THIRD
        //
        // **A ROOM THREE STOREYS TALL WITH NOTHING IN IT IS NOT BIG, IT IS EMPTY** (2026-08-21, by
        // request: more structure, things coming up out of the floor, and a half-floor like a second
        // storey - the kind that is open in the middle). Height only reads as height when there is
        // something AT the heights. Two partial decks, five columns and seven blocks that come up out
        // of the floor are what turn sixteen metres of air into a place with an up and a down.
        //
        // Everything here is authored off the grid the walls are: a deck's surface is a whole number
        // of ROWS off the floor, a block's footprint is a whole CELL, and a block's travel is a whole
        // number of rows. That is what lets the coloured stairs arrive exactly on a deck without
        // either layout ever being measured against the other.

        // ROOM3-2N'S TWO LIFT SHAFTS, placed off the decks rather than eyeballed.
        //
        // Lift A stands beside deck A's inner edge on the open side; lift B stands inside deck B's
        // void. Both are written here rather than inside the builder because they are facts about
        // THIS ROOM's floor plan, and the builder is about what a lift is.
        private const float DeckALiftPad = 2.0f;
        // Deck A is two cells deep down the west wall; its edge plus half a pad puts the panel's west
        // face on that edge.
        private const float DeckALiftX = -RoomWidth + 2f * GridCellWidth + DeckALiftPad / 2f;
        private const float DeckALiftZ = 2.0f;
        // **HOW HIGH A PANEL SITS ABOVE THE FLOOR IT RESTS ON, and it is not decoration.** A slab
        // whose top face were exactly flush with the deck below it would occupy the same space as that
        // deck's own slab - `DeckSlab` is 0.25 thick - which is the coplanar-faces rule broken in the
        // worst way, two solids sharing a volume. A step clears it, reads as a platform rather than as
        // a seam in the floor, and is well under the 0.72m the controller walks up.
        private const float LiftRestingStep = 0.20f;

        // Deck B's void is 1.75 wide (one cell) and runs z 5.25 to 8.75. 1.6 leaves 7.5cm of daylight
        // on each side - enough that the slab is never scraping the deck it passes through, tight
        // enough that the gap is not a place to fall down.
        private const float DeckBLiftPad = 1.6f;
        private const float DeckBLiftX = -3f * GridCellWidth;
        private const float DeckBVoidMaxZ = 8.75f;

        private const float DeckSlab = 0.25f;
        // How far a deck runs INTO the wall it lands on. Not zero, because a deck that stops exactly
        // on the wall's panel face puts one of its own faces on that plane; not `WallDepth`, because
        // that reaches the backing plane and does the same thing one layer further in. 0.06 is
        // between the two, which is the only place it can be - `docs/gotchas.md`.
        private const float DeckIntoWall = 0.06f;
        private const int DeckARows = 4;
        private const int DeckBRows = 8;

        // A DECK: a partial floor, the rectangles it is made of, and the holes cut in it. A `Rect`'s
        // x and y are x and z in the room's frame throughout this section.
        private struct Mezzanine
        {
            public string name;
            public float surfaceY;
            public Rect[] area;
            public Rect[] voids;
        }

        private static Mezzanine[] PlanMezzanines(float width, float depth)
        {
            float wx = width / 2f + DeckIntoWall;
            float wz = depth / 2f + DeckIntoWall;

            return new[]
            {
                // DECK A, one storey up: an L down the west wall and along the north. **No parapet
                // anywhere on it**, and that is deliberate rather than unfinished - the whole request
                // was for a floor you can see past, and a rail on the inside edge would hide the one
                // thing this room is for, which is looking down at which colour of step is out.
                new Mezzanine
                {
                    name = "DeckA",
                    surfaceY = DeckARows * GridCellHeight,
                    area = new[]
                    {
                        Rect.MinMaxRect(-wx, -7f, -width / 2f + 2f * GridCellWidth, 7f),
                        Rect.MinMaxRect(-wx, 7f, wx, wz),
                    },
                    // **ONE HOLE, FOR THE SECOND LIFT'S COLUMN TO PASS THROUGH.** That lift runs
                    // between the two decks and its glass column is carried to the ground floor, so
                    // it has to cross this one. Cut rather than clipped: a tube intersecting a slab
                    // is two solids sharing a volume, and this deck is already built by subtracting
                    // rectangles, so a hole costs one entry.
                    //
                    // Sized off the column rather than chosen - `DeckBLiftPad * 0.28` is the tube's
                    // own diameter, and a quarter-metre of daylight round it keeps the two surfaces
                    // from grazing. It sits under the panel's rest position, so when the lift is down
                    // the hole is covered by the thing that made it.
                    voids = new[]
                    {
                        Rect.MinMaxRect(
                            DeckBLiftX - DeckBLiftPad * 0.14f - 0.25f,
                            DeckBVoidMaxZ - DeckBLiftPad / 2f - DeckBLiftPad * 0.14f - 0.25f,
                            DeckBLiftX + DeckBLiftPad * 0.14f + 0.25f,
                            DeckBVoidMaxZ - DeckBLiftPad / 2f + DeckBLiftPad * 0.14f + 0.25f),
                    },
                },

                // DECK B, two storeys up, over the north-west quarter, with a square cut clean out of
                // the middle. That hole is the request taken literally: standing on deck B you look
                // through it onto deck A and past deck A's open side to the floor, which is three
                // storeys in one glance and the only place in the building you can get one.
                new Mezzanine
                {
                    name = "DeckB",
                    surfaceY = DeckBRows * GridCellHeight,
                    // **3.62 AND NOT 3.5, WHICH IS MEASURED RATHER THAN CHOSEN.** A deck buried in a
                    // wall has an end cap inside that wall, and at 3.5 deck B's landed 5mm off a west
                    // wall panel's edge while overlapping it - `CycleThreeDiagnostics` found it, the
                    // eye would have found it later as a flickering sliver at the top of the stair.
                    // Nothing here is on the wall grid on purpose: an edge that ends INSIDE a wall
                    // must miss that wall's panel lines, and the only way to be sure is to scan.
                    area = new[] { Rect.MinMaxRect(-wx, 3.62f, -GridCellWidth, wz) },
                    voids = new[]
                    {
                        Rect.MinMaxRect(-3.5f * GridCellWidth, 5.25f,
                                        -2.5f * GridCellWidth, 8.75f),
                    },
                },
            };
        }

        private static void BuildMezzanines(Transform room, float width, float depth,
                                            Material floorMat, Material panelMat)
        {
            GameObject root = new GameObject("Mezzanines");
            root.transform.SetParent(room, false);

            foreach (Mezzanine deck in PlanMezzanines(width, depth))
            {
                var parts = new System.Collections.Generic.List<Rect>(deck.area);
                parts = SubtractRects(parts, deck.voids);

                GameObject deckGO = new GameObject(deck.name);
                deckGO.transform.SetParent(root.transform, false);

                int i = 0;
                foreach (Rect part in parts)
                {
                    if (part.width < 0.02f || part.height < 0.02f) continue;
                    Prim(PrimitiveType.Cube, $"Slab_{i++:00}", deckGO.transform,
                        new Vector3(part.center.x, deck.surfaceY - DeckSlab / 2f, part.center.y),
                        new Vector3(part.width, DeckSlab, part.height), floorMat);
                }
            }

            BuildDeckColumns(root.transform, panelMat);
        }

        // **~~A dark edge beam on every exposed deck edge~~ REMOVED 2026-08-28, by request.** It was
        // built to make a bare slab read as a floor sitting on something, and what it actually did was
        // draw a heavy black outline round both mezzanines - in a white building whose only other dark
        // lines are the wall grooves, that read as a diagram of the decks rather than as structure.
        //
        // **The lifts keep their black rims**, and the difference is worth keeping straight: a rim on
        // a 2m platform is a mark on a thing you stand on, and an outline round a 17m mezzanine is a
        // border round the room. Same material, same idea, opposite result at opposite scales.
        //
        // THE COLUMNS UNDER THEM. A deck hung off two walls needs no support to stand up in a game
        // engine, and does need one to look like it is standing up.
        private static void BuildDeckColumns(Transform parent, Material mat)
        {
            float aTop = DeckARows * GridCellHeight;
            float aUnder = aTop - DeckSlab;
            float bUnder = DeckBRows * GridCellHeight - DeckSlab;

            void Column(string name, float x, float z, float from, float to) =>
                Prim(PrimitiveType.Cube, name, parent,
                    new Vector3(x, (from + to) / 2f, z),
                    new Vector3(0.5f, to - from, 0.5f), mat);

            Column("Column_A0", -5.75f, -6f, 0f, aUnder);
            Column("Column_A1", -5.75f, 6f, 0f, aUnder);
            Column("Column_A2", 7.5f, 7.6f, 0f, aUnder);
            // Ten metres of column straight off the floor, past deck A without touching it, up to
            // deck B. It is the one thing in the room that shows the whole height at once.
            Column("Column_B0", -2.25f, 4.4f, 0f, bUnder);
            Column("Column_B1", -2.25f, 9.8f, aTop, bUnder);
        }

        // ==================================================== WHAT FINISHING THE CUBE COSTS
        //
        // The clock stops, the cube goes into the floor, every structure in cycle 3 leaves, and the
        // facility says what the player has done. `FacilityFailure` owns the sequence; this owns the
        // LIST - which is the only part of it that needs to know what was built in here.
        //
        // **GATHERED, NOT ENUMERATED.** Every room's own children are walked and everything that is
        // not the SHELL is a mover. Naming the decks, the lifts, the beam, the bed and the pads by
        // hand would be a list to keep in step with a cycle that is still being built - and the
        // failure mode of forgetting one is a white room with a lift still standing in it, which is
        // the one thing this sequence must not leave behind.
        //
        // What stays is what makes the room a room: floor, ceiling, walls, the panels on them, the
        // lights, the gates that ARE walls, the reflection probe and the gas emitters the boundary
        // owns. Everything else is furniture.
        private static readonly string[] FailureKeeps =
        {
            "Floor", "Ceiling", "Wall_", "Gate", "ReflectionProbe", "_CeilingLights", "_Gas",
            // **THE CORRIDOR SHUTTER IS PART OF THE SHELL, NOT FURNITURE.** Without this line the
            // teardown gathers it like everything else and drives it out of the room - and it is the
            // one object in here whose entire job is to arrive DURING the teardown. It would have
            // left at the same moment it was asked to close.
            "CorridorShutter",
        };

        // How far a thing travels before it is switched off. Down goes clear under the floor slab;
        // sideways goes past the far wall of a room twice the standard size. Both only have to be
        // "out of sight", and being generous costs nothing - the object is gone at the end of it.
        private const float FailureDropDistance = 9f;
        private const float FailureSlideDistance = 16f;
        // The wave spreads from the cube at roughly a room a second, which is slow enough to watch
        // and fast enough that the room is empty before the announcement finishes.
        private const float FailureWaveSeconds = 2.6f;
        private const float FailureMoveSeconds = 1.9f;
        // Below this above its room's floor, a thing is STANDING and goes down through it. Above it,
        // a thing is hung or held up and goes sideways. Measured off each object rather than listed,
        // so a new prop is classified by what it is rather than by being remembered here.
        private const float FailureStandingClearance = 0.35f;

        // **THE SHUTTER OVER ROOM3-2N'S CORRIDOR MOUTH.**
        //
        // The mouth is a permanent cutout in that room's south wall - `BuildBigRoom` is handed it as
        // `corridorMouth` and there has never been anything to close it, because until the cycle had
        // an ending there was never a moment that wanted it closed.
        //
        // **PARKED BEHIND THE PANELLING DIRECTLY ABOVE THE OPENING**, where the wall is intact (the
        // cutout stops at `GateHeight` and everything above it is ordinary wall). Sitting at the
        // BACKING depth rather than the panel depth is what hides it: the panels stand proud of the
        // backing by `GrooveDepth`, so a slab at the backing plane is behind them and out of sight
        // until it descends past the opening's lintel.
        //
        // It is one slab and not a `BuildPanelWall`, deliberately: a shutter is a shutter and should
        // read as a different object from the wall it drops out of - it is the building sealing
        // itself, not a wall growing back.
        private static Transform BuildMouthShutter(Transform cycleRoot)
        {
            Transform room = null;
            foreach (Transform t in cycleRoot.GetComponentsInChildren<Transform>(true))
                if (t.name == "Room3_2N") { room = t; break; }

            if (room == null)
            {
                Debug.LogWarning("[SceneBuilder] No Room3_2N to hang a corridor shutter in - the "
                               + "mouth will stand open through the teardown.");
                return null;
            }

            const float bigDepth = 2f * RoomDepth;
            const float bigOffsetX = GridCellWidth / 2f;

            // The mouth's own width and its centre along the wall, both taken from the same values
            // `BuildCycleThreeShell` cut the opening with - see `corridorMouth` there.
            Rect gate = GateCutout(RoomWidth);
            float width = gate.width;
            float centreX = -bigOffsetX;

            // **AT THE BACKING PLANE, NOT AT THE FACE.** The first placement put it `WallThickness`
            // INTO the room, which is in front of the panels rather than behind them - a slab parked
            // in mid-air above the doorway for the whole of cycle 3.
            //
            // `BuildPanelWall` sets its backing at `GrooveDepth + WallThickness/2` behind the face,
            // and the panels stand at the face. Anything at the backing's depth is behind them and
            // therefore out of sight, which is the whole trick that lets this be parked in the wall.
            float behindFace = GrooveDepth + WallThickness / 2f;

            // **A PANEL WALL, NOT A GREY SLAB** (2026-09-01, by request: it should not read as a
            // different object from the wall it fills). It was one cube in a flat grey, which is the
            // one thing in this building that nothing else is - every surface here is white panels
            // standing proud of a dark backing, and a plain slab in the middle of that is a patch.
            //
            // Built with the same call the wall itself is, so it arrives with the same chamfered
            // panels, the same backing and the same groove. What tells the player it moved is that it
            // moves, not that it is a different colour.
            GameObject shutter = new GameObject("CorridorShutter");
            shutter.transform.SetParent(room, false);
            shutter.transform.localPosition = new Vector3(
                centreX, GateHeight + GateHeight / 2f, -bigDepth / 2f - behindFace);

            // The wall's own two materials, fetched by name rather than threaded down through four
            // signatures, so these are the same two objects every other wall in the building is
            // built with - which is the whole point of the change.
            //
            // **`FindColorMaterial`, NOT `MakeColorMaterial`.** The second one re-authors what it
            // finds, and this line ran after the walls had been given their gloss: it put the matte
            // default back on `PanelWhite` and took the reflection off every wall in the game. See
            // the note on `FindColorMaterial`.
            BuildPanelWall(shutter.transform, "Panels", new Vector3(0f, -GateHeight / 2f, 0f),
                Vector3.right, Vector3.forward, width,
                FindColorMaterial("GrooveDark", new Color(0.04f, 0.04f, 0.045f)),
                FindColorMaterial("PanelWhite", PanelLitColor),
                Rect.zero, GateHeight);

            Debug.Log($"[SceneBuilder] Corridor shutter: {width:0.##} x {GateHeight:0.##}m parked "
                    + $"above room3-2N's mouth, dropping {GateHeight:0.##}m at the break.");
            return shutter.transform;
        }

        private static FacilityFailure BuildFacilityFailure(Transform cycleRoot, BedlamCube cube,
                                                            WallPanelDisplay panels)
        {
            if (cube == null) return null;

            GameObject go = new GameObject("FacilityFailure");
            go.transform.SetParent(cycleRoot, false);

            FacilityFailure failure = go.AddComponent<FacilityFailure>();
            // The blocks and nothing else. The mat and the rope round it are the STAND, and they
            // leave with the rest of the furniture - the cube goes first and on its own because it
            // is the only thing in this room the player made.
            failure.cube = cube.transform.Find("Cube");
            failure.wallPanels = panels;
            // 2D, at 0 spatial blend: an evacuation siren comes out of the whole building, and one
            // that got quieter as the player walked away from a speaker would be a speaker.
            failure.sirenSource = MakeSource(go.transform, "Siren", 0f, 0.55f, loop: true);
            failure.sirenClip = LoadClip(SfxDir, "sfx_alarm_siren");
            failure.wayDownSigns = wayDownHint;
            // The pictogram that explains the ceiling hole, taken down with the decks it describes.
            failure.ladderSign = ladderSignHint;
            // **THE WAY THE PLAYER CAME IN.** Found rather than passed down: it is the one
            // `CrushingBarrier` in this cycle, and threading it through four call signatures to say
            // so would be four more places to keep in step. See `FacilityFailure.wayIn`.
            failure.wayIn = cycleRoot.GetComponentInChildren<CrushingBarrier>(true);
            if (failure.wayIn == null)
                Debug.LogWarning("[SceneBuilder] Cycle 3 has no CrushingBarrier - the way into "
                               + "room3-2N will stand open through the teardown.");

            // AND THE MOUTH OF THE CORRIDOR, WHICH IS THE HOLE THE PLAYER SEES. See
            // `FacilityFailure.mouthShutter`: the barrier above is twenty-two metres away at the far
            // end, and closing it left this end standing open.
            failure.mouthShutter = BuildMouthShutter(cycleRoot);
            failure.mouthShutterDrop = GateHeight;

            // **ROOM3-0 GOES RED** (2026-08-31, by request). Its four white ceiling spots and their
            // emissive faces are handed over to be put out, and one red light is built to replace
            // them - see the block of fields on `FacilityFailure` for why the faces have to go too
            // and why they are driven through a property block rather than the material.
            //
            // GATHERED BY WALKING, not returned from `BuildBreakRoomThree`. The room is built through
            // the general `BuildEmptyRoom`, which hands back a Transform and nothing else, and
            // widening its return for one caller would be a signature change felt by every room in
            // the game. The names come from `BuildCeilingLights`, which is one function.
            Transform roomZero = null;
            foreach (Transform t in cycleRoot.GetComponentsInChildren<Transform>(true))
                if (t.name == "Room3_0") { roomZero = t; break; }

            if (roomZero == null)
            {
                Debug.LogError("[SceneBuilder] No Room3_0 under cycle 3 - the break cannot put its "
                             + "lights out, and the room will end the game lit white.");
            }
            else
            {
                Transform fixtures = roomZero.Find("Room3_0_CeilingLights");
                if (fixtures == null)
                {
                    Debug.LogError("[SceneBuilder] Room3_0 has no 'Room3_0_CeilingLights' - check what "
                                 + "BuildCeilingLights named it. The break will leave the room lit.");
                }
                else
                {
                    failure.roomLights = fixtures.GetComponentsInChildren<Light>(true);
                    failure.roomFixtures = fixtures.GetComponentsInChildren<Renderer>(true);
                }

                // THE ONE LIGHT LEFT. In the middle of the room and low - a red source ABOVE the
                // player lights the floor and leaves the walls, which is the arrangement that was
                // just taken away; at head height it washes the panels and the arrow, which are the
                // only two things in here that are meant to be seen.
                GameObject alarmGO = new GameObject("AlarmLight");
                alarmGO.transform.SetParent(roomZero, false);
                alarmGO.transform.localPosition = new Vector3(0f, 2.4f, 0f);
                Light alarm = alarmGO.AddComponent<Light>();
                alarm.type = LightType.Point;
                alarm.color = new Color(0.85f, 0.06f, 0.06f);
                alarm.range = 16f;
                alarm.intensity = 0f;
                alarm.shadows = LightShadows.None;
                // Realtime and OFF at build. It is a light that exists for ninety seconds at the very
                // end of the game; baking it would put a red room into the lightmap of a white one.
                alarm.lightmapBakeType = LightmapBakeType.Realtime;
                alarm.enabled = false;
                failure.alarmLight = alarm;
            }
            // `cameraShaker` and `narration` live in the core scene and are bound at runtime by
            // `CycleBinding` - see the note there.

            var movers = new System.Collections.Generic.List<FacilityFailure.Mover>();
            Vector3 from = cube.aim != null ? cube.aim.position : cube.transform.position;
            float furthest = 0.001f;

            foreach (Transform room in cycleRoot.GetComponentsInChildren<Transform>(true))
            {
                // A ROOM, NOT ANYTHING WHOSE NAME BEGINS THAT WAY. `Room3_1_CeilingLights` does,
                // and the first version of this walked into it and took all four fixtures out of
                // every room in the cycle - which leaves a white room the player cannot see. The
                // keep list is the same one the children are filtered against, applied one level up:
                // a group that is part of the shell has no contents that are furniture.
                if (!room.name.StartsWith("Room3_") || room.name.EndsWith("_Root")) continue;
                if (IsFailureKeep(room.name)) continue;
                // **NOT THE ROOM THE PLAYER IS STANDING IN.** room3-0 is where the break happens and
                // its floor is the way down to cycle 4; taking it apart would be taking apart the
                // thing being escaped through. What comes apart is the cycle BEHIND them, seen from
                // room3-0's mouth looking back over deck B and down sixteen metres - which is the
                // only place in the building that view exists.
                if (room.name == "Room3_0") continue;

                foreach (Transform child in room)
                {
                    if (IsFailureKeep(child.name)) continue;
                    AddFailureMovers(child, room.position, from, movers, ref furthest);
                }
            }

            // THE WAVE, normalised after the fact. Distance is collected first and turned into
            // seconds here, because "how long until this one goes" is a fraction of the room and the
            // room's size is not known until every mover has been found.
            for (int i = 0; i < movers.Count; i++)
            {
                FacilityFailure.Mover m = movers[i];
                m.delay = m.delay / furthest * FailureWaveSeconds;
                movers[i] = m;
            }

            failure.movers = movers.ToArray();

            int down = 0;
            foreach (FacilityFailure.Mover m in movers) if (m.travel.y < -0.5f) down++;
            Debug.Log($"[SceneBuilder] Facility failure: {movers.Count} structures leave cycle 3 "
                    + $"({down} through the floor, {movers.Count - down} sideways), over "
                    + $"{FailureWaveSeconds + FailureMoveSeconds:0.#}s from the cube outward.");
            return failure;
        }

        // THE ONE GROUP THAT IS NOT ONE STRUCTURE.
        //
        // `Mezzanines` is both decks AND the five columns under them. Taken whole it measures from
        // the floor to ten metres up, so it classifies as STANDING and is driven down through the
        // ground floor the player is watching from. Split, each piece gets the answer it deserves:
        // the columns reach the floor and are swallowed by it, the decks do not and slide out.
        //
        // **A LIST OF ONE, AND IT IS A LIST BECAUSE THE DERIVED VERSION WAS WRONG.** The first
        // attempt split anything taller than a storey and a half, on the reasoning that nothing that
        // tall is a single structure. It caught the mezzanines and three things it should not have:
        // the beam (its own segments reach the ceiling call sixteen metres up), the upper lift (its
        // glass column runs the whole height of the room) and the cube's own stand - whose bounds at
        // BUILD time include twelve blocks scattered over three storeys, a state that by definition
        // cannot exist when this sequence runs. A rule measured against a scene that is saved
        // unsolved cannot describe a room that has just been solved.
        private static readonly string[] FailureSplits = { "Mezzanines" };

        private static void AddFailureMovers(Transform what, Vector3 roomAt, Vector3 from,
                                             System.Collections.Generic.List<FacilityFailure.Mover> movers,
                                             ref float furthest)
        {
            Bounds b = MeasuredBounds(what.gameObject);
            if (b.size == Vector3.zero) return;

            if (System.Array.IndexOf(FailureSplits, what.name) >= 0 && what.childCount > 0)
            {
                foreach (Transform part in what) AddFailureMovers(part, roomAt, from, movers, ref furthest);
                return;
            }

            // STANDING OR HELD UP. A bed, a plinth and a lift's own shaft all reach the floor and
            // are swallowed by it; a deck, a receiver on a wall and the beam's muzzle do not, and a
            // thing with nothing under it cannot sink through anything.
            bool standing = b.min.y - roomAt.y < FailureStandingClearance;
            Vector3 outward = what.position - roomAt;
            outward.y = 0f;
            // A thing exactly on the room's axis has no side to leave by. West, arbitrarily and
            // consistently - it only has to be A direction.
            if (outward.sqrMagnitude < 0.01f) outward = Vector3.left;

            movers.Add(new FacilityFailure.Mover
            {
                what = what,
                travel = standing
                    ? Vector3.down * FailureDropDistance
                    : outward.normalized * FailureSlideDistance,
                // Distance for now; turned into seconds by the caller, once the furthest thing in
                // the cycle is known.
                delay = Vector3.Distance(what.position, from),
                duration = FailureMoveSeconds,
            });
            furthest = Mathf.Max(furthest, movers[movers.Count - 1].delay);
        }

        private static bool IsFailureKeep(string name)
        {
            foreach (string keep in FailureKeeps)
                if (name.StartsWith(keep) || name.EndsWith(keep)) return true;
            return false;
        }

        // **A CYCLE BOUNDARY THE SIZE OF A ROOM.** `BuildCycleExit` opens one grid cell in a floor;
        // this opens room4-1's whole ceiling, which is what "the way down is in the middle of the
        // room" turns into once the room below is the thing being opened onto.
        //
        // TWO LEAVES THAT PART, not one slab that slides. A single 8.75m lid has nowhere to go: it
        // would have to travel its own width, and room3-2N has exactly 4.375m of floor either side of
        // the hole to hide in. Halved, each leaf travels its own half-width and parks exactly under
        // the floor beside it - the same trick the panel gates use, at ten times the size.
        //
        // ONE LID, NOT TWO. `BuildCycleExit` carries a ceiling lid as well because it opens onto a
        // room whose own ceiling is otherwise intact; here room4-1 has NO ceiling of its own - the
        // underside of this lid IS its ceiling, one service void above its walls. So there is one
        // slab, and what the player falls through is the void the skirt encloses.
        private static CycleExit BuildRoomSizedExit(Transform parent, Material floorMat, Transform player)
        {
            GameObject go = new GameObject("CycleExit");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, CycleExitZ);

            float halfW = RoomWidth / 2f;

            GameObject west = Prim(PrimitiveType.Cube, "Cover_Floor_West", go.transform,
                new Vector3(-halfW / 2f, -WallThickness / 2f, 0f),
                new Vector3(halfW, WallThickness, RoomDepth), floorMat);
            GameObject east = Prim(PrimitiveType.Cube, "Cover_Floor_East", go.transform,
                new Vector3(halfW / 2f, -WallThickness / 2f, 0f),
                new Vector3(halfW, WallThickness, RoomDepth), floorMat);

            CycleExit exit = go.AddComponent<CycleExit>();
            exit.covers = new[] { west.transform, east.transform };
            // Its own half-width each way, so each leaf ends exactly under the floor beside the hole.
            // No drop with it: these retract INTO the slab they are flush with, which is solid, so
            // there is nothing to be coplanar with once they are inside it.
            exit.openOffsets = new[] { new Vector3(-halfW, 0f, 0f), new Vector3(halfW, 0f, 0f) };
            // Slower than a hatch, because it is ten times the hatch. The seal is quicker than the
            // open for the reason every door in this game is: closing behind you is not a thing to
            // watch.
            exit.openDuration = 3.4f;
            exit.sealDuration = 1.6f;
            exit.player = player;
            exit.audioSource = MakeSource(go.transform, "ExitAudio", 1f, 0.9f);
            exit.openClip = LoadClip(SfxDir, "sfx_door_open");
            exit.sealClip = LoadClip(SfxDir, "sfx_door_open");
            return exit;
        }

        // The void between room3-2N's floor and room4-1's walls, walled in. Without it the gap is
        // open at the sides and the hole looks out into nothing on the way down.
        private static void BuildRoomSizedSkirt(Transform parent, Material mat)
        {
            GameObject skirt = new GameObject("ExitSkirt_Cycle3");
            skirt.transform.SetParent(parent, false);
            skirt.transform.localPosition = new Vector3(0f, -WallThickness, CycleExitZ);

            float halfW = RoomWidth / 2f, halfD = RoomDepth / 2f;
            float outer = RoomDepth + 2f * WallThickness;

            Prim(PrimitiveType.Cube, "Skirt_West", skirt.transform,
                new Vector3(-halfW - WallThickness / 2f, -ServiceVoid / 2f, 0f),
                new Vector3(WallThickness, ServiceVoid, outer), mat);
            Prim(PrimitiveType.Cube, "Skirt_East", skirt.transform,
                new Vector3(halfW + WallThickness / 2f, -ServiceVoid / 2f, 0f),
                new Vector3(WallThickness, ServiceVoid, outer), mat);
            Prim(PrimitiveType.Cube, "Skirt_South", skirt.transform,
                new Vector3(0f, -ServiceVoid / 2f, -halfD - WallThickness / 2f),
                new Vector3(RoomWidth, ServiceVoid, WallThickness), mat);
            Prim(PrimitiveType.Cube, "Skirt_North", skirt.transform,
                new Vector3(0f, -ServiceVoid / 2f, halfD + WallThickness / 2f),
                new Vector3(RoomWidth, ServiceVoid, WallThickness), mat);
        }

        // The bed a player falls into has to be under the hole they fell through, and the hole is
        // room3-2N's own middle now rather than a cell somebody placed.
        private static void AssertUnderCycleFourHole(Transform cycleFourRoot)
        {
            if (cycleFourRoot == null) return;

            float wantX = CycleThreeX + GridCellWidth / 2f;
            float wantZ = CycleThreeZ + NorthRoomZ;
            Vector3 at = cycleFourRoot.position;

            if (Mathf.Abs(at.x - wantX) < 0.05f && Mathf.Abs(at.z - wantZ) < 0.05f) return;

            Debug.LogError($"[SceneBuilder] Cycle 4 is not under room3-2N's opening: it is at "
                         + $"({at.x:0.##}, {at.z:0.##}) and the hole is at ({wantX:0.##}, {wantZ:0.##}). "
                         + "A player dropping through would land outside the room.");
        }

        // ==================================================== THE LADDER
        //
        // **THE WAY OUT OF ROOM3-2N IS A THING YOU CARRY IN.** There is a hole in the ceiling above
        // deck B and room3-0 on the other side of it, and no way up: the room ships with the top of
        // its own climb unreachable. The ladder is in room3-2S, two rooms away, and standing it in
        // the shaft with a left click is what turns the hole into a route.
        //
        // WHY ROOM3-2S. That room already has one job - it is where the riser pane is fetched from -
        // and this is the same job: somewhere you FETCH from rather than somewhere you look. It also
        // keeps the four-rooms-four-jobs split the cycle is built on.
        private const string LadderModel = ToolsDir + "/ladder.glb";
        private const string LadderItemId = "Ladder";
        // **DERIVED FROM THE CLIMB, not written.** Deck B to room3-0's floor, plus enough to stand
        // proud of the opening at the top - which is what a ladder does and what tells the player
        // from below that there is something up there. Shorten the gap above and the ladder follows;
        // the two cannot disagree.
        // **THE LADDER LEANS** (2026-08-31, by request: "make it look like it is leaning against the
        // wall"). Every number below follows from that one sentence and from where the wall is; none
        // of them is chosen.
        //
        // Vertical, the ladder stood in the middle of a 1.75m shaft and 35cm of it poked out of
        // room3-0's floor - a stub, with nothing to take hold of and nothing to rest on. To lean it
        // needs two things the old arrangement did not have: **a wall within reach of its head**,
        // which is why the shaft moved (`LadderShaftXZ`), and **length**, because a leaning ladder
        // covers the same rise over a longer run.
        //
        //          room3-0        |<- west wall
        //                         |__
        //                         |  \   head, LadderHeadRise above the floor
        //          ---------------+---\-------  room3-0's floor / the shaft mouth
        //                         |    \
        //          room3-2N       |     \  LadderLeanDegrees off vertical
        //                         |      \
        //          ===============+=======*==  deck B, the foot on its mark
        //
        // How much height there is to gain: deck B to room3-0's floor.
        private const float LadderClimbRise = RoomZeroY - DeckBSurfaceY;      // 5.96
        // How far the head stands proud of the floor once it is up there. A grab rail - the height a
        // hand wants when it is stepping off the top of something, and what turns a hole in a floor
        // into somewhere you can obviously climb out of.
        private const float LadderHeadRise = 1.20f;
        // OFF VERTICAL. **TWENTY, RAISED FROM TWELVE ON 2026-08-31 BECAUSE TWELVE READ AS
        // VERTICAL.** The lean was applied correctly at twelve - the scene had it, the wiring had it
        // - and play still reported a ladder standing straight up. The reason is where the player is
        // when they look at it: on deck B, at the FOOT, looking up the ladder's own axis. Foreshortened
        // along its length, a shallow lean is invisible; 1.27m of run over six metres reads as nothing
        // from the one place everybody sees it from.
        //
        // **A NUMBER THAT IS RIGHT IN PLAN CAN STILL BE WRONG FROM THE ONLY ANGLE ANYONE SEES IT.**
        // The geometry was checked in section, which is where a lean is most obvious and where no
        // player ever stands.
        //
        // Twenty is 70 degrees off horizontal, which is what a leaning ladder is actually set at, and
        // it costs 0.30m of length. It also RELAXES the tight constraint rather than tightening it:
        // the head is fixed against the wall, so a steeper lean carries the whole climb further from
        // that wall. The player's capsule clears the shaft mouth by 29cm at twenty, against 11cm at
        // twelve - so if this is ever raised again, the mouth is not what stops it.
        private const float LadderLeanDegrees = 20f;
        // How far the head sits off the wall FACE - half the ladder's own thickness, so it touches
        // rather than intersects. `BuildPanelWall` is given the finished panel surface, so a room's
        // half-width IS its visible wall (see that method's `faceCenterAtBase`).
        private const float LadderHeadClearance = 0.13f;

        // And the rest is trigonometry. `static readonly` rather than `const` because `Mathf` cannot
        // run in a constant initialiser - the values are still fixed at load and still written in
        // exactly one place.
        private static readonly float LadderRun =
            (LadderClimbRise + LadderHeadRise) * Mathf.Tan(LadderLeanDegrees * Mathf.Deg2Rad);
        private static readonly float LadderLength =
            Mathf.Sqrt(LadderRun * LadderRun
                     + (LadderClimbRise + LadderHeadRise) * (LadderClimbRise + LadderHeadRise));
        // UP THE LADDER, as a unit vector in the room's own frame. The lean is westward, so x is
        // negative; z is untouched, which keeps the climb in one plane and the shaft square.
        private static readonly Vector3 LadderClimbDir =
            new Vector3(-Mathf.Sin(LadderLeanDegrees * Mathf.Deg2Rad),
                         Mathf.Cos(LadderLeanDegrees * Mathf.Deg2Rad), 0f);
        // WHERE THE FOOT STANDS, which is the head's x plus the whole run. Room3-2N's west wall face
        // is at -RoomWidth (the room is two `RoomWidth` across), and room3-0 shares that same line -
        // which is what makes one number serve both rooms.
        private static readonly float LadderFootX =
            -RoomWidth + LadderHeadClearance + LadderRun;
        // Where it lies in room3-2S before anybody moves it, and how it is held.
        private static readonly Vector3 LadderRestXZ = new Vector3(2.6f, 0f, -2.2f);

        private static CarryableItem BuildLadder(Transform room, Material propMat)
        {
            (GameObject go, Bounds box) = PlaceModelLocal(LadderModel, room, "Ladder",
                new Vector3(LadderRestXZ.x, 0f, LadderRestXZ.z), Quaternion.identity, LadderLength);
            if (go == null) return null;

            // LYING DOWN, because it is not standing in anything yet. Which axis is its length is
            // measured rather than assumed - the model comes in with its own idea of up, and this
            // project has been caught by that once already (see `BedlamRopePitch`).
            Debug.Log($"[SceneBuilder] Ladder as imported: {box.size.x:0.##} x {box.size.y:0.##} x "
                    + $"{box.size.z:0.##}m. Its longest side is the one the mount stands upright.");

            // Sat on the floor. `PlaceModelLocal` centres what it places, which buries half of
            // anything that is meant to rest on something.
            go.transform.position += Vector3.up * (room.position.y - box.min.y);
            box = MeasuredBounds(go);

            // **THE PIVOT MOVES TO THE MIDDLE OF THE LADDER** (2026-08-30, by request: "hold it by
            // the middle instead"), and it is the one change that retires a whole family of bugs
            // rather than another of them.
            //
            // This model's origin is at one END, and five separate faults came out of that in three
            // days - it could not be picked up, then only in the middle, then not by ghosts, then it
            // landed facing north, then it sank under the bed (`docs/gotchas.md`). Every one was some
            // system asking "where is this object" and getting an answer 3m from what the player was
            // looking at. Centred, they all get the obvious answer.
            //
            // Done by moving the ROOT and putting the children back, which leaves the mesh exactly
            // where it is in the world and changes only what the transform's origin means. The same
            // move `MakeChessPiece` makes for the mirrored half of the chess set, for the same
            // reason: the composite is untouched and what reads off the transform is fixed.
            {
                Vector3 delta = box.center - go.transform.position;
                var kids = new System.Collections.Generic.List<Transform>();
                foreach (Transform kid in go.transform) kids.Add(kid);
                go.transform.position = box.center;
                foreach (Transform kid in kids) kid.position -= delta;
            }

            BoxCollider trigger = go.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = go.transform.InverseTransformPoint(box.center);
            trigger.size = Abs(go.transform.InverseTransformVector(box.size + Vector3.one * 0.8f));

            // THE PROMPT ANCHOR. It starts at the middle - which is now the pivot as well - and
            // `LongItemAnchor` slides it from there to wherever along the length the player is
            // looking, so E works anywhere on the ladder rather than at one point on it.
            GameObject anchor = new GameObject("HintAnchor");
            anchor.transform.SetParent(go.transform, false);
            anchor.transform.position = box.center;

            // THE SPAN, MEASURED: end to end along the model's own long axis, which the import log
            // above prints. Centred on the pivot now, which is what `CarryableItem.heldSpan` means.
            Vector3 span = go.transform.InverseTransformVector(
                Vector3.forward * (box.size.z > box.size.x ? box.size.z : box.size.x));

            LongItemAnchor slider = go.AddComponent<LongItemAnchor>();
            slider.anchor = anchor.transform;
            // `item` is assigned after it exists, below.

            CarryableItem item = go.AddComponent<CarryableItem>();
            item.itemId = LadderItemId;
            item.displayName = "LADDER";
            item.icon = LadderIcon();
            item.hintAnchor = anchor.transform;
            // **IT LIES WHERE IT WAS PUT DOWN.** Without this `LieDown` restores the pose it was
            // BUILT in, which for this model is lying along +Z - so a ladder dropped facing anywhere
            // snapped round to north. See `CarryableItem.keepsDropYaw`.
            item.keepsDropYaw = true;
            // The same line, read by the other thing that needs it: `HeldItemClearance` sweeps it so
            // the whole ladder meets a wall rather than only the end in the hand.
            item.heldSpan = span;
            // Half its own height, which is what `floorY` means for an object whose pivot IS its
            // centre - and this one's is, since the re-centring above.
            item.floorY = box.size.y / 2f;
            // **IT POSES ITSELF, LEVEL, OFF THE BODY** - `LevelCarry`, which is `Mirror`'s trick made
            // general. The ordinary hold hangs off the CAMERA, so the ladder inherited the player's
            // pitch: look down and eight metres of it drove into the floor, look up and into the
            // ceiling, and both read as the ladder disappearing because what was left on screen was
            // the inside of a slab. Reported from play as exactly that.
            //
            // Level and yawed off the body, it cannot do either. Front to back along the player's own
            // axis (by request), four degrees off it so a long thin thing is not a single line
            // vanishing at the crosshair.
            //
            // `handLocalPosition` is left as the fallback for anything that ever poses this WITHOUT
            // the component - a ghost's belt layout reads it - and `posesItself` is what tells
            // `HeldItemClearance` not to drag an object that is placing itself.
            item.posesItself = true;
            item.handLocalPosition = new Vector3(0.42f, -0.58f, 0.55f);
            item.handLocalEuler = new Vector3(0f, 4f, 4f);

            LevelCarry carry = go.AddComponent<LevelCarry>();
            carry.item = item;
            // Chest height and just off the shoulder, which is where a person's hands are on a ladder
            // they are walking with.
            carry.carryHeight = 1.15f;
            carry.carryAhead = 0.50f;
            carry.carrySide = 0.34f;
            carry.yaw = 4f;
            item.handLocalScale = go.transform.lossyScale;
            item.audioSource = MakeSource(go.transform, "PickupAudio", 1f, 0.85f);
            item.pickupClip = LoadClip(SfxDir, "sfx_item_pickup");
            slider.item = item;
            return item;
        }

        // THE SPOT ON DECK B, and everything the climb needs to know. Built as a child of room3-2N so
        // its two heights can be stated in that room's frame and turned into world ones here.
        private static LadderMount BuildLadderMount(Transform room, Material propMat,
                                                   Transform dismounts)
        {
            GameObject go = new GameObject("LadderMount");
            go.transform.SetParent(room, false);
            // **AT THE FOOT, WHICH IS NO LONGER UNDER THE HOLE.** A leaning ladder stands out from
            // what it leans on: the mark is `LadderRun` east of the shaft's centre, and the head is
            // what sits at the mouth. See `LadderLeanDegrees` for the whole arrangement.
            go.transform.localPosition = new Vector3(LadderFootX, DeckBSurfaceY, LadderShaftXZ.y);

            // A MARK ON THE DECK, so the spot exists before the ladder does. Near-black among white
            // surfaces reads as a recess, which is the same trick every plinth top in the game plays.
            Prim(PrimitiveType.Cube, "Mark", go.transform, new Vector3(0f, 0.008f, 0f),
                new Vector3(GridCellWidth * 0.8f, 0.014f, GridCellWidth * 0.8f),
                MakeColorMaterial("LadderMark", new Color(0.14f, 0.14f, 0.16f)), removeCollider: true);

            // Where an accepted ladder stands: at the mark, upright, its foot on the deck. The seat's
            // own frame is what the ladder arrives in, so the rotation lives here and the ladder
            // itself needs to know nothing about which way up it was carried.
            GameObject seat = new GameObject("Seat");
            seat.transform.SetParent(go.transform, false);
            // HALF THE LADDER UP THE SLANT. Its pivot is its own centre (`BuildLadder` re-centres
            // it), so the seat is the midpoint of the line from the foot to the head.
            seat.transform.localPosition = LadderClimbDir * (LadderLength / 2f);
            // THREE TURNS, AND THE MIDDLE ONE IS THE ONE THAT WAS MISSING. Read right to left.
            //
            // 1. `Euler(-90, 0, 0)` stands the model up: its long axis is local +Z, and this puts
            //    that on world +Y. Measured rather than written - `BuildLadder` logs the model's
            //    extents so a re-export that changes them is visible.
            //
            // 2. `Euler(0, 90, 0)` **rolls it a quarter turn about its own length**, and without it
            //    the lean is in the wrong plane. The model is 1.18m wide and 0.24m thick, with the
            //    width on local X - so after step 1 the two stiles are separated along world X, and
            //    the lean below (about world Z) tilts the length and the width TOGETHER. The ladder
            //    ends up tipping SIDEWAYS, like one falling over, rather than leaning back against
            //    the wall. Play reported it as "the ladder is standing vertical", which is what a
            //    sideways lean looks like from the one place anybody sees it: at the foot, on deck B,
            //    looking straight up the face.
            //
            //    **The lean angle was innocent.** It was applied, it was in the scene, and it was in
            //    the wrong plane - so raising it only tipped the ladder over further. A rotation is
            //    two facts, an axis and an amount, and only one of them was ever checked.
            //
            // 3. `AngleAxis(LadderLeanDegrees, +Z)` leans it west, along `LadderClimbDir`, so the
            //    ladder is drawn on the line the climb actually runs on. With the roll in place this
            //    axis is now across the ladder's width, which is what leaning back MEANS.
            seat.transform.localRotation = Quaternion.AngleAxis(LadderLeanDegrees, Vector3.forward)
                                         * Quaternion.Euler(0f, 90f, 0f)
                                         * Quaternion.Euler(-90f, 0f, 0f);

            // THE PROMPT, AT CHEST HEIGHT IN THE SHAFT rather than on the mark. See
            // `LadderMount.hintAnchor`: a point on the deck is a point anything standing on the deck
            // occludes, and an occluded anchor is a left click that does nothing.
            GameObject hint = new GameObject("HintAnchor");
            hint.transform.SetParent(go.transform, false);
            // Up the ladder's own line rather than straight up, so the disc hangs where the ladder
            // will be rather than beside it.
            hint.transform.localPosition = LadderClimbDir * 1.2f;

            LadderMount mount = go.AddComponent<LadderMount>();
            mount.acceptedItemId = LadderItemId;
            mount.seat = seat.transform;
            mount.hintAnchor = hint.transform;
            mount.bottomY = room.position.y + DeckBSurfaceY;
            mount.topY = room.position.y + RoomZeroY;
            // WHICH WAY UP THE LADDER GOES, in world space - the volume follows it and so does the
            // climb. `TransformDirection` rather than the local vector straight across, so a room
            // that is ever yawed does not silently send the climb sideways.
            mount.climbDirection = room.TransformDirection(LadderClimbDir).normalized;
            // EVERY LIP room3-0 AUTHORED, in order. `LadderMount` picks the nearest at the moment
            // somebody steps off - see the note on `Dismounts` in `BuildBreakRoomThree`.
            if (dismounts != null)
            {
                var spots = new System.Collections.Generic.List<Transform>();
                foreach (Transform spot in dismounts) spots.Add(spot);
                mount.dismounts = spots.ToArray();
                if (spots.Count == 0)
                    Debug.LogError("[SceneBuilder] the ladder mount has no step-off points. A climb "
                                 + "would end with the player standing over the hole.");
            }
            mount.audioSource = MakeSource(go.transform, "MountAudio", 1f, 0.9f);
            mount.installClip = LoadClip(SfxDir, "sfx_door_open");

            Debug.Log($"[SceneBuilder] Ladder mount on deck B at {go.transform.position}, climb "
                    + $"{mount.bottomY:0.##} to {mount.topY:0.##} ({mount.topY - mount.bottomY:0.##}m), "
                    + $"ladder {LadderLength:0.##}m leaning {LadderLeanDegrees:0.#}deg over a run of "
                    + $"{LadderRun:0.##}m. Head {LadderHeadRise:0.##}m above room3-0's floor, "
                    + $"{LadderHeadClearance:0.##}m off the west wall. Shaft centre "
                    + $"{LadderShaftXZ.x:0.##}, axis at the mouth "
                    + $"{LadderFootX - LadderClimbRise * LadderRun / (LadderClimbRise + LadderHeadRise):0.##}.");
            return mount;
        }

        // THE SIGN THAT SAYS WHAT IS MISSING: a pictogram of a ladder on the wall beside a spot with
        // no ladder in it. That pairing is the whole sentence, and it is the only thing in the room
        // that explains the hole in the ceiling.
        //
        // Built off `MakeWallFace` like cycle 2's pedestal signs, and set to full alpha for the same
        // reason theirs are: those are faded in and retired by `PanelMessage`, and this is true for
        // as long as the room is unsolved.
        private static void BuildLadderSign(Transform room, Vector3 localPos, Quaternion facing)
        {
            CanvasGroup face = MakeWallFace(room, "LadderSign", localPos, facing,
                f => MakeWallIcon(f, "Ladder", LadderIcon(), Vector2.zero, 1280f,
                                  new Color(0.20f, 0.20f, 0.23f, 0.88f)),
                worldWidth: 0.85f, withPlate: false, authoredHeight: 1600f);

            if (face != null) face.alpha = 1f;
            // Kept so the teardown can take it down - see `FacilityFailure.ladderSign`.
            ladderSignHint = face;
        }

        // HOW WIDE THE CUBE'S RECESS IS, and it is the RIM: `BuildFinalSlot` cuts the hole at 0.80
        // of it. The held cube is 0.40m across (`BedlamHeldScale` of the 1.6m assembly), so the hole
        // wants to be a little over that - 0.42m here, which is 12mm of daylight on each side. The
        // 0.34 this inherited from cycle 1's console gave a 0.27m hole for a 0.40m object.
        private const float CycleThreeSlotSize = 0.53f;

        // How deep the console's recess is cut. A quarter of the block that goes in it, which is
        // enough that the sides catch a shadow and the imprint reads as being DOWN there, and not so
        // deep that the colours at the bottom stop being legible from a standing eye.
        private const float CubeRecessDepth = 0.10f;

        // THE FLOOR OF THAT RECESS, AS THE CUBE'S OWN UNDERSIDE.
        //
        // `BedlamSolvedCells` indexes as `(x * n + y) * n + z`, so the bottom layer of the solved
        // packing - `y == 0` - is the sixteen entries at `x * 16 + z`. Each names a piece; the model
        // gives its thirteen pieces three shared materials; so those sixteen cells resolve to
        // sixteen tiles in the cube's own three colours, laid out as the cube actually packs.
        //
        // Read out rather than written down, which is the point: there is one statement of the
        // packing in this project and this is not a second one. Change the solution and the recess
        // changes with it.
        // **AND IT IS A REAL CAVITY NOW** (2026-09-01, by request: *the groove is not carved, make it
        // carved*). It was a printed square: a flat dark quad with the sixteen coloured tiles laid
        // 6mm proud of it, so the imprint was a picture of a hole rather than a hole.
        //
        // The tiles drop `CubeRecessDepth` and four inner walls run down to them, which is what makes
        // the shadow. `BuildFinalSlot`'s own flat "Hole" is switched off on the way past - it is the
        // thing this replaces, and left on it would sit across the mouth of the cavity.
        private static void BuildCubeRecessFloor(Transform slotRoot, float across)
        {
            const int n = BedlamCells;
            float tile = across / n;

            // The flat stand-in goes. Found by name because that is what `BuildFinalSlot` calls it,
            // and a rename there should show up here rather than silently leaving both.
            Transform flat = slotRoot.Find("Hole");
            if (flat == null)
                Debug.LogWarning("[SceneBuilder] The cycle-3 slot has no 'Hole' to replace with a "
                               + "cavity - check what BuildFinalSlot named it. The recess will have "
                               + "a flat quad across its mouth.");
            else
            {
                Renderer flatRenderer = flat.GetComponent<Renderer>();
                if (flatRenderer != null) flatRenderer.enabled = false;
            }

            // The four sides of the cavity, in the same near-black the flat hole used - a groove wall
            // is the shadowed part of the recess and has no business being lighter than one.
            Material sideMat = MakeColorMaterial("CubeRecessSide", new Color(0.05f, 0.05f, 0.06f));
            float half = across / 2f;
            foreach (int axis in new[] { 0, 1 })
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 at = axis == 0
                        ? new Vector3(side * half, -CubeRecessDepth / 2f, 0f)
                        : new Vector3(0f, -CubeRecessDepth / 2f, side * half);
                    Vector3 size = axis == 0
                        ? new Vector3(0.006f, CubeRecessDepth, across)
                        : new Vector3(across, CubeRecessDepth, 0.006f);
                    Prim(PrimitiveType.Cube, $"RecessSide_{axis}{side}", slotRoot,
                         at, size, sideMat, removeCollider: true);
                }
            // A hair of the dark hole left between tiles, so the grid reads as sixteen cells rather
            // than as one mottled square.
            float gap = tile * 0.10f;

            GameObject grid = new GameObject("CubeFace");
            grid.transform.SetParent(slotRoot, false);
            // At the BOTTOM of the cavity now, not 6mm above the pedestal's top face.
            grid.transform.localPosition = new Vector3(0f, -CubeRecessDepth, 0f);

            for (int x = 0; x < n; x++)
            {
                for (int z = 0; z < n; z++)
                {
                    int piece = BedlamSolvedCells[(x * n + 0) * n + z];
                    Prim(PrimitiveType.Cube, $"Cell_{x}{z}", grid.transform,
                        new Vector3((x - (n - 1) / 2f) * tile, 0f, (z - (n - 1) / 2f) * tile),
                        new Vector3(tile - gap, 0.004f, tile - gap),
                        MakeColorMaterial($"BedlamFace_{BedlamPieceMaterial(piece)}",
                                          BedlamPieceColor(piece)),
                        removeCollider: true);
                }
            }
        }

        // WHICH OF THE MODEL'S THREE MATERIALS A PIECE WEARS. Measured off `bedlam_cube.glb` and
        // stated here because a `.glb` is not a thing this file can read at build time: pieces 0-3
        // are the blue one, 4-7 the red, 8-12 the amber. If the model is ever re-split, this is the
        // one line that has to follow it.
        private static int BedlamPieceMaterial(int piece) =>
            piece <= 3 ? 0 : piece <= 7 ? 1 : 2;

        private static Color BedlamPieceColor(int piece)
        {
            switch (BedlamPieceMaterial(piece))
            {
                // The glTF base colours, lifted off black a little: a recess is a shadowed hole and
                // pure primaries in one read as ink rather than as plastic.
                case 0: return new Color(0.12f, 0.20f, 0.78f);
                case 1: return new Color(0.78f, 0.13f, 0.13f);
                default: return new Color(0.85f, 0.62f, 0.05f);
            }
        }

        // A LADDER, DRAWN. Two stiles and four rungs - the least that reads as one at 24 pixels, and
        // the same flat orthographic style the block icon uses.
        private static Sprite LadderIcon()
        {
            var icon = new IconCanvas(128);
            icon.Bar(new Vector2(0.32f, 0.5f), new Vector2(0.055f, 0.42f));
            icon.Bar(new Vector2(0.68f, 0.5f), new Vector2(0.055f, 0.42f));
            for (int i = 0; i < 4; i++)
                icon.Bar(new Vector2(0.5f, 0.20f + i * 0.20f), new Vector2(0.24f, 0.045f));
            return SaveSprite(icon, "icon_ladder");
        }

        // ==================================================== CYCLE 4
        //
        // **ONE SEALED ROOM UNDER ROOM3-0'S HATCH: a bed, an empty chest and the gas.** Exactly what
        // cycle 3 was on the day it was started, and for the same reason - the BOUNDARY is the thing
        // being exercised, and a puzzle would be in the way of testing it. `finalRoom` stays null,
        // which is a supported state: the loop simply keeps iterating here, which is the honest
        // shape of a cycle with no puzzles in it.
        //
        // It sits a storey below ROOM3-0 rather than below cycle 3's floor - see `CycleFourFloorY`
        // for why, and `AssertCycleFourClear` for the check that makes that safe.
        private static (Transform root, Transform bedSpawn, ParticleSystem[] gas,
                        GhostInteractable[] signals)
            BuildCycleFourShell(Material floorMat, Material grooveMat, Material panelMat,
                                Material propMat)
        {
            // Its own fixture material, like cycles 2 and 3: `CeilingFixture` is one emissive
            // material shared by every room in a cycle, and a fourth cycle borrowing another's would
            // be a fourth cycle that goes dark when something two storeys up is finished.
            Material fixtureMat = MakeEmissiveMaterial("CeilingFixtureCycle4", Color.white, CeilingPanelEmission);

            GameObject root = new GameObject("Room_Cycle4");
            // Axis-aligned, like cycle 3 and for the same reason: one room, nothing to avoid, and
            // whatever grows out of it can decide its own facing.
            root.transform.position = new Vector3(CycleFourX, CycleFourFloorY, CycleFourZ);

            // THE HOLE THE PLAYER ARRIVES THROUGH. Its lid is not here - it lives on the join in the
            // core scene with the floor lid above it, because one `CycleExit` drives both and a
            // reference across a scene boundary comes back null. What this room owns is the absence.
            // **NO CEILING OF ITS OWN.** The hole is the whole slab: what closes this room in is
            // room3-2N's floor, one service void above its walls, and that slab is the lid that
            // opens onto it. A ceiling here would be a second one under the first, and the player
            // would arrive by falling through both.
            Rect wholeCeiling = Rect.MinMaxRect(
                -RoomWidth, -RoomDepth, RoomWidth, RoomDepth);

            Transform r1 = BuildEmptyRoom(root.transform, "Room4_1", Vector3.zero,
                                          floorMat, grooveMat, panelMat, fixtureMat,
                                          doorwayNorth: false, ceilingHole: wholeCeiling);

            (_, Transform spawn) = BuildBed(r1, propMat, floorY: CycleFourFloorY,
                                            zCentre: CycleFourZ, yaw: 0f, xCentre: CycleFourX);

            // Empty, and wired as `GhostInteractable`s whatever is in it - which is what makes
            // putting something in one later a one-line change rather than a signal-bit problem
            // (CLAUDE.md §1.6).
            Drawer[] drawers = BuildDresser(r1, "Dresser4_1", new Vector3(-0.95f, 0f, 1.35f),
                                            yaw: 0f, withLamp: true);

            ParticleSystem[] gas = BuildGasEmitters(r1, "Room4_1_Gas", 0f);

            var signals = new GhostInteractable[drawers.Length];
            for (int i = 0; i < drawers.Length; i++) signals[i] = drawers[i];

            Debug.Log($"[SceneBuilder] Cycle 4: Room4_1 at ({CycleFourX:0.##}, {CycleFourFloorY:0.###}, "
                    + $"{CycleFourZ:0.##}), one sealed room, {signals.Length} signal(s), no way out.");
            return (root.transform, spawn, gas, signals);
        }

        private static (Cycle cycle, WallPanelDisplay display) AssembleCycleFour(
            Transform root, Transform bedSpawn, ParticleSystem[] gasEmitters,
            GhostInteractable[] signals, Texture2D testCard, Texture2D staticNoise)
        {
            var panels = new System.Collections.Generic.List<Renderer>();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                if (r.transform.parent == null || !r.transform.parent.name.EndsWith("_Panels")) continue;
                panels.Add(r);
            }
            WallPanelDisplay display = MakeWallPanelDisplay(
                "WallPanelDisplay_Cycle4", panels.ToArray(), testCard, staticNoise);

            GameObject go = new GameObject("Cycle4");
            Cycle cycle = go.AddComponent<Cycle>();
            cycle.bedSpawnPoint = bedSpawn;
            cycle.doors = root.GetComponentsInChildren<Door>(true);
            cycle.drawers = root.GetComponentsInChildren<Drawer>(true);
            cycle.gasEmitters = gasEmitters;
            CheckGhostSignals("Cycle 4", signals);
            cycle.ghostInteractables = signals;
            cycle.conditions = root.GetComponentsInChildren<RoomCondition>(true);
            // No room0, so no console and no way to finish. See the note in AssembleCycleTwo for what
            // a `finalRoom` costs to add when there is one.
            cycle.finalRoom = null;
            cycle.worldRoot = root;
            cycle.wallPanels = display;

            SetFloorBase(root, CycleFourFloorY);
            return (cycle, display);
        }

        // ==================================================== ROOM3-0, AND THE WAY DOWN TO CYCLE 4
        //
        // Cycle 3's `-0`, and the only room in the building that is not on its cycle's ground floor.
        //
        // **IT IS REACHED FROM DECK B, TWO STOREYS UP INSIDE ROOM3-2N.** That is the whole reason the
        // tall room exists. Everything in it - the beam, the mirrors, both lifts, the blocks spread
        // over three storeys - was a climb that ended in a dead end: deck B had nothing on it. This
        // is what the climb buys, so the light puzzle and the way out of the cycle are one errand
        // instead of two features sharing a shell.
        //
        // A DOORWAY AT 10.8m, which is a thing no other wall in this building has. `BuildPanelWall`
        // takes its cutout as a rect in the wall's own frame with y measured from the room's floor,
        // so a hole this far up costs nothing extra - but it is the first one that is not at floor
        // level, and `AssertWalkable` is aimed at it for that reason.
        private const float DeckBSurfaceY = DeckBRows * GridCellHeight;      // 10.8156

        // **ROOM3-0 SITS ON TOP OF ROOM3-2N** (2026-08-30, by request), where it was north of it for
        // a day. Its floor is one service void above that room's ceiling - the same gap every storey
        // in this building has - and the way up is a LADDER through a hole in the ceiling, not a
        // doorway in a wall. See `LadderShaft`.
        //
        // Over the room's NORTH-WEST QUARTER rather than its middle, and that is forced rather than
        // chosen: the hole has to be above DECK B, which is the only floor at that height, and deck B
        // is an L from x -8.81 to -1.75 and z 3.62 to 10.56. A standard room centred here covers the
        // bulk of it. All three are in room3-2N's own frame.
        private const float RoomZeroX = -RoomWidth / 2f;                     // -4.375
        private const float RoomZeroZ = RoomDepth / 2f;                      //  5.25
        // **THE GAP BETWEEN ROOM3-2N'S CEILING AND ROOM3-0'S FLOOR, and it is NOT a service void**
        // (2026-08-30, by request: the ladder was too long to carry).
        //
        // Every other storey in this building is `StoreyDrop` apart, of which 1.6m is plenum. That
        // 1.6m was doing nothing for the player here - it is a gap they climb THROUGH, in the dark,
        // between two holes - and it was 1.6m of ladder. At 0.35 the two slabs still cannot be
        // coplanar (the rule that stops them z-fighting) and the climb loses a fifth of its height.
        //
        // **This is the only floor pair in the game that is not a storey apart**, which is why it has
        // its own constant rather than borrowing `ServiceVoid`: the next thing to reuse that number
        // must not inherit a decision made about a ladder.
        private const float LadderVoid = 0.35f;
        private const float RoomZeroY = 3f * RoomHeight + 2f * WallThickness + LadderVoid;

        // WHERE THE LADDER STANDS, in room3-2N's frame: **the far corner of deck B** (2026-08-30, by
        // request), one cell in from the west wall and one from the north. That is as far from
        // everything as this room can put it - the square void cut through deck B, both lifts and the
        // column all sit south and east of here - and it is the corner a player crossing deck B walks
        // toward rather than past. One grid cell across, like every other hole in a floor here.
        //
        // It has to be under ROOM3-0 as well, which is what stops it going right into the corner: that
        // room covers x -8.75 to 0 and z 0 to 10.5 of this one, so the hole's own half-cell has to
        // stay inside those.
        // **THE SHAFT IS AGAINST THE WEST WALL**, moved there in 2026-08-31 so the ladder has
        // something to lean on. Its west edge is bitten 25mm INTO the wall rather than made flush
        // with it: flush puts the hole's cut edge and the wall's face on the same plane facing the
        // same way, which is the coplanar flicker CLAUDE.md §3 warns about, and it would leave a
        // 0-width sliver of floor nobody can stand on. Buried, the floor simply ends at the wall.
        //
        // The shaft's own tube walls land inside the room wall's depth for the same reason, so there
        // is no seam between them to see.
        private const float LadderWallBite = 0.025f;

        // **AND INTO THE NORTH WALL TOO, once the roll above was fixed** (2026-08-31). The ladder's
        // 1.18m WIDTH lies along z now rather than x, so its foot is nearly four times broader in
        // that direction than it was - and at z 9.2 it overhung deck B's void by 14cm, which is a
        // ladder standing with one foot over a hole. Pushed into the corner it clears the void by
        // 31cm, and the same 25mm bite the west edge takes keeps a sliver of unstandable floor from
        // appearing between the mouth and the north wall.
        //
        // Room3-0 is `RoomDepth` deep and sits `RoomZeroZ` north of room3-2N's middle, so its north
        // wall face is at `RoomDepth` in this room's frame - one number serving both rooms, exactly
        // as the west wall does.
        private static readonly Vector2 LadderShaftXZ =
            new Vector2(-RoomWidth - LadderWallBite + GridCellWidth / 2f,
                         RoomDepth + LadderWallBite - GridCellWidth / 2f);

        private static Rect LadderShaftHole(float xCentre, float zCentre) => Rect.MinMaxRect(
            xCentre - GridCellWidth / 2f, zCentre - GridCellWidth / 2f,
            xCentre + GridCellWidth / 2f, zCentre + GridCellWidth / 2f);

        // WHERE CYCLE 4 BEGINS: **directly under room3-2N, a storey below cycle 3's floor** - the
        // ordinary arrangement, restored (2026-08-30). It hung under room3-0 for a day, and room3-0
        // moving on top of room3-2N put that 18m above the floor; a cycle boundary that high would
        // have needed the hatch's two lids pulled apart and a long tube between them, for a fall
        // nobody sees the middle of. The way down is in room3-2N's floor now, so cycle 4 goes where
        // every other cycle has gone: one storey straight down.
        private const float CycleFourX = CycleThreeX + GridCellWidth / 2f;
        private const float CycleFourZ = CycleThreeZ + NorthRoomZ;
        private const float CycleFourFloorY = CycleThreeFloorY - StoreyDrop;
        // room3-0's position in the CYCLE's frame rather than room3-2N's: the big room is offset half
        // a cell (see BuildCycleThreeShell) and everything about this room is hung off it.
        private const float RoomZeroXInCycle = GridCellWidth / 2f + RoomZeroX;
        private const float RoomZeroZInCycle = NorthRoomZ + RoomZeroZ;

        // **THE WAY DOWN IS A ROOM-SIZED HOLE IN THE MIDDLE OF ROOM3-2N'S FLOOR** (2026-08-30, by
        // request), where every other cycle boundary in this building is one grid cell. It is
        // room4-1's whole ceiling: the storey below opens, rather than a hatch opening onto it.
        //
        // Centred on room3-2N, which is what "in the middle of the room" means and is also what puts
        // room4-1 squarely under it - the bed a player falls into has to be under the hole they fell
        // through, and `AssertUnderHatch` is what says so.
        private static readonly Rect CycleFourHole = Rect.MinMaxRect(
            -RoomWidth / 2f, -RoomDepth / 2f, RoomWidth / 2f, RoomDepth / 2f);

        // **CYCLE 3'S ESCAPE OBJECT IS THE ASSEMBLED CUBE ITSELF** (2026-08-30, by request), where
        // for a day it was a green cube on a plinth that rose beside it. Finishing the puzzle shrinks
        // the thing you built until it fits in one hand, and that is what goes in room3-0's console.
        //
        // The id and the colour stay because the SLOT still needs both: a recess is cut to a shape
        // and lit in a colour, and the shape it is cut to is a cube whichever object fills it.
        private const string CycleThreeKeyItemId = "Cycle3Key";
        // **NOT GREEN ANY MORE** (2026-09-01, by request). This is the slot's RIM - the ring that
        // says idle / ready / refused - and it was a green nothing else in the building is. One
        // escape object per cycle means the colour never distinguished anything, and green beside a
        // recess printed in the cube's own blue, red and amber was the one wrong note in the room.
        //
        // Pale, so the ring reads as a lit edge rather than as a colour, and the only colours at the
        // console are the ones the object that goes in it is made of. Refused is still red, and it is
        // `FinalSlot`'s own.
        private static readonly Color CycleThreeKeyColor = new Color(0.88f, 0.89f, 0.92f);

        // ROOM3-0 ITSELF: one recess, one hatch, and the break.
        //
        // ONE SLOT, because cycle 3 pays out one object (by request, 2026-08-30). That makes this the
        // shortest `-0` in the game - cycle 1 takes three and cycle 2 takes three - and the length is
        // deliberately not here: what cycle 3 charges is the CLIMB, and the console is the receipt.
        private static (Transform room, FinalRoomSequence final) BuildBreakRoomThree(
            Transform parent, Material floorMat, Material grooveMat, Material panelMat,
            Material propMat, Material fixtureMat)
        {
            // **NO DOORWAY AT ALL.** The only way in is the hole in its own floor, which is the top
            // of the ladder shaft - a room you arrive in by climbing into it. Its floor hole and
            // room3-2N's ceiling hole are the same hole, stated in each room's own frame.
            Rect hole = LadderShaftHole(LadderShaftXZ.x - RoomZeroX, LadderShaftXZ.y - RoomZeroZ);

            Transform t = BuildEmptyRoom(parent, "Room3_0",
                new Vector3(RoomZeroXInCycle, RoomZeroY, RoomZeroZInCycle),
                floorMat, grooveMat, panelMat, fixtureMat,
                doorwayNorth: false, floorHole: hole);

            // --- the console ------------------------------------------------------------------------
            GameObject rack = new GameObject("Room3_0_Console");
            rack.transform.SetParent(t, false);

            // **ALREADY STANDING**, unlike cycle 1's and cycle 2's consoles, which come up out of the
            // floor when the player arrives. By request (2026-08-30): you climb into this room and
            // the thing the cube goes on is simply there. `RewardPlinth` is still what owns it, with
            // no travel - so `FinalRoomSequence` has the reference it expects and the loop still
            // resets it, without a rise nobody asked for.
            RewardPlinth risen = rack.AddComponent<RewardPlinth>();
            risen.plinth = rack.transform;
            risen.riseHeight = 0f;
            risen.riseSeconds = 0.01f;

            GameObject unit = new GameObject("Pedestal_Cube");
            unit.transform.SetParent(rack.transform, false);
            // **IN THE MIDDLE OF THE ROOM** (2026-08-31, by request). `PedestalRowZ` puts cycle 1's
            // and cycle 2's consoles 2.8m south of centre, because in those rooms the way down opens
            // in the middle and the row has to stand clear of it. Room3-0's way down is not in this
            // room at all - it is a storey below, in room3-2N - so nothing here is competing with the
            // middle, and one object on its own belongs at the centre of the room it is the point of.
            unit.transform.localPosition = Vector3.zero;

            // **FULL HEIGHT, and the `- SocketCapThickness` this used to carry was a copy-paste from
            // room2-0.** That subtraction is right for a BALL socket, where the missing 76mm is
            // filled by a `Cap` mesh with the bowl cut out of it - a box primitive cannot have a
            // hole in it, so the top of the pedestal has to be a different object. A SQUARE recess
            // is a painted quad, not a carved bowl, and `BuildFinalSlot` builds no cap for it: the
            // pedestal's top face sat 76mm below its own rim with nothing between them, open all the
            // way round. Cycle 1's console, which uses the same square recess, always did this the
            // right way - its plinth is full height and the slot sits on top.
            Prim(PrimitiveType.Cube, "Body", unit.transform,
                 new Vector3(0f, PedestalHeight / 2f, 0f),
                 new Vector3(PedestalWidth, PedestalHeight, PedestalDepth),
                 propMat, removeCollider: true);

            BoxCollider solid = unit.AddComponent<BoxCollider>();
            solid.center = new Vector3(0f, PedestalHeight / 2f, 0f);
            solid.size = new Vector3(PedestalWidth, PedestalHeight, PedestalDepth);

            // A SQUARE RECESS FOR A SQUARE OBJECT, which is the rule cycle 1's console already
            // follows: the hole says what goes in it, and there is nothing else in the room to say
            // it. Cycle 2's four identical bowls could not - they all took the same KIND of thing -
            // and had to carry a pictogram each. One slot needs no pictogram.
            FinalSlot slot = BuildFinalSlot(unit.transform, "Slot", SlotShape.Square,
                new Vector3(0f, PedestalHeight, 0f), CycleThreeSlotSize, CycleThreeKeyColor,
                reachCentre: new Vector3(0f, -0.55f, 0.42f),
                reachSize: new Vector3(1.30f, 2.60f, 2.00f));
            slot.acceptedItemId = CycleThreeKeyItemId;
            slot.refuseClip = LoadClip(SfxDir, "sfx_switch_off");

            // **AND THE HOLE IS THE CUBE'S OWN UNDERSIDE** (2026-08-31, by request: a recess with
            // several colours in it, like the block cube, rather than a green one).
            //
            // Not a decorative pattern - it is READ OUT of `BedlamSolvedCells`. The bottom layer of
            // the solved packing is `y == 0`, which by that table's own indexing is every sixteenth
            // entry, and each cell names the piece that fills it. The model has three materials
            // (blue, red, amber) shared between its thirteen pieces, so those sixteen cells resolve
            // to sixteen colours and the recess floor is the imprint of the thing that goes in it.
            //
            // It also does the job the green was doing badly. One escape object per cycle means the
            // colour was never distinguishing anything - it was just a colour nothing in the room
            // was - and this says what fits here in the only language the cube speaks.
            BuildCubeRecessFloor(slot.transform, CycleThreeSlotSize * 0.80f);

            // --- the way in -------------------------------------------------------------------------
            //
            // THE TOP OF THE LADDER, which is the only way into this room. **The console does NOT
            // rise here** (2026-08-30, by request: the structure the cube goes on is already
            // standing when you arrive) - the trigger is kept so `FinalRoomSequence.Active` still
            // turns on when somebody gets here, which is what makes the slot answer a press.
            GameObject escapeGO = new GameObject("EscapeTrigger_Cycle3");
            escapeGO.transform.SetParent(t, false);
            escapeGO.transform.localPosition = new Vector3(hole.center.x, 0f, hole.center.y);
            EscapeTrigger escape = escapeGO.AddComponent<EscapeTrigger>();
            // **NO DOOR TO BE OPEN, AND THAT USED TO MEAN NOBODY EVER ARRIVED.** `TryArm` began by
            // requiring one, which is right for every other final room in the building and fatal
            // here: `PlayerArrived` stayed false, `FinalRoomSequence.Active` with it, and the recess
            // refused the cube in silence. See `EscapeTrigger.requiresOpenDoor`.
            escape.requiresOpenDoor = false;
            // BOTH HALF-EXTENTS, and the depth was the second half of the same bug: it kept its
            // 0.5m default while the step-off point is 1.37m away in z, so even with the door test
            // gone a player standing where the climb puts them was outside the volume.
            escape.halfWidth = GridCellWidth;
            escape.halfDepth = GridCellWidth;
            // AND A CEILING ON IT. This volume ignores Y by design - a doorway is a doorway at any
            // height - which stopped being harmless when room3-0 was stacked over room3-2N: without
            // this, standing at the ladder's FOOT two storeys down armed the console up here.
            escape.halfHeight = RoomHeight / 2f;

            // WHERE THE CLIMB PUTS YOU DOWN, and there are TWO of them (2026-08-31).
            //
            // `LadderMount` cannot work this out for itself - the top of its shaft is a hole, and
            // which side of it is floor is a fact about THIS room. The mouth sits in the north-west
            // corner with barely 40cm between it and either wall, so of its four sides only the
            // SOUTH and the EAST are places a person can be put down at all.
            //
            // **TWO, because one was a shove.** A single point meant every step-off travelled to the
            // same lip however the player had come - so somebody who dipped into the mouth from the
            // south and climbed straight back out was carried across the opening to the other side,
            // which play reported as "walking at the ladder teleports me backwards". `LadderMount`
            // takes the nearer, so the step-off is the shortest move that reaches floor.
            //
            // Just clear of the rim rather than a whole cell out: the lip is where you step, and a
            // metre and three quarters is a stride and a half past it.
            float lip = GridCellWidth / 2f + 0.55f;

            GameObject dismounts = new GameObject("Dismounts");
            dismounts.transform.SetParent(t, false);

            GameObject dismountS = new GameObject("Dismount_South");
            dismountS.transform.SetParent(dismounts.transform, false);
            dismountS.transform.localPosition = new Vector3(hole.center.x, 0f, hole.center.y - lip);

            GameObject dismountE = new GameObject("Dismount_East");
            dismountE.transform.SetParent(dismounts.transform, false);
            dismountE.transform.localPosition = new Vector3(hole.center.x + lip, 0f, hole.center.y);

            // **AND THE SIGN THAT SAYS WHERE THE WAY OUT IS.** The floor that opens is in room3-2N,
            // a storey below and out of sight from here; without this the break ends with a player
            // in a red room with nothing to do. Authored at alpha 0 and raised by `FacilityFailure`,
            // because before the break there is nothing to point at.
            // **IDENTITY, NOT 180.** This is a north (+Z) wall, and a wall face in this game points
            // its canvas's local +Z INTO the wall - see `MakeFourWallFaces`, whose north entry is
            // identity, and the ladder sign, which is -90 on a west wall and describes itself as
            // facing east into the room. At 180 the arrow was drawn on the inside of the panelling,
            // which is why the room appeared to have no sign in it at all.
            // **ONE ON EVERY WALL, NOT ONE ON THE NORTH ONE** (2026-09-01, by request).
            //
            // Which way the player is facing when the break ends is not something this sequence gets
            // to choose - they are standing at a console they have just filled, and the room goes red
            // around them. A single arrow is an arrow half of them have their back to, in a windowless
            // room whose only exit is a hole in the floor they cannot see from most of it.
            //
            // Every face points its canvas's local +Z INTO its own wall, which is this game's
            // convention and the one that has been got wrong twice (`docs/gotchas.md`) - so these are
            // the same four rotations `MakeFourWallFaces` uses.
            System.Action<Transform> arrow = f =>
                MakeWallIcon(f, "WayDown", DownArrowIcon(), Vector2.zero, 1280f,
                             new Color(0.85f, 0.10f, 0.10f, 0.95f));

            CanvasGroup[] wayDown =
            {
                MakeWallFace(t, "WayDownSign_North",
                    new Vector3(0f, 2.1f, RoomDepth / 2f - 0.06f), Quaternion.identity,
                    arrow, worldWidth: 1.5f, withPlate: false, authoredHeight: 1600f),
                MakeWallFace(t, "WayDownSign_South",
                    new Vector3(0f, 2.1f, -RoomDepth / 2f + 0.06f), Quaternion.Euler(0f, 180f, 0f),
                    arrow, worldWidth: 1.5f, withPlate: false, authoredHeight: 1600f),
                MakeWallFace(t, "WayDownSign_West",
                    new Vector3(-RoomWidth / 2f + 0.06f, 2.1f, 0f), Quaternion.Euler(0f, -90f, 0f),
                    arrow, worldWidth: 1.5f, withPlate: false, authoredHeight: 1600f),
                MakeWallFace(t, "WayDownSign_East",
                    new Vector3(RoomWidth / 2f - 0.06f, 2.1f, 0f), Quaternion.Euler(0f, 90f, 0f),
                    arrow, worldWidth: 1.5f, withPlate: false, authoredHeight: 1600f),
            };

            GameObject seqGO = new GameObject("BreakSequence");
            seqGO.transform.SetParent(t, false);
            FinalRoomSequence sequence = seqGO.AddComponent<FinalRoomSequence>();
            sequence.console = risen;
            sequence.slots = new[] { slot };
            slot.sequence = sequence;
            sequence.arrival = escape;
            // The boundary owns the hatch, as it has since cycle 3 existed: `CrossToNextCycle` wakes
            // the storey below and repoints the gas before opening the floor, and a player must not
            // be able to drop into a cycle that is still asleep.
            sequence.opensWayOutOnBreak = false;
            // No timer. `FacilityFailure` is what plays here and it runs to its own length; the ten
            // seconds only ever bounded the window before the ENDING's scrim, and this is not that.
            sequence.breakDuration = 0f;

            wayDownHint = wayDown;
            return (t, sequence);
        }

        // Carried out of `BuildBreakRoomThree` rather than returned, because it belongs to
        // `FacilityFailure` and that is assembled a scene apart. One field is cheaper than a fourth
        // member on a tuple two callers already thread through.
        private static CanvasGroup[] wayDownHint;
        // Room3-2N's ladder pictogram, kept so the teardown can take it down - see
        // `FacilityFailure.ladderSign`.
        private static CanvasGroup ladderSignHint;

        // AN ARROW POINTING DOWN, THROUGH A FLOOR. Two marks: the arrow, and the line under it that
        // makes the arrow read as going THROUGH something rather than merely pointing at it.
        private static Sprite DownArrowIcon()
        {
            var icon = new IconCanvas(128);
            icon.Bar(new Vector2(0.5f, 0.66f), new Vector2(0.10f, 0.22f));
            icon.Shape(p =>
            {
                if (p.y < 0.26f || p.y > 0.50f) return false;
                float k = (p.y - 0.26f) / 0.24f;
                return Mathf.Abs(p.x - 0.5f) <= 0.30f * k;
            });
            icon.Bar(new Vector2(0.5f, 0.15f), new Vector2(0.34f, 0.045f));
            return SaveSprite(icon, "icon_way_down");
        }

        // NOTHING OF CYCLE 4 MAY BE INSIDE CYCLE 3, which is the one thing this arrangement could get
        // silently wrong. Room3-0 is up in the air and cycle 4 hangs under it, so for the first time
        // in this building two cycles overlap in ELEVATION - and whether that matters is entirely a
        // question about their footprints, which is exactly the kind of thing that is obvious in a
        // plan and wrong in a build.
        // Every MESH in a subtree, and nothing else. Zero-size entries are skipped as well as
        // particles: a renderer with no extent is a renderer that is not anywhere.
        private static Bounds SolidBounds(Transform root)
        {
            bool any = false;
            Bounds box = new Bounds(root.position, Vector3.zero);

            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.bounds.size == Vector3.zero) continue;
                if (!any) { box = r.bounds; any = true; }
                else box.Encapsulate(r.bounds);
            }

            return box;
        }

        private static void AssertCycleFourClear(Transform cycleThreeRoot, Transform cycleFourRoot)
        {
            if (cycleThreeRoot == null || cycleFourRoot == null) return;

            // **MEASURED OFF MESHES, NOT OFF `MeasuredBounds`.** That helper encapsulates every
            // Renderer, and a cycle has ParticleSystemRenderers in it - the four gas emitters - whose
            // bounds before they have ever played are a degenerate box at the WORLD ORIGIN. Both
            // cycles therefore measured as reaching from (0,0,0), which made them overlap each other
            // and everything else. The first run of this check reported exactly that.
            Bounds four = SolidBounds(cycleFourRoot);

            // **ROOM BY ROOM, NOT CYCLE BY CYCLE.** One box round the whole of cycle 3 spans from its
            // ground floor at -18.2 up to room3-0's ceiling at -1.8, and a box that tall overlaps
            // anything anywhere near it - the first run of this check reported a collision that was
            // nothing but that. What actually has to be true is that no ROOM of cycle 3 shares space
            // with cycle 4, and the rooms are what the player can be inside.
            int clashes = 0;
            float nearest = float.MaxValue;

            foreach (Transform room in cycleThreeRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!room.name.StartsWith("Room3_") || room.name.EndsWith("_Root")) continue;
                if (room.name.EndsWith("_CeilingLights") || room.name.EndsWith("_Gas")) continue;

                Bounds box = SolidBounds(room);
                if (box.size == Vector3.zero) continue;

                if (box.Intersects(four))
                {
                    clashes++;
                    Debug.LogError($"[SceneBuilder] CYCLE 4 IS INSIDE {room.name}. That room spans "
                                 + $"{box.min} to {box.max} and cycle 4 spans {four.min} to "
                                 + $"{four.max}. Room3-0 is {DeckBSurfaceY:0.##}m up and cycle 4 hangs "
                                 + "a storey under it, so the two share an elevation band - they must "
                                 + "not share a footprint.");
                    continue;
                }

                // How close the nearest miss is, on the axis it misses by. Worth logging: the whole
                // arrangement rests on a gap that is one service void deep and one wall build-up
                // wide, and a number in the log is what notices it closing.
                Vector3 gap = Vector3.Max(box.min - four.max, four.min - box.max);
                nearest = Mathf.Min(nearest, Mathf.Max(gap.x, Mathf.Max(gap.y, gap.z)));
            }

            if (clashes == 0)
                Debug.Log($"[SceneBuilder] Cycle 4 clear of every room in cycle 3, nearest miss "
                        + $"{nearest:0.###}m. Cycle 4 floor {CycleFourFloorY:0.###} against cycle 3's "
                        + $"{CycleThreeFloorY:0.###} and room3-0's "
                        + $"{CycleThreeFloorY + RoomZeroY:0.###}.");
        }

        // ==================================================== ROOM3-2N'S GROUND FLOOR: THE BEDLAM CUBE
        //
        // Thirteen blocks, one of them standing on the floor inside a roped-off square to the right
        // of the door, and the other twelve spread across all THREE of the room's storeys: four on
        // the floor, four on deck A, four on deck B.
        //
        // **THE SCATTER IS WHAT TIES THE PUZZLE TO THE ROOM.** On one floor this is Room2West's chess
        // board again in a bigger room. Across three, every block above the ground costs a ride on a
        // `BeamLift` - which costs a past self holding a mirror on a receiver - so the light puzzle
        // and the cube puzzle are one errand instead of two rooms sharing a shell. The lift runs the
        // right way round for it: it is pulled DOWN by the beam and climbs when the beam goes, so the
        // player's hands are free the whole ride and a block can be carried in them.
        //
        // **THE PACKING IS SOLVED OFFLINE AND BAKED INTO THE MODEL.** `bedlam_cube.glb` here is not
        // the file that was downloaded - `Tools/split_bedlam_cube.py` takes that one, splits its
        // three colour-merged meshes into the thirteen pieces they actually hold, solves the exact
        // cover, and writes each piece out already rotated into its solved pose with that pose as
        // its NODE TRANSLATION. So this function computes nothing about the puzzle: it reads the
        // model's own arrangement, keeps it as thirteen anchors, and scatters the blocks.
        //
        // That is the same trick `BuildChessSet` uses - the home square is where the piece started -
        // and it is here for the same reason. A pose captured off the model cannot disagree with the
        // model, where a table of thirteen positions and rotations written in this file could.
        //
        // THE ANCHOR IS A SIBLING OF THE PIECE, carrying the local pose the piece had before it was
        // moved. `CarryableItem.InsertInto` parents at local identity, so an anchor built that way
        // reproduces the solved cube exactly - and it does so WITHOUT this file knowing which way
        // the glTF importer turned the model on the way in. Anything measured off the file's own
        // axes would have to know; a sibling does not.
        private const string BedlamModel = PlayDir + "/bedlam_cube.glb";
        private const string RopeModel = FurnitureDir + "/velvet_rope.glb";

        private const int BedlamPieceCount = 13;
        private const int BedlamCells = 4;
        // The block that starts on the floor, and it is chosen rather than picked. Block 2 is the
        // only one that is both on the cube's BOTTOM LAYER - so it lies flat on the mat and the cube
        // is seen to grow upward out of it, rather than hanging in the air waiting for a floor - and
        // among the best connected in the solution: eight of the other twelve share a face with it,
        // so the player has eight blocks that go on straight away instead of a hunt for the one that
        // does. `CheckBedlamSolution` proves the rest are reachable from it whatever is chosen here.
        private const int BedlamSeedPiece = 2;
        private const int BedlamScatterRngSeed = 20260829;

        // WHICH BLOCK FILLS WHICH CELL OF THE FINISHED CUBE, printed by the splitter and pasted
        // here. Sixty-four entries, cell `(x * 4 + y) * 4 + z` in the model's own grid.
        //
        // **THIS IS THE ONE THING IN THE PIPELINE COPIED BY HAND, so the build checks it** - see
        // `CheckBedlamSolution`. It is what `BedlamCube` derives "these two blocks touch" from, and
        // touching is the whole rule for whether a block may be clicked home yet; a table that did
        // not describe this model would be a cube with blocks that never go on, and no symptom
        // until somebody played it.
        //
        // Which AXIS is which does not matter and cannot be read off this - the importer is free to
        // permute them - because nothing here is ever compared against a world direction. Only the
        // adjacency it encodes is used.
        private static readonly int[] BedlamSolvedCells =
        {
             0,  0,  0,  1,   5,  0,  3,  3,  12, 12,  3,  8,  12,  8,  8,  8,
             5,  7,  0,  1,   5,  7,  2,  1,   5,  3,  3,  1,  12,  8,  4,  9,
             7,  7,  2,  2,   5, 10,  2,  1,  11,  6,  6,  9,   6,  6,  4,  9,
             7, 10, 10, 10,  11, 10,  2,  9,  11, 11,  4,  9,  11,  6,  4,  4,
        };

        // Where the cube stands, measured off the CORRIDOR MOUTH rather than off the room's middle:
        // "on your right as you come out" is a statement about the door, and the room is offset half
        // a cell from the corridor (see BuildCycleThreeShell) so its centre is the wrong thing to
        // hang it on. Far enough in that the rope clears the south wall, near enough that it is the
        // first thing on that side.
        // WHERE A PUT-DOWN LANDS, before the object's own half-width is added. One number, read by
        // `PlayerHand` and by every ghost - see the note where it is assigned.
        private const float DropAheadDistance = 0.55f;

        private const float BedlamStandRight = 3.8f;
        private const float BedlamStandIn = 3.3f;

        // What the splitter was run at - the finished cube is four of these across, so 1.6m on the
        // floor. **DOUBLED 2026-08-29, by request**, from the 0.2 it was built at. It scales nothing
        // here (the model already carries metres); it is the number the splitter is re-run with, and
        // it is stated so `CheckBedlamSolution` can measure the cell map's box in the same units the
        // geometry is in.
        private const float BedlamCellSize = 0.4f;

        // The mat the cube is built on. **~~A waist-high plinth~~ GONE 2026-08-29, by request: the
        // first block lies on the FLOOR.** What the plinth was actually doing was two things, and
        // only one of them was height - the other was being a dark inset that says something is MEANT
        // to stand here, and that half is kept as a mat because the flash that answers a refused
        // click has to live on something.
        //
        // Sized to the finished cube plus a hand's width, and only just proud of the floor: at 2cm it
        // would be a step, and coplanar with the slab it would z-fight (CLAUDE.md §3).
        private const float BedlamMatSize = 2.2f;
        private const float BedlamMatThickness = 0.012f;
        private const float BedlamMatLift = 0.004f;

        // THE ROPE ROUND IT, AND IT IS THE MODEL'S OWN ARRANGEMENT. `velvet_rope.glb` is not a single
        // stanchion - it is nine posts and eight spans already laid out as a museum enclosure with
        // one side left open, which is exactly the thing that was asked for. Placed once and sized,
        // rather than rebuilt post by post out of its parts: a rope run assembled in this file would
        // be this file deciding where the rings sit on the posts, which is the artist's decision and
        // is already correct in the file.
        //
        // **NO COLLIDER, deliberately.** A velvet rope that stopped a `CharacterController` would be
        // a 5m box round the one thing in the room the player has to walk up to twelve times, and the
        // ways in and out of it would depend on a gap two ropes happen to leave. It is a mark on the
        // floor plan, and the cube inside it is the solid thing.
        private const float BedlamRopeSpan = 4.6f;
        // **HOW IT ARRIVES, MEASURED RATHER THAN DERIVED.** `velvet_rope.glb` comes in LYING DOWN -
        // its posts run along Unity's +Z with the ball ends at the far one - and no amount of reading
        // its node matrices predicted that: the two root nodes compose to a plain 1:100 scale, so on
        // paper it should stand up on its own. It does not. Working out why is not worth a build; the
        // built scene was measured instead, which is the rule this project already keeps for
        // `MakeChessPiece`'s tilt and for every other imported pose.
        //
        // -90 about X stands it up. 180 about Y then turns the OPENING - the one gap in the ring,
        // between the posts the model leaves unroped - to face the corridor, so the way in is on the
        // side the player arrives from. Unity applies Z, then X, then Y, so the yaw lands after the
        // pitch, which is the order these two want.
        private const float BedlamRopePitch = -90f;
        private const float BedlamRopeYaw = 180f;

        private static BedlamCube BuildBedlamCube(Transform room, float roomWidth, float roomDepth,
                                                  float mouthX, Material propMat)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(BedlamModel);
            if (source == null)
            {
                Debug.LogError($"[SceneBuilder] no model at {BedlamModel} - room3-2N has no puzzle. "
                             + "Run Tools/split_bedlam_cube.py over the downloaded bedlam_cube.glb.");
                return null;
            }

            Vector3 standAt = new Vector3(mouthX + BedlamStandRight, 0f,
                                          -roomDepth / 2f + BedlamStandIn);

            // THE STAND IS THE CUBE'S OWN TRANSFORM, and that is not tidiness. `BedlamPlacer` asks
            // `PlayerLookup.InView(cube.aim, cube.transform)`, which forgives every collider under
            // the named owner - so the mat and the cube's own solid box have to be INSIDE this
            // subtree or they occlude the very point the prompt hangs on. Everything the cube is made
            // of hangs off this one transform for that reason.
            GameObject stand = new GameObject("BedlamStand");
            stand.transform.SetParent(room, false);
            stand.transform.localPosition = standAt;

            BedlamCube cube = stand.AddComponent<BedlamCube>();
            cube.solvedCells = BedlamSolvedCells;
            cube.cellsPerSide = BedlamCells;
            cube.seedPiece = BedlamSeedPiece;

            // The dark inset the cube is built on, and the thing that answers a click. A pale block
            // on a pale floor is one surface; the mat is what says something is MEANT to be here, and
            // flashing it is how a refused block is told apart from a control that did nothing.
            GameObject mat = Prim(PrimitiveType.Cube, "Mat", stand.transform,
                new Vector3(0f, BedlamMatLift + BedlamMatThickness / 2f, 0f),
                new Vector3(BedlamMatSize, BedlamMatThickness, BedlamMatSize),
                MakeColorMaterial("BedlamMat", new Color(0.14f, 0.14f, 0.16f)),
                removeCollider: true);
            cube.statusRenderer = mat.GetComponent<Renderer>();

            // THE MODEL, AT ITS OWN SIZE. `PlaceModelLocal` would rescale it to a longest side, and
            // there is nothing to rescale: the splitter wrote it out in metres, four cells across,
            // so a scale of one is the size the puzzle was cut at.
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source, stand.transform);
            instance.name = "Cube";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            // UNPACKED, like the chess set and for the same reason: Unity lets components be added
            // to a prefab instance but not the hierarchy restructured, and every block here is about
            // to be reparented out across three storeys. The .glb stays the source of truth for the
            // geometry - the scene is build output, so nothing is lost by flattening it.
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);

            var pieceNodes = new Transform[BedlamPieceCount];
            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Piece_")) continue;
                if (!int.TryParse(t.name.Substring("Piece_".Length), out int index)) continue;
                if (index >= 0 && index < BedlamPieceCount) pieceNodes[index] = t;
            }

            // Measured while the model is still assembled, which is the only moment it IS a cube.
            Bounds assembled = MeasuredBounds(instance);
            // STOOD ON THE MAT, i.e. its bottom face on the floor rather than its centre at a height.
            // The seed block is on the cube's bottom layer, so this is also what puts THAT block flat
            // on the ground, which is the whole of the request.
            instance.transform.localPosition +=
                new Vector3(0f, BedlamMatLift + BedlamMatThickness + assembled.size.y / 2f, 0f)
                - stand.transform.InverseTransformPoint(assembled.center);
            assembled = MeasuredBounds(instance);

            // WHERE THE CLICK IS AIMED AND WHERE THE DISC HANGS: the middle of the finished cube.
            // Positioned from the re-measured bounds rather than left at the model's local origin,
            // because the importer is free to have put a node between the two.
            GameObject aim = new GameObject("Aim");
            aim.transform.SetParent(instance.transform, false);
            aim.transform.position = assembled.center;
            cube.aim = aim.transform;

            // THE ASSEMBLED CUBE IS SOLID, from the first block rather than the last. One box over
            // the whole 4x4x4 volume, not one collider per block: the blocks come and go and their
            // colliders would come and go with them, and a shape that changes under the player is a
            // shape they can be standing inside when it appears. The cost is honest and small - the
            // empty part of a half-built cube is solid too - and the alternative is a metre and a
            // half of geometry the player walks straight through.
            BoxCollider solid = aim.AddComponent<BoxCollider>();
            solid.size = assembled.size;

            BuildBedlamRope(stand.transform);

            var blocks = new CarryableItem[BedlamPieceCount];
            var homes = new Transform[BedlamPieceCount];
            var boxes = new Bounds[BedlamPieceCount];

            Sprite icon = BedlamBlockIcon();
            System.Random rng = new System.Random(BedlamScatterRngSeed);
            BedlamShelf[] shelves = BedlamShelves(roomWidth, roomDepth, mouthX, standAt);
            var spots = new System.Collections.Generic.List<Vector3>();
            // Every spot on every shelf. `spots` is cleared between storeys - it exists to space
            // blocks apart from their own neighbours - and the check at the end of the loop is about
            // one fixture, so it needs the ones that were thrown away.
            var landed = new System.Collections.Generic.List<Vector3>();
            int shelf = 0, placedOnShelf = 0;

            for (int i = 0; i < BedlamPieceCount; i++)
            {
                Transform piece = pieceNodes[i];
                if (piece == null)
                {
                    Debug.LogError($"[SceneBuilder] {BedlamModel}: no node called Piece_{i:00} - the "
                                 + "split has changed. Re-run Tools/split_bedlam_cube.py.");
                    return null;
                }

                // CAPTURED BEFORE ANYTHING MOVES. This pose is the solved cube, and it is only true
                // of the model as the splitter left it.
                Vector3 homeLocalPosition = piece.localPosition;
                Quaternion homeLocalRotation = piece.localRotation;
                boxes[i] = MeasuredBounds(piece.gameObject);

                GameObject anchor = new GameObject($"Home_{i:00}");
                anchor.transform.SetParent(piece.parent, false);
                anchor.transform.localPosition = homeLocalPosition;
                anchor.transform.localRotation = homeLocalRotation;
                anchor.transform.localScale = Vector3.one;
                homes[i] = anchor.transform;

                blocks[i] = MakeBedlamBlock(piece, boxes[i], i, icon);

                // **THE SEED IS SCATTERED LIKE THE REST, and it has to be.** It was left standing
                // at its home pose for a while, so that the saved scene showed a mat with a block on
                // it - and `CheckHintAnchors` reported it: a block at its home pose has its prompt
                // anchor buried inside the cube's own solid box, which is a collider `PlayerLookup`
                // would not forgive it. Inert, because the seed is seated on the first frame and a
                // seated block has no trigger - and a warning that is inert is still a warning the
                // next reader has to work out, which is what CLAUDE.md §3 says about permanent
                // console noise.
                //
                // Nothing about play changes: `BedlamCube.Start` seats it before the first frame is
                // drawn, and every iteration's reset does it again. What changes is that the scene on
                // disk now shows the puzzle unsolved, which is what it is.

                // ONE SHELF AT A TIME, in order, so the four counts are exact rather than emergent.
                while (shelf < shelves.Length - 1 && placedOnShelf >= shelves[shelf].blocks)
                {
                    shelf++;
                    placedOnShelf = 0;
                    spots.Clear();     // a different storey - nothing up here can crowd anything down there
                }
                placedOnShelf++;

                Vector3 spot = BedlamScatterSpot(rng, shelves[shelf], spots);
                spots.Add(spot);
                landed.Add(spot);
                // The pivot is the block's own centre, so it has to be lifted by half its height to
                // stand on the surface rather than half through it - the same number `floorY`
                // carries, added to the deck this shelf is.
                piece.position = room.TransformPoint(spot) + Vector3.up * (boxes[i].size.y / 2f);
                // Turned where it fell. Yaw only: a block tipped onto a corner would have to be read
                // as a different shape from the one it becomes on the cube, and this puzzle is hard
                // enough without that. Yaw also leaves its height alone, which is what keeps the
                // lift above true.
                piece.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * piece.rotation;
            }

            // **AND NOTHING LANDED ON THE LADDER'S MARK.** The exclusion above is what keeps that
            // true; this is what says so if it ever stops being. A block on the mark does not look
            // like a bug - it looks like one of twelve blocks lying about a room full of blocks -
            // and what it actually does is make the ladder impossible to install, silently, because
            // `LadderPlacer`'s view ray runs into it. Play spent a session on that. Cheap to check
            // and it names the cause rather than the symptom.
            Rect mark = Grow(LadderShaftHole(LadderFootX, LadderShaftXZ.y), 3f * BedlamCellSize / 2f);
            foreach (Vector3 at in landed)
                if (mark.Contains(new Vector2(at.x, at.z)))
                    Debug.LogError($"[SceneBuilder] a Bedlam block landed at ({at.x:0.##}, {at.z:0.##}), "
                                 + "on the ladder's mark. It will occlude the mount and the ladder "
                                 + "cannot be installed. Fix the deck B keepClear in BedlamShelves.");

            // **THE OBJECT THE PUZZLE BECOMES.** A second copy of the model with nothing moved - so
            // every node sits at its solved translation and the thing is a finished cube - built
            // small, hidden, and revealed at the end of the shrink. See `BedlamCube.Shrink` for why
            // the assembly itself cannot be the carryable.
            cube.assembled = instance.transform;
            cube.held = BuildHeldCube(stand.transform, source, assembled, icon);
            cube.heldScale = BedlamHeldScale;

            cube.pieces = blocks;
            cube.homeAnchors = homes;
            cube.audioSource = MakeSource(stand.transform, "SeatAudio", 1f, 0.9f);
            cube.insertClip = LoadClip(SfxDir, "sfx_item_pickup");

            CheckBedlamSolution(cube, boxes, assembled, shelves);
            return cube;
        }

        // THE ROPE, PLACED ONCE. Centred on the cube and sized by its longest side, which is the run
        // end to end; everything else about it - post height, spacing, where the rings sit, which
        // side is left open - comes with the model.
        private static void BuildBedlamRope(Transform stand)
        {
            (GameObject rope, Bounds bounds) = PlaceModelLocal(RopeModel, stand, "Rope",
                Vector3.zero, Quaternion.Euler(BedlamRopePitch, BedlamRopeYaw, 0f), BedlamRopeSpan);
            if (rope == null) return;

            // Sat on the floor rather than centred on the cube's middle. `PlaceModelLocal` centres
            // what it places, which is right for a thing hung on a wall and wrong for a thing
            // standing on one - the posts would be buried to the waist.
            rope.transform.position += Vector3.up * (stand.position.y - bounds.min.y);

            // NOT SOLID. See the note on `BedlamRopeSpan`: the rope is a mark on the floor plan and
            // the cube inside it is the thing you cannot walk through. Colliders come off rather than
            // being left off, because a `.glb` import decides for itself whether it has any.
            foreach (Collider c in rope.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);

            // The posts' HEIGHT is the number to read here, and it is the one that says the model
            // came in the right way up: it should be about a metre. Anything near the enclosure's
            // own width means the pitch above is wrong again and the whole thing is lying on the
            // floor - which is exactly how this arrived the first time.
            Debug.Log($"[SceneBuilder] Bedlam rope: {bounds.size.x:0.##} x {bounds.size.z:0.##}m "
                    + $"enclosure, posts {bounds.size.y:0.##}m tall, centred on the cube.");
        }

        // ONE SURFACE BLOCKS ARE THROWN ONTO: a storey, where a block may land on it, and how many
        // land there. Written as data rather than as three code paths because the only thing that
        // differs between the ground, deck A and deck B is a height and a set of rectangles.
        private struct BedlamShelf
        {
            public string name;
            public float surfaceY;
            // Where a block's CENTRE may be, in the room's own XZ. Inset from the real edge by half a
            // block, so a 1.2m block thrown at the lip of a deck does not hang over it.
            public Rect[] spots;
            public Rect[] keepClear;
            public int blocks;
        }

        // Deck A is an L down the west wall and along the north; deck B is a rectangle over the
        // north-west quarter with a square cut out of it. Both are `PlanMezzanines`' shapes, inset -
        // stated again here rather than derived from that function, because what it returns is where
        // the SLAB is and this is where a metre of block may stand on it, which is not the same
        // rectangle and would need the inset applying to each piece anyway.
        private static BedlamShelf[] BedlamShelves(float roomWidth, float roomDepth, float mouthX,
                                                   Vector3 standAt)
        {
            float halfX = roomWidth / 2f, halfZ = roomDepth / 2f;
            // Half a block's longest footprint. Every piece is at most three cells across.
            float inset = 3f * BedlamCellSize / 2f + 0.15f;

            return new[]
            {
                // THE GROUND FLOOR, east of the corridor mouth and along the south wall. Everything
                // else down here - both lifts, all five columns, the beam's lane - is west or north.
                new BedlamShelf
                {
                    name = "ground floor",
                    surfaceY = 0f,
                    spots = new[]
                    {
                        Rect.MinMaxRect(mouthX + 1.6f, -halfZ + 1.3f, halfX - inset, -halfZ + 8.5f),
                    },
                    // The roped-off square. A block dropped inside the rope is one the player has to
                    // step over to reach the thing they are aiming at, and it would read as part of
                    // the exhibit rather than as something to fetch.
                    keepClear = new[]
                    {
                        Rect.MinMaxRect(standAt.x - 2.7f, standAt.z - 2.2f,
                                        standAt.x + 2.7f, standAt.z + 2.2f),
                    },
                    // FIVE, not four: the seed is thrown down here with them (see the note in
                    // `BuildBedlamCube`), and it is on the ground because that is where it ends up -
                    // `BedlamCube` lifts it onto the mat before the first frame.
                    blocks = 5,
                },

                // DECK A, one storey up: the west strip and the north strip of its L.
                new BedlamShelf
                {
                    name = "deck A",
                    surfaceY = DeckARows * GridCellHeight,
                    spots = new[]
                    {
                        Rect.MinMaxRect(-halfX + inset, -6.4f,
                                        -halfX + 2f * GridCellWidth - inset, 6.4f),
                        Rect.MinMaxRect(-halfX + inset, 7f + inset, halfX - inset, 10.5f - inset),
                    },
                    keepClear = new[]
                    {
                        // The hole deck A carries for lift B's column, and the lift panel that rests
                        // in it - see PlanMezzanines.
                        Rect.MinMaxRect(DeckBLiftX - 1.4f, DeckBVoidMaxZ - DeckBLiftPad / 2f - 1.4f,
                                        DeckBLiftX + 1.4f, DeckBVoidMaxZ - DeckBLiftPad / 2f + 1.4f),
                        // Column B1, which stands ON deck A and carries on up to deck B.
                        Rect.MinMaxRect(-3.4f, 8.6f, -1.1f, 11f),
                        // Where lift A arrives, so the ride does not end with a block underfoot.
                        Rect.MinMaxRect(DeckALiftX - 1.8f, DeckALiftZ - 1.8f,
                                        DeckALiftX + 1.8f, DeckALiftZ + 1.8f),
                    },
                    blocks = 4,
                },

                // DECK B, two storeys up: the rectangle minus the square hole you look down through.
                new BedlamShelf
                {
                    name = "deck B",
                    surfaceY = DeckBRows * GridCellHeight,
                    spots = new[]
                    {
                        Rect.MinMaxRect(-halfX + inset, 3.62f + inset,
                                        -GridCellWidth - inset, 10.5f - inset),
                    },
                    keepClear = new[]
                    {
                        // The void, and lift B's panel resting inside it. One rectangle: the panel is
                        // in the hole, so the hole plus a block's half-width covers both.
                        Rect.MinMaxRect(-3.5f * GridCellWidth - inset, 5.25f - inset,
                                        -2.5f * GridCellWidth + inset, 8.75f + inset),
                        // **THE LADDER'S MARK, AND THE SHAFT ABOVE IT.** Deck B's strip runs straight
                        // through the one spot on it that has a job, and nothing said so - so a block
                        // could be thrown onto the mark. That is not merely untidy: `LadderPlacer`
                        // ends in `PlayerLookup.InView`, whose ray to the mount ran along the deck
                        // and straight into the block, so **the ladder could not be installed at
                        // all** and the prompt never appeared to say why. One rectangle, both
                        // symptoms. (The prompt anchor has come off the deck as well - see
                        // `LadderMount.hintAnchor` - because the build is not the only thing that can
                        // leave an object standing here.)
                        Grow(LadderShaftHole(LadderShaftXZ.x, LadderShaftXZ.y), inset),
                        // **AND THE MARK, WHICH IS NO LONGER UNDER THE MOUTH.** The ladder leans, so
                        // its foot stands `LadderRun` out from the shaft - two separate squares to
                        // keep clear rather than one, and the foot is the one the prompt is on.
                        Grow(LadderShaftHole(LadderFootX, LadderShaftXZ.y), inset),
                    },
                    blocks = 4,
                },
            };
        }

        // A rectangle with a margin on every side. Written out because `Rect` has no such thing and
        // an exclusion always wants one: what has to be kept clear is the FIXTURE plus half a block,
        // or a block placed just outside it still overhangs.
        private static Rect Grow(Rect r, float by) =>
            Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);

        // WHERE ONE BLOCK IS THROWN on a given shelf. Best of thirty rather than reject-and-retry: a
        // retry loop either has to be allowed to fail - which puts two blocks inside each other - or
        // to run forever when the area is too small for the spacing asked of it. Taking the roomiest
        // of a fixed number of throws degrades instead, so crowding a deck simply tightens the spots.
        private static Vector3 BedlamScatterSpot(System.Random rng, BedlamShelf shelf,
                                                 System.Collections.Generic.List<Vector3> taken)
        {
            Vector3 best = Vector3.zero;
            float bestClearance = float.MinValue;

            // Weighted by AREA, so a shelf made of a long strip and a short one does not put half its
            // blocks on the short one. Computed per call rather than cached: three shelves, twelve
            // blocks, thirty throws each is nothing.
            float total = 0f;
            foreach (Rect r in shelf.spots) total += Mathf.Max(0f, r.width * r.height);

            for (int attempt = 0; attempt < 30; attempt++)
            {
                Rect area = shelf.spots[0];
                float pick = (float)rng.NextDouble() * total;
                foreach (Rect r in shelf.spots)
                {
                    float a = Mathf.Max(0f, r.width * r.height);
                    if (pick <= a) { area = r; break; }
                    pick -= a;
                }

                Vector2 flat = new Vector2(Mathf.Lerp(area.xMin, area.xMax, (float)rng.NextDouble()),
                                           Mathf.Lerp(area.yMin, area.yMax, (float)rng.NextDouble()));
                if (InAny(shelf.keepClear, flat)) continue;

                float clearance = float.MaxValue;
                foreach (Vector3 other in taken)
                    clearance = Mathf.Min(clearance, Vector2.Distance(flat, new Vector2(other.x, other.z)));

                if (clearance <= bestClearance) continue;
                bestClearance = clearance;
                best = new Vector3(flat.x, shelf.surfaceY, flat.y);
            }

            // Every throw landed in a keep-clear rectangle, which is a shelf whose spots and
            // exclusions have been written to cancel each other out. Reported rather than silently
            // stacking a block at the room's origin.
            if (bestClearance == float.MinValue)
                Debug.LogError($"[SceneBuilder] no clear spot on {shelf.name} for a Bedlam block - "
                             + "its keep-clear rectangles cover its whole area.");

            return best;
        }

        private static bool InAny(Rect[] rects, Vector2 point)
        {
            if (rects == null) return false;
            foreach (Rect r in rects) if (r.Contains(point)) return true;
            return false;
        }

        // What the finished cube shrinks INTO. A quarter of its own size, which is 0.4m across - the
        // scale of every other escape object in the game (cycle 1's three are 0.30) and small enough
        // that a console recess can be cut for it.
        private const float BedlamHeldScale = 0.25f;

        private static CarryableItem BuildHeldCube(Transform parent, GameObject source,
                                                   Bounds assembled, Sprite icon)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            instance.name = "HeldCube";
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * BedlamHeldScale;
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);

            // Centred on its own pivot, so the reveal can put it exactly where the assembly stood.
            Bounds box = MeasuredBounds(instance);
            instance.transform.position += instance.transform.position - box.center;
            box = MeasuredBounds(instance);

            BoxCollider trigger = instance.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = instance.transform.InverseTransformPoint(box.center);
            trigger.size = Abs(instance.transform.InverseTransformVector(Vector3.one * 1.4f));

            CarryableItem item = instance.AddComponent<CarryableItem>();
            item.itemId = CycleThreeKeyItemId;
            item.displayName = "CUBE";
            item.icon = icon;
            // **NOT THE CYCLE'S GREEN** (2026-08-31, by request). Every other escape object in the
            // game is one colour and its HUD tint is that colour; this one is three, and tinting a
            // white pictogram green named a colour the object does not have anywhere on it. Left
            // untinted, the icon reads as the drawing it is.
            item.iconTint = Color.white;
            item.floorY = box.size.y / 2f;
            item.handLocalPosition = HandPoseFor(box.size.x);
            // Off-axis on two axes, so what is in the hand reads as a CUBE - square-on it is a
            // square, which is the shape of the recess and not of the object.
            item.handLocalEuler = new Vector3(-22f, 26f, 0f);
            item.handLocalScale = instance.transform.lossyScale;
            item.audioSource = MakeSource(instance.transform, "PickupAudio", 1f, 0.85f);
            item.pickupClip = LoadClip(SfxDir, "sfx_item_pickup");

            Debug.Log($"[SceneBuilder] Bedlam held cube: {box.size.x:0.##}m against the assembly's "
                    + $"{assembled.size.x:0.##}m ({BedlamHeldScale:0.##} of it).");
            return item;
        }

        private static CarryableItem MakeBedlamBlock(Transform piece, Bounds bounds, int index,
                                                     Sprite icon)
        {
            // THE BLOCK PLUS AN ARM'S REACH, so it is taken from standing beside it rather than
            // from inside it. Triggers overlapping is fine and expected: `PlayerLookup.AimedAnchor`
            // gives the press to whichever one the player is actually looking at (CLAUDE.md §1.2).
            //
            // ABS on the size, and it is not paranoia: `InverseTransformVector` divides through the
            // transform, so under a parent the importer chose to mirror this comes back negative and
            // the trigger is built inside out. The chess set found that the hard way.
            BoxCollider trigger = piece.gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = piece.InverseTransformPoint(bounds.center);
            trigger.size = Abs(piece.InverseTransformVector(bounds.size)) + Vector3.one * 0.7f;

            // SOLID, ON A CHILD, since the blocks doubled in size. At 0.6m across they were scenery
            // to walk over like a chess piece; at 1.2m a block is a thing you go round, and one you
            // can walk through in a room built to be climbed reads as a hole in the floor plan.
            //
            // The child is load-bearing rather than tidiness: `CarryableItem` takes
            // `GetComponent<Collider>()` as its reach trigger, so a solid box on this same object
            // would be a coin toss over which of the two becomes the reach - the trap
            // `BuildKeyPlinth` documents. Handed over as `blocker`, so every path that puts the block
            // in a hand switches it off.
            GameObject solid = new GameObject("Blocker");
            solid.transform.SetParent(piece, false);
            BoxCollider block = solid.AddComponent<BoxCollider>();
            block.center = piece.InverseTransformPoint(bounds.center);
            block.size = Abs(piece.InverseTransformVector(bounds.size));

            CarryableItem item = piece.gameObject.AddComponent<CarryableItem>();
            item.blocker = block;
            item.itemId = $"Bedlam_{index:00}";
            item.displayName = "BLOCK";
            item.icon = icon;
            // Its own half-height. The mesh is centred on its pivot - the splitter put it there so
            // that a node's translation could be the whole of where the block goes - so this is what
            // stands a dropped block ON the floor rather than half through it. It is a height above
            // whatever `FallingItem.SurfaceUnder` finds, which is what lets a block be put down on a
            // deck five metres up rather than sinking to the one floor the cycle was told about.
            item.floorY = bounds.size.y / 2f;
            item.handLocalPosition = HandPoseFor(Mathf.Max(bounds.size.x, bounds.size.z));
            // Tipped toward the eye and turned off square. A polycube seen face-on is a rectangle,
            // and which rectangle it is is the only thing the player has to go on.
            item.handLocalEuler = new Vector3(-18f, 24f, 0f);
            item.ghostLocalEuler = Vector3.zero;
            // ITS OWN SIZE, which is what "held at true size" means with an unscaled hand anchor.
            // Written even where the parent is unscaled, because the SIGN is what keeps a mirrored
            // import mirrored and the importer is free to have made one.
            item.handLocalScale = piece.lossyScale;
            return item;
        }

        // THE ONE HAND-COPIED THING IN THIS PUZZLE, CHECKED. `BedlamSolvedCells` is printed by the
        // splitter and pasted into this file, and everything about which blocks may join which hangs
        // off it. These are every property of it that can be tested without knowing how the importer
        // turned the model:
        //
        //   - it describes 64 cells and names only blocks that exist,
        //   - the counts are the Bedlam Cube's own - twelve blocks of five cells and one of four,
        //   - the graph it makes is CONNECTED from the seed, or some block could never be clicked
        //     home no matter what order they were carried in, and
        //   - each block's cells span the same box its geometry does. That last one is what ties the
        //     table to THIS model rather than to any thirteen-piece packing, and it is compared as a
        //     multiset of extents so it holds whichever way the importer permuted the axes.
        private static void CheckBedlamSolution(BedlamCube cube, Bounds[] boxes, Bounds assembled,
                                                BedlamShelf[] shelves)
        {
            const int n = BedlamCells;

            foreach (int piece in BedlamSolvedCells)
                if (piece < 0 || piece >= BedlamPieceCount)
                {
                    Debug.LogError($"[SceneBuilder] BedlamSolvedCells names block {piece}, which does "
                                 + "not exist. Re-paste it from Tools/split_bedlam_cube.py.");
                    return;
                }

            if (BedlamSolvedCells.Length != n * n * n)
            {
                Debug.LogError($"[SceneBuilder] BedlamSolvedCells has {BedlamSolvedCells.Length} cells "
                             + $"where {n * n * n} are wanted.");
                return;
            }

            var counts = new int[BedlamPieceCount];
            var lo = new Vector3Int[BedlamPieceCount];
            var hi = new Vector3Int[BedlamPieceCount];
            for (int i = 0; i < BedlamPieceCount; i++)
            {
                lo[i] = new Vector3Int(n, n, n);
                hi[i] = new Vector3Int(-1, -1, -1);
            }

            for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                    for (int z = 0; z < n; z++)
                    {
                        int piece = BedlamSolvedCells[(x * n + y) * n + z];
                        counts[piece]++;
                        lo[piece] = Vector3Int.Min(lo[piece], new Vector3Int(x, y, z));
                        hi[piece] = Vector3Int.Max(hi[piece], new Vector3Int(x, y, z));
                    }

            int fours = 0, wrong = 0;
            for (int i = 0; i < BedlamPieceCount; i++)
            {
                if (counts[i] == 4) fours++;
                else if (counts[i] != 5) wrong++;

                // The cells' box against the geometry's, as sorted extents. The importer may name
                // the axes what it likes; it cannot change how long the block is. This is also what
                // catches the cell size and the model's scale drifting apart - re-run the splitter
                // with a different `--cell` and forget to move `BedlamCellSize`, and every one of
                // the thirteen fails at once.
                Vector3 fromCells = (Vector3)(hi[i] - lo[i] + Vector3Int.one) * BedlamCellSize;
                float[] a = { fromCells.x, fromCells.y, fromCells.z };
                float[] b = { boxes[i].size.x, boxes[i].size.y, boxes[i].size.z };
                System.Array.Sort(a);
                System.Array.Sort(b);
                for (int k = 0; k < 3; k++)
                    if (Mathf.Abs(a[k] - b[k]) > 0.02f)
                    {
                        Debug.LogError($"[SceneBuilder] Bedlam block {i} fills a "
                                     + $"{a[0]:0.##}x{a[1]:0.##}x{a[2]:0.##} box in BedlamSolvedCells "
                                     + $"and measures {b[0]:0.##}x{b[1]:0.##}x{b[2]:0.##}. The table "
                                     + "does not describe this model at this cell size.");
                        break;
                    }
            }

            if (fours != 1 || wrong != 0)
                Debug.LogError($"[SceneBuilder] BedlamSolvedCells has {fours} blocks of four cells and "
                             + $"{wrong} of neither four nor five - a Bedlam Cube is twelve fives and "
                             + "one four.");

            // CONNECTED FROM THE SEED, walked the way `BedlamCube.CanSeat` walks it. A packing whose
            // graph came apart leaves blocks that are refused forever, and the symptom in play is a
            // click that does nothing on a block the player has no way of seeing is stranded.
            var reached = new System.Collections.Generic.HashSet<int> { BedlamSeedPiece };
            var queue = new System.Collections.Generic.Queue<int>();
            queue.Enqueue(BedlamSeedPiece);
            while (queue.Count > 0)
            {
                int here = queue.Dequeue();
                for (int x = 0; x < n; x++)
                    for (int y = 0; y < n; y++)
                        for (int z = 0; z < n; z++)
                        {
                            if (BedlamSolvedCells[(x * n + y) * n + z] != here) continue;
                            foreach (Vector3Int step in BedlamSteps)
                            {
                                int nx = x + step.x, ny = y + step.y, nz = z + step.z;
                                if (nx < 0 || ny < 0 || nz < 0 || nx >= n || ny >= n || nz >= n) continue;
                                if (reached.Add(BedlamSolvedCells[(nx * n + ny) * n + nz]))
                                    queue.Enqueue(BedlamSolvedCells[(nx * n + ny) * n + nz]);
                            }
                        }
            }

            if (reached.Count != BedlamPieceCount)
                Debug.LogError($"[SceneBuilder] only {reached.Count} of {BedlamPieceCount} Bedlam blocks "
                             + $"can be reached from the seed (block {BedlamSeedPiece}) - the rest could "
                             + "never be put on.");

            string spread = string.Empty;
            foreach (BedlamShelf s in shelves) spread += $"{s.blocks} on {s.name}, ";

            Debug.Log($"[SceneBuilder] Bedlam cube at {cube.transform.position}: {BedlamPieceCount} "
                    + $"blocks, assembled {assembled.size.x:0.##}x{assembled.size.y:0.##}"
                    + $"x{assembled.size.z:0.##}m standing on the floor, seed block {BedlamSeedPiece} "
                    + $"lifted onto the mat at run time, {reached.Count} reachable from it, {spread}"
                    + "thirteen in all.");
        }

        private static readonly Vector3Int[] BedlamSteps =
        {
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
        };

        // AN L OF THREE SQUARES, which is the smallest drawing that reads as one of these blocks
        // rather than as a box. Deliberately flat and orthographic: the readout is 24 pixels of
        // silhouette, and an isometric cube at that size is a hexagon.
        private static Sprite BedlamBlockIcon()
        {
            var icon = new IconCanvas(128);
            const float half = 0.15f;
            icon.Bar(new Vector2(0.34f, 0.34f), new Vector2(half, half));
            icon.Bar(new Vector2(0.34f, 0.66f), new Vector2(half, half));
            icon.Bar(new Vector2(0.66f, 0.66f), new Vector2(half, half));
            return SaveSprite(icon, "icon_bedlam_block");
        }
    }
}
