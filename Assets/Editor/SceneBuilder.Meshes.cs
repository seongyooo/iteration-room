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
    // GENERATED MESHES, AND THE CHECKS THAT READ THE FINISHED SCENE. A mesh's WINDING decides its
    // normals, and getting it backwards renders BLACK rather than as a hole - build one and LOOK at
    // it before trusting it (CLAUDE.md 3). The build-time assertions live here too: hint anchors,
    // ghost signal bits, walkability.
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {

        // Which silhouette a recess is cut to. Named rather than passed as a mesh, because the
        // caller is stating what the hole IS for, and where that mesh comes from is this file's
        // problem - two of the three are Unity primitives and the third has to be built.
        // `Dish` is the odd one out and says so: the other three are FLAT SILHOUETTES faked into
        // reading as wells, and this is a real hemispherical cavity with real geometry. Room2-0 needed
        // it because a flat disc says "something round belongs here" and the room has to say
        // "a BILLIARD BALL belongs here" - a bowl cut to the ball's own radius is the only shape that
        // can only be for a ball.
        private enum SlotShape { Square, Round, Triangle, Dish }

        // One recess: a coloured rim with a darker hole inside it, and a seat at the bottom for
        // whatever goes in.
        //
        // NOT A REAL HOLE, because there is no CSG here and the top plate is one box. What reads as
        // depth is the pair: a rim standing 12mm proud of the plate and a near-black floor 4mm above
        // it, so the eye takes the dark shape as the inside of a well. That is the same trick the
        // plinth's own TopPlate already plays - near-black among white surfaces reads as a cavity.
        // `reachCentre` / `reachSize` default to cycle 1's console, which is a 1.15m-deep block
        // approached from -Z. Room2-0's pedestals are 0.80 deep and approached from +Z, so the volume
        // has to lean the other way - a reach biased toward the side the player is NOT on is the exact
        // fault that made this recess unreachable on its first build.
        // `dishPlateHalf` is the cap's footprint, and only `SlotShape.Dish` uses it: that shape
        // replaces the caller's top plate rather than sitting on one, because a cavity has to have
        // material taken OUT of the thing above it and this project has no CSG. Everything else
        // ignores it.
        private static FinalSlot BuildFinalSlot(Transform parent, string name, SlotShape shape,
                                                Vector3 localTop, float size, Color accent,
                                                Vector3 reachCentre = default,
                                                Vector3 reachSize = default,
                                                Vector2 dishPlateHalf = default)
        {
            const float rimProud = 0.012f;
            const float floorProud = 0.004f;
            const float rimInset = 0.80f;   // the hole, as a fraction of the rim

            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localTop;

            // ONE material for all three rims. Each says something different about itself - idle,
            // ready, filled - and says it through a property block, which is what FinalSlot drives.
            Material rimMat = MakeEmissiveMaterial("FinalSlotRim", Color.white, 1f);
            Material holeMat = MakeColorMaterial("FinalSlotHole", new Color(0.03f, 0.03f, 0.035f));

            GameObject rim;
            GameObject seat = new GameObject("Seat");
            seat.transform.SetParent(root.transform, false);

            if (shape == SlotShape.Dish)
            {
                float bowl = size / 2f;
                // Enough material under the pole that the cap is a solid thing with a hole in it
                // rather than a shell that meets itself at a point.
                float thickness = bowl + 0.006f;

                // THE CAP: the top of the pedestal, with the bowl taken out of the middle of it. One
                // mesh, because the plate's top face and the bowl's inside are the same surface -
                // a hole is not a separate object, it is the absence of one.
                GameObject cap = new GameObject("Cap");
                cap.transform.SetParent(root.transform, false);
                cap.AddComponent<MeshFilter>().sharedMesh = BallSocketMesh(
                    $"BallSocket_{Mathf.RoundToInt(bowl * 1000f)}"
                    + $"_{Mathf.RoundToInt(dishPlateHalf.x * 1000f)}"
                    + $"x{Mathf.RoundToInt(dishPlateHalf.y * 1000f)}",
                    dishPlateHalf.x, dishPlateHalf.y, bowl, thickness);
                cap.AddComponent<MeshRenderer>().sharedMaterial = holeMat;

                // THE RING ROUND THE MOUTH, and it is the lit part. `FinalSlot` paints one renderer
                // to say idle / ready / filled / refused, and painting the whole cap would light a
                // dark inset the size of the pedestal top - the mark has to be AT the hole, which is
                // the thing the player is aiming at.
                GameObject ringGO = new GameObject("Rim");
                ringGO.transform.SetParent(root.transform, false);
                // A hair proud, or it z-fights the cap's top face along the whole ring.
                ringGO.transform.localPosition = new Vector3(0f, 0.0015f, 0f);
                ringGO.AddComponent<MeshFilter>().sharedMesh = AnnulusMesh(
                    $"BallSocketRing_{Mathf.RoundToInt(bowl * 1000f)}", bowl, bowl + 0.026f);
                ringGO.AddComponent<MeshRenderer>().sharedMaterial = rimMat;
                rim = ringGO;

                // AT THE MOUTH, WHICH IS THE BOWL'S OWN CENTRE - so a ball seated here sits exactly
                // half in and half out, the way a ball rests in a dish cut to its own radius. That is
                // the whole reason the bowl is a hemisphere and not a cup.
                seat.transform.localPosition = Vector3.zero;
            }
            else
            {
                rim = ShapePrim(shape, "Rim", root.transform,
                    new Vector3(0f, rimProud / 2f, 0f), new Vector3(size, rimProud, size), rimMat);
                ShapePrim(shape, "Hole", root.transform,
                    new Vector3(0f, floorProud / 2f + rimProud * 0.35f, 0f),
                    new Vector3(size * rimInset, floorProud, size * rimInset), holeMat);

                // Where the object lands. At the floor of the well rather than on the plate, so an
                // inserted object sits IN the recess - the seat is the socket CarryableItem parents to.
                seat.transform.localPosition = new Vector3(0f, rimProud * 0.35f + floorProud, 0f);
            }

            // THE REACH, and it has to reach past the console rather than sit over it. The recesses
            // are in the TOP of a 1.15m-deep block, so a volume centred on a recess is inside the
            // block: the nearest a player can physically get is pressed against the front face, which
            // is 0.45m short of where the first version's box began. It was unreachable, and only
            // standing at it in play found that - the geometry all measured correctly.
            //
            // Biased toward -Z, which is the way in from Room3, and wide enough that the three boxes
            // overlap. Overlapping is right: a player at the middle of the console is at all three
            // recesses, and which one answers is decided by what is in their hand.
            BoxCollider reach = root.AddComponent<BoxCollider>();
            reach.isTrigger = true;
            reach.center = reachCentre == Vector3.zero ? new Vector3(0f, -0.55f, -0.45f) : reachCentre;
            reach.size = reachSize == Vector3.zero ? new Vector3(1.5f, 2.6f, 2.7f) : reachSize;

            FinalSlot slot = root.AddComponent<FinalSlot>();
            slot.seat = seat.transform;
            slot.rimRenderer = rim.GetComponent<Renderer>();
            slot.idleColor = accent * 0.35f;
            slot.readyColor = accent;
            slot.filledColor = accent;
            slot.audioSource = MakeSource(root.transform, "SlotAudio", 1f, 0.8f);
            slot.insertClip = LoadClip(SfxDir, "sfx_floor_button_press");
            // acceptedItemId is deliberately left EMPTY. None of the three escape objects exists as
            // a carryable yet, and a slot that names an id nothing wears would prompt for a press
            // that cannot succeed. See TODO.md.
            return slot;
        }

        // A BALL SOCKET: a rectangular cap with a hemispherical bowl sunk into the middle of it.
        //
        // Its origin is the BOWL'S CENTRE, which is also the plate's top plane - so the caller places
        // it at the height the pedestal's top surface should be and everything else follows.
        //
        // **EVERY FACE IS DOUBLE-SIDED**, wound both ways with opposite normals, which is the same
        // thing `FlatPanelMesh` does and for a better reason here: a hand-derived winding is the one
        // kind of mistake in this file that cannot be checked without rendering it, and the cost is
        // nothing. Nothing is lost to z-fighting either - of any coincident pair exactly one is
        // front-facing from any camera, so the other is culled rather than fighting. The back faces
        // are all inside a sealed box (the cap's own walls and the pedestal body under it) and are
        // never seen.
        private static Mesh BallSocketMesh(string assetName, float halfX, float halfZ,
                                           float bowlRadius, float thickness)
        {
            return SaveGeneratedMesh(assetName, () =>
            {
                const int segments = 48;
                const int rings = 10;

                var verts = new System.Collections.Generic.List<Vector3>();
                var norms = new System.Collections.Generic.List<Vector3>();
                var tris = new System.Collections.Generic.List<int>();

                void Face(Vector3[] corners, Vector3 n)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        int o = verts.Count;
                        foreach (Vector3 c in corners) { verts.Add(c); norms.Add(side == 0 ? n : -n); }
                        for (int i = 1; i + 1 < corners.Length; i++)
                        {
                            if (side == 0) { tris.Add(o); tris.Add(o + i); tris.Add(o + i + 1); }
                            else { tris.Add(o); tris.Add(o + i + 1); tris.Add(o + i); }
                        }
                    }
                }

                // THE ANGLES ROUND THE CAP, and the four CORNERS are in the list on purpose: without
                // them a segment can straddle a corner, and the side wall built on it is a single
                // non-planar quad bridging two faces of the box.
                var angles = new System.Collections.Generic.List<float>();
                for (int i = 0; i < segments; i++) angles.Add(i / (float)segments * Mathf.PI * 2f);
                angles.Add(Mathf.Atan2(halfZ, halfX));
                angles.Add(Mathf.Atan2(halfZ, -halfX));
                angles.Add(Mathf.Atan2(-halfZ, -halfX) + Mathf.PI * 2f);
                angles.Add(Mathf.Atan2(-halfZ, halfX) + Mathf.PI * 2f);
                angles.Sort();

                // Where a ray at this angle leaves the rectangle.
                Vector3 OnEdge(float a)
                {
                    float c = Mathf.Cos(a), s = Mathf.Sin(a);
                    float t = Mathf.Min(halfX / Mathf.Max(0.0001f, Mathf.Abs(c)),
                                        halfZ / Mathf.Max(0.0001f, Mathf.Abs(s)));
                    return new Vector3(c * t, 0f, s * t);
                }
                Vector3 OnMouth(float a) =>
                    new Vector3(Mathf.Cos(a) * bowlRadius, 0f, Mathf.Sin(a) * bowlRadius);

                for (int i = 0; i < angles.Count; i++)
                {
                    float a0 = angles[i], a1 = angles[(i + 1) % angles.Count];
                    Vector3 e0 = OnEdge(a0), e1 = OnEdge(a1);
                    Vector3 m0 = OnMouth(a0), m1 = OnMouth(a1);

                    // The top face: the strip between the mouth of the bowl and the edge of the cap.
                    Face(new[] { m0, m1, e1, e0 }, Vector3.up);

                    // The side wall under that strip. Its normal is the BOX face it lies on, decided
                    // from the midpoint - which is unambiguous because the corners are vertices.
                    Vector3 mid = (e0 + e1) * 0.5f;
                    Vector3 outward = Mathf.Abs(Mathf.Abs(mid.x) - halfX) < 0.0005f
                        ? new Vector3(Mathf.Sign(mid.x), 0f, 0f)
                        : new Vector3(0f, 0f, Mathf.Sign(mid.z));
                    Vector3 down = Vector3.down * thickness;
                    Face(new[] { e0, e1, e1 + down, e0 + down }, outward);
                }

                // THE BOWL. Latitude bands from the mouth down to the pole, on the sphere centred on
                // this mesh's own origin - so a vertex's position IS its outward direction and the
                // face the player sees, which is the INSIDE, takes the negative of it.
                for (int r = 0; r < rings; r++)
                {
                    float lat0 = r / (float)rings * Mathf.PI * 0.5f;
                    float lat1 = (r + 1) / (float)rings * Mathf.PI * 0.5f;
                    float r0 = Mathf.Cos(lat0) * bowlRadius, y0 = -Mathf.Sin(lat0) * bowlRadius;
                    float r1 = Mathf.Cos(lat1) * bowlRadius, y1 = -Mathf.Sin(lat1) * bowlRadius;

                    for (int i = 0; i < angles.Count; i++)
                    {
                        float a0 = angles[i], a1 = angles[(i + 1) % angles.Count];
                        Vector3 p00 = new Vector3(Mathf.Cos(a0) * r0, y0, Mathf.Sin(a0) * r0);
                        Vector3 p01 = new Vector3(Mathf.Cos(a1) * r0, y0, Mathf.Sin(a1) * r0);
                        Vector3 p10 = new Vector3(Mathf.Cos(a0) * r1, y1, Mathf.Sin(a0) * r1);
                        Vector3 p11 = new Vector3(Mathf.Cos(a1) * r1, y1, Mathf.Sin(a1) * r1);

                        // The last band closes on the pole, where both inner points are the same
                        // vertex - a triangle rather than a quad, so no zero-area faces are emitted.
                        Vector3 n = -((p00 + p01 + p10 + p11) * 0.25f).normalized;
                        if (r1 < 0.0001f) Face(new[] { p00, p01, new Vector3(0f, y1, 0f) }, n);
                        else Face(new[] { p00, p01, p11, p10 }, n);
                    }
                }

                var mesh = new Mesh { name = assetName };
                mesh.indexFormat = verts.Count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                return mesh;
            });
        }

        // A FLAT RING, lying in the XZ plane about its own origin. The lit mark round the mouth of a
        // ball socket, and double-sided for the reason the socket is.
        private static Mesh AnnulusMesh(string assetName, float inner, float outer)
        {
            return SaveGeneratedMesh(assetName, () =>
            {
                const int segments = 48;
                var verts = new System.Collections.Generic.List<Vector3>();
                var norms = new System.Collections.Generic.List<Vector3>();
                var tris = new System.Collections.Generic.List<int>();

                for (int i = 0; i < segments; i++)
                {
                    float a0 = i / (float)segments * Mathf.PI * 2f;
                    float a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                    Vector3 i0 = new Vector3(Mathf.Cos(a0) * inner, 0f, Mathf.Sin(a0) * inner);
                    Vector3 i1 = new Vector3(Mathf.Cos(a1) * inner, 0f, Mathf.Sin(a1) * inner);
                    Vector3 o0 = new Vector3(Mathf.Cos(a0) * outer, 0f, Mathf.Sin(a0) * outer);
                    Vector3 o1 = new Vector3(Mathf.Cos(a1) * outer, 0f, Mathf.Sin(a1) * outer);

                    for (int side = 0; side < 2; side++)
                    {
                        int o = verts.Count;
                        foreach (Vector3 c in new[] { i0, i1, o1, o0 })
                        {
                            verts.Add(c);
                            norms.Add(side == 0 ? Vector3.up : Vector3.down);
                        }
                        if (side == 0) { tris.Add(o); tris.Add(o + 1); tris.Add(o + 2); tris.Add(o); tris.Add(o + 2); tris.Add(o + 3); }
                        else { tris.Add(o); tris.Add(o + 2); tris.Add(o + 1); tris.Add(o); tris.Add(o + 3); tris.Add(o + 2); }
                    }
                }

                var mesh = new Mesh { name = assetName };
                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                return mesh;
            });
        }

        // `bevelled` swaps the primitive for a generated chamfered version of the same silhouette.
        // Only the three escape objects ask for it - the recesses they go into keep the plain
        // shapes, because a hole is read as an outline and a chamfer on one is detail nobody looks
        // at. See BevelledPrismMesh for what the chamfer buys and what it costs.
        private static GameObject ShapePrim(SlotShape shape, string name, Transform parent,
                                            Vector3 localPos, Vector3 localScale, Material mat,
                                            bool keepCollider = false, bool bevelled = false)
        {
            if (bevelled)
            {
                GameObject bev = new GameObject(name);
                bev.transform.SetParent(parent, false);
                bev.transform.localPosition = localPos;
                bev.transform.localScale = localScale;
                bev.AddComponent<MeshFilter>().sharedMesh = BevelledPrismMesh(shape, 0.06f);
                bev.AddComponent<MeshRenderer>().sharedMaterial = mat;
                if (keepCollider) bev.AddComponent<BoxCollider>();
                return bev;
            }

            if (shape == SlotShape.Square)
                return Prim(PrimitiveType.Cube, name, parent, localPos, localScale, mat,
                            removeCollider: !keepCollider);

            if (shape == SlotShape.Round)
                // A Unity cylinder is 2 units tall at scale 1, so its Y has to be halved to mean the
                // same thing the cube's does. Everything else here states a thickness in metres.
                return Prim(PrimitiveType.Cylinder, name, parent, localPos,
                    new Vector3(localScale.x, localScale.y * 0.5f, localScale.z), mat,
                    removeCollider: !keepCollider);

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            // Chamfer 0: the same three-sided prism this always used, minus the inverted caps. Unity
            // has no triangular primitive, so this is the one shape of the three that has always had
            // to be generated.
            go.AddComponent<MeshFilter>().sharedMesh = BevelledPrismMesh(shape, 0f);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            // A box round a prism. The shape is what the player reads; what they bump into can be the
            // bounding box, and a MeshCollider for a triangle you walk past is a cost with no payer.
            if (keepCollider) go.AddComponent<BoxCollider>();
            return go;
        }

        // ONE GENERATOR FOR EVERY PRISM IN THE GAME: an n-gon extruded, optionally with its top and
        // bottom rims chamfered. Square is a 4-gon, Triangle a 3-gon, Round a 48-gon.
        //
        // It replaces TriangularPrismMesh, which it is a superset of at chamfer 0 - AND WHICH HAD
        // BOTH CAPS WOUND INSIDE OUT. Unity's front face is the one whose cross(v1-v0, v2-v0) points
        // along the normal (checked against the built-in Quad, whose winding and normals are known),
        // and that prism's caps pointed into the solid while its sides pointed out. The caps were
        // therefore backface-culled: looking down at the yellow triangle you were seeing through its
        // top face to the inside of the far side. It survived unnoticed because the material was
        // emissive at 2.4 and saturated, so the inside of the shape is very nearly the same image as
        // the outside - and it would NOT survive the polished metal above, which is what sent
        // anybody to look. Anything generated here now gets its winding checked against that Quad.
        //
        // WHY A CHAMFER AT ALL. A Unity cube has six flat faces meeting at perfectly sharp edges, so
        // under any lighting the whole of each face is one shade and the object is three flat tones
        // in a row. That is what makes a primitive look like a primitive, and no material fixes it,
        // because there is no geometry near the edge for a highlight to run along. A 6% chamfer puts
        // a narrow band at every rim facing halfway between two faces, and on a polished metal that
        // band catches a bright line that moves as the player turns. It is the cheapest thing in
        // rendering that reads as "manufactured" rather than "default".
        //
        // The same generator handles Round, which lets the three actually match: a puck with a
        // machined rim beside a chamfered cube beside a chamfered prism reads as one set of objects
        // from one factory, where a Unity cylinder's razor rim next to a chamfered cube does not.
        //
        // SMOOTHING IS PER SHAPE. Square and Triangle give every face its own vertices, so the edges
        // stay hard - the existing prism's rule, and a chamfer whose bands got smoothed into the
        // faces would be a lumpy cube rather than a bevelled one. Round shares its side vertices all
        // the way round, so RecalculateNormals averages across the barrel and the chamfers and the
        // puck comes out genuinely round instead of a 48-sided drum.
        //
        // SAVED AS AN ASSET, for the reason TriangularPrismMesh gives: a Mesh made with `new Mesh()`
        // and assigned in a scene is a runtime object with no home, and the scene serialises a
        // reference to nothing.
        //
        // NORMALISED TO A UNIT BOUNDING BOX like the prism it generalises, so a localScale of 0.30
        // still means 0.30 ACROSS for every one of the three. The barrel ring is the widest part, so
        // it is the ring the bounds are taken from.
        // EVERY BIT A GHOST WILL EVER READ, CHECKED AT BUILD TIME.
        //
        // `RecordedFrame.signals` is one `uint`, so an interactable's index IS its bit and there are
        // 32 of them. `PlayerRecorder.SampleSignals` clamps to that width and DROPS the rest, which
        // is the correct thing for it to do at runtime and a terrible way to find out: the 33rd
        // fixture simply never records, the ghosts never operate it, and nothing about the room it is
        // in looks wrong. Said here instead, where the array is known exactly and a build log is
        // already being read.
        //
        // Two more faults are the same shape and cost nothing to catch alongside it. A NULL entry is
        // a bit that can never be set - usually a builder that returned null because its object was
        // not built - and it silently shifts nothing, so it is a wasted bit rather than a corruption.
        // A DUPLICATE entry is worse: one fixture on two bits, where the second bit's rising edge
        // fires a second time on a ghost that only ever did the thing once.
        // **EVERY PROMPT ANCHOR, CHECKED AGAINST THE SOLID THINGS THAT ARE NOT FORGIVEN.**
        //
        // Written 2026-08-29 after the same bug shipped TWICE in two days, and both times it was
        // found by a person playing rather than by anything here.
        //
        // The shape of it: `PlayerLookup.InView` refuses a press whose anchor is behind geometry, and
        // it forgives the fixture's OWN geometry by walking up from the anchor to the nearest
        // `IInteractHintTarget`. A fixture that implements no such interface - `WaterTank` is a
        // `RoomCondition` - resolves to the anchor itself, so its own body is not forgiven and **it
        // occludes its own prompt.** Play reported that as "you have to jump to interact with it",
        // which is a description of a raycast clearing a rim and reads like nothing at all.
        //
        // **THE PREDICATE IS: BURIED IN A SOLID THE FIXTURE DOES NOT OWN, DEEPER THAN THE RAY STOPS
        // SHORT.** An anchor that far inside is occluded from essentially every angle - no camera
        // position helps, short of getting inside the collider.
        //
        // **THE FIRST VERSION LEFT THE SECOND HALF OFF AND WAS 39/39 NOISE.** It flagged every object
        // resting ON a surface, because a carryable's anchor is its own origin and its `floorY` is
        // zero - so the anchor sits on the floor plane, technically inside the floor's bounds, and
        // `Occluded` already forgives precisely that (`SurfaceClearance`). A check that does not know
        // what has already been fixed reports the fix.
        //
        // **IT DOES NOT CATCH EVERYTHING, and saying so is the point.** An anchor a centimetre in
        // FRONT of its own unforgiven body passes here and still fails from a low angle. This is the
        // sharp end of the class, not the whole of it - a player is still the instrument for the rest.
        // The same number `PlayerLookup.SurfaceClearance` uses, restated here because that one is
        // private runtime code and a build check that reached into it would break the moment either
        // moved. If they ever disagree this check gets quieter, never louder - which is the direction
        // a duplicated constant should fail in.
        private const float AnchorSurfaceClearance = 0.05f;

        private static void CheckHintAnchors()
        {
            var colliders = new System.Collections.Generic.List<Collider>();
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                colliders.AddRange(root.GetComponentsInChildren<Collider>(true));

            int checked_ = 0, bad = 0;

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (!(behaviour is IInteractHintTarget target)) continue;

                    Transform anchor = target.HintAnchor;
                    if (anchor == null) continue;
                    checked_++;

                    // The same resolution `PlayerLookup.OwnerObject` performs: the nearest ancestor
                    // that claims the press. Duplicated rather than shared because that one is
                    // runtime code and this is a build check, and a check that calls the thing it is
                    // checking proves nothing.
                    var owner = anchor.GetComponentInParent<IInteractHintTarget>() as Component;
                    Transform ownerTransform = owner != null ? owner.transform : anchor;

                    foreach (Collider c in colliders)
                    {
                        if (c == null || c.isTrigger || !c.enabled) continue;
                        if (c.transform.IsChildOf(ownerTransform)) continue;
                        // The player's own capsule is never an occluder either.
                        if (c.transform.root.CompareTag("Player")) continue;
                        if (!c.bounds.Contains(anchor.position)) continue;

                        // **AND DEEPER IN THAN THE RAY'S OWN CLEARANCE, or it is not a problem.**
                        //
                        // The first run of this check reported 39 of 110 and every one was the same
                        // thing: an object RESTING ON a surface. A carryable's anchor is its own
                        // origin and its `floorY` is how far that origin sits above the floor - zero
                        // for a chess piece, an axe, a bucket - so the anchor is on the floor plane
                        // and inside the floor's bounds by a hair.
                        //
                        // `PlayerLookup.Occluded` already forgives exactly that: it drops the last
                        // `SurfaceClearance` of the cast, because the surface a thing stands on is not
                        // hiding it. A check that does not know about that mitigation reports the
                        // thing it fixed, which is worse than reporting nothing - a warning list that
                        // is 39/39 noise is a warning list nobody reads.
                        //
                        // So the question is not "is the anchor inside" but "is it buried DEEPER than
                        // the ray stops short". Measured off the bounds rather than the mesh, which is
                        // conservative in the safe direction: an AABB is never smaller than the shape.
                        Vector3 lo = anchor.position - c.bounds.min;
                        Vector3 hi = c.bounds.max - anchor.position;
                        float buried = Mathf.Min(
                            Mathf.Min(lo.x, hi.x), Mathf.Min(Mathf.Min(lo.y, hi.y), Mathf.Min(lo.z, hi.z)));
                        if (buried <= AnchorSurfaceClearance) continue;

                        bad++;
                        Debug.LogWarning(
                            $"[SceneBuilder] PROMPT ANCHOR INSIDE UNFORGIVEN GEOMETRY: "
                          + $"{behaviour.GetType().Name} on '{behaviour.name}' has its anchor "
                          + $"'{anchor.name}' at {anchor.position} inside the collider on "
                          + $"'{c.name}', which is not under '{ownerTransform.name}'. "
                          + "`PlayerLookup.InView` will refuse this press from most angles - the "
                          + "symptom is a prompt that only appears if the player jumps or crouches. "
                          + "Fix by moving the anchor clear, or by passing the owning fixture to the "
                          + "`InView(anchor, owner)` overload.");
                        break;
                    }
                }

            Debug.Log($"[SceneBuilder] Hint anchors checked: {checked_}, {bad} inside geometry their "
                    + "own fixture does not own. Anything but 0 is a prompt the player has to find a "
                    + "camera angle for.");
        }

        private static void CheckGhostSignals(string cycleName, GhostInteractable[] signals)
        {
            if (signals == null) return;

            if (signals.Length > 32)
            {
                Debug.LogError($"[SceneBuilder] {cycleName} wires {signals.Length} ghost interactables, "
                    + "and RecordedFrame.signals is a uint - 32 is the hard cap. Everything from index "
                    + $"32 on ({signals[32]?.name ?? "null"} first) is dropped by "
                    + "PlayerRecorder.SampleSignals and will never be replayed by a ghost. Widen the "
                    + "mask to a ulong (RecordedFrame, PlayerRecorder, GhostReplayer) or spend fewer "
                    + "bits - see CLAUDE.md 1.6.");
            }

            for (int i = 0; i < signals.Length; i++)
            {
                if (signals[i] == null)
                {
                    Debug.LogError($"[SceneBuilder] {cycleName} ghost interactable {i} is NULL - "
                        + "that bit can never be set, and every entry after it is one a ghost will "
                        + "read but nothing will ever write.");
                    continue;
                }

                for (int j = i + 1; j < signals.Length; j++)
                {
                    if (signals[j] != signals[i]) continue;
                    Debug.LogError($"[SceneBuilder] {cycleName} wires '{signals[i].name}' at both bit "
                        + $"{i} and bit {j}. One fixture on two bits fires its rising edge twice.");
                }
            }
        }

        // CAN THIS CYCLE ACTUALLY BE FINISHED? Asked as a build error, for the same reason
        // `CheckGhostSignals` is: the failure is silent and the build log is already being read.
        //
        // A cycle ends by putting the objects its console asks for into that console, and the console
        // asks by ID. Nothing checks that an object wearing that id exists - `FinalSlot` registers as
        // a socket for it, `ItemRegistry` happily holds a socket with no supply, and the room simply
        // waits forever. Cycle 2 is in exactly that state as this is written: its console declares
        // three shards, `BuildRingShard` is never called, and the only way to discover that is to walk
        // the whole cycle carrying nothing.
        //
        // Scoped to the cycle's own root, because an id is only reachable from inside the cycle that
        // holds it - one cycle is awake at a time, and a carryable in a sleeping one is unregistered.
        private static void CheckCycleFinishable(string cycleName, Transform cycleRoot,
                                                 FinalRoomSequence final)
        {
            if (cycleRoot == null || final == null || final.slots == null) return;

            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (CarryableItem item in cycleRoot.GetComponentsInChildren<CarryableItem>(true))
                if (item != null && !string.IsNullOrEmpty(item.itemId)) ids.Add(item.itemId);

            int declared = 0;
            foreach (FinalSlot slot in final.slots)
            {
                if (slot == null || !slot.Declared) continue;
                declared++;
                if (ids.Contains(slot.AcceptedItemId)) continue;

                Debug.LogError($"[SceneBuilder] {cycleName}'s console declares a slot for "
                    + $"'{slot.AcceptedItemId}' and NOTHING IN THE CYCLE WEARS THAT ID. The cycle "
                    + "cannot be finished: the slot waits for an object that does not exist, and the "
                    + "only symptom is a player walking the whole cycle finding nothing to carry.");
            }

            if (declared == 0)
                Debug.LogWarning($"[SceneBuilder] {cycleName}'s console declares no slots at all, so "
                    + "nothing can complete it. That is correct only for a cycle still being designed.");
        }

        // CAN THE PLAYER ACTUALLY WALK THROUGH HERE? Asked as a build error rather than left for
        // somebody to find by walking.
        //
        // It has been got wrong twice already - three chorus levers went squarely in front of both of
        // room2's doors, and a hatch lid was left out of a ceiling - and both times the only thing
        // that caught it was a play-through. That is the most expensive test there is for the
        // cheapest possible mistake, when every position involved is known exactly, here.
        //
        // **A SWEPT SPHERE RATHER THAN BOUNDS ARITHMETIC**, and the first attempt is why. Comparing
        // AABBs against a doorway band flagged the floor, the ceiling, all four walls and the door
        // slab itself - twenty false errors, which is worse than no check at all because it teaches
        // everyone to ignore the output. Walking a sphere the size of the player through the gap
        // asks the only question that matters and cannot be fooled by a large bounding box.
        //
        // **IT CHECKS THE DOORWAY, NOT THE ROOM.** The second attempt swept right across each room
        // and flagged the bed - which is furniture in the middle of a room, walked around rather than
        // through. This is not a pathfinder and should not pretend to be one; what it is for is the
        // approach to an opening, which is where the mistakes it exists to catch actually happen.
        // WHAT THE PLAYER IS ACTUALLY STOPPED BY - the same mask `BuildPlayer` gives the
        // CharacterController, and asking anything else makes both asserts below liars.
        //
        // Swept with `~0` these checked layers the player walks straight THROUGH, and room2-6 is where
        // that surfaced: 210 balls, 3 ducks and 2 beach balls float in its pool on the balloon layer,
        // which `cc.excludeLayers` removes outright precisely so there is no invisible wall in the
        // water. One ball drifting into the doorway at build time therefore failed the walk with
        // `'Solid' blocks the way through` - naming a `MakeFloatBody` collider that cannot block
        // anybody - and the room was reported as impassable for days while being perfectly walkable in
        // play. A false error is worse than no check: this one taught its reader to skip the output,
        // which is the failure mode the bounds-arithmetic version was already rewritten to avoid.
        private static int PlayerBlockingMask() => ~(1 << EnsureLayer(BalloonLayerName));

        private static void AssertWalkable(Transform roomRoot, string label, Vector3 localFrom, Vector3 localTo)
        {
            // Colliders built this frame are not in the physics scene until it is told about them.
            Physics.SyncTransforms();

            const float radius = 0.34f;      // the controller's 0.3, plus a little
            Vector3 from = roomRoot.TransformPoint(localFrom + Vector3.up * 0.95f);
            Vector3 to = roomRoot.TransformPoint(localTo + Vector3.up * 0.95f);

            int steps = Mathf.CeilToInt(Vector3.Distance(from, to) / (radius * 0.8f));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 at = Vector3.Lerp(from, to, i / (float)steps);
                Collider[] hits = Physics.OverlapSphere(at, radius, PlayerBlockingMask(),
                                                        QueryTriggerInteraction.Ignore);
                foreach (Collider hit in hits)
                {
                    // Things that are MEANT to be in the way, and open. A door slab is the doorway;
                    // the tree is the hall's entire puzzle, standing where it stands until it is felled.
                    //
                    // `Tree` USED TO BE NAMED HERE AND WAS SILENTLY `UnityEngine.Tree` - the project's
                    // own Tree went out with the reverted cycle-2 puzzle set and the terrain type took
                    // the name over without a compile error. The check has been doing nothing since.
                    if (hit.GetComponentInParent<Door>() != null) continue;
                    if (hit.GetComponentInParent<CycleExit>() != null) continue;
                    // Room3-1's north corridor is SOLID until a pad is held - that is the room, not a
                    // fault. What this check is for there is the geometry around the block: the two
                    // wall cutouts, the corridor floor and its side walls. Exactly the same reason a
                    // door slab is skipped.
                    if (hit.GetComponentInParent<CrushingBarrier>() != null) continue;
                    if (hit.GetComponentInParent<TreeTrunk>() != null) continue;
                    Debug.LogError($"[SceneBuilder] {label}: '{hit.name}' blocks the way through "
                                 + $"(swept at {at}).");
                    return;
                }
            }
        }

        // THE OPPOSITE ASSERT, and the only place in the project that wants one: a gap the player
        // must NOT be able to walk over. `AssertWalkable` proves a doorway is clear; this proves a
        // pit is still a pit, which is a thing a stray collider or a mis-signed Rect can quietly
        // undo - and would undo the whole room without any other symptom.
        private static void AssertNotWalkable(Transform roomRoot, string label, Vector3 localFrom, Vector3 localTo)
        {
            Physics.SyncTransforms();

            const float radius = 0.34f;
            // Swept at ANKLE height, not the chest height `AssertWalkable` uses: what is being looked
            // for is floor under the player, and a chest-high sweep through an open shaft finds
            // nothing whether or not there is a floor beneath it.
            Vector3 from = roomRoot.TransformPoint(localFrom + Vector3.up * 0.15f);
            Vector3 to = roomRoot.TransformPoint(localTo + Vector3.up * 0.15f);

            int steps = Mathf.CeilToInt(Vector3.Distance(from, to) / (radius * 0.8f));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 at = Vector3.Lerp(from, to, i / (float)steps);
                // Same mask as `AssertWalkable`, and for a sharper reason here: a floating ball is not
                // floor. Swept with `~0`, one drifting over the pit would report the hole as spanned
                // and hide a genuinely missing floor behind a prop the player falls straight past.
                foreach (Collider hit in Physics.OverlapSphere(at, radius, PlayerBlockingMask(),
                                                               QueryTriggerInteraction.Ignore))
                {
                    // The bridge is built disabled, so it cannot be what is found here - and if it
                    // ever is, that is exactly the bug this assert exists to catch.
                    Debug.LogError($"[SceneBuilder] {label}: '{hit.name}' spans the pit that is "
                                 + $"supposed to be impassable (swept at {at}).");
                    return;
                }
            }
        }

        // ONE SHARD, as a carryable. `BuildPin` is the template this follows - the simplest complete
        // carryable in the project.
        //
        // The mesh is shared; only the yaw differs, and it is the yaw that says which third of the
        // ring this is. Held, they all look alike, which is correct: what the player is collecting is
        // three of a thing, not three different things.
        private static CarryableItem BuildRingShard(Transform parent, string name, string itemId,
                                                     Vector3 localPos, float yaw, Mesh mesh, Material mat)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            // The mesh on a CHILD, carrying the yaw. The item's own transform stays axis-aligned so
            // the hand pose and the reach trigger are read off something unrotated - the same
            // separation `MakeChessPiece` makes for the mirrored pieces.
            GameObject body = new GameObject("Body");
            body.transform.SetParent(go.transform, false);
            body.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            body.AddComponent<MeshRenderer>().sharedMaterial = mat;

            // The reach volume, on the item itself - CarryableItem takes GetComponent<Collider>() as
            // its trigger, so this must be the only collider on this object.
            BoxCollider reach = go.AddComponent<BoxCollider>();
            reach.isTrigger = true;
            reach.center = new Vector3(0f, 0.12f, 0f);
            reach.size = new Vector3(0.9f, 0.5f, 0.9f);

            CarryableItem item = go.AddComponent<CarryableItem>();
            item.itemId = itemId;
            item.displayName = "SHARD";
            item.floorY = 0.03f;
            item.handLocalPosition = HandPoseFor(0.45f);
            item.handLocalEuler = new Vector3(12f, 0f, 0f);
            item.audioSource = MakeSource(go.transform, "ShardAudio", spatialBlend: 1f, volume: 0.8f);
            item.pickupClip = LoadClip(SfxDir, "sfx_item_pickup");
            return item;
        }

        // A THIRD OF A RING: the shape all three of cycle 2's escape objects share.
        //
        // Generated rather than composed out of primitives, because the one thing this shape has to
        // do is look BROKEN OFF - an arc with two flat radial faces where it parted from its
        // neighbours. A box approximation gives a staircase on the inner and outer curves, which
        // reads as a low-poly wedge rather than as a piece of something.
        //
        // Saved as an asset and served from cache on the next build, exactly as `BevelledPrismMesh`
        // is: a Mesh created at edit time and assigned to a scene object is lost on reload unless it
        // lives somewhere.
        //
        // All three shards are the SAME mesh, rotated. They are thirds of one ring, so they are
        // identical by construction - and a puzzle that asked the player to tell them apart would be
        // cycle 1's shape-matching again under a new name.
        private static Mesh RingShardMesh(float sweepDegrees, float innerRadius, float outerRadius, float thickness)
        {
            string assetName = $"RingShard{Mathf.RoundToInt(sweepDegrees)}";
            string path = GeneratedDir + "/" + assetName + ".mesh";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            const int segments = 24;
            float half = thickness / 2f;
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();

            // Four rings of vertices - inner and outer, top and bottom - walked together so every
            // face of the solid can be stitched from the same index arithmetic.
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.Deg2Rad * (-sweepDegrees / 2f + sweepDegrees * i / segments);
                float sin = Mathf.Sin(a), cos = Mathf.Cos(a);
                verts.Add(new Vector3(sin * innerRadius, half, cos * innerRadius));
                verts.Add(new Vector3(sin * outerRadius, half, cos * outerRadius));
                verts.Add(new Vector3(sin * innerRadius, -half, cos * innerRadius));
                verts.Add(new Vector3(sin * outerRadius, -half, cos * outerRadius));
            }

            void Quad(int a, int b, int c, int d)
            {
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(d);
            }

            for (int i = 0; i < segments; i++)
            {
                int p0 = i * 4, p1 = (i + 1) * 4;
                Quad(p0 + 0, p0 + 1, p1 + 1, p1 + 0);   // top
                Quad(p0 + 2, p1 + 2, p1 + 3, p0 + 3);   // bottom
                Quad(p0 + 1, p0 + 3, p1 + 3, p1 + 1);   // outer curve
                Quad(p0 + 0, p1 + 0, p1 + 2, p0 + 2);   // inner curve
            }

            // THE BROKEN ENDS. Flat radial faces, and the whole reason this is not a torus segment
            // with open sides - they are what the eye reads as a fracture.
            int last = segments * 4;
            Quad(0, 2, 3, 1);
            Quad(last + 1, last + 3, last + 2, last + 0);

            Mesh mesh = new Mesh { name = assetName };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (!Directory.Exists(GeneratedDir)) Directory.CreateDirectory(GeneratedDir);
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        // A WALL PANEL WITH ITS FRONT RIM CHAMFERED, at true size.
        //
        // **THE ARGUMENT IS ALREADY IN THIS FILE, one screen down, and it was only ever applied to
        // three objects.** `BevelledPrismMesh` says it: a Unity cube's faces meet at perfectly sharp
        // edges, so each face is one flat shade under any lighting, "and no material fixes it,
        // because there is no geometry near the edge for a highlight to run along". That was written
        // about the escape objects. The building is made of some three thousand cubes.
        //
        // Here it is worth more than it is there, because of what the panels already are: each one
        // stands `GrooveDepth` proud of its backing, so the building is a grid of raised rectangles
        // with a shadow line around every one. Chamfering the front rim puts a lit line inside every
        // one of those shadow lines - thousands of them - and that pairing is most of what separates
        // a photographed wall from an extruded one.
        //
        // **ONLY THE FRONT RIM.** The back face is buried against the backing slab and the four side
        // walls are the groove itself; neither is where the light is. Four edges, not twelve.
        //
        // **TRUE SIZE, NOT A SCALED UNIT CUBE, and that is the whole reason this exists as a mesh per
        // size.** A panel is 1.72 x 1.32 x 0.025: a chamfer written as a fraction and scaled with the
        // box comes out 100mm across the face and 1.5mm through the depth, which is not a chamfer, it
        // is a wedge. The metres have to survive into the vertices.
        //
        // Cached by size, so the grid's identical cells share one asset and go on batching - the
        // partial panels around doorways are the only ones that mint new ones. The build logs how
        // many distinct meshes it ended up with; if that number is ever in the hundreds, the sizes
        // have stopped repeating and this trade needs looking at again.
        // **A DISC WITH A HOLE THROUGH IT - and it has to be a generated mesh, because nothing else
        // here can make a hole.**
        //
        // The socket's grey face was a `Cylinder` primitive with a dark cylinder behind it, and play
        // reported the obvious consequence: "홈이 안보여" - the notch is invisible. Of course it is. A
        // solid disc in front of a dark one hides it completely; there is no CSG in this project, so
        // "put the dark thing behind" can never produce a hole. Either the geometry has a hole in it
        // or the picture does not.
        //
        // `SymbolSlot` solves the same problem for a SQUARE recess by standing four bars proud of the
        // wall and putting the glyph at the bottom of the well between them - a real hollow made out
        // of what a primitive can do. A round one has no such trick: forty bars in a circle is a
        // polygon nobody asked for. So this is an annulus, extruded, with its inner wall facing the
        // axis so you can see down the bore from in front.
        //
        // **THE WINDING IS EXPLICIT AND THE NORMALS ARE SET BY HAND**, rather than left to
        // `RecalculateNormals`. CLAUDE.md records what a backwards winding costs here: it does not
        // read as a geometry bug, it renders BLACK, and it cost this project a day of chasing
        // lighting theories. Each of the three surfaces is derived from Unity's own convention -
        // normal = (v1-v0) x (v2-v0), checked against the built-in Quad - and written down below.
        private static Mesh RingMesh(float outerRadius, float innerRadius, float depth)
        {
            int ko = Mathf.RoundToInt(outerRadius * 10000f);
            int ki = Mathf.RoundToInt(innerRadius * 10000f);
            int kd = Mathf.RoundToInt(depth * 10000f);

            string assetName = $"Ring_{ko}_{ki}_{kd}";
            string path = GeneratedDir + "/" + assetName + ".mesh";

            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            // Enough that the bore reads as round at the half-metre the player gets to it, and few
            // enough that the whole fixture is under a thousand triangles.
            const int segments = 48;

            var verts = new System.Collections.Generic.List<Vector3>();
            var norms = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();

            // Local +Z is OUT of the wall, and the ring is extruded back to -Z. The caller orients it
            // with `LookRotation(inward)`, which maps local +Z onto the direction the socket faces.
            Vector3 At(float radius, int i, float z)
            {
                float a = 2f * Mathf.PI * i / segments;
                return new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, z);
            }
            Vector3 Radial(int i)
            {
                float a = 2f * Mathf.PI * i / segments;
                return new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            }

            void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
            {
                int at = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                norms.Add(na); norms.Add(nb); norms.Add(nc);
                tris.Add(at); tris.Add(at + 1); tris.Add(at + 2);
            }

            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                Vector3 ri = Radial(i), rj = Radial(j);
                Vector3 outI = At(outerRadius, i, 0f), outJ = At(outerRadius, j, 0f);
                Vector3 inI = At(innerRadius, i, 0f), inJ = At(innerRadius, j, 0f);
                Vector3 outIb = At(outerRadius, i, -depth), outJb = At(outerRadius, j, -depth);
                Vector3 inIb = At(innerRadius, i, -depth), inJb = At(innerRadius, j, -depth);

                // THE FRONT FACE, an annulus whose normal points out of the wall. The order is
                // checked rather than guessed: with normal = (v1-v0) x (v2-v0) - Unity's own
                // convention, verified against its built-in Quad - (outer_i, inner_j, inner_i)
                // crosses to +Z and the reverse of it to -Z.
                Tri(outI, inJ, inI, Vector3.forward, Vector3.forward, Vector3.forward);
                Tri(outI, outJ, inJ, Vector3.forward, Vector3.forward, Vector3.forward);

                // THE BORE'S WALL, facing the axis - the surface that says the hole has DEPTH, and
                // the whole reason this is a mesh rather than a flat ring.
                Tri(inI, inJb, inIb, -ri, -rj, -ri);
                Tri(inI, inJ, inJb, -ri, -rj, -rj);

                // THE OUTER WALL, facing away from the axis: the rim's own thickness seen from the
                // side, which is what makes the face read as standing PROUD of the plate.
                Tri(outI, outIb, outJb, ri, ri, rj);
                Tri(outI, outJb, outJ, ri, rj, rj);
            }

            Mesh mesh = new Mesh { name = assetName };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            if (!Directory.Exists(GeneratedDir)) Directory.CreateDirectory(GeneratedDir);
            AssetDatabase.CreateAsset(mesh, path);
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        // A ring standing on a surface, with a genuine hole through it. `inward` is the way the face
        // points; the mesh is built about local +Z and turned onto it.
        private static GameObject MakeRing(Transform parent, string name, Vector3 localPos,
                                           Vector3 inward, float outerRadius, float innerRadius,
                                           float depth, Material mat)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.LookRotation(inward);

            go.AddComponent<MeshFilter>().sharedMesh = RingMesh(outerRadius, innerRadius, depth);
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            return go;
        }

        private static Mesh ChamferedPanelMesh(float width, float height, float depth, float chamfer)
        {
            // Rounded to the tenth of a millimetre BEFORE it becomes a key, or floating point turns
            // one panel size into a dozen assets that differ in the seventh decimal.
            int kw = Mathf.RoundToInt(width * 10000f);
            int kh = Mathf.RoundToInt(height * 10000f);
            int kd = Mathf.RoundToInt(depth * 10000f);
            int kc = Mathf.RoundToInt(chamfer * 10000f);

            string assetName = $"Panel_{kw}_{kh}_{kd}_{kc}";
            string path = GeneratedDir + "/" + assetName + ".mesh";
            panelMeshNames.Add(assetName);

            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            float hx = width / 2f, hy = height / 2f, hz = depth / 2f;
            // Never eat the whole panel. A sliver beside a doorway can be narrower than the chamfer
            // wants to be, and a chamfer wider than half the piece inverts it.
            float c = Mathf.Min(chamfer, Mathf.Min(hx, hy) * 0.45f);
            c = Mathf.Min(c, depth * 0.5f);

            // The front face, inset by the chamfer, and the rim it meets the sides at. Connecting two
            // concentric rectangles corner to corner gives four quads and needs no corner facet - the
            // twist at each corner IS the mitre.
            Vector3[] front =
            {
                new Vector3(-(hx - c), -(hy - c), hz), new Vector3(hx - c, -(hy - c), hz),
                new Vector3(hx - c, hy - c, hz), new Vector3(-(hx - c), hy - c, hz),
            };
            Vector3[] rim =
            {
                new Vector3(-hx, -hy, hz - c), new Vector3(hx, -hy, hz - c),
                new Vector3(hx, hy, hz - c), new Vector3(-hx, hy, hz - c),
            };
            Vector3[] back =
            {
                new Vector3(-hx, -hy, -hz), new Vector3(hx, -hy, -hz),
                new Vector3(hx, hy, -hz), new Vector3(-hx, hy, -hz),
            };

            var verts = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var tris = new System.Collections.Generic.List<int>();

            // **UV IS AN XY PROJECTION ACROSS THE WHOLE PANEL, not a face-by-face 0-1.** The cube this
            // replaces gave its front face exactly 0..1 and the panel materials tile against that
            // (`ApplySurfaceDetail(panelMat, ..., new Vector2(5f, 3f), ...)`). This reproduces it to
            // within the chamfer - 6mm on a 1.72m panel is three thousandths of a UV - and has the
            // advantage of running continuously over the rim, so the grain does not break at the
            // edge the way per-face mapping would.
            void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d)
            {
                int at = verts.Count;
                foreach (Vector3 v in new[] { a, b, cc, d })
                {
                    verts.Add(v);
                    uvs.Add(new Vector2((v.x + hx) / width, (v.y + hy) / height));
                }
                // **WOUND THIS WAY ROUND BECAUSE THE OTHER WAY BUILT THE BUILDING INSIDE OUT**, and
                // it shipped that way for a day (2026-08-25). `a,b,cc,d` arrive counter-clockwise
                // seen from OUTSIDE the solid, and Unity treats clockwise-from-the-front as
                // front-facing - so `(0,2,1) / (0,3,2)`, which is what this was, faces every quad
                // backwards. `RecalculateNormals` derives normals from exactly this, so every panel
                // in the game got a normal pointing INTO the wall and the face the room can see was
                // shaded as though it faced away from every light in it. **Result: a white building
                // with pure black walls**, floors and ceilings untouched because they are plain
                // slabs and never came through here.
                //
                // The comment that used to sit here claimed the winding was correct and cited
                // `BevelledPrismMesh` learning it the hard way - the same mistake, in the same
                // project, caught the first time only because that mesh was emissive. **Do not
                // reason about winding from the comment. Build it and look at it.**
                tris.Add(at); tris.Add(at + 1); tris.Add(at + 2);
                tris.Add(at); tris.Add(at + 2); tris.Add(at + 3);
            }

            Quad(front[0], front[1], front[2], front[3]);                 // the face
            for (int i = 0; i < 4; i++)                                   // the chamfer, four quads
            {
                int j = (i + 1) % 4;
                Quad(rim[i], rim[j], front[j], front[i]);
            }
            for (int i = 0; i < 4; i++)                                   // the groove walls
            {
                int j = (i + 1) % 4;
                Quad(back[i], back[j], rim[j], rim[i]);
            }
            Quad(back[3], back[2], back[1], back[0]);                     // the buried face

            Mesh mesh = new Mesh { name = assetName };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            // Every face has its own vertices, so this leaves the edges hard - a chamfer smoothed
            // into the face it borders is a rounded-off cube, not a bevelled one. Same rule
            // `BevelledPrismMesh` states for Square and Triangle.
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            if (!Directory.Exists(GeneratedDir)) Directory.CreateDirectory(GeneratedDir);
            AssetDatabase.CreateAsset(mesh, path);
            panelMeshesBuilt++;
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        // WHICH distinct panel sizes the building turned out to have, and how many of them this run
        // had to generate. The two differ on every build after the first - the meshes are assets, so
        // a rebuild serves them from disk - and reporting only the second reads as "0 panels" on a
        // healthy build. See `ChamferedPanelMesh` for why the count is worth watching at all.
        private static readonly System.Collections.Generic.HashSet<string> panelMeshNames =
            new System.Collections.Generic.HashSet<string>();
        private static int panelMeshesBuilt;

        // One chamfered panel, in place of the cube `Prim` would have made.
        //
        // **SCALE STAYS AT ONE, and the rotation is what places it.** The metres live in the mesh -
        // that is the whole point of a mesh per size - so the transform may not stretch it. What the
        // transform does instead is turn it to face the way the wall does.
        //
        // `inward` points INTO the room from the wall face, and the mesh is built facing +Z, so
        // that is the look direction. NOT the `depthAxis` the wall also carries: that one is
        // componentwise absolute, so it has the panel's thickness but no idea which side of the wall
        // the room is on, and a panel built against it faces out of the building half the time.
        private static GameObject ChamferedPanel(string name, Transform parent, Vector3 localPos,
                                                 float width, float height, float depth,
                                                 Vector3 inward, Material mat)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.LookRotation(inward, Vector3.up);
            go.transform.localScale = Vector3.one;

            go.AddComponent<MeshFilter>().sharedMesh =
                ChamferedPanelMesh(width, height, depth, PanelChamfer);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static Mesh BevelledPrismMesh(SlotShape shape, float chamfer)
        {
            // The chamfer is in the asset name, so a shape and its bevelled twin are two assets and
            // neither can be served from the other's cache. It is also what retires the old
            // TriangularPrism.mesh without having to find and delete it: nothing asks for that path
            // any more.
            string assetName = $"Prism{shape}{Mathf.RoundToInt(chamfer * 100f)}";
            string path = GeneratedDir + "/" + assetName + ".mesh";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            // Round gets enough segments that the silhouette is a circle at arm's length, which is
            // where these are looked at hardest - it is in the hand for most of a final lap.
            int sides = shape == SlotShape.Round ? 48 : (shape == SlotShape.Triangle ? 3 : 4);
            bool smooth = shape == SlotShape.Round;

            // Where vertex 0 sits. The triangle keeps its apex at +Z, away from the player and so the
            // top of the shape as it is looked down on - the existing prism's choice, kept because
            // the recess in Room4 is cut from the unbevelled mesh at the same angle. A 4-gon starts
            // at 45 degrees, which is what turns it into an axis-aligned square rather than a
            // diamond.
            float turn = shape == SlotShape.Triangle ? Mathf.PI * 0.5f
                       : shape == SlotShape.Square ? Mathf.PI * 0.25f : 0f;

            Vector2[] unit = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = turn + i * Mathf.PI * 2f / sides;
                unit[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }

            // Fit the widest ring to a unit box in XZ. A triangle inscribed in a circle is neither
            // square nor centred on that circle, so without this it comes out smaller and offset
            // against the other two - which in a row of three reads as a mistake, not as a triangle.
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector2 v in unit)
            {
                minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
                minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
            }
            Vector2 centre = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            Vector2 span = new Vector2(maxX - minX, maxY - minY);
            for (int i = 0; i < sides; i++)
                unit[i] = new Vector2((unit[i].x - centre.x) / span.x, (unit[i].y - centre.y) / span.y);

            // Rings bottom to top. With a chamfer there are four - bottom cap edge, bottom of the
            // barrel, top of the barrel, top cap edge - and the caps are pulled in by the chamfer
            // while the barrel stays full width. Without one there are just the two ends, and no
            // degenerate zero-height band is emitted for RecalculateNormals to choke on.
            //
            // 6% is the chamfer these objects use. Big enough to catch a light at 0.30m across - 18mm
            // on the real object, about the width of the highlight it exists to hold - and small
            // enough that the silhouette is still unmistakably a cube, a triangle or a disc, which is
            // the one thing these three must never stop being: the recesses are cut to match.
            float[] ringY, ringScale;
            if (chamfer > 0f)
            {
                float inset = 1f - chamfer * 2f;
                ringY = new[] { -0.5f, -0.5f + chamfer, 0.5f - chamfer, 0.5f };
                ringScale = new[] { inset, 1f, 1f, inset };
            }
            else
            {
                ringY = new[] { -0.5f, 0.5f };
                ringScale = new[] { 1f, 1f };
            }
            int rings = ringY.Length;

            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();

            Vector3 At(int ring, int i) =>
                new Vector3(unit[i].x * ringScale[ring], ringY[ring], unit[i].y * ringScale[ring]);

            // WINDING, stated once because getting it wrong is invisible on an emissive object and
            // fatal on a metal one: Unity's front face is the triangle whose cross(v1-v0, v2-v0)
            // points ALONG the outward normal. The ring runs counter-clockwise in XZ seen from +Y.
            if (smooth)
            {
                // One continuous side surface with shared vertices, so the normals average round the
                // circumference and across both chamfers - a rounded-off puck rather than a drum.
                int baseIndex = verts.Count;
                for (int r = 0; r < rings; r++)
                    for (int i = 0; i < sides; i++) verts.Add(At(r, i));

                for (int r = 0; r < rings - 1; r++)
                    for (int i = 0; i < sides; i++)
                    {
                        int j = (i + 1) % sides;
                        int a = baseIndex + r * sides + i, b = baseIndex + r * sides + j;
                        int c = baseIndex + (r + 1) * sides + j, d = baseIndex + (r + 1) * sides + i;
                        tris.Add(a); tris.Add(d); tris.Add(c);
                        tris.Add(a); tris.Add(c); tris.Add(b);
                    }
            }
            else
            {
                // Every band of every face gets its own four vertices, so nothing smooths into
                // anything: the faces stay flat and the chamfers stay as distinct bands.
                for (int r = 0; r < rings - 1; r++)
                    for (int i = 0; i < sides; i++)
                    {
                        int j = (i + 1) % sides;
                        int b = verts.Count;
                        verts.Add(At(r, i)); verts.Add(At(r, j));
                        verts.Add(At(r + 1, j)); verts.Add(At(r + 1, i));
                        tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                        tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    }
            }

            // Caps, as fans off their own vertices so they stay flat whatever the sides do. Wound
            // opposite ways so both face outward - and note the TOP fan runs backwards against the
            // ring. A fan following a counter-clockwise ring produces a downward normal, which is
            // exactly the inversion the mesh this replaces shipped with.
            int topBase = verts.Count;
            for (int i = 0; i < sides; i++) verts.Add(At(rings - 1, i));
            for (int i = 1; i < sides - 1; i++)
            {
                tris.Add(topBase); tris.Add(topBase + i + 1); tris.Add(topBase + i);
            }

            int botBase = verts.Count;
            for (int i = 0; i < sides; i++) verts.Add(At(0, i));
            for (int i = 1; i < sides - 1; i++)
            {
                tris.Add(botBase); tris.Add(botBase + i); tris.Add(botBase + i + 1);
            }

            Mesh mesh = new Mesh { name = assetName };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            // No RecalculateTangents: tangents are derived from UVs, this mesh has none, and nothing
            // wearing it uses a normal map. Asking for them buys a warning and a garbage array.
            mesh.RecalculateBounds();

            if (!Directory.Exists(GeneratedDir)) Directory.CreateDirectory(GeneratedDir);
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            return mesh;
        }

        // The canvas, the plate and the placement, shared by every wall sign in the game. What goes ON
        // it comes from the caller: Room3's message is two lines of text, Room2's is a row of
        // pictograms, and both want exactly this plate at exactly this height on all four walls.
        private static CanvasGroup MakeWallFace(Transform parent, string name, Vector3 localPosition,
                                                Quaternion localRotation, System.Action<Transform> fillContent,
                                                float worldWidth = 7.2f, bool withPlate = true,
                                                float authoredHeight = 440f)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            // Authored large and scaled down, which is the standard way to keep world-space UI text
            // from rendering as a handful of blocky pixels: the font rasterises at the RectTransform
            // size, not the final world size.
            // Always authored 1600 WIDE whatever the final size, so every sign in the game rasterises
            // at the same resolution and the x layout numbers inside fillContent mean the same thing at
            // every scale. Only the scale changes: 7.2m for a wall-wide message, 3.6m over a door.
            //
            // The authored HEIGHT is separate because a sign's aspect is set by what it has to cover.
            // 440 is the text plate's shape; the side-wall pictograms are 618, which is what makes their
            // box exactly two rows of the wall grid tall at their width.
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1600f, authoredHeight);
            rect.localScale = Vector3.one * (worldWidth / 1600f);

            // Placed through anchoredPosition3D, set after the Canvas exists. AddComponent<Canvas>()
            // replaces the GameObject's plain Transform with a RectTransform, and a RectTransform's
            // local x and y come from its anchored position - so this is the field that means
            // anything. Anchors centred, so the offset is measured from the parent's origin (a
            // non-RectTransform parent is treated as a zero-size rect there).
            //
            // Worth knowing when verifying this in the saved scene, because it looks exactly like a
            // bug: a RectTransform's serialized m_LocalPosition is stale - here it stays {0,0,0}
            // while m_AnchoredPosition carries the real placement, and Unity recomputes the former
            // on load. Read m_AnchoredPosition, not m_LocalPosition.
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition3D = localPosition;
            rect.localRotation = localRotation;

            CanvasGroup group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            // The plate. This is what makes it a display rather than PAINT - GrooveDark is the same
            // near-black that sits at the bottom of every groove in the room.
            //
            // Optional, because that distinction is the whole choice. A lit black plate is the
            // facility interrupting, which is right for the message that stops an iteration; it is
            // far too loud for a small hint over a doorway, which wants to read as something
            // stencilled on the wall and be ignorable once it has been understood.
            if (withPlate)
            {
                GameObject plateGO = new GameObject("Plate");
                plateGO.transform.SetParent(go.transform, false);
                Image plate = plateGO.AddComponent<Image>();
                plate.color = new Color(0.04f, 0.04f, 0.045f, 0.94f);
                plate.raycastTarget = false;
                Stretch(plate.GetComponent<RectTransform>());
            }

            fillContent?.Invoke(go.transform);

            return group;
        }

        // All four walls of one room, at one height, carrying one piece of content.
        //
        // Each canvas's forward (+Z) points INTO its wall, i.e. away from the room. That reads
        // backwards and it is the opposite of what was built first, which came out mirrored.
        //
        // The rule: a world-space canvas is legible when its forward matches the direction the viewer
        // is LOOKING, not when it points at the viewer. Unity's own default scene is the proof -
        // camera at z = -10 looking toward +Z, canvas at the origin unrotated, text the right way
        // round. So a wall sign must face the same way as the eyes reading it, and a player anywhere
        // in the middle of a room looks outwards at every one of these.
        //
        // Worth knowing: this is why wall signs are canvases and not textures on the panelling. A
        // panel is a PrimitiveType.Cube, and a cube's +X and -X faces carry opposite U - so the same
        // texture reads mirrored on the east wall against the west (docs/gotchas.md). A canvas has no
        // such handedness, only the facing rule above, which is one decision instead of per-wall
        // bookkeeping.
        private static CanvasGroup[] MakeFourWallFaces(Transform parent, float y, float standoff,
                                                       System.Action<Transform> fillContent)
        {
            float halfWidth = RoomWidth / 2f;
            float halfDepth = RoomDepth / 2f;

            return new[]
            {
                MakeWallFace(parent, "South", new Vector3(0f, y, -halfDepth + standoff), Quaternion.Euler(0f, 180f, 0f), fillContent),
                MakeWallFace(parent, "North", new Vector3(0f, y, halfDepth - standoff), Quaternion.identity, fillContent),
                MakeWallFace(parent, "West", new Vector3(-halfWidth + standoff, y, 0f), Quaternion.Euler(0f, -90f, 0f), fillContent),
                MakeWallFace(parent, "East", new Vector3(halfWidth - standoff, y, 0f), Quaternion.Euler(0f, 90f, 0f), fillContent),
            };
        }

        // Room3's content: the two text lines that were the only thing a wall face ever held.
        private static void FillTerminationMessage(Transform face)
        {
            MakeWallLine(face, "Headline", "H O L D   [ N ]", 132, Color.red,
                new Vector2(0f, 78f), new Vector2(1600f, 190f));
            // Not spaced out, unlike the headline: this line is 29 characters and spacing it would
            // put it past the wall. The headline carries the treatment for both.
            MakeWallLine(face, "Detail", "TO SKIP TO THE NEXT ITERATION", 74,
                new Color(1f, 0.35f, 0.35f, 0.9f), new Vector2(0f, -90f), new Vector2(1600f, 130f));
        }

        // Room2's sign: PIN, then LEFT CLICK, then a balloon, then that balloon gone. No words at all.
        //
        // It exists because of a specific failure, not as decoration. A player on the itch build could
        // not work out how to burst a balloon and gave up - "kept trying and closed it". The
        // left-click prompt now sits on the balloon a swing would burst, but that prompt only appears
        // while the pin is IN HAND, so it says nothing at all to the player who walked past the drawer
        // and arrived here empty-handed. This is the sign for that player: it names the tool, the
        // button and the outcome, in the order they happen.
        //
        // Four icons and three arrows, drawn to the same 1600px width the text lines use. Sizes and
        // centres are laid out from one total so the row stays centred if any of them change.
        //
        // CHARCOAL ON THE BARE WALL, no plate behind it. The first version was red on a lit black
        // plate, borrowed from Room3's message, and it dominated the room - which is wrong for this
        // one. Room3's sign interrupts an iteration and should be impossible to miss once; this is a
        // standing hint that has to be findable when wanted and ignorable for the rest of the run, so
        // it reads as stencilled on the panelling. The value sits well clear of GrooveDark (0.04) so
        // it cannot be mistaken for a seam, and the arrows go dimmer still - they are punctuation, and
        // at equal weight the row reads as seven things rather than four and three.
        //
        // The icon size is a parameter because the two placements want different weights: 190 over the
        // door leaves margin either side of the row, 295 fills the authored width exactly for the big
        // side-wall signs. The arrow keeps its ratio to the icon so the row's rhythm is the same at
        // both sizes.
        private static void FillBalloonPictogram(Transform face, float icon)
        {
            float arrow = icon * (90f / 190f);
            float total = icon * 4f + arrow * 3f;
            float x = -total / 2f;

            Color ink = new Color(0.20f, 0.20f, 0.23f, 0.72f);
            // The arrows are RED while the things they connect stay charcoal, which inverts what the
            // first version did - it dimmed them, on the grounds that they are punctuation. Colour
            // separates them better than weight does: the eye now gets the direction of the sequence
            // before it has identified any of the four objects, and "left to right, then it pops" is
            // the half of this sign that has to survive being glanced at. Same red the facility uses
            // for its own text, held slightly off full so it reads as printed rather than lit.
            Color punctuation = new Color(0.82f, 0.12f, 0.12f, 0.85f);

            Sprite[] sprites = { PinIcon(), MouseLeftIcon(), BalloonIcon(), BalloonBurstIcon() };
            string[] names = { "Pin", "Click", "Balloon", "Burst" };
            Sprite arrowSprite = ArrowRightIcon();

            for (int i = 0; i < sprites.Length; i++)
            {
                MakeWallIcon(face, names[i], sprites[i], new Vector2(x + icon / 2f, 0f), icon, ink);
                x += icon;

                if (i == sprites.Length - 1) continue;
                MakeWallIcon(face, $"Arrow{i}", arrowSprite, new Vector2(x + arrow / 2f, 0f), arrow, punctuation);
                x += arrow;
            }
        }

        private static void MakeWallIcon(Transform parent, string name, Sprite sprite,
                                         Vector2 anchoredPosition, float size, Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            // The icons are square and drawn square; preserveAspect keeps a future non-square one
            // from being stretched to fit rather than fitted.
            image.preserveAspect = true;

            RectTransform rect = image.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = anchoredPosition;
        }

        // Room2's pictogram: the two SIDE walls, retired the moment the player pops anything.
        //
        // The side walls, and only those. A sign went over the key door first, on the reasoning that
        // the way out is the one surface every player in here is guaranteed to face - and it was
        // removed, because that is a description of where a player is LOOKING and not of where they
        // are stuck. Someone who cannot burst a balloon is standing among balloons turning on the
        // spot, which puts a side wall in front of them; and a hint over the exit reads as being about
        // the exit. Two big signs beside the puzzle say it where the puzzle is.
        //
        // South is bare for the opposite reason: it is at the player's back on the way in, and nobody
        // turns round for it.
        //
        // These are the ANSWER rather than a label, so they are sized to be read from anywhere in the
        // room - 7.0m, up from the 2.2m first tried and the 3.6m after it, both of which read as too
        // small from across the room. Charcoal on bare wall, no plate, at every size.
        //
        // The retirement is the other difference from Room3's. That sign goes on the first visit out,
        // because skipping an iteration is learned in one reading. This one stays until the player has
        // actually burst something, because the player it exists for is the one who arrives with empty
        // hands, fails, and walks back out to look for the tool - under the leave-once rule they would
        // return to a blank wall, having been shown the answer at the one moment they could not use it.
        private static PanelMessage BuildBalloonPictogram(Transform parent, float roomCenterZ, BalloonTool tool)
        {
            GameObject root = new GameObject("BalloonPictogram");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 0f, roomCenterZ);

            const float standoff = 0.05f;
            float halfWidth = RoomWidth / 2f;
            float halfDepth = RoomDepth / 2f;

            // THE WIDTH IS SIZED TO THE WALL GRID, and the height is sized to clear a doorway.
            //
            // A side wall is RoomDepth long, so it is 10.5 / 1.75 = SIX columns: the sign spans the
            // middle four, 7.0m, with the column boundaries falling at -3.5 and +3.5. That is why it
            // reads as printed on those panels rather than floating across them.
            //
            // The HEIGHT used to be grid-aligned too - the middle two rows, centred on RoomHeight/2 -
            // and it cannot be any more, because Room2's side walls now have DOORWAYS in them. A door
            // is 2.5 tall with its lamp at 2.725..2.835, and the icons in a sign centred at 2.7039 hang
            // down to 2.06: two of the four would have sat across the top of the door. Centred at 3.55
            // the icons run 2.905..4.195, clearing the lamp by 7cm, and only the middle arrow is over
            // the doorway at all. Nothing is lost visually since there is no plate - the rect is
            // invisible and only the row of icons reads.
            const float sideWidth = GridCellWidth * 4f;                  // 7.0, four columns
            const float sideAuthoredHeight = 618f;
            const float sideY = 3.55f;                                   // icons 2.905..4.195

            // 295 fills the authored width exactly - 4 icons plus 3 arrows at the row's own ratio come
            // to 1600 - so the sequence spans all four columns. A four-step row laid out horizontally
            // cannot also be two rows TALL: at 295 the icons are 1.29m against the block's 2.70m, and
            // making them taller would mean fewer than four fitting across. So the row fills the width
            // of the eight panels and sits centred in their height.
            const float sideIcon = 295f;

            // Each canvas's forward (+Z) points INTO its wall: a world-space canvas is legible when its
            // forward matches the direction the viewer is LOOKING, not when it points at the viewer.
            // See MakeFourWallFaces for the full rule, and for why signs are canvases and not textures
            // on the panelling.
            var faces = new[]
            {
                MakeWallFace(root.transform, "West", new Vector3(-halfWidth + standoff, sideY, 0f),
                             Quaternion.Euler(0f, -90f, 0f), f => FillBalloonPictogram(f, sideIcon),
                             sideWidth, withPlate: false, authoredHeight: sideAuthoredHeight),
                MakeWallFace(root.transform, "East", new Vector3(halfWidth - standoff, sideY, 0f),
                             Quaternion.Euler(0f, 90f, 0f), f => FillBalloonPictogram(f, sideIcon),
                             sideWidth, withPlate: false, authoredHeight: sideAuthoredHeight),
            };

            PanelMessage message = root.AddComponent<PanelMessage>();
            message.faces = faces;
            message.roomCenterZ = roomCenterZ;
            message.halfDepth = halfDepth;
            message.retireOnPop = tool;
            return message;
        }

        private static void MakeWallLine(Transform parent, string name, string content, int fontSize,
                                         Color color, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Text text = go.AddComponent<Text>();
            text.font = UIFont();
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }
    }
}
