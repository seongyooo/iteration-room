using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // THE BUILDING SEEN FROM OUTSIDE, which until the last minute of the game does not exist.
    //
    // Every room in this project is a sealed shell. That is fine for the thirty-odd hours of play
    // inside them and it means the exterior is, as built, a heap of white boxes with nothing visible
    // in any of them - so the single most important thing this class does is TAKE ONE WALL OFF EACH
    // ROOM. A cutaway is the only way the ride can show what it is for: the corridor the player has
    // spent an hour walking, all of it at once, with the past selves still standing in it.
    //
    // **AND THE SECOND MOST IMPORTANT THING IT DOES IS TURN THE LIGHTS ON OUT THERE.**
    //
    // This is worth stating plainly because it cost a playthrough. Every light in this game is a
    // spot in a ceiling pointing DOWN inside a sealed room; nothing has ever lit the outside of the
    // building, because until this sequence existed nobody could see it. So the first ride happened
    // past a facility that was, correctly and completely, BLACK - the exterior faces were taking the
    // trilight ambient's ground band and nothing else. It read as broken geometry rather than as
    // unlit geometry, which is why it is called out here: **anything new that is only ever seen from
    // outside has no light on it unless this puts one there.**
    //
    // WHAT IT OWNS: what the outside looks like at the moment it is revealed. Not the vehicle
    // (`CableCarRide`), not the order of events (`EndingDeparture`), not the grading (`RunEvaluation`).
    //
    // NOTHING HERE IS REVERSIBLE and nothing needs to be. It runs once, after `RunOver` is set, in a
    // run that ends at the title screen.
    public class FacilityExterior : MonoBehaviour
    {
        // Every cycle in the building. All of them are already loaded - `CycleSceneLoader` brings
        // every scene in at startup and `Cycle.SetAwake` is what decides which one is running - so
        // showing the whole facility costs four `SetActive` calls and no loading at all. That is the
        // single fact that makes this ending affordable; see `CycleSceneLoader`'s header, which chose
        // that design for the hatch-peek at a cycle boundary and paid for this by accident.
        //
        // Filled at runtime by `EndingDeparture` from `LoopManager`, not serialized: a reference to a
        // Cycle crosses a scene boundary and Unity nulls those silently on save (CLAUDE.md 3).
        [System.NonSerialized] public Cycle[] cycles;

        // WHERE THE CABLE ACTUALLY RUNS, as points rather than as one X.
        //
        // **A SINGLE X WAS WRONG AND THE BUILDING IS WHY.** This started as one number - the cable's
        // nominal axis - and each room lost whichever wall was nearest to it. That works for a tower
        // and this is not one: cycle 2 wanders twenty metres west of where cycle 1 sits, so the ride
        // weaves to stay close to each cycle in turn (see `BuildCableCarPath`), and a wall chosen
        // against the average of that weave is the wrong wall for the cycles at either end of it.
        //
        // Measuring against the path itself asks the only question that matters: *which of this
        // room's walls is the car going to be looking through?*
        //
        // Authored by `SceneBuilder`, which is also what built the path - the two are the same array.
        public Vector3[] cablePath;

        // **THE CYCLE THE PLAYER IS STANDING IN, AND IT IS NOT CUT AWAY.**
        //
        // The cutaway takes the lid off every room so the ride can see in. Cycle 3 is the one cycle
        // that must be left alone, because the player is INSIDE it when this runs - they are in
        // room3-2N waiting for the car, with room3-0 over their head. Play watched both of those
        // ceilings vanish while standing under them, which is the building disassembling itself
        // around a person rather than a dollhouse being opened for them.
        //
        // The ride loses nothing: it leaves through room3-2N's own wall and everything it climbs
        // past afterwards is cycles 2 and 1.
        [System.NonSerialized] public Cycle occupied;

        // What the outside of the building is repainted to at the reveal.
        //
        // **THE FACILITY WAS NOT BLACK FOR WANT OF LIGHT.** It is black by MATERIAL: a wall in this
        // game is a dark backing slab with white panels standing proud of it, and the backing is
        // `GrooveDark` at 0.04 - the near-black that sits at the bottom of every groove. From inside
        // a room you see the panels; from outside you see the backing, and no amount of light makes
        // a 0.04 albedo anything but black. A directional light was added first on the assumption it
        // was a lighting fault, which it was not (though it is kept - the panels and the racks do
        // need it).
        //
        // Mid grey rather than white, and that is a compromise worth naming: this same backing is
        // what shows THROUGH the grooves on the inside, so painting it white would flatten every
        // wall in the building at the moment the ride is looking into them. 0.45 reads as a grey
        // structure from outside while still being clearly darker than the 0.85 panels beside it.
        public Color exteriorPaint = new Color(0.45f, 0.46f, 0.48f);

        // What a past self is built from. The ghost prefab, instantiated frozen - see `RaiseGhosts`.
        public GhostReplayer ghostPrefab;
        public Transform ghostParent;

        // The shaft, the racks, and the backdrop - everything that exists only for this minute. Built
        // asleep and woken here, because building it now would be a hitch at the worst possible time.
        public GameObject[] cellBlocks;

        // **THE ONE LIGHT THAT ACTUALLY FIXES THE BLACK BUILDING, and why the twenty-one point
        // lights beside it did not.**
        //
        // The building is LIGHTMAP-STATIC. Its exterior faces were baked with nothing outside to
        // light them, so they carry a lightmap that is black, and a black lightmap is not a surface
        // waiting for light - it is a surface that already has its answer. Raising the ambient does
        // not touch it (a lightmapped surface takes no ambient) and that is why the backdrop, which
        // is not static, came out lit in the same frame where the facility came out black.
        //
        // Realtime lights DO add on top of a lightmap, which is what the point lights were for - but
        // URP hands each renderer only a handful of ADDITIONAL lights, chosen per object, and the
        // building's walls are enormous single meshes. A directional is not an additional light: it
        // is the main light, every renderer gets it, and it costs one.
        //
        // **OFF UNTIL THE REVEAL, and it has to be.** Shadows are off (forty shadow maps in the
        // heaviest frame in the game is not a trade worth making), so this light passes straight
        // through walls. Enabled during play it would flood every sealed room in the building.
        public Light sun;

        // THE LIGHT OUT THERE, and all of it comes up at once. It was staggered a storey at a time as
        // the car climbed, which is a nice idea about pacing and a bad one about visibility: it meant
        // the first half of the ride was spent looking at the unlit half of the building. See the
        // header - there is no other light on any of this.
        public Light[] shaftLights;

        // **THE ENVIRONMENT IS SWAPPED FOR THIS SHOT, and it is the one place in the game that does
        // it.** `ApplyEnvironment`'s trilight is tuned for standing inside a white room, where almost
        // all the wall and ceiling brightness comes from it; out here it is the ONLY term on every
        // exterior surface, and at those values that is a black building.
        //
        // Flat rather than trilight, because there is no floor and no ceiling to a shaft - the three
        // bands exist to describe a room and there is no room out here to describe.
        public Color endingAmbient = new Color(0.62f, 0.64f, 0.68f);
        // And the sky, which is what the OPEN TOP of the shaft shows. Without this it is Unity's
        // default procedural sky - play called it "the editor's default screen", which is exactly
        // what it is and exactly what it looks like at the end of a game set entirely indoors.
        public Material endingSkybox;

        private readonly List<GameObject> raised = new List<GameObject>();

        // Everything at once, called the moment the wall opens. The car has not moved yet, so the
        // cost of this frame is hidden behind a wall panel sliding aside.
        public void Reveal()
        {
            if (cycles != null)
                foreach (Cycle cycle in cycles)
                    cycle?.SetAwake(true);

            Cutaway();
            PaintTheOutside();

            if (cellBlocks != null)
                foreach (GameObject block in cellBlocks)
                    if (block != null) block.SetActive(true);

            LightTheOutside();
            RaiseGhosts();
        }

        // The ambient, the sky, and every fixture out there. One call, no pacing - see `shaftLights`.
        private void LightTheOutside()
        {
            // Flat, and written rather than blended: this is a hard cut that happens behind a wall
            // coming apart, and a fade would be an environment visibly changing while the player
            // watches it.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = endingAmbient;

            if (endingSkybox != null)
            {
                RenderSettings.skybox = endingSkybox;
                // Ambient is Flat above, so the skybox is not feeding it - but the reflection probes
                // still resolve against whatever the environment says, and a stale one here would
                // put the old sky into every metal surface on the ride.
                DynamicGI.UpdateEnvironment();
            }

            // The sun first, because it is the one doing the work - see the note on the field.
            if (sun != null) sun.enabled = true;

            if (shaftLights == null) return;
            foreach (Light light in shaftLights)
                if (light != null) light.enabled = true;
        }

        // Every backing slab in the building, lightened - see `exteriorPaint` for why this is a
        // material problem and not a lighting one.
        //
        // Through a `MaterialPropertyBlock`, never the material: `GrooveDark` is one shared material
        // used by every groove in every room of every cycle, and writing to it would be a permanent
        // edit to an asset on disk that the next build would not undo.
        private void PaintTheOutside()
        {
            if (cycles == null) return;

            var block = new MaterialPropertyBlock();
            int painted = 0, unbaked = 0, doused = 0;

            foreach (Cycle cycle in cycles)
            {
                if (cycle == null || cycle.worldRoot == null) continue;
                // The room the player is standing in is left exactly as it is - they are inside it,
                // reading a wall, and it is lit for that.
                if (occupied != null && cycle == occupied) continue;

                foreach (Renderer r in cycle.worldRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;

                    // **THE BAKED LIGHTMAP IS THROWN AWAY, AND THIS IS WHAT ACTUALLY FIXES BLACK.**
                    //
                    // Repainting the albedo was not enough and the arithmetic says why: a
                    // lightmapped surface renders as albedo TIMES its baked irradiance, and the
                    // outside of this building was baked with nothing out there to light it. Black
                    // times 0.45 is still black. It is not a surface waiting for light; it has its
                    // answer and the answer is zero.
                    //
                    // `lightmapIndex = -1` makes a renderer forget it was baked, so it falls back to
                    // the ambient and the realtime lights - which is exactly the state this scene
                    // wants for a building being looked at from outside for the first time.
                    if (r.lightmapIndex >= 0) { r.lightmapIndex = -1; unbaked++; }

                    if (!r.name.StartsWith("Backing")) continue;
                    r.GetPropertyBlock(block);
                    block.SetColor(LitPropertyIds.BaseColor, exteriorPaint);
                    r.SetPropertyBlock(block);
                    painted++;
                }

                // **AND THE CEILING FIXTURES GO OUT** (2026-09-01, by request: too bright). With the
                // lids off, every room's four downlights point straight at the camera from above -
                // twenty rooms' worth of bare spots in shot at once. The interiors are lit by the
                // exterior sun and the raised ambient now, which is what a cutaway wants: an even
                // wash that shows the floor plan, not four hotspots per room.
                foreach (Light light in cycle.worldRoot.GetComponentsInChildren<Light>(true))
                    if (light != null && light.enabled) { light.enabled = false; doused++; }

                // The emissive panels those fixtures sit in, killed with them - a dark room with
                // four glowing white squares in its ceiling is a room with the lights still on.
                foreach (Renderer r in cycle.worldRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || r.name != "Panel") continue;
                    r.GetPropertyBlock(block);
                    block.SetColor(LitPropertyIds.EmissionColor, Color.black);
                    r.SetPropertyBlock(block);
                }
            }

            Debug.Log($"[FacilityExterior] Outside: {painted} backing slab(s) repainted, {unbaked} "
                    + $"renderer(s) taken off their baked lightmap, {doused} ceiling fixture(s) out.");
        }

        // TAKING THE LID AND ONE WALL OFF EVERY ROOM, worked out here rather than at build time.
        //
        // **NOT A LIST OF RENDERERS GATHERED BY `SceneBuilder`.** It was, for one build, and it built
        // cleanly and logged "46 walls and 27 ceilings taken off for the view" - and this component
        // lives in `Cycle3` while 73 of those 74 renderers are in `Cycle1` and `Cycle2`, so Unity
        // nulled every one of them on save and the ride would have been a tour of a solid white
        // building. `cross-scene-report.txt` named all 74 (CLAUDE.md 3, the rule that says to read
        // it). What crosses a scene boundary now is a `Vector3[]` of positions, which is data.
        //
        // WHICH WALL, and it is chosen by MEASUREMENT rather than by name. Cycle 2 is turned about
        // its own bed, so the wall of its rooms that faces the cable is not the one called
        // `Wall_East`; a rule written in names would take the wrong face off twenty rooms and read as
        // a lighting fault rather than as a wrong wall.
        //
        // HIDDEN BY RENDERER, NOT BY `SetActive`. The collider stays, so a past self standing against
        // that wall is not suddenly standing in a void, and nothing that reads the room's geometry
        // has to be told the room changed shape.
        private void Cutaway()
        {
            if (cycles == null) return;

            // **A WALL IS TWO OBJECTS AND NEITHER OF THEM IS WHERE IT LOOKS.** Both facts broke this
            // and both are worth stating, because the symptom was one hidden wall per room and no
            // way to tell which:
            //
            //   - `BuildPanelWall` makes `Wall_North` (the dark backing slabs) AND
            //     `Wall_North_Panels` (the white panels standing proud of it) as SEPARATE children of
            //     the room. Hiding one leaves the other, so a "removed" wall was still a wall.
            //   - Both of those objects sit at the room's own origin. The geometry is in their
            //     children at real offsets; the parents never move. So asking `transform.position`
            //     which wall is nearest the cable gives the same answer four times, and the room lost
            //     whichever one the hierarchy happened to list first.
            //
            // So walls are grouped by base name and measured by RENDERER BOUNDS, which is where the
            // geometry actually is.
            // One entry per (room, wall) - a tuple key rather than a string built out of ids,
            // which is both faster and the version that does not have to be parsed back apart.
            var groups = new Dictionary<(Transform room, string wall), List<Renderer>>();
            int ceilings = 0;

            foreach (Cycle cycle in cycles)
            {
                if (cycle == null || cycle.worldRoot == null) continue;
                // The room the player is standing in keeps its lid - see `occupied`.
                if (occupied != null && cycle == occupied) continue;

                foreach (Transform t in cycle.worldRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null) continue;

                    if (t.name.StartsWith("Ceiling"))
                    {
                        foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                            if (r != null) { r.enabled = false; ceilings++; }
                        continue;
                    }

                    if (!t.name.StartsWith("Wall_") || t.parent == null) continue;
                    // `Wall_North` and `Wall_North_Panels` are ONE wall. `_Collision` carries no
                    // renderer and drops out of its own accord.
                    string wall = t.name.Replace("_Panels", string.Empty)
                                        .Replace("_Collision", string.Empty);
                    var key = (t.parent, wall);

                    if (!groups.TryGetValue(key, out List<Renderer> list))
                        groups[key] = list = new List<Renderer>();

                    foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                        if (r != null) list.Add(r);
                }
            }

            // Which of each room's walls is nearest the ride, measured where the geometry actually
            // is rather than where its parent object sits.
            var best = new Dictionary<Transform, (string wall, float gap)>();

            foreach (KeyValuePair<(Transform room, string wall), List<Renderer>> group in groups)
            {
                if (group.Value.Count == 0) continue;

                Bounds bounds = group.Value[0].bounds;
                for (int i = 1; i < group.Value.Count; i++) bounds.Encapsulate(group.Value[i].bounds);

                float gap = DistanceToPath(bounds.center);
                if (best.TryGetValue(group.Key.room, out var current) && gap >= current.gap) continue;
                best[group.Key.room] = (group.Key.wall, gap);
            }

            int walls = 0;
            foreach (KeyValuePair<Transform, (string wall, float gap)> room in best)
                if (groups.TryGetValue((room.Key, room.Value.wall), out List<Renderer> chosen))
                    foreach (Renderer r in chosen)
                        if (r != null) { r.enabled = false; walls++; }

            if (walls == 0 || best.Count == 0)
                Debug.LogError("[FacilityExterior] The cutaway found nothing to remove. The rooms are "
                             + "sealed boxes and the ride will show the outside of them. Check that "
                             + "walls are still named 'Wall_*' and that `cycles` was handed over.");
            else
                Debug.Log($"[FacilityExterior] Cutaway: {walls} wall renderer(s) in {best.Count} "
                        + $"wall group(s) and {ceilings} ceiling renderer(s) hidden. One wall per "
                        + "room, chosen by renderer bounds against the cable.");
        }

        // Distance from a point to the cable, as a polyline rather than as a line - the path bends at
        // every cycle, and the leg a room should be judged against is the one that passes it.
        private float DistanceToPath(Vector3 point)
        {
            if (cablePath == null || cablePath.Length == 0) return Mathf.Abs(point.x);
            if (cablePath.Length == 1) return Vector3.Distance(point, cablePath[0]);

            float closest = float.MaxValue;
            for (int i = 1; i < cablePath.Length; i++)
            {
                Vector3 a = cablePath[i - 1];
                Vector3 b = cablePath[i];
                Vector3 leg = b - a;
                float lengthSq = leg.sqrMagnitude;
                float along = lengthSq > 0.0001f
                    ? Mathf.Clamp01(Vector3.Dot(point - a, leg) / lengthSq) : 0f;
                float gap = Vector3.Distance(point, a + leg * along);
                if (gap < closest) closest = gap;
            }
            return closest;
        }

        // THE PAST SELVES, PUT BACK. One instance of the ghost prefab per archived pose, with its
        // `GhostReplayer` switched off before it can run a frame.
        //
        // **THE REPLAYER MUST BE DISABLED, NOT LEFT TO ITS OWN DEVICES.** A GhostReplayer that was
        // never handed a timeline is not harmless: `Tick` runs off a null cursor, `OnDestroy`
        // releases carried items it does not have, and it registers for signal bits belonging to a
        // cycle that is not running. Disabling the component leaves the model, the shader and the
        // pose, which is the entire thing being asked for here.
        private void RaiseGhosts()
        {
            if (ghostPrefab == null) return;

            foreach (GhostArchive.Pose pose in GhostArchive.Poses)
            {
                GhostReplayer instance = Instantiate(ghostPrefab, ghostParent);
                instance.enabled = false;
                instance.transform.SetPositionAndRotation(
                    pose.Position, Quaternion.Euler(0f, pose.Yaw, 0f));
                raised.Add(instance.gameObject);
            }
        }
    }
}
