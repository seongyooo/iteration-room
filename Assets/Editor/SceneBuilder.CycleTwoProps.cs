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
    // ROOM2EAST'S SYMBOLS AND CUBES, and the props cycle 2 stands on its furniture: the dresser,
    // the billiard balls, the pins and the nightstand. Every cycle's chest is ONE DRAWER THAT NEVER
    // SHUTS, and a drawer that holds anything must stay open-only (CLAUDE.md 4).
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {


        // THE SIX SYMBOLS, drawn once and used twice: as an opaque picture on a cube and on the
        // recess that wants it, and as a HUD glyph in the carried row. One drawing rather than two
        // means the thing on the wall and the thing in the readout can never drift apart.
        //
        // A CARD FAMILY, extended past the four suits with a star and a crescent. The spec named
        // three; six cubes need six, and picking the two extras from the same visual world - flat,
        // solid, symmetrical, no interior detail - is what keeps them reading as one set rather than
        // as four suits and two strays.
        private struct SymbolSpec
        {
            public string id;
            public string name;
            public System.Action<IconCanvas> draw;
        }

        private static SymbolSpec[] CubeSymbols() => new[]
        {
            new SymbolSpec { id = "Cube_Spade", name = "spade", draw = icon =>
            {
                // The blade: a triangle standing on two lobes.
                icon.Shape(pt =>
                {
                    if (pt.y < 0.42f || pt.y > 0.90f) return false;
                    float k = (0.90f - pt.y) / 0.48f;
                    return Mathf.Abs(pt.x - 0.5f) <= 0.40f * k;
                });
                icon.Disc(new Vector2(0.30f, 0.44f), 0.20f);
                icon.Disc(new Vector2(0.70f, 0.44f), 0.20f);
                // The stem, wide at the foot and narrow where it meets the blade.
                icon.Shape(pt =>
                {
                    if (pt.y < 0.10f || pt.y > 0.44f) return false;
                    float k = (pt.y - 0.10f) / 0.34f;
                    return Mathf.Abs(pt.x - 0.5f) <= Mathf.Lerp(0.17f, 0.045f, k);
                });
            }},
            new SymbolSpec { id = "Cube_Heart", name = "heart", draw = icon =>
            {
                // The textbook parametric heart curve - the one every graphing calculator and
                // Valentine's card uses - traced as a polygon and filled by point-in-polygon, rather
                // than two discs butted against a triangle. Discs-and-a-wedge always read as exactly
                // that up close: two circles with a corner where the straight edges meet them. This
                // is one closed curve with no seam anywhere on it.
                const int n = 240;
                Vector2[] poly = new Vector2[n];
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    float t = i * Mathf.PI * 2f / n;
                    float sx = 16f * Mathf.Pow(Mathf.Sin(t), 3f);
                    float sy = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);
                    poly[i] = new Vector2(sx, sy);
                    minX = Mathf.Min(minX, sx); maxX = Mathf.Max(maxX, sx);
                    minY = Mathf.Min(minY, sy); maxY = Mathf.Max(maxY, sy);
                }
                float span = Mathf.Max(maxX - minX, maxY - minY);
                float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
                const float fill = 0.74f; // how much of the 0..1 canvas the curve's own box fills
                for (int i = 0; i < n; i++)
                    poly[i] = new Vector2(0.5f + (poly[i].x - cx) / span * fill,
                                          0.5f + (poly[i].y - cy) / span * fill);

                icon.Shape(p =>
                {
                    bool inside = false;
                    for (int i = 0, j = n - 1; i < n; j = i++)
                    {
                        Vector2 a = poly[i], b = poly[j];
                        if ((a.y > p.y) != (b.y > p.y)
                            && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                            inside = !inside;
                    }
                    return inside;
                });
            }},
            new SymbolSpec { id = "Cube_Clover", name = "clover", draw = icon =>
            {
                // A four-petal rose - r = cos(2*theta), rotated 45 degrees so the petals fall where
                // the old diagonal 2x2 discs did - instead of four circles and a hub disc filling the
                // gap between them. The rose passes through its own centre on all four petals at
                // once, so it needs no separate hub: the seam the old version had at every join
                // between a leaf-disc and the hub is simply not a feature of this curve.
                const float radius = 0.40f;
                Vector2 centre = new Vector2(0.5f, 0.58f);
                icon.Shape(p =>
                {
                    Vector2 d = p - centre;
                    float r = d.magnitude;
                    if (r > radius) return false;
                    float theta = Mathf.Atan2(d.y, d.x) - Mathf.PI / 4f;
                    return r <= radius * Mathf.Abs(Mathf.Cos(2f * theta));
                });
                icon.Bar(new Vector2(0.5f, 0.155f), new Vector2(0.036f, 0.125f));
            }},
            new SymbolSpec { id = "Cube_Diamond", name = "diamond", draw = icon =>
                icon.Shape(pt => Mathf.Abs(pt.x - 0.5f) / 0.34f + Mathf.Abs(pt.y - 0.5f) / 0.44f <= 1f)
            },
            new SymbolSpec { id = "Cube_Star", name = "star", draw = icon =>
            {
                // Five points, and the EDGES HAVE TO BE STRAIGHT. Lerping the radius between the tip
                // and the valley across each sector is the obvious version and it draws a FLOWER -
                // the valleys come out round and the points blunt, because a star is not linear in
                // angle. What it is instead is a straight segment from tip to valley, so the test is
                // which side of that segment the sample falls on.
                const float sector = Mathf.PI * 2f / 5f;
                const float outer = 0.47f, inner = 0.20f;
                Vector2 tip = new Vector2(0f, outer);
                Vector2 valley = new Vector2(inner * Mathf.Sin(sector * 0.5f), inner * Mathf.Cos(sector * 0.5f));
                Vector2 edge = valley - tip;
                // Which side of that edge the centre is on. Everything inside the star is on it too.
                float centreSide = edge.x * -tip.y - edge.y * -tip.x;
                icon.Shape(pt =>
                {
                    Vector2 d = pt - new Vector2(0.5f, 0.5f);
                    float r = d.magnitude;
                    if (r > outer) return false;
                    if (r < 0.0001f) return true;
                    // Folded into one half-sector, so one segment describes all ten edges.
                    float angle = Mathf.Atan2(d.x, d.y);
                    float t = Mathf.Abs(Mathf.Repeat(angle + sector * 0.5f, sector) - sector * 0.5f);
                    Vector2 f = new Vector2(r * Mathf.Sin(t), r * Mathf.Cos(t));
                    float side = edge.x * (f.y - tip.y) - edge.y * (f.x - tip.x);
                    return side * centreSide > 0f;
                });
            }},
            new SymbolSpec { id = "Cube_Moon", name = "moon", draw = icon =>
            {
                icon.Disc(new Vector2(0.44f, 0.5f), 0.40f);
                // sign -1 cuts the bite out, which is the whole shape.
                icon.Disc(new Vector2(0.62f, 0.56f), 0.355f, -1f);
            }},
        };

        // Room2East, behind the blue door: six cubes on the floor, six recesses in the walls and the
        // floor, and a blue sphere on a plinth when every cube is home.
        //
        // THREE ON THE FLOOR, ONE WEST, TWO EAST, by request (2026-08-13) - the room used to put two
        // on every wall that had no doorway. West keeps one recess on its own mid-line, which a
        // doorway can never reach (a doorway is always cut into a Z wall); east keeps two, spaced
        // clear of both its doorways; the remaining three sit on the floor, clear of the walls, the
        // doorways and the plinth in the room's own middle. The recesses find the player's hand
        // themselves, the way every CarryableItem already does - this room is built before the player
        // is, and reordering the build to hand one in would put the player's construction behind a
        // room that does not need it.
        //
        // THE CUBES ARE GLASS, in a silver frame, with the symbol suspended INSIDE - all by request.
        // Glass rather than the wall/floor plates' own opaque material because the ask was specifically
        // for the cube, not the recess it goes home to: the recess stays the flat matte ink the room's
        // "no legend on the wall" rule already asks of it, and the cube is the one thing in the room
        // that gets to look precious. Every cube shares ONE glass tint - see MakeGlassMaterial - for
        // the same reason the plates share one ink colour: SYMBOLS, NOT COLOURS is the room's whole
        // design rule, and a cube tinted by its own answer would undo that as surely as a lit plate did.
        private static (CarryableItem[] cubes, CubeRoom room) BuildCubeRoom(
            Transform parent, Vector3 roomCentre, Material propMat)
        {
            const float cubeSize = 1.0f;    // 3/4 again, by request - everything else follows it
            const float slotY = 2.5f;       // a wide frame at the old chest-height centre clips the floor
            const float alongWall = 3.3f;   // clear of the frame beside it and of the doorway past it

            GameObject root = new GameObject("CubeRoom");
            root.transform.SetParent(parent, false);

            // The reward first, because the scatter has to keep clear of it.
            (Transform plinth, Transform seat, CarryableItem sphere) = BuildKeyPlinth(
                parent, "SpherePlinth", roomCentre, propMat,
                SlotShape.Round, new Color(0.16f, 0.40f, 0.95f), "KeySphereMat",
                SphereKeyItemId, "SPHERE", SphereIcon());

            RewardPlinth rise = plinth.gameObject.AddComponent<RewardPlinth>();
            rise.plinth = plinth;
            rise.key = sphere;
            rise.keySeat = seat;
            rise.riseHeight = 1.25f;

            // pos/rot/floor for all six recesses. A floor mount's rotation (-90 about X) is the same
            // one the chess board's own reward plinth was verified against: it carries local +Z onto
            // world +Y, so "the seat sits cubeSize*0.30 out along local +Z" - the wall recesses' own
            // rule, below - means exactly "up off the floor" here without a second formula.
            float halfX = RoomWidth / 2f;
            var mounts = new (Vector3 pos, Quaternion rot, bool floor)[]
            {
                (new Vector3(roomCentre.x - halfX, slotY, roomCentre.z), Quaternion.Euler(0f, 90f, 0f), false),
                (new Vector3(roomCentre.x + halfX, slotY, roomCentre.z - alongWall), Quaternion.Euler(0f, -90f, 0f), false),
                (new Vector3(roomCentre.x + halfX, slotY, roomCentre.z + alongWall), Quaternion.Euler(0f, -90f, 0f), false),
                (new Vector3(roomCentre.x - 2.2f, 0f, roomCentre.z - 3.0f), Quaternion.Euler(-90f, 0f, 0f), true),
                (new Vector3(roomCentre.x + 2.4f, 0f, roomCentre.z + 1.8f), Quaternion.Euler(-90f, 0f, 0f), true),
                (new Vector3(roomCentre.x - 2.2f, 0f, roomCentre.z + 3.0f), Quaternion.Euler(-90f, 0f, 0f), true),
            };

            SymbolSpec[] symbols = CubeSymbols();
            var cubes = new CarryableItem[symbols.Length];
            var slots = new SymbolSlot[symbols.Length];

            // ONE body material for all six, built once, because the body is the same clear glass on
            // every cube now - what differs is the core inside it. It used to be six materials that
            // differed only by the glyph printed on their faces.
            Material glassMat = MakeGlassMaterial("GlassBody", null,
                                                  new Color(0.92f, 0.96f, 1.00f, 0.28f), 0.97f);

            // Deterministic, like the chess scatter and for the same reason: a scene is build output,
            // so two builds of one commit have to lay the room out identically.
            System.Random rng = new System.Random(CubeScatterSeed);

            // FOUR FIXED FLOOR PLACES, JITTERED - not the chess room's rejection sampler, which this
            // room used until 2026-08-13 and which put the cubes in a huddle. The sampler is right
            // for a room whose only obstacle is a board in the middle; this one has a plinth in its
            // centre, THREE of its six recesses on the floor, and a doorway at each end, and every
            // one of those is a keep-out the sampler has to satisfy at once. What is left is a thin
            // ring, and 400 attempts at "far enough from everything" inside a thin ring either fails
            // into the fallback - which returns a point that satisfies NOTHING - or lands them all in
            // whichever lobe of it the seed happened to like.
            //
            // Written out, the room is laid out on purpose and cannot huddle. The rng still decides
            // the jitter and every cube's yaw, so it does not read as a grid, and the build stays
            // reproducible. Offsets are from the room's own centre: X is the short axis (8.75), Z the
            // long one (10.5), the plinth is at 0,0 and the floor recesses at (-2.2,-3.0), (2.4,1.8)
            // and (-2.2,3.0) - every number below is placed against those.
            var places = new[]
            {
                new Vector2( 2.75f, -1.35f),   // east, opposite the two west recesses
                new Vector2( 1.85f, -3.45f),   // south-east, clear of the entrance's swing
                new Vector2(-2.70f,  0.40f),   // west, between the two west recesses
                new Vector2( 0.10f,  3.45f),   // north, past the far side of the plinth
            };
            // WHICH CUBE STANDS WHERE, and how high. Index into `places`, and a level: 1 means this
            // cube is stacked on the one before it, which by construction names the same place. Two
            // towers of two rather than one of three - a metre cube three high is taller than the
            // player and reads as scenery to walk round rather than a thing to take apart. Placed at
            // opposite ends of the room so the shape is legible from wherever you come in.
            var stand = new (int place, int level)[]
            {
                (0, 0), (0, 1),   // tower, east
                (1, 0),
                (2, 0), (2, 1),   // tower, west
                (3, 0),
            };

            // One jitter per PLACE, not per cube: a stacked cube has to sit on the one below it, and
            // it can only do that if both were told the same place to be.
            var floorSpot = new Vector2[places.Length];
            for (int p = 0; p < places.Length; p++)
                floorSpot[p] = places[p] + new Vector2(((float)rng.NextDouble() * 2f - 1f) * 0.22f,
                                                       ((float)rng.NextDouble() * 2f - 1f) * 0.22f);

            // One yaw per PLACE as well, and a stacked cube takes its own small offset off it rather
            // than a fresh angle: a cube balanced across the corners of the one below it would be a
            // physics claim this project cannot make good on, and a few degrees out reads as stacked
            // by hand where a clean 0 reads as a shop display.
            var baseYaw = new float[places.Length];
            for (int p = 0; p < places.Length; p++) baseYaw[p] = (float)rng.NextDouble() * 360f;

            for (int i = 0; i < symbols.Length; i++)
            {
                SymbolSpec spec = symbols[i];

                // A thin ring behind the glyph on every WORLD-facing copy - the plate and the cube -
                // so each reads as a struck medallion rather than bare ink. Left off the HUD glyph
                // below: that one is 58px in a fixed-width row, where a second stroke competing with
                // the symbol is clutter rather than polish.
                System.Action<IconCanvas> withRing = icon =>
                {
                    icon.Ring(new Vector2(0.5f, 0.5f), 0.475f, 0.430f);
                    spec.draw(icon);
                };

                var paper = new IconCanvas(128);
                withRing(paper);
                Material plateMat = MakeSymbolMaterial("Symbol_" + spec.name,
                                                       SaveSymbolTexture(paper, "symbol_" + spec.name));

                // The same drawing again, INVERTED, for the core suspended inside the glass. The wall
                // plate is ink on paper because it is a printed sign; the core is read through a
                // tinted body in a white room, where a pale face is a white card inside a white cube
                // against a white wall. Its own texture rather than the plate's, so "the sign is
                // dark-on-light and the object is light-on-dark" is a fact about this room's look
                // rather than something a shared file forces on both.
                var core = new IconCanvas(128);
                withRing(core);
                Material coreMat = MakeSymbolMaterial("Inclusion_" + spec.name,
                    SaveSymbolTexture(core, "symbol_core_" + spec.name,
                                      new Color(0.06f, 0.06f, 0.08f), new Color(0.93f, 0.95f, 1.00f)));

                var glyph = new IconCanvas(128);
                spec.draw(glyph);
                Sprite hudIcon = SaveSprite(glyph, "icon_symbol_" + spec.name);

                (int place, int level) = stand[i];
                slots[i] = BuildSymbolSlot(root.transform, spec, plateMat, mounts[i].pos, mounts[i].rot,
                                           cubeSize, mounts[i].floor);
                cubes[i] = BuildSymbolCube(root.transform, spec, glassMat, coreMat, hudIcon, cubeSize,
                    new Vector3(roomCentre.x + floorSpot[place].x, 0f, roomCentre.z + floorSpot[place].y),
                    baseYaw[place] + (level == 0 ? 0f : ((float)rng.NextDouble() * 2f - 1f) * 13f),
                    level);

                // Take the bottom of a tower and the top of it falls. `stand` puts a stacked cube
                // directly after the one it stands on, at the same place, so the support is always
                // the cube built one step earlier - there is no lookup to get wrong.
                if (level > 0) StandOn(cubes[i], cubes[i - 1]);

                slots[i].acceptedItemId = spec.id;
            }

            CubeRoom room = root.AddComponent<CubeRoom>();
            room.cubes = cubes;
            room.slots = slots;
            room.reward = rise;
            foreach (SymbolSlot slot in slots) slot.room = room;

            // The closest pair is the one number that says whether the room reads as scattered or as
            // a huddle, and it is worth printing rather than eyeballing: measured in XZ only, so a
            // tower does not report itself as two cubes on top of each other.
            float closest = float.MaxValue;
            for (int a = 0; a < cubes.Length; a++)
                for (int b = a + 1; b < cubes.Length; b++)
                {
                    Vector3 d = cubes[a].transform.position - cubes[b].transform.position;
                    float flat = new Vector2(d.x, d.z).magnitude;
                    if (flat > 0.01f) closest = Mathf.Min(closest, flat);
                }

            int stacked = 0;
            foreach (var s in stand) if (s.level > 0) stacked++;

            Debug.Log($"[SceneBuilder] Cube room: {cubes.Length} cubes ({stacked} stacked), "
                    + $"{slots.Length} recesses, closest pair {closest:F2}m apart (cube is {cubeSize}m), "
                    + $"plinth at {plinth.position}");
            return (cubes, room);
        }

        // One recess: a symbol plate in a dark frame, standing proud of its mount, with a seat in
        // front of it for the cube. Local +Z is the recess's own outward normal - into the room for a
        // wall mount, straight up for a floor mount (its rotation carries local +Z onto world +Y) - so
        // the frame, plate and seat are built once here and never need to know which kind they are.
        // Only the reach trigger differs between the two, because "stand in front of it" and "stand
        // over it" are different shapes.
        private static SymbolSlot BuildSymbolSlot(Transform parent, SymbolSpec spec, Material faceMat,
                                                  Vector3 position, Quaternion rotation, float cubeSize,
                                                  bool floorMounted)
        {
            GameObject root = new GameObject("Slot_" + spec.name);
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            root.transform.localRotation = rotation;

            // A REAL HOLLOW, not a dark rectangle pretending to be one. It used to be a flat frame
            // with the glyph panel laid on top of it, on the reasoning that there is no CSG here so a
            // cavity has to be near-black inside a lighter border - true, but only ever convincing
            // head-on. A raised RIM makes the same hole out of geometry: four bars standing proud of
            // the wall with the glyph panel at the bottom of the well between them, so the depth is
            // something the light and the viewing angle agree about instead of a trick of tone.
            //
            // The wall cannot be cut into, which is what decides the direction. Everything at local
            // -Z is behind an opaque wall and simply invisible, so the well is built OUT of the
            // surface. `lip` is therefore both how far the fixture protrudes and how deep the recess
            // reads - shallow, because a deep box at head height on a corridor wall stops being a
            // recess and becomes a shelf.
            float mouth = cubeSize + 0.10f;    // the opening: 5cm of clearance round the cube
            const float lip = 0.16f;           // how far the rim stands proud = how deep it reads
            const float rimBand = 0.20f;       // the width of the rim itself
            float frameSize = mouth + 2f * rimBand;

            Material rimMat = MakeColorMaterial("SymbolSlotFrame", new Color(0.10f, 0.10f, 0.12f));

            // Four bars, mitred the way BuildCubeEdging mitres its own and for the same reason: bars
            // cut to one length overlap at the corners with coplanar faces and flicker. The
            // horizontals run the full width and take the corners; the verticals stop against them.
            var rim = new Renderer[4];
            float band = (mouth + rimBand) / 2f;
            rim[0] = Prim(PrimitiveType.Cube, "RimTop", root.transform,
                new Vector3(0f, band, lip / 2f), new Vector3(frameSize, rimBand, lip),
                rimMat, removeCollider: true).GetComponent<Renderer>();
            rim[1] = Prim(PrimitiveType.Cube, "RimBottom", root.transform,
                new Vector3(0f, -band, lip / 2f), new Vector3(frameSize, rimBand, lip),
                rimMat, removeCollider: true).GetComponent<Renderer>();
            rim[2] = Prim(PrimitiveType.Cube, "RimLeft", root.transform,
                new Vector3(-band, 0f, lip / 2f), new Vector3(rimBand, mouth, lip),
                rimMat, removeCollider: true).GetComponent<Renderer>();
            rim[3] = Prim(PrimitiveType.Cube, "RimRight", root.transform,
                new Vector3(band, 0f, lip / 2f), new Vector3(rimBand, mouth, lip),
                rimMat, removeCollider: true).GetComponent<Renderer>();

            // The floor of the well, carrying the glyph. A cube rather than a quad: every face of a
            // Unity cube takes the whole texture, so the one facing the room shows the glyph the
            // right way round with nothing to configure. Sized to the mouth, so the well has no
            // visible seam between its walls and its back.
            Prim(PrimitiveType.Cube, "Plate", root.transform,
                new Vector3(0f, 0f, 0.015f), new Vector3(mouth, mouth, 0.03f), faceMat,
                removeCollider: true);

            // Where the cube ends up. Deep enough that it is INSIDE the well rather than resting on
            // its lip - a third of the cube is past the rim, and the back of it is inside the wall,
            // which is the one thing this project can do that a real hollow cannot: the wall is the
            // occluder, so the cube looks buried rather than clipped. It also covers its own symbol
            // once it is home, which is the correct thing for it to do - that pairing has been
            // answered by the time anything is placed.
            GameObject seat = new GameObject("Seat");
            seat.transform.SetParent(root.transform, false);
            seat.transform.localPosition = new Vector3(0f, 0f, cubeSize * 0.30f);

            // A wall recess is stood IN FRONT OF; a floor recess is stood OVER. The box below is
            // sized for whichever this is - wide on the two axes now horizontal, tall enough on the
            // third to catch a standing player regardless of exactly how close they walk up.
            BoxCollider reach = root.AddComponent<BoxCollider>();
            reach.isTrigger = true;
            if (floorMounted)
            {
                reach.center = new Vector3(0f, 0f, cubeSize * 0.6f);
                reach.size = new Vector3(3.4f, 3.4f, 3.0f);
            }
            else
            {
                reach.center = new Vector3(0f, -1.5f, cubeSize * 0.6f);
                reach.size = new Vector3(3.4f, 3.5f, 3.0f);
            }

            SymbolSlot slot = root.AddComponent<SymbolSlot>();
            slot.seat = seat.transform;
            slot.plateRenderers = rim;
            slot.audioSource = MakeSource(root.transform, "SlotAudio", 1f, 0.8f);
            slot.insertClip = LoadClip(SfxDir, "sfx_floor_button_press");

            // Presented clear of the mouth before it slides home: the cube is 1m and sits with its
            // centre cubeSize*0.30 inside the seat, so it has to start far enough out that no part of
            // it begins already through the rim. The tilt is small and deliberately not symmetrical -
            // a cube that straightens up as it goes in reads as placed by a hand, where one that
            // arrives square reads as a machine feeding it.
            slot.insertOffer = new Vector3(0f, 0f, cubeSize * 0.55f + lip);
            slot.insertTilt = new Vector3(-7f, 13f, 4f);
            slot.insertDuration = 0.45f;
            return slot;
        }

        // One cube, where the room was disturbed - on the floor, or on another cube. Everything here
        // is the chess piece over again - trigger, CarryableItem, hand pose - except that what
        // identifies it is suspended inside it, and its body is glass in a steel frame rather than
        // the recess's opaque ink (see BuildCubeRoom).
        //
        // `floorY` stays half a cube whatever `level` says, and that is right rather than an
        // oversight: a stack is how the room was FOUND, not a property of the cube. Take the top one
        // and put it down and it belongs on the floor; only the loop's own ReturnToOrigin rebuilds
        // the tower, which is exactly the rewind it is there to do.
        private static CarryableItem BuildSymbolCube(Transform parent, SymbolSpec spec, Material glassMat,
                                                     Material coreMat, Sprite icon, float cubeSize,
                                                     Vector3 spot, float yaw, int level)
        {
            // World metres of reach past a face, NOT a multiple of the cube: the reach an arm has is
            // the same whatever size the thing is, so both numbers below are divided back out by
            // cubeSize rather than written as a ratio that silently changes meaning every time the
            // cube is resized. Taller than it is wide because the trigger has to overlap a STANDING
            // player's capsule, whose bounds start well above a knee-high cube's lid.
            const float reachOut = 0.45f;
            const float reachUp = 0.60f;
            // World thickness of one edge of the silver frame, same reasoning.
            const float edge = 0.055f;

            // `level` 0 stands on the floor, 1 stands on the cube below it. Stacked by a WHOLE EDGE
            // THICKNESS more than the cube's own height, so the upper cube's bottom bars come to rest
            // ON the lower cube's top bars: each bar straddles its edge, so stacking by exactly
            // cubeSize would put the two frames through each other with coplanar faces - the same
            // flicker BuildCubeEdging mitres its own corners to avoid - and the two glass faces in
            // the same plane on top of it.
            GameObject cube = Prim(PrimitiveType.Cube, spec.id, parent,
                new Vector3(spot.x, cubeSize / 2f + level * (cubeSize + edge), spot.z),
                Vector3.one * cubeSize, glassMat, removeCollider: true);
            // Turned where it fell, about its own vertical only: a cube tipped onto a corner would
            // read as physics that has run, and nothing here simulates.
            cube.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            BuildCubeEdging(cube.transform, edge / cubeSize);

            // THE SYMBOL, SUSPENDED INSIDE THE GLASS rather than printed on its faces - by request,
            // 2026-08-13. A solid core rather than the glyph alone floating in the middle: the glyph
            // alone means an alpha-clipped shell whose near and far faces are both visible through
            // each other, which reads as two symbols rather than one seen in depth. Opaque, so the
            // near face hides the far one and there is exactly one mark to read from any direction -
            // including from ABOVE, which a flat wafer could not answer and which matters in a room
            // whose cubes are on the floor and stacked. Opaque also settles the sort order for free:
            // it draws in the opaque queue with depth, and the glass blends over it afterwards.
            Prim(PrimitiveType.Cube, "Core", cube.transform, Vector3.zero, Vector3.one * 0.58f,
                 coreMat, removeCollider: true);

            BoxCollider trigger = cube.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(1f + 2f * reachOut / cubeSize,
                                       1f + 2f * reachUp / cubeSize,
                                       1f + 2f * reachOut / cubeSize);

            // SOLID, so a metre-plus of glass is walked around rather than through. On a CHILD, and
            // that is load-bearing rather than tidiness: CarryableItem takes `GetComponent<Collider>()`
            // as its reach trigger, so a solid box added to this same object would be a coin toss over
            // which of the two becomes the reach - the trap BuildKeyPlinth documents. Handed to the
            // item as `blocker` so every path that puts the cube in a hand can switch it off; see
            // CarryableItem.blocker for why holding a solid box would be a bug in two directions.
            //
            // Sized to the glass itself, not to the frame that stands proud of it by half an edge:
            // the frame is a detail you can put a shoulder through without noticing, and matching the
            // collider to the silhouette a player reads as "the cube" is what makes it feel solid.
            GameObject solid = new GameObject("Blocker");
            solid.transform.SetParent(cube.transform, false);
            BoxCollider block = solid.AddComponent<BoxCollider>();

            CarryableItem item = cube.AddComponent<CarryableItem>();
            item.blocker = block;
            item.itemId = spec.id;
            item.displayName = spec.name.ToUpperInvariant();
            item.icon = icon;
            item.floorY = cubeSize / 2f;
            item.handLocalPosition = HandPoseFor(cubeSize);
            // Off-axis on two axes, so what is in the hand is seen as a CUBE - square-on it is a
            // square, which is one of the six symbols and would read as a mistake.
            item.handLocalEuler = new Vector3(-20f, 28f, 0f);
            // ITS OWN SIZE. It used to be held at 0.20 - the size the cube was before it grew - so a
            // metre of glass would not fill the screen. It is a metre of glass, and it is carried
            // like one now: further out, lower, and genuinely in the way. That is the trade, taken
            // deliberately (2026-08-13). HandPoseFor is where it is tuned.
            item.handLocalScale = Vector3.one * cubeSize;
            return item;
        }

        // `upper` rests on `lower` and drops to the floor when it stops doing so. The values live
        // here and the mechanism lives in FallingItem, like every other pairing in this file.
        //
        // The landing borrows the floor pads' clunk, pitched well down. It is the only impact in the
        // library and a metre of glass hitting a floor in silence is worse than a borrowed sound -
        // see docs/audio.md on the pads themselves.
        private static void StandOn(CarryableItem upper, CarryableItem lower)
        {
            FallingItem stack = AddFalling(upper);
            stack.support = lower;
        }

        // WHERE A HELD OBJECT SITS, from how big it is. Held objects are their TRUE SIZE now - the
        // shrink-to-fit that every carryable used to carry is gone - so a big one has to be held
        // further out and lower, the way a person carries a box rather than a key.
        //
        // Grows SUB-LINEARLY with size on purpose. Scaling the distance in step with the object
        // would put everything at the same angular size, which is exactly what the old shrink did by
        // another route: a metre of glass would look like the 20cm prop it used to be. These
        // coefficients keep a small object at roughly the pose it always had and push a large one out
        // far enough to be seen at all, while still reading as large.
        //
        // `size` is the object's world size across, and the caller knows it because it built it.
        private static Vector3 HandPoseFor(float size)
        {
            return new Vector3(0.28f + size * 0.20f,
                              -(0.22f + size * 0.40f),
                               0.45f + size * 0.55f);
        }

        // Everything that can be carried can be DROPPED, and a dropped object falls. One component
        // per carryable, added here so a new carryable anywhere in the building gets it without its
        // author having to know - the same reason the reflection-probe sweep walks the scene rather
        // than trusting every room to opt in.
        //
        // Idempotent: the cube room's towers wire their own support before this runs, and get it
        // back rather than a second copy.
        private static FallingItem AddFalling(CarryableItem item)
        {
            FallingItem fall = item.GetComponent<FallingItem>();
            if (fall != null) return fall;

            fall = item.gameObject.AddComponent<FallingItem>();
            fall.item = item;
            // Its own clip, and it had to be: the landing borrowed the floor pads' clunk pitched
            // down, and that clip is a struck C6 left to ring for a second, so a dropped object
            // announced itself like a doorbell. Pitching a pitched sound down does not stop it being
            // pitched. `sfx_item_drop` is 85ms with its energy under 500Hz - see Tools/generate_sfx.
            fall.audioSource = MakeSource(item.transform, "FallAudio", 1f, 0.85f);
            // A fixed offset PER OBJECT rather than a random one per landing: two cubes off one
            // tower must not land in unison, and the build has to be reproducible. Derived from the
            // name, so an object's own thud is the same weight every iteration - which is a thing
            // the player can learn, where a per-landing shuffle is just noise.
            fall.audioSource.pitch = 0.94f + (Mathf.Abs(item.name.GetHashCode()) % 13) * 0.01f;
            fall.landClip = LoadClip(SfxDir, "sfx_item_drop");
            return fall;
        }

        // TELLS EVERY CARRYABLE UNDER `root` WHICH STOREY IT IS ON. One call per cycle subtree,
        // rather than a line in every builder that makes a carryable.
        //
        // `CarryableItem.floorY` is the object's own half-thickness above ITS OWN floor, and that
        // number is genuinely the object's business. Which floor is not - it is a property of where
        // the thing was placed, so it is set here, in one sweep, at the point the storey is already
        // known. Asking each builder to remember to add it is asking for exactly one of them to
        // forget, and the failure mode is silent: the object falls through the floor to the height of
        // the storey above and hangs there.
        //
        // The ground storey needs no call at all - `floorBaseY` defaults to zero, which is why every
        // value in this file could be written as a bare half-height for as long as there was one
        // level, and why none of them had to change when there stopped being.
        private static int SetFloorBase(Transform root, float floorY)
        {
            if (root == null) return 0;

            CarryableItem[] items = root.GetComponentsInChildren<CarryableItem>(includeInactive: true);
            foreach (CarryableItem item in items) item.floorBaseY = floorY;

            Debug.Log($"[SceneBuilder] Floor base {floorY:0.###} set on {items.Length} carryables under {root.name}");
            return items.Length;
        }

        private static int AddFallingToEveryCarryable()
        {
            int added = 0;
            CarryableItem[] items = UnityEngine.Object.FindObjectsByType<CarryableItem>(
                FindObjectsInactive.Include);

            foreach (CarryableItem item in items)
                if (item.GetComponent<FallingItem>() == null) { AddFalling(item); added++; }

            Debug.Log($"[SceneBuilder] Falling: {items.Length} carryables, {added} newly wired");
            return added;
        }

        // The silver frame: the twelve edges of the cube, in polished steel, on a glass body. Glass
        // alone has no silhouette of its own in a white room - it is only what it reflects, so from
        // the wrong angle a clear cube is a faint smudge on a white floor and the symbol on it looks
        // like it is floating. The frame is what draws the SHAPE, and it does it in the one material
        // this project can already light convincingly (see MakePolishedMetalMaterial and SS3 on why a
        // metal here is entirely its reflections).
        //
        // `t` is the edge thickness in the cube's own LOCAL units, so a caller expressing it in metres
        // divides by the cube's size once and the frame stays in proportion at any size - including in
        // the hand, where the whole object is rescaled to 0.20.
        //
        // The bars are MITRED rather than all cut to one length, and the reason is z-fighting rather
        // than fussiness: twelve equal bars overlap inside each corner with coplanar side faces, which
        // flicker. So one axis takes the corners outright - X, run the full width and half an edge
        // proud at each end - and the other eight stop exactly where it starts. Faces then touch
        // without ever overlapping.
        private static void BuildCubeEdging(Transform cube, float t)
        {
            Material steel = MakePolishedMetalMaterial(
                "CubeEdgeSteel", new Color(0.88f, 0.89f, 0.93f), 0f);

            // Every bar sits astride its edge - half of it inside the glass, half proud - so the frame
            // catches a highlight along its length instead of lying flush and vanishing into the face
            // it is drawn on.
            var lengths = new[]
            {
                new Vector3(1f + t, t, t),
                new Vector3(t, 1f - t, t),
                new Vector3(t, t, 1f - t),
            };

            for (int axis = 0; axis < 3; axis++)
            {
                for (int corner = 0; corner < 4; corner++)
                {
                    float a = (corner & 1) == 0 ? -0.5f : 0.5f;
                    float b = (corner & 2) == 0 ? -0.5f : 0.5f;
                    Vector3 pos = axis == 0 ? new Vector3(0f, a, b)
                                : axis == 1 ? new Vector3(a, 0f, b)
                                            : new Vector3(a, b, 0f);

                    Prim(PrimitiveType.Cube, $"Edge{axis}{corner}", cube, pos, lengths[axis], steel,
                         removeCollider: true);
                }
            }
        }

        // A filled disc and a filled square, for the two escape objects whose shape IS their name.
        // Deliberately plain: the row they appear in already says which is which by tint, and an
        // outline at 58px closes up under the filter.
        private static Sprite SphereIcon()
        {
            var icon = new IconCanvas(128);
            icon.Disc(new Vector2(0.5f, 0.5f), 0.34f);
            return SaveSprite(icon, "icon_key_sphere");
        }

        // A SQUARE THAT READS AS A CUBE. It was a plain filled square, which names a shape rather
        // than an object - and the thing it labels is a cube.
        //
        // The lines are CUT OUT rather than drawn on: these icons are a single colour with an alpha
        // mask, so there is no second colour to draw with, and a hole is the only mark available. It
        // shows through as whatever is behind - the pale HUD disc, the wall - which is the white line
        // asked for.
        //
        // Two lines from the top-left corner, one across and one down, which is the minimum that
        // makes a square read as a box seen corner-on: they separate a top face and a side face from
        // the front one without changing the silhouette.
        private static Sprite SquareIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 mid = new Vector2(0.5f, 0.5f);
            const float half = 0.300f;
            const float inset = 0.105f;      // how deep the two implied faces are
            const float line = 0.021f;

            icon.Bar(mid, new Vector2(half, half));

            // The top face's lower edge: across, stopping short of the right side so it meets the
            // other line's corner rather than cutting the square in two.
            icon.Bar(mid + new Vector2(inset / 2f, half - inset),
                     new Vector2(half - inset / 2f, line), 0f, -1f);
            // The side face's inner edge: down from that corner.
            icon.Bar(mid + new Vector2(-half + inset, -inset / 2f),
                     new Vector2(line, half - inset / 2f), 0f, -1f);
            // And the short diagonal at the corner where the two faces meet, which is what tells the
            // eye they are receding rather than being panels painted on a flat square.
            for (int i = 0; i < 7; i++)
            {
                float t = i / 6f;
                icon.Bar(mid + new Vector2(-half + inset * (1f - t), half - inset * (1f - t)),
                         new Vector2(line, line), 0f, -1f);
            }
            return SaveSprite(icon, "icon_key_square");
        }

        // The yellow triangle in silhouette. A plain solid triangle rather than an outline: the row
        // it appears in is 58px, the other two glyphs there are solid, and an outline at that size is
        // two thin lines that close up under the bilinear filter.
        private static Sprite TriangleIcon()
        {
            var icon = new IconCanvas(128);
            icon.Shape(p =>
            {
                if (p.y < 0.22f || p.y > 0.80f) return false;
                // 0 at the apex, 1 at the base - so the half-width opens out on the way down.
                float k = (0.80f - p.y) / 0.58f;
                return Mathf.Abs(p.x - 0.5f) <= 0.40f * k;
            });
            return SaveSprite(icon, "icon_key_triangle");
        }

        // The nightstand and the drawer in it, built here rather than imported.
        //
        // It used to be nightstand.glb with a generated drawer bolted to its front face, because the
        // model bakes its whole body into one mesh and has no drawer node to pull. That read exactly
        // as what it was: a pale slab stuck on a dark cabinet, sliding out to reveal the two drawer
        // fronts the model already had painted on it. There is no recess to import, so the carcass
        // has to be built to have one - and the same numbers then cut the opening and fill it, which
        // is the whole reason the two halves are one method.
        //
        // The model's lamp and pot are rebuilt from primitives here; they were the reason it was
        // chosen and the room would be barer without them. Geometry only, no Light: the shadow atlas
        // is sized for exactly the four fixtures that cast, and a fifth would silently halve them.
        //
        // The footprint reproduces the model's measured bounds - min (-1.23, 0, 1.16), max
        // (-0.67, 0.59, 1.52) - so nothing else in the room has to move.
        // pinItemId: the id the three pins share. A parameter rather than a constant because a second
        // cycle gets its own nightstand, and `ItemRegistry` maps an id to ONE socket with the last
        // writer winning - two cycles' pins under one id would be a single six-deep supply that
        // either cycle's ghosts could draw from.
        // A CHEST OF DRAWERS: two bays, and both of them open.
        //
        // Built rather than imported, for the reason the nightstand is: a drawer that opens needs its
        // front to be a separate object from the carcass, and every imported chest is one mesh with
        // drawer fronts modelled into it. The nightstand deliberately has ONE opening bay and a shelf
        // below, because "a second front that does not move is a lie" - this is the other answer to
        // that, where both fronts move and neither is lying.
        //
        // **NOTHING IS IN IT**, and that is deliberate rather than unfinished: what goes in a drawer
        // belongs to a puzzle that has not been designed, and the point of building the container now
        // is to have somewhere for that to go. Both bays are wired as `GhostInteractable`s from the
        // start anyway - the moment anything IS in one, a past self has to be able to open it, and
        // adding a bit later is the one thing `RecordedFrame.signals` makes awkward (CLAUDE.md §1.6).
        //
        // Everything here is LOCAL to the unit, including the drawers, which are children of it. That
        // is not a style choice: the same code written against world positions is what put cycle 2's
        // nightstand drawer fifty metres from its own carcass.
        private static Drawer[] BuildDresser(Transform parent, string name, Vector3 localPosition,
                                             float yaw, bool withLamp = false,
                                             bool withBilliards = false)
        {
            const float w = 0.82f, d = 0.44f, h = 0.86f;
            const float panel = 0.018f;     // carcass stock
            const float legH = 0.09f;       // floor to the underside of the case
            const float topT = 0.034f;      // the slab the cube stands on

            const float caseBottom = legH;
            const float caseTop = h - topT;
            // **ONE DRAWER, IN THE UPPER HALF** (2026-08-31, by request: every chest in the game is
            // the nightstand's shape now). It was two equal bays; the height is left exactly as it
            // was so the drawer is the same object it always was, and what was the lower bay is a
            // solid front below it.
            const float bayH = (caseTop - caseBottom - panel) / 2f;
            const float bayBottom = caseTop - bayH;

            Material wood = MakeColorMaterial("NightstandWood", new Color(0.14f, 0.085f, 0.06f));
            SetSmoothness(wood, 0.25f);
            Material brass = MakeColorMaterial("DrawerHandle", new Color(0.72f, 0.55f, 0.25f));
            SetSmoothness(brass, 0.55f);

            GameObject unit = new GameObject(name);
            unit.transform.SetParent(parent, false);
            unit.transform.localPosition = localPosition;
            unit.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            // Carcass: five boards with the FRONT LEFT OFF. That absence is the two openings.
            Prim(PrimitiveType.Cube, "Top", unit.transform, new Vector3(0f, h - topT / 2f, 0f),
                new Vector3(w + 0.03f, topT, d + 0.02f), wood);
            Prim(PrimitiveType.Cube, "Bottom", unit.transform, new Vector3(0f, caseBottom + panel / 2f, 0f),
                new Vector3(w, panel, d), wood);
            Prim(PrimitiveType.Cube, "Back", unit.transform,
                new Vector3(0f, (caseBottom + caseTop) / 2f, d / 2f - panel / 2f),
                new Vector3(w, caseTop - caseBottom, panel), wood);
            Prim(PrimitiveType.Cube, "SideLeft", unit.transform,
                new Vector3(-w / 2f + panel / 2f, (caseBottom + caseTop) / 2f, 0f),
                new Vector3(panel, caseTop - caseBottom, d), wood);
            Prim(PrimitiveType.Cube, "SideRight", unit.transform,
                new Vector3(w / 2f - panel / 2f, (caseBottom + caseTop) / 2f, 0f),
                new Vector3(panel, caseTop - caseBottom, d), wood);
            // The shelf the drawer runs on. It was the divider BETWEEN two bays and it does the same
            // job for one - what changed is only what is under it.
            Prim(PrimitiveType.Cube, "Shelf", unit.transform,
                new Vector3(0f, bayBottom - panel / 2f, 0f),
                new Vector3(w - panel * 2f, panel, d - panel), wood);

            // **AND THE FRONT THAT WAS LEFT OFF FOR THE SECOND BAY IS BACK ON.** The carcass is built
            // with no front because the openings ARE that absence; with one drawer, the lower half
            // has no opening and a chest with a hole in it is not furniture.
            Prim(PrimitiveType.Cube, "FrontLower", unit.transform,
                new Vector3(0f, (caseBottom + bayBottom - panel) / 2f, -d / 2f + panel / 2f),
                new Vector3(w - panel * 2f - 0.01f, bayBottom - panel - caseBottom - 0.01f, panel),
                wood);

            for (int i = 0; i < 4; i++)
            {
                float lx = (i % 2 == 0 ? -1f : 1f) * (w / 2f - 0.04f);
                float lz = (i < 2 ? -1f : 1f) * (d / 2f - 0.04f);
                Prim(PrimitiveType.Cube, $"Leg{i}", unit.transform, new Vector3(lx, legH / 2f, lz),
                    new Vector3(0.048f, legH, 0.048f), wood);
            }

            Drawer drawer = BuildDresserBay(unit.transform, name + "_Drawer",
                bayBottom, caseTop, w, d, panel, wood, brass);

            // AND NOW THERE IS SOMETHING IN IT. The tray geometry is recomputed rather than handed
            // back out of `BuildDresserBay`, because it is three lines of arithmetic off numbers this
            // method already owns and returning a second thing from a bay builder to describe the
            // inside of a drawer would be worse.
            if (withBilliards)
                BuildBilliardBalls(drawer, w - panel * 2f - 0.01f, bayH - 0.01f, d * 0.76f);

            // AND SOMETHING ON TOP THE PLAYER CAN PICK UP. The nightstand's cube is scenery - a model
            // placed with no collider, which cannot be taken - and this is the opposite: a carryable
            // in its own right, standing on the chest the way the pin stands in the drawer.
            BuildDresserCube(unit.transform, name + "_Cube", new Vector3(w * 0.26f, h, -d * 0.08f),
                             CycleTwoBlockItemId, weighable: true);

            // The bedside lamp, when this chest is standing where a nightstand was. Scenery, and the
            // only thing carried over from the unit it replaced - a bed with nothing lit beside it
            // reads as a room nobody sleeps in.
            if (withLamp) BuildNightstandLamp(unit.transform, new Vector3(-w * 0.28f, h, 0.02f));

            return new[] { drawer };
        }

        // ONE BAY of the chest above: the tray that slides, the front that carries it, and the volume
        // that answers E. Modelled on the nightstand's single drawer, which is where every number in
        // here was settled.
        private static Drawer BuildDresserBay(Transform unit, string name, float bayBottom, float bayTop,
                                              float w, float d, float panel,
                                              Material wood, Material brass)
        {
            float trayD = d * 0.76f;
            float frontW = w - panel * 2f - 0.01f;
            float frontH = (bayTop - bayBottom) - 0.01f;

            // The drawer's own root sits at the CENTRE OF ITS FRONT PANEL rather than at the unit's
            // origin, because `Drawer.HintAnchor` is the body - anchored at the floor, the E prompt
            // would float at the player's feet instead of on the front they are about to pull.
            GameObject root = new GameObject(name);
            root.transform.SetParent(unit, false);
            root.transform.localPosition =
                new Vector3(0f, (bayBottom + bayTop) / 2f, -d / 2f + panel / 2f);

            GameObject bodyGO = new GameObject("DrawerBody");
            bodyGO.transform.SetParent(root.transform, false);

            Material liner = MakeColorMaterial("DrawerLiner", new Color(0.60f, 0.58f, 0.55f));
            SetSmoothness(liner, 0.15f);

            Prim(PrimitiveType.Cube, "Front", bodyGO.transform, Vector3.zero,
                new Vector3(frontW, frontH, panel), wood, removeCollider: true);
            Prim(PrimitiveType.Cube, "TrayBase", bodyGO.transform,
                new Vector3(0f, -frontH / 2f + 0.008f, trayD / 2f),
                new Vector3(frontW - 0.02f, 0.016f, trayD), liner, removeCollider: true);
            Prim(PrimitiveType.Cube, "TrayLeft", bodyGO.transform,
                new Vector3(-frontW / 2f + 0.008f, 0f, trayD / 2f),
                new Vector3(0.016f, frontH * 0.8f, trayD), liner, removeCollider: true);
            Prim(PrimitiveType.Cube, "TrayRight", bodyGO.transform,
                new Vector3(frontW / 2f - 0.008f, 0f, trayD / 2f),
                new Vector3(0.016f, frontH * 0.8f, trayD), liner, removeCollider: true);
            Prim(PrimitiveType.Cube, "TrayBack", bodyGO.transform, new Vector3(0f, 0f, trayD),
                new Vector3(frontW - 0.02f, frontH * 0.8f, 0.016f), liner, removeCollider: true);
            Prim(PrimitiveType.Cube, "Pull", bodyGO.transform,
                new Vector3(0f, 0f, -panel / 2f - 0.012f),
                new Vector3(frontW * 0.42f, 0.016f, 0.024f), brass, removeCollider: true);

            BoxCollider trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(w + 0.9f, 1.0f, 1.4f);

            Drawer drawerComp = root.AddComponent<Drawer>();
            drawerComp.drawerBody = bodyGO.transform;
            drawerComp.audioSource = MakeSource(root.transform, "DrawerAudio", 1f, 0.8f);
            drawerComp.openClip = LoadClip(SfxDir, "sfx_drawer_open");
            // AND E SHUTS IT AGAIN. Safe here in the way it is not on cycle 1's nightstand: these bays
            // are empty, so a past self's replayed pull closing one costs nothing. See Drawer.canClose
            // for what turning this on for a drawer that HOLDS something would mean.
            //
            // No close clip exists, so `Drawer` pitches the open one down for the return - the same
            // runners in the same carcass, which is the one case where pitching a clip is honest.
            // **CLOSABLE AGAIN, 2026-08-20, BY EXPLICIT REQUEST, AND THE COST IS KNOWN AND ACCEPTED.**
            //
            // It was turned off earlier the same day because the bays stopped being empty. A closable
            // drawer makes E a TOGGLE, `Drawer.SetGhostSignal` reproduces the PULL rather than the
            // resulting state, and so the drawer's openness is the PARITY of how many past selves
            // have reached for it - one ghost opens it, two leave it shut, three open it again. Every
            // ball inside gates on `IsFullyOpen`, so on even iterations a ghost's take, and with it
            // that ghost's whole delivery, silently does not happen.
            //
            // **AND IT IS FALSE AGAIN, BECAUSE THE REASON IT WAS TRUE IS GONE** (2026-08-31). It was
            // set for one reason and one only: an OPEN drawer stays in the aim contest and its front
            // has slid 0.23m nearer the eye, so the upper bay won the press from in front of the
            // LOWER one and play reported the lower bay as unopenable. Being able to shut it was the
            // only way out of that from inside the game.
            //
            // There is no lower bay now. Nothing is behind this drawer for it to hide, so the trade
            // has nothing left to buy and only its cost remains - which is real: a closable drawer
            // makes E a TOGGLE, and a toggle replayed by several past selves is order-dependent
            // where an idempotent `Open()` is not. Two ghosts that both recorded a pull open it and
            // then shut it again, and everything inside gates on `IsFullyOpen`. That is the drawer
            // parity `docs/gotchas.md` records as costing cycle 2 an unknown number of iterations,
            // and it is what this line was causing.
            drawerComp.canClose = false;
            // Two thirds out, like the nightstand's: leaving a third of the tray inside the carcass is
            // what reads as a drawer rather than as a tray hanging in mid-air.
            drawerComp.openLocalOffset = new Vector3(0f, 0f, -trayD * 0.68f);
            return drawerComp;
        }

        // THE CUBE ON THE CHEST, and unlike the nightstand's it can be picked up.
        //
        // THE RUBIK'S CUBE THE NIGHTSTAND USED TO HOLD, and now it can be picked up.
        //
        // The same `rubiks_cube.glb` at the same size it was - about 90mm, which is what the old
        // `PlaceModel(..., 0.8f)` came out at. What changes is only that it is a carryable rather than
        // scenery: the nightstand's was placed with `addBoxCollider: false`, so it could be looked at
        // and never touched.
        //
        // NOT through `PlaceModel`, which corrects a WORLD-space delta into a localPosition. That is
        // safe under an unrotated parent at the origin and wrong everywhere else - it is the same
        // mistake that drew every bucket 1.6m behind itself, and this chest hangs off a furniture
        // holder that is yawed 180 and a storey down. Measured in the ROOT's own frame instead, with
        // the helper that exists for exactly this (`ModelBounds`).
        private static void BuildDresserCube(Transform unit, string name, Vector3 topLocal,
                                             string itemId, bool weighable, float size = 0.16f)
        {
            // 90mm was a keyring trinket on top of a chest - hard to see and harder to aim at, and
            // play reported both. A Rubik's cube is 57mm in life; this is deliberately larger than
            // life because it is an OBJECT in a puzzle game, and the thing it competes with for a
            // press is a drawer front the size of a dinner tray.
            //
            // **AND CYCLE 1'S NIGHTSTAND USES THIS TOO NOW** (2026-08-31, by request: its cube can be
            // picked up as well). It had a builder of its own that placed the same model with
            // `addBoxCollider: false` - scenery, lookable and untouchable. Two builders for one
            // object where the only real difference was whether you could have it.
            //
            // What is NOT shared is the id or the weight. Cycle 2's cube is 8.3kg on room2-7's scale
            // and there is exactly one solution to that room's 26.7kg across everything it can carry;
            // a second cube wearing the same id, in another cycle, is a second answer to a question
            // that must only have one.

            GameObject root = new GameObject(name);
            root.transform.SetParent(unit, false);
            // Standing ON the top rather than in it: the object's own half-height above the slab.
            root.transform.localPosition = topLocal + new Vector3(0f, size / 2f, 0f);

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>($"{FurnitureDir}/rubiks_cube.glb");
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            model.name = "Visual";
            model.transform.localPosition = Vector3.zero;
            // ITS OWN IMPORT ROTATION IS LEFT ALONE. The prefab root carries (270, 0, 0) to bring a
            // Z-up export into Unity's Y-up; forcing identity stands the cube on a corner, which is
            // what the nightstand's version had to pass a rotation in to avoid.

            Bounds raw = ModelBounds(root.transform, model);
            float longest = Mathf.Max(raw.size.x, Mathf.Max(raw.size.y, raw.size.z));
            float modelScale = size / Mathf.Max(0.0001f, longest);
            model.transform.localScale = Vector3.one * modelScale;
            // CENTRED ON THE ROOT, because `floorY` below is half the object's height - which is only
            // true if the root sits at the middle of it.
            model.transform.localPosition = -raw.center * modelScale;

            GameObject solid = new GameObject("Blocker");
            solid.transform.SetParent(root.transform, false);
            BoxCollider block = solid.AddComponent<BoxCollider>();
            block.size = Vector3.one * size;

            // TIGHT, AND LIFTED. The old volume was the cube plus 0.4m in every direction, which from
            // a 90mm object is a 0.5m ball - and it hung down over the drawer fronts underneath, so
            // standing at the chest put the player inside the cube's reach and the drawer's at once.
            // The two then argued about the press every time. Kept generous sideways, where nothing
            // competes, and cut back below, where the drawers are.
            BoxCollider reach = root.AddComponent<BoxCollider>();
            reach.isTrigger = true;
            reach.size = new Vector3(size + 0.34f, size + 0.20f, size + 0.34f);
            reach.center = new Vector3(0f, size * 0.35f, 0f);

            CarryableItem item = root.AddComponent<CarryableItem>();
            item.blocker = block;
            item.itemId = itemId;
            item.displayName = "CUBE";
            item.icon = SquareIcon();
            // Its own half-height above whatever floor it ends up on - which is NOT where it starts.
            // It begins on the chest, and `FallingItem` leaves it there because nothing dropped it
            // (CarryableItem.Released); put it down and it comes to rest at this height instead.
            item.floorY = size / 2f;
            item.handLocalPosition = HandPoseFor(size);
            // The root is unscaled and so is the hand anchor, so this only says "do not change it".
            item.handLocalScale = Vector3.one;
            item.audioSource = MakeSource(root.transform, "PickupAudio", 1f, 0.85f);
            item.pickupClip = LoadClip(SfxDir, "sfx_item_pickup");
            // The heaviest thing one hand can carry to room2-7, and the only one of its kind. Cycle
            // 1's has no scale to stand on and must not answer that question - see the header.
            if (weighable) MakeWeighable(item, WeightCube);
        }

        // NINE BILLIARD BALLS, SPILLED ACROSS BOTH TRAYS OF THE CHEST - and four of them are the
        // keys to room2-0.
        //
        // WHY NINE AND NOT SIXTEEN. The four the puzzle wants are 2, 3, 4 and 5, so the decoys have to
        // reach past 5 or the answer is "the ones that are there"; 1 and 6, 7, 8 and the cue ball do
        // that, and a drawer that has to be searched for a number is a drawer with a handful in it
        // rather than a rack. Sixteen would also be sixteen more E fixtures a square apart in a volume
        // the two drawer fronts already contest.
        //
        // ONE ID EACH, which is the opposite of every other multiple object in this building. Pins,
        // buckets, ducks and beach balls are SUPPLIES - an id naming several interchangeable things -
        // because a recorded "took a Tool" only ever meant "a free one". Here which one is the entire
        // question, so nine ids with one member each: a pedestal's socket names the ball it wants and
        // `ItemRegistry` can never hand it a different one.
        //
        // NO `Weighable`, AND THAT IS DELIBERATE RATHER THAN FORGOTTEN. Room2-7's target of 26.7kg has
        // exactly one solution across everything cycle 2 can carry - one duck, one beach ball, one
        // full bucket, one axe, one cube - with the nearest miss 0.1kg away. A ball worth anything at
        // all destroys that: at a real 0.17kg the search finds FORTY-NINE ways to make the number, and
        // at 0.16 or 0.20 it is much the same, because nine small addends fill every gap the coarse
        // weights leave. The scale's own sign already names the five things it accepts, so a ball
        // reading zero is what that sign says rather than a lie it tells.
        private static CarryableItem[] BuildBilliardBalls(Drawer drawer,
                                                          float frontW, float frontH, float trayD)
        {
            string path = PlayDir + "/billiard_balls.glb";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null)
            {
                Debug.LogError($"[SceneBuilder] {path} is missing - room2-0 has no keys and cannot "
                             + "be finished.");
                return new CarryableItem[0];
            }

            // INSTANTIATED ONCE AND ROBBED, like the chess set. Every ball in this file is a separate
            // node with its own textured material sitting at the origin on top of all the others, so
            // "one ball" is one child pulled out of the set - and pulling a child out of a prefab
            // instance is exactly the restructuring Unity refuses, hence the unpack.
            GameObject set = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(set, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);

            float y = -frontH / 2f + 0.016f + BilliardBallSize / 2f;

            var built = new System.Collections.Generic.List<CarryableItem>();

            foreach (var spec in BilliardPlan)
            {
                Transform node = FindChildByName(set.transform, spec.node);
                if (node == null)
                {
                    Debug.LogError($"[SceneBuilder] billiard_balls.glb has no node '{spec.node}' - "
                                 + "a re-export has renamed the balls.");
                    continue;
                }

                // **ONE ICON PER BALL, NOT ONE FOR ALL OF THEM** (2026-09-01, by request). The
                // number is the whole of what a billiard ball is here - room2-0 wants a 5, a 3 and a
                // 4 and refuses everything else - so a HUD line reading BALL 5 next to a picture of
                // an anonymous sphere was the one place in the game where the icon said less than
                // the label did.
                built.Add(MakeBilliardBall(node, drawer, spec.id,
                    new Vector3(spec.x, y, spec.z), spec.yaw, BilliardIcon(spec.id)));
            }

            Object.DestroyImmediate(set);
            Debug.Log($"[SceneBuilder] Billiards: {built.Count} balls of {BilliardBallSize:0.000}m "
                    + $"in Dresser2_1's drawer - {BilliardAnswers} answers and "
                    + $"{built.Count - BilliardAnswers} decoys.");
            return built.ToArray();
        }

        // How many of the plan below are ANSWERS. The first entries are room2-0's three, in the
        // order its pedestals want them, and everything after is a decoy - so the split is a count
        // rather than a second list to keep in step. Only the build log reads it.
        private const int BilliardAnswers = 3;

        // WHICH FIVE, AND WHERE EACH ONE LIES. Positions are in the drawer BODY's frame, so the balls
        // ride out with the tray instead of hanging in the air in front of a shut drawer - the same
        // parenting the pins have.
        //
        // **FIVE, DOWN FROM NINE** (2026-08-31, by request), and in ONE tray now that the chest has
        // one drawer. Room2-0 asks for three - 5 for the axes, 3 for the ducks, 4 for the buckets -
        // and the decoys are **2 and 6**, chosen to BRACKET that run rather than sit outside it. A
        // decoy is only doing its job if a player who miscounts by one can reach it: 1 and 8 are
        // numbers nobody arrives at by miscounting three ducks, where 2 and 6 are exactly what an
        // off-by-one gives you at either end of the answers.
        //
        // **WHAT THIS COSTS, stated because it is a real cost**: three answers among five balls is a
        // much cheaper thing to brute-force than three among nine. A player who does not count at all
        // can carry each ball to each pedestal, and the whole search is now a handful of trips. The
        // counting is what the puzzle IS, so if it ever stops being worth doing, decoys are the lever
        // - not the answers.
        //
        // A FIELD RATHER THAN A LOCAL, because room2-0 reads it too: its recesses take a press for
        // any ball in the game (`FinalSlot.offerItemIds`), and that list has to BE this one. Two
        // hand-kept lists of the same ids is how a pedestal ends up silently refusing to prompt for a
        // ball that exists.
        //
        // Two staggered rows rather than a line, and no two centres closer than 0.18 against a 0.13
        // ball: a drawer somebody tipped balls into rather than a rack, and it still has to be
        // possible to aim at one of them.
        private static readonly (string node, string id, float x, float z, float yaw)[]
            BilliardPlan =
        {
            // The three room2-0 wants.
            ("Ball5",      "5",  -0.240f, 0.235f,  141f),
            ("Ball3",      "3",   0.000f, 0.235f,  -38f),
            ("Ball4",      "4",   0.240f, 0.235f,  112f),
            // And the two either side of them.
            ("Ball2",      "2",  -0.120f, 0.095f,  -63f),
            ("Ball6",      "6",   0.120f, 0.095f,   -9f),
        };

        // Every ball id in the game, in the order above. What a room2-0 pedestal entertains a press
        // for - see FinalSlot.offerItemIds.
        private static string[] BilliardIds()
        {
            var ids = new string[BilliardPlan.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = BilliardIdPrefix + BilliardPlan[i].id;
            return ids;
        }

        // One ball, out of the set and into a drawer.
        private static CarryableItem MakeBilliardBall(Transform node, Drawer drawer, string id,
                                                      Vector3 localPos, float yaw, Sprite icon)
        {
            GameObject root = new GameObject("Billiard_" + id);
            root.transform.SetParent(drawer.drawerBody, false);
            root.transform.localPosition = localPos;

            node.SetParent(root.transform, false);
            node.localPosition = Vector3.zero;
            node.name = "Visual";
            GameObject model = node.gameObject;

            // THE NUMBER UP, AND THE BALL SIZED - both MEASURED off the mesh rather than written
            // down, and in one pass because both answers come from the same vertex list.
            //
            // **NOT `ModelBounds`**, which is what every other model here is sized by and is wrong for
            // this one. That helper transforms the eight CORNERS OF THE MESH'S BOX into the target
            // frame, which is exact for a box and an over-estimate for anything else - and the
            // over-estimate depends on the ROTATION. Scattering nine balls through nine different
            // orientations therefore measured nine different "widths": the first build came out with
            // radii spread 23% apart, which is a drawer of visibly mismatched balls. A sphere has to
            // be measured as a sphere, so this takes the extent of the actual vertices.
            (Vector3 centre, float radius) = OrientBilliardBall(root.transform, model, yaw, -22f,
                                                                out Vector3 numberDir,
                                                                out Vector3 numberUp);

            float modelScale = radius > 0.0001f ? BilliardBallSize / (2f * radius) : 1f;
            model.transform.localScale = Vector3.one * modelScale;
            // Centred on the root, because `floorY` below is half the ball's height - which is only
            // true if the root sits at the middle of it.
            model.transform.localPosition = -centre * modelScale;

            // The pin's volume, because this is the pin's problem: a small object lying in a drawer
            // that has to be reachable from where a player stands at the chest. Nine of these overlap
            // heavily and that is fine and expected - `ItemRegistry.AimedTakeable` hands the press to
            // whichever ball is nearest the crosshair, which is the same arbitration thirty-two chess
            // pieces a square apart already run on.
            BoxCollider trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = Vector3.one * 0.7f;

            CarryableItem item = root.AddComponent<CarryableItem>();
            item.itemId = BilliardIdPrefix + id;
            item.displayName = "BALL " + (id == "Cue" ? "-" : id);
            item.icon = icon;
            item.floorY = BilliardBallSize / 2f;
            item.handLocalPosition = HandPoseFor(BilliardBallSize);

            // **AND IT IS HELD WITH THE NUMBER TOWARD THE EYE** (2026-09-01, by request). Which ball
            // this is is the only fact about it that matters, and the drawer pose puts the number
            // UPWARDS with a scatter tilt on top - correct for a ball lying in a tray, and in the
            // hand it means looking at the blank underside of a sphere.
            //
            // Derived, not authored. Each ball is scattered to its own `yaw`, so there is no single
            // euler that turns all five to face the player; the number's direction is measured off
            // the mesh (see `OrientBilliardBall`) and the rotation that takes it to the eye is
            // computed from it. Writing a number here would be right for one ball out of five.
            //
            // The eye is where `handLocalPosition` says the ball is, negated - the hand hangs off the
            // camera, so the direction back to it is exactly the direction the object is offset in.
            // Taken from the same value rather than assumed to be straight ahead, because it is not:
            // the ball sits 0.28 right and 0.24 down.
            Vector3 toEye = -item.handLocalPosition.normalized;
            item.handLocalEuler =
                (Quaternion.LookRotation(toEye, Vector3.up)
                 * Quaternion.Inverse(Quaternion.LookRotation(numberDir, numberUp))).eulerAngles;
            // The root is unscaled and so is the drawer body, so this only says "do not change it".
            item.handLocalScale = Vector3.one;
            item.requiresOpenDrawer = drawer;
            // AND A DROP LEAVES THE DRAWER BEHIND. The chest itself, which does not move - the tray
            // does. See CarryableItem.dropParent: a ball put down in room2-0 must not slide 0.22m
            // sideways every time a past self opens a drawer at the other end of the building.
            item.dropParent = drawer.transform.parent;
            item.audioSource = MakeSource(root.transform, "PickupAudio", 1f, 0.8f);
            item.pickupClip = LoadClip(SfxDir, "sfx_item_pickup");
            // NO MakeWeighable - see BuildBilliardBalls.
            return item;
        }

        // WHICH WAY THE PRINTED NUMBER FACES, AND HOW BIG THE BALL IS. Both off the mesh, and in one
        // pass because they read the same vertices.
        //
        // THE NUMBER. It sits at the middle of this model's texture, so the vertex whose UV is nearest
        // (0.5, 0.5) is the point it is printed on - and the sphere being centred on its own origin,
        // that vertex's position IS the outward direction. A written euler would be a guess about the
        // product of the export's axis convention and Unity's import correction, and a ball with its
        // number underneath is indistinguishable from a bug in the drawer.
        //
        // `tilt` leans the ball over once the number is upright, and `yaw` spins it about the room's
        // up afterwards - so the number ends up `tilt` degrees off vertical in a direction `yaw`
        // chooses. That is the scatter: a negative tilt leans it toward the front of the drawer, which
        // is the side the player is standing on.
        //
        // The spin also makes the digit's own upright IRRELEVANT, which is why it is not measured:
        // whatever in-plane rotation it lands at reads as one more ball tipped into a drawer.
        //
        // Returns the ball's centre and radius IN THE ROOT'S FRAME, before scaling. Rotation-invariant
        // by construction, which `ModelBounds` is not - see the call site.
        //
        // **AND IT REPORTS WHERE THE NUMBER ENDED UP**, in the root's frame and after the rotation,
        // because the hand pose needs it and re-deriving it at the call site would be the same
        // measurement written twice. `numberUp` is the digit's own upright, taken from the texture's
        // V axis: the vertex above the middle of the map in UV is above the middle of the digit on
        // the ball. It is unused in the drawer - a ball in a tray is allowed any roll - and is what
        // makes the number the right way up in the hand.
        private static (Vector3 centre, float radius) OrientBilliardBall(
            Transform root, GameObject model, float yaw, float tilt,
            out Vector3 numberDir, out Vector3 numberUp)
        {
            numberDir = Vector3.up;
            numberUp = Vector3.forward;
            MeshFilter filter = model.GetComponentInChildren<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            Vector3[] verts = mesh != null ? mesh.vertices : null;
            Vector2[] uvs = mesh != null ? mesh.uv : null;

            if (verts == null || verts.Length == 0)
            {
                Debug.LogError($"[SceneBuilder] {model.name}: no readable mesh, so the ball is "
                             + "unmeasured and unoriented.");
                return (Vector3.zero, 0f);
            }

            if (uvs == null || uvs.Length != verts.Length)
            {
                Debug.LogWarning($"[SceneBuilder] {model.name}: no readable UVs, so the number is "
                               + "wherever the export left it. Scatter only.");
                model.transform.localRotation = Quaternion.AngleAxis(yaw, Vector3.up)
                                              * model.transform.localRotation;
            }
            else
            {
                int best = NearestUV(uvs, new Vector2(0.5f, 0.5f));
                // A short step along the texture's V axis from the same point. 0.10 is far enough to
                // be a different vertex on a sphere this dense and near enough to still be on the
                // digit's own patch of the map.
                int above = NearestUV(uvs, new Vector2(0.5f, 0.6f));

                Vector3 outward = filter.transform.TransformPoint(verts[best]) - filter.transform.position;
                Vector3 inRoot = root.InverseTransformDirection(outward);
                if (inRoot.sqrMagnitude > 1e-8f)
                    model.transform.localRotation =
                          Quaternion.AngleAxis(yaw, Vector3.up)
                        * Quaternion.AngleAxis(tilt, Vector3.right)
                        * Quaternion.FromToRotation(inRoot.normalized, Vector3.up)
                        * model.transform.localRotation;

                // AFTER the rotation, so these are where the number actually points now.
                Vector3 dir = root.InverseTransformDirection(
                    filter.transform.TransformPoint(verts[best]) - filter.transform.position);
                Vector3 up = root.InverseTransformDirection(
                    filter.transform.TransformPoint(verts[above]) - filter.transform.TransformPoint(verts[best]));

                if (dir.sqrMagnitude > 1e-8f) numberDir = dir.normalized;
                // Only the part of it across the face - the step along V also goes round the sphere,
                // and a `LookRotation` up-vector that is not perpendicular quietly skews the roll.
                up = Vector3.ProjectOnPlane(up, numberDir) * BilliardDigitFlip;
                numberUp = up.sqrMagnitude > 1e-8f
                    ? up.normalized
                    : Vector3.ProjectOnPlane(Vector3.up, numberDir).normalized;
            }

            // MEASURED AFTER THE ROTATION, which costs nothing to say and is free to be true: a
            // radius about a centre does not care how the thing is turned. Stated this way round so
            // the pair is unambiguously the pose that is kept.
            Vector3 min = root.InverseTransformPoint(filter.transform.TransformPoint(verts[0]));
            Vector3 max = min;
            for (int i = 1; i < verts.Length; i++)
            {
                Vector3 v = root.InverseTransformPoint(filter.transform.TransformPoint(verts[i]));
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }

            Vector3 centre = (min + max) * 0.5f;
            float radius = 0f;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = root.InverseTransformPoint(filter.transform.TransformPoint(verts[i]));
                radius = Mathf.Max(radius, (v - centre).magnitude);
            }

            return (centre, radius);
        }

        // Nearest vertex to a point on the texture. Two calls want it, and a linear scan of a
        // twelve-hundred-vertex sphere twice per ball is not worth being cleverer than.
        private static int NearestUV(Vector2[] uvs, Vector2 target)
        {
            int best = 0;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < uvs.Length; i++)
            {
                float d = (uvs[i] - target).sqrMagnitude;
                if (d >= bestSqr) continue;
                bestSqr = d;
                best = i;
            }
            return best;
        }

        // Depth-first by exact name. `Transform.Find` walks a PATH and these nodes are two levels down
        // under a machine-generated root, so the path would be a second thing to keep in step with the
        // export.
        private static Transform FindChildByName(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;

            return null;
        }

        // A BALL WITH ITS NUMBER IN THE SPOT, for the HUD line that says what is in the hand. A
        // plain disc would be the beach ball's icon at a different size; the white circle bitten out
        // of the middle is the one mark that separates a billiard ball from every other sphere in
        // this game, and the digit in it is the one mark that separates the five from each other.
        //
        // **SEVEN-SEGMENT, because that is the only alphabet `IconCanvas` has.** It draws discs,
        // capsules, arcs and bars in normalised coordinates and there is no font rasteriser at this
        // end of the build - the one bitmap font in this file (`BlitGlyph`) knows three letters and
        // writes into a raw `Color[]`, which is a different pipeline. Seven bars is a digit, it is
        // legible at 128px in a way a hand-drawn outline would not be, and a machine-shaped numeral
        // is the right register for a facility HUD anyway.
        //
        // The spot is opened from 0.155 to 0.21 to hold one: the digit's far corner sits at 0.19
        // from the middle, so anything tighter clips it. A ball with no number - the cue - keeps the
        // small blank spot, which is what a cue ball looks like.
        private static Sprite BilliardIcon(string id)
        {
            var icon = new IconCanvas(128);
            Vector2 mid = new Vector2(0.5f, 0.5f);

            bool numbered = id != null && id.Length == 1 && id[0] >= '0' && id[0] <= '9';

            icon.Disc(mid, 0.40f);
            icon.Disc(mid, numbered ? 0.21f : 0.155f, -1f);
            if (numbered) BilliardDigit(icon, mid, id[0]);

            return SaveSprite(icon, $"icon_billiard_{(numbered ? id : "cue")}");
        }

        // One digit as seven bars, filled back INTO the hole the spot bit out - so the icon reads
        // ring, white circle, dark number, which is what the object looks like.
        //
        //      a          Segments are named the way every seven-segment display names them, so the
        //   f     b       table below can be read against any datasheet rather than against a
        //      g          drawing that only exists here.
        //   e     c
        //      d
        private static void BilliardDigit(IconCanvas icon, Vector2 mid, char digit)
        {
            const float h = 0.135f;    // half the digit's height, middle bar to top bar
            const float w = 0.075f;    // half its width
            const float t = 0.026f;    // half the stroke

            // a b c d e f g, in that order.
            string on;
            switch (digit)
            {
                case '0': on = "1111110"; break;
                case '1': on = "0110000"; break;
                case '2': on = "1101101"; break;
                case '3': on = "1111001"; break;
                case '4': on = "0110011"; break;
                case '5': on = "1011011"; break;
                case '6': on = "1011111"; break;
                case '7': on = "1110000"; break;
                case '8': on = "1111111"; break;
                case '9': on = "1111011"; break;
                default: return;
            }

            void Seg(int index, Vector2 centre, Vector2 half)
            {
                if (on[index] == '1') icon.Bar(centre, half);
            }

            Seg(0, mid + new Vector2(0f, h), new Vector2(w, t));              // a, top
            Seg(1, mid + new Vector2(w, h / 2f), new Vector2(t, h / 2f));     // b, upper right
            Seg(2, mid + new Vector2(w, -h / 2f), new Vector2(t, h / 2f));    // c, lower right
            Seg(3, mid + new Vector2(0f, -h), new Vector2(w, t));             // d, bottom
            Seg(4, mid + new Vector2(-w, -h / 2f), new Vector2(t, h / 2f));   // e, lower left
            Seg(5, mid + new Vector2(-w, h / 2f), new Vector2(t, h / 2f));    // f, upper left
            Seg(6, mid, new Vector2(w, t));                                   // g, middle
        }

        private static (Drawer, CarryableItem[]) BuildNightstand(Transform parent, string pinItemId = ToolItemId)
        {
            // Footprint centre, on the floor. The front face looks down the room, away from the
            // pillow, which is the side the player is on when they turn round from the bed.
            Vector3 centre = new Vector3(-0.95f, 0f, 1.35f);
            const float w = 0.56f, d = 0.36f, h = 0.59f;
            const float panel = 0.018f;     // carcass stock
            const float legH = 0.075f;      // floor to the underside of the case
            const float topT = 0.032f;      // the slab the lamp stands on

            float caseBottom = legH;
            float caseTop = h - topT;
            const float bayH = 0.17f;              // the drawer opening
            float bayTop = caseTop;
            float bayBottom = bayTop - bayH;

            Material wood = MakeColorMaterial("NightstandWood", new Color(0.14f, 0.085f, 0.06f));
            SetSmoothness(wood, 0.25f);
            // Brass, as the model's pulls were. Not metallic: this project cannot light a pure metal
            // (see docs/gotchas.md), so it is a warm colour with some gloss instead.
            Material brass = MakeColorMaterial("DrawerHandle", new Color(0.72f, 0.55f, 0.25f));
            SetSmoothness(brass, 0.55f);

            GameObject unit = new GameObject("Nightstand");
            unit.transform.SetParent(parent, false);
            // LOCAL, not world. It was `position`, which was the same thing for as long as the only
            // nightstand was in a room whose parent sat at the origin - and silently wrong the moment
            // one is built under a parent carrying a storey offset, where it would land on the floor
            // above.
            unit.transform.localPosition = centre;

            // Carcass: five boards with the front left OFF. That absence is the recess - there is
            // nothing else to build, and it is what the imported mesh could not give.
            Prim(PrimitiveType.Cube, "Top", unit.transform, new Vector3(0f, h - topT / 2f, 0f),
                new Vector3(w + 0.03f, topT, d + 0.02f), wood);
            Prim(PrimitiveType.Cube, "Bottom", unit.transform, new Vector3(0f, caseBottom + panel / 2f, 0f),
                new Vector3(w, panel, d), wood);
            Prim(PrimitiveType.Cube, "Back", unit.transform, new Vector3(0f, (caseBottom + caseTop) / 2f, d / 2f - panel / 2f),
                new Vector3(w, caseTop - caseBottom, panel), wood);
            Prim(PrimitiveType.Cube, "SideLeft", unit.transform, new Vector3(-w / 2f + panel / 2f, (caseBottom + caseTop) / 2f, 0f),
                new Vector3(panel, caseTop - caseBottom, d), wood);
            Prim(PrimitiveType.Cube, "SideRight", unit.transform, new Vector3(w / 2f - panel / 2f, (caseBottom + caseTop) / 2f, 0f),
                new Vector3(panel, caseTop - caseBottom, d), wood);
            // Separates the drawer bay above from an open shelf below. A second drawer front would
            // have matched the old model better and lied: only one drawer opens.
            Prim(PrimitiveType.Cube, "Divider", unit.transform, new Vector3(0f, bayBottom - panel / 2f, 0f),
                new Vector3(w - panel * 2f, panel, d - panel), wood);

            for (int i = 0; i < 4; i++)
            {
                float lx = (i % 2 == 0 ? -1f : 1f) * (w / 2f - 0.035f);
                float lz = (i < 2 ? -1f : 1f) * (d / 2f - 0.035f);
                Prim(PrimitiveType.Cube, $"Leg{i}", unit.transform, new Vector3(lx, legH / 2f, lz),
                    new Vector3(0.042f, legH, 0.042f), wood);
            }

            BuildNightstandLamp(unit.transform, new Vector3(-0.13f, h, 0.03f));
            BuildNightstandCube(unit.transform, new Vector3(0.16f, h, -0.05f));

            // The drawer's own root sits at the CENTRE OF ITS FRONT PANEL, not at the unit origin,
            // because Drawer.HintAnchor is drawerBody - anchored at the floor the E prompt would
            // float at the player's feet.
            float frontZ = -d / 2f + panel / 2f;
            float frontY = (bayBottom + bayTop) / 2f;

            GameObject root = new GameObject("NightstandDrawer");
            root.transform.SetParent(parent, false);
            // LOCAL, NOT WORLD - the same fix the carcass above already carries, and this half was
            // missed. `centre` is a position in the PARENT's space, so assigning it to `.position`
            // only lands correctly while that parent sits at the origin unrotated. Cycle 2's
            // nightstand hangs off a furniture holder that is yawed 180 with the bed, one storey
            // down and fifty-odd metres along the corridor - so its drawer was built at world
            // (0.95, 0.47, 107.3) while its carcass stood at (-0.95, -7.21, 55.6). A drawer front
            // floating in the void half a building away, with the bay it belongs in left empty.
            root.transform.localPosition = centre + new Vector3(0f, frontY, frontZ);

            GameObject bodyGO = new GameObject("DrawerBody");
            bodyGO.transform.SetParent(root.transform, false);

            // Inset into the opening rather than laid over it, by 5mm all round: the gap is what
            // shows there is a hole here even with the drawer shut.
            const float trayD = 0.30f;
            float frontW = w - panel * 2f - 0.01f;
            float frontH = bayH - 0.01f;

            // The tray is LINED, in pale grey, and the wood stops at the front panel. This is the one
            // place the unit is not the colour it wants to be: the pin is a dark handle with a bright
            // 6mm needle on it, and against wood the handle disappears - the first version of this
            // carcass lost the tool completely, because the tray it inherited had been white. A liner
            // is what a drawer has anyway, and it puts the contrast back where it is load-bearing.
            Material liner = MakeColorMaterial("DrawerLiner", new Color(0.60f, 0.58f, 0.55f));
            SetSmoothness(liner, 0.15f);

            Prim(PrimitiveType.Cube, "Front", bodyGO.transform, Vector3.zero,
                new Vector3(frontW, frontH, panel), wood, removeCollider: true);
            Prim(PrimitiveType.Cube, "TrayBase", bodyGO.transform, new Vector3(0f, -frontH / 2f + 0.008f, trayD / 2f),
                new Vector3(frontW - 0.02f, 0.016f, trayD), liner, removeCollider: true);
            Prim(PrimitiveType.Cube, "TrayLeft", bodyGO.transform, new Vector3(-frontW / 2f + 0.008f, 0f, trayD / 2f),
                new Vector3(0.016f, frontH * 0.8f, trayD), liner, removeCollider: true);
            Prim(PrimitiveType.Cube, "TrayRight", bodyGO.transform, new Vector3(frontW / 2f - 0.008f, 0f, trayD / 2f),
                new Vector3(0.016f, frontH * 0.8f, trayD), liner, removeCollider: true);
            Prim(PrimitiveType.Cube, "TrayBack", bodyGO.transform, new Vector3(0f, 0f, trayD),
                new Vector3(frontW - 0.02f, frontH * 0.8f, 0.016f), liner, removeCollider: true);
            Prim(PrimitiveType.Cube, "Pull", bodyGO.transform, new Vector3(0f, 0f, -panel / 2f - 0.012f),
                new Vector3(frontW * 0.42f, 0.016f, 0.024f), brass, removeCollider: true);

            BoxCollider trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(w + 0.9f, 1.4f, 1.4f);

            Drawer drawerComp = root.AddComponent<Drawer>();
            drawerComp.drawerBody = bodyGO.transform;
            drawerComp.audioSource = MakeSource(root.transform, "DrawerAudio", 1f, 0.8f);
            drawerComp.openClip = LoadClip(SfxDir, "sfx_drawer_open");
            // Two thirds out, not all the way. Clearing the carcass entirely was right when the
            // drawer was a slab on a solid face and had nowhere to be; now that there is an opening
            // to sit in, leaving a third of the tray inside is what reads as a drawer rather than a
            // tray hanging in mid-air. It still puts the tool well clear of the front.
            drawerComp.openLocalOffset = new Vector3(0f, 0f, -trayD * 0.68f);

            // THREE pins, not one, laid out across the tray.
            //
            // Popping requires the pin in hand for ghosts as for the player, and with one pin in the
            // world that capped the entire room at ONE popper at any moment - so Room2's
            // accumulation, which is the thing every iteration is supposed to add to, did not survive
            // its own rule. The mitigation was always a supply rather than a softer rule
            // (docs/decisions.md), and ItemRegistry now resolves an id to a free instance so several
            // objects can share one.
            //
            // Three buys the player and TWO past selves popping at once. It is not a cap on ghosts,
            // only on simultaneous poppers: a third ghost reaching for a fourth pin finds none, and
            // its errand simply does not happen. Raising the number is now one edit here.
            CarryableItem[] pins = new CarryableItem[3];
            float pinSpacing = (frontW - 0.09f) / 2f;
            for (int i = 0; i < pins.Length; i++)
                pins[i] = BuildPin(bodyGO.transform, i,
                    new Vector3((i - 1) * pinSpacing, -frontH / 2f + 0.032f, trayD * 0.38f), drawerComp, pinItemId);

            return (drawerComp, pins);
        }

        // One pin: a slim needle on a dark handle. Parented to the drawer body, so it rides out with
        // the drawer instead of hanging in the air in front of a shut one, and sat forward in the
        // tray so an open drawer presents it.
        //
        // All three share ToolItemId, which is the point - a recorded "took the Tool" has to be
        // satisfiable by whichever one is going spare. They differ only in name, so the console can
        // say which object a ghost picked up.
        private static CarryableItem BuildPin(Transform drawerBody, int index, Vector3 localPos, Drawer drawer, string itemId)
        {
            GameObject toolRoot = new GameObject(index == 0 ? "BalloonTool" : $"BalloonTool_{index}");
            toolRoot.transform.SetParent(drawerBody, false);
            toolRoot.transform.localPosition = localPos;

            Material handleMat = MakeColorMaterial("ToolHandle", new Color(0.16f, 0.16f, 0.18f));
            Material pinMat = MakeColorMaterial("ToolPin", new Color(0.78f, 0.79f, 0.82f));
            SetSmoothness(pinMat, 0.7f);

            Prim(PrimitiveType.Cylinder, "Handle", toolRoot.transform, new Vector3(0f, 0f, -0.045f),
                new Vector3(0.028f, 0.045f, 0.028f), handleMat, removeCollider: true)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Prim(PrimitiveType.Cylinder, "Pin", toolRoot.transform, new Vector3(0f, 0f, 0.05f),
                new Vector3(0.006f, 0.05f, 0.006f), pinMat, removeCollider: true)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            BoxCollider toolTrigger = toolRoot.AddComponent<BoxCollider>();
            toolTrigger.isTrigger = true;
            toolTrigger.size = new Vector3(0.7f, 0.7f, 0.7f);

            CarryableItem toolItem = toolRoot.AddComponent<CarryableItem>();
            toolItem.itemId = itemId;
            toolItem.displayName = "PIN";
            toolItem.icon = PinIcon();
            // Nearer and higher than CarryableItem's default, which put almost none of the pin on
            // screen: at fov 60 a hold 0.42m out has 0.242m of half-height, so the default y of
            // -0.24 sat the tool's CENTRE on the bottom edge of the frame and cropped the handle
            // into the corner. 0.32m out puts half-height at 0.185m, so -0.125 is 68% down - the
            // whole tool inside the frame with room under it, and bigger for being closer.
            //
            // x is 0.14 rather than the 0.175 that looked right in the Editor, which runs a 2.03
            // ultrawide viewport: checked again at 16:9 the handle came within 5% of the right edge,
            // and 16:9 is the narrower case the build has to survive.
            //
            // The ROTATION is deliberately the tuned default, unchanged. It is what the swing arc
            // was built around - BalloonTool pitches about the item's local X from this rest pose, so
            // yawing it turns the chop into a sideways slash. Yawed versions also read worse, not
            // better: past about 25 degrees the handle turns broadside and becomes a black slab.
            toolItem.handLocalPosition = new Vector3(0.14f, -0.125f, 0.32f);
            toolItem.requiresOpenDrawer = drawer;
            // The nightstand's carcass, for the reason the billiard balls' is the chest's - see
            // CarryableItem.dropParent. A pin is normally used in the room it lives in, so this has
            // never visibly mattered; it is still the same latent fault.
            toolItem.dropParent = drawer.transform.parent;
            toolItem.audioSource = MakeSource(toolRoot.transform, "PickupAudio", 1f, 0.8f);
            toolItem.pickupClip = LoadClip(SfxDir, "sfx_item_pickup");

            return toolItem;
        }

        // The drum-shade lamp the imported nightstand carried, rebuilt from three cylinders. It gives
        // off no light - see BuildNightstand for why - so the shade is a pale matte solid rather than
        // anything emissive: a lamp that glowed without lighting the room would read as broken.
        private static void BuildNightstandLamp(Transform unit, Vector3 baseLocal)
        {
            Material metal = MakeColorMaterial("LampStem", new Color(0.12f, 0.09f, 0.07f));
            SetSmoothness(metal, 0.45f);
            Material shade = MakeColorMaterial("LampShade", new Color(0.87f, 0.85f, 0.79f));
            SetSmoothness(shade, 0.08f);

            GameObject lamp = new GameObject("Lamp");
            lamp.transform.SetParent(unit, false);
            lamp.transform.localPosition = baseLocal;

            // Cylinder primitives are 1 unit across and TWO tall, so the Y scale is a half-height.
            Prim(PrimitiveType.Cylinder, "Foot", lamp.transform, new Vector3(0f, 0.011f, 0f),
                new Vector3(0.092f, 0.011f, 0.092f), metal);
            Prim(PrimitiveType.Cylinder, "Stem", lamp.transform, new Vector3(0f, 0.10f, 0f),
                new Vector3(0.034f, 0.078f, 0.034f), metal, removeCollider: true);
            Prim(PrimitiveType.Cylinder, "Shade", lamp.transform, new Vector3(0f, 0.245f, 0f),
                new Vector3(0.20f, 0.072f, 0.20f), shade);
        }

        // The little Rubik's cube beside the lamp, swapped in for a potted plant 2026-08-13 - the
        // point is still just that the top of the nightstand is not bare, not anything about which
        // prop it is.
        //
        // `baseLocal` is where the pot's own root used to sit - `unit`'s local space, Y already at
        // the nightstand's top surface - so PlaceModel is handed unit.position + baseLocal as its
        // WORLD target rather than being reparented under a second empty the way the pot's two
        // primitives were. addBoxCollider false: a solid decorative object nobody can reach behind
        // the lamp does not need one, and it is one fewer collider on a footprint this small.
        // **THE NIGHTSTAND'S CUBE CAN BE PICKED UP NOW** (2026-08-31, by request), which is the whole
        // of what changed: it is `BuildDresserCube` at this unit's own size, rather than a second
        // builder placing the same model as scenery.
        //
        // It was `PlaceModel(..., addBoxCollider: false)` - a thing to look at and never touch. The
        // 90mm it came out at is kept, because a nightstand is smaller than a chest and the cube
        // that sits on it should be too, but 0.13 rather than 0.09: the old size was reported in play
        // as hard to see and harder to aim at on the CHEST, and this one has a lamp beside it
        // competing for the same press.
        //
        // **ITS OWN ID.** Cycle 1's cube is a trinket with nowhere to go; cycle 2's is 8.3kg on
        // room2-7's scale and part of the only solution to that room. They must never resolve to
        // each other through `ItemRegistry`.
        private static void BuildNightstandCube(Transform unit, Vector3 baseLocal)
        {
            BuildDresserCube(unit, "NightstandCube", baseLocal,
                             CycleOneBlockItemId, weighable: false, size: 0.13f);
        }
    }
}
