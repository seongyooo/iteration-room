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
    // CYCLE 2'S ROOMS, in the order they were built: the weighing room and its pictogram, the pool
    // and what floats in it, the valve and the drain, the ball pit, the seesaw, and the tree that
    // has to be felled.
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {

        // ROOM2-7'S SIGNS: what can be weighed, and what the total has to come to.
        //
        // THE ROOM CANNOT BE SOLVED WITHOUT THEM, and that is why they exist rather than as decoration.
        // The design is that a player is told no WEIGHTS - they learn those on the scale - but a player
        // told nothing at all has no reason to think this room wants a sum at all, let alone which
        // objects are eligible or what number to hit. So the signs say exactly the two things that are
        // not discoverable by experiment and nothing else:
        //
        //   - the SIDE WALLS carry the shape of the answer: `A + B + C = ?`, drawn from the objects
        //     themselves, so "bring things here and add them up" is stated without a word;
        //   - the WALL OVER THE DOOR carries the target, `= 23.0`, over the thing it opens.
        //
        // The question mark is the point of the side walls. It says the total is the unknown, which
        // makes the scale's readout the place to look - and it leaves every weight, and which three
        // objects to use, entirely to the player.
        //
        // DRAWN, NOT WRITTEN. Everything but the number is an icon, for the reason the balloon
        // pictogram gives: this facility does not explain itself in sentences, and a caption in among
        // the objects reads as a different kind of thing on the wall.
        private static void BuildWeighPictogram(Transform room, WeighScale scale)
        {
            GameObject root = new GameObject("WeighPictogram");
            root.transform.SetParent(room, false);

            const float standoff = 0.05f;
            float halfWidth = RoomWidth / 2f;
            float halfDepth = RoomDepth / 2f;

            // --- the equation, on both side walls ---------------------------------------------------
            // WHAT CAN BE WEIGHED, AND THAT THE ANSWER IS A SUM. Restored 2026-08-19 after a pass that
            // cut it back to the number alone: without it the room says what total it wants and
            // nothing about what to bring, and the objects it names are scattered across four rooms
            // the player has already walked through.
            //
            // FIVE OBJECTS, not the three that make the cheapest solution. That is the difference
            // between a sign and an answer: this one lists what the scale ACCEPTS - cube, full bucket,
            // axe, beach ball, rubber duck - and the question mark says the total is the unknown. Which
            // of them add up to the number over the door is left entirely to the player.
            //
            // BOTH SIDE WALLS, because the player is turning on the spot at a scale in the middle of
            // the room: whichever way they face, one of the two is in front of them.
            const float sideWidth = GridCellWidth * 5f;
            const float sideAuthoredHeight = 618f;
            // Above head height, and above the scale - which is 5.7m across and sits between the
            // player and both of these.
            const float sideY = 3.4f;

            System.Action<Transform> equation = f => FillWeighEquation(f);

            CanvasGroup west = MakeWallFace(root.transform, "West",
                new Vector3(-halfWidth + standoff, sideY, 0f), Quaternion.Euler(0f, -90f, 0f),
                equation, sideWidth, withPlate: false, authoredHeight: sideAuthoredHeight);
            CanvasGroup east = MakeWallFace(root.transform, "East",
                new Vector3(halfWidth - standoff, sideY, 0f), Quaternion.Euler(0f, 90f, 0f),
                equation, sideWidth, withPlate: false, authoredHeight: sideAuthoredHeight);

            // OVER THE DOOR ONTO ROOM2-8, in the SOUTH wall - which is the door this scale opens, so
            // the number is written on the thing it is the price of. Facing back into the room, which
            // is where somebody who has just weighed something is standing: a canvas's forward must
            // match the direction the VIEWER is looking, not point at them (see MakeWallFace).
            //
            // THE EQUATION IS GONE, 2026-08-19 by request. It drew `full bucket + cube + axe = ?` on
            // the side walls, which named the three objects that work - the number alone leaves that
            // to the player, and it is the only part of this that experiment cannot supply.
            //
            // NO PLATE. `withPlate` draws the near-black backing every other sign in this building
            // sits on, and it is what makes those read as DISPLAYS. This one is meant to read as
            // painted on the wall - a number stencilled over a doorway - so the plate is off and the
            // wall shows through behind the figures.
            float overDoorY = DoorHeight + 0.95f;
            CanvasGroup target = MakeWallFace(root.transform, "Target",
                new Vector3(0f, overDoorY, -halfDepth + standoff), Quaternion.Euler(0f, 180f, 0f),
                f => FillWeighTarget(f, scale != null ? scale.targetKilograms : WeighTarget),
                worldWidth: 3.6f, withPlate: false, authoredHeight: 440f);

            // FULLY VISIBLE, AND PERMANENT. `MakeWallFace` authors every sign at alpha 0 because the
            // ones it was built for are faded in and retired by `PanelMessage` - room2's pictogram
            // goes when the player pops a balloon. These never go: what they say is true for as long
            // as the room is unsolved, and a player who has forgotten it has to be able to look up
            // again.
            foreach (CanvasGroup group in new[] { west, east, target })
                if (group != null) group.alpha = 1f;
        }

        // `CUBE + BUCKET + AXE + BEACH BALL + DUCK = ?`, in objects.
        //
        // EVERYTHING THE SCALE ACCEPTS, not a solution. The objects, a plus between each, and a
        // question mark at the end saying the total is the unknown - so the sign names the
        // INGREDIENTS and the number over the door names the answer, and what adds up to it is the
        // player's to work out. That is the whole puzzle: they are told no weights.
        //
        // **~~The bucket~~ GONE 2026-08-29**, and with it the one thing on this sign that could not be
        // drawn honestly: an empty pail and a full one are the same carryable at two very different
        // weights, so the icon had to pick one and mean it.
        private static void FillWeighEquation(Transform face)
        {
            // FOUR NOW, the bucket having left the equation - see the weight table for why. The
            // layout below is derived from the count rather than written down, so a row that names
            // one fewer object simply comes out wider per icon.
            Sprite[] objects = { CubeIcon(), AxeIcon(), BeachBallIcon(), DuckIcon() };
            string[] names = { "Cube", "Axe", "BeachBall", "Duck" };

            // N objects, N-1 pluses, an equals and a question mark, across a sign authored 1600 wide.
            // The icon size is DERIVED from that rather than written down, so the row fills the sign
            // however many objects it ends up naming - which is what made dropping one a one-line
            // change here.
            const float authoredWidth = 1600f;
            const float opRatio = 90f / 190f;                     // the balloon pictogram's proportion
            int ops = objects.Length;                             // four pluses plus the equals
            float icon = authoredWidth / (objects.Length + 1f + ops * opRatio);
            float op = icon * opRatio;

            float x = -authoredWidth / 2f;

            Color ink = new Color(0.20f, 0.20f, 0.23f, 0.72f);
            // The operators are RED while the things they join stay charcoal, exactly as room2's
            // pictogram does it: the eye gets the shape of the sum before it has identified any of the
            // objects in it.
            Color punctuation = new Color(0.82f, 0.12f, 0.12f, 0.85f);

            for (int i = 0; i < objects.Length; i++)
            {
                MakeWallIcon(face, names[i], objects[i], new Vector2(x + icon / 2f, 0f), icon, ink);
                x += icon;

                Sprite between = i < objects.Length - 1 ? PlusIcon() : EqualsIcon();
                string label = i < objects.Length - 1 ? $"Plus{i}" : "Equals";
                MakeWallIcon(face, label, between, new Vector2(x + op / 2f, 0f), op, punctuation);
                x += op;
            }

            // The unknown, in the same red as the operators - it is punctuation about the sum rather
            // than another object in it.
            MakeWallIcon(face, "Question", QuestionIcon(), new Vector2(x + icon / 2f, 0f), icon, punctuation);
        }

        // The target, over the door it opens. Written to ONE DECIMAL like the scale's own readout,
        // so the two are visibly the same kind of value and a player can compare them without
        // wondering whether "23" and "23.0" mean the same thing.
        //
        // DARK ON A WHITE WALL, because there is no plate behind it any more: the pale grey it used to
        // be was legible against near-black and would be invisible against the panelling. Same
        // charcoal every other mark in this facility is printed in.
        private static void FillWeighTarget(Transform face, float kilograms)
        {
            MakeWallLine(face, "Target", $"{kilograms:0.0} KG", 260,
                         new Color(0.17f, 0.17f, 0.20f, 0.88f), Vector2.zero,
                         new Vector2(1400f, 320f));
        }

        // How wide the scale is. A bathroom scale is 0.3m across and would be a coaster in this
        // building; this is a PLATFORM scale, big enough to stand on with an axe beside your feet,
        // which is what the room asks of it.
        //
        // TRIPLED, 2026-08-19 by request: 5.7m across in a room 8.75m wide, so it is most of the
        // floor and the room reads as being BUILT around the weighing rather than as a room with a
        // scale in it. Two consequences worth knowing, both accepted:
        //   - the PAN is derived from this (0.95 of it), so the volume that counts as "on the scale"
        //     is now 5.4m across. Anything put down near the middle of the room is being weighed,
        //     which is generous - but the room contains nothing else, so there is nothing to put down
        //     near the middle by accident;
        //   - the readout is sized off the model's own display panel, so it grows with it and the
        //     digits stay legible from the doorway.
        private const float WeighScaleWidth = 5.7f;

        // THE SCALE, and its readout.
        //
        // THE MODEL IS A BATHROOM SCALE and it is scaled up by roughly six. That is not a fudge: every
        // dimension in this building is oversized (a 220mm ball-pit ball, a 10.5m room, a 13m tree),
        // and a scale you can put a full bucket AND an axe AND a cube on has to be a pallet, not a
        // tile.
        //
        // THE READOUT IS OURS, NOT THE MODEL'S. The model has a green display panel painted on it,
        // which cannot show a number; the digits are a world-space canvas laid flat just above that
        // panel, found by its material name and fallen back to the model's own top face. UI text is
        // unlit, which is what makes it read as a lit LCD rather than as paint.
        private static WeighScale BuildWeighScale(Transform room, Material propMat)
        {
            GameObject root = new GameObject("WeighScale2_7");
            root.transform.SetParent(room, false);

            float top = 0.18f;
            Bounds raw = new Bounds();
            Transform display = null;

            string path = PlayDir + "/weigh_scale.glb";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source != null)
            {
                ShrinkModelTextures(path, 1024);

                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                model.name = "Scale";
                model.transform.localPosition = Vector3.zero;

                raw = ModelBounds(root.transform, model);
                float across = Mathf.Max(0.001f, Mathf.Max(raw.size.x, raw.size.z));
                float scale = WeighScaleWidth / across;
                model.transform.localScale = Vector3.one * scale;

                raw = ModelBounds(root.transform, model);
                model.transform.localPosition = new Vector3(-raw.center.x, -raw.min.y, -raw.center.z);
                raw = ModelBounds(root.transform, model);
                top = raw.max.y;

                // SOLID, so things put on it stay on it. A mesh collider on the parts as they are -
                // convex would swallow the dish the platform makes and objects would slide off a dome.
                foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                }

                // The display is the part wearing the model's own `Green` material. Found by name
                // because that is what the export calls it, and harmless if it ever stops being true:
                // the readout falls back to the middle of the platform's top face.
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
                {
                    if (r.sharedMaterial == null || !r.sharedMaterial.name.Contains("Green")) continue;
                    display = r.transform;
                    break;
                }
            }
            else
            {
                Debug.LogWarning($"[SceneBuilder] {path} is missing - room2-7 gets a scale with no scale.");
                Prim(PrimitiveType.Cube, "Platform", root.transform, new Vector3(0f, 0.09f, 0f),
                     new Vector3(WeighScaleWidth, 0.18f, WeighScaleWidth * 0.9f), propMat);
            }

            // --- the digits ---------------------------------------------------------------------
            // ON THE MODEL'S OWN DISPLAY PANEL, and nothing is added to the scene to do it. Two
            // earlier versions put something over the panel instead - a world-space `Canvas`, then a
            // grid of quads - and both are the same mistake: a thing stuck on the prop rather than the
            // prop showing something.
            //
            // THE MODEL'S OWN NUMBER HAS TO GO FIRST, and it is not a texture - it is GEOMETRY.
            // `weigh_scale.glb` ships no textures at all and its green display is one connected
            // surface with a fixed number embossed into it, so drawing the live reading onto that
            // surface put a picture of digits across a thing that already was digits. The submesh is
            // replaced with a clean quad over the same footprint - see FlatPanelMesh.
            Bounds panel = display != null
                ? ModelBounds(root.transform, display.gameObject)
                : new Bounds(new Vector3(0f, top, -WeighScaleWidth * 0.28f),
                             new Vector3(WeighScaleWidth * 0.42f, 0.01f, WeighScaleWidth * 0.20f));

            PanelDigits digits = null;
            if (display != null)
            {
                MeshFilter panelFilter = display.GetComponent<MeshFilter>();
                if (panelFilter != null && panelFilter.sharedMesh != null)
                    panelFilter.sharedMesh = FlatPanelMesh(panelFilter.sharedMesh, "ScalePanel");

                // UNLIT, because an LCD is not lit by the room - and the texture carries its own dark
                // background and bright bars, so there is nothing for the room's light to add.
                Renderer panelRenderer = display.GetComponent<Renderer>();
                if (panelRenderer != null)
                    panelRenderer.sharedMaterial = MakeUnlitMaterial("ScalePanel", Color.white);

                digits = display.gameObject.AddComponent<PanelDigits>();
                digits.panel = panelRenderer;
                digits.digitCount = 4;
                digits.decimalsAfter = 1;
            }
            else
            {
                Debug.LogWarning("[SceneBuilder] the scale has no display panel - its readout is gone.");
            }

            WeighScale weigh = root.AddComponent<WeighScale>();
            weigh.display = digits;
            weigh.targetKilograms = WeighTarget;
            weigh.playerKilograms = WeightPlayer;
            // THE PAN, and it is generous on purpose. It has to hold a person standing up, so it is
            // taller than a person; anything within the platform's footprint and under two metres of
            // it counts as being on the scale.
            weigh.panCentre = new Vector3(0f, top + 1.0f, 0f);
            weigh.panSize = new Vector3(WeighScaleWidth * 0.95f, 2.0f, WeighScaleWidth * 0.9f);
            weigh.audioSource = MakeSource(root.transform, "ScaleAudio", spatialBlend: 1f, volume: 0.9f);
            weigh.acceptClip = MakeValveStopClip("sfx_valve_stop");

            Debug.Log($"[SceneBuilder] Room2_7 scale: {WeighScaleWidth:0.00}m platform, "
                    + $"target {WeighTarget:0.0}kg, top at {top:0.00}m, display panel "
                    + $"{panel.size.x:0.00}x{panel.size.z:0.00}"
                    + (digits != null ? " (panel replaced)" : " - MISSING"));
            return weigh;
        }

        // THE POOL AT THE BOTTOM OF THE SLIDE, and the plastic balls floating on it.
        //
        // WHAT IT IS FOR. The chute comes through this room's CEILING, so whoever rides it arrives
        // falling - and a fall that ends on a hard floor is a landing, while one that ends in water is
        // a plunge. That is the whole ask: the ride has somewhere to go that answers it.
        //
        // NOT SOLID, NOT FATAL, NOT RECORDED. There is no collider on any of it (see WaterPool for why
        // one would break the slide's own measured path), no `KillVolume` under it, no signal bit and
        // nothing for `Cycle.ResetRooms` to put back. The player wades out; a past self wades out the
        // same way, because walking is already recorded and this changes nothing about walking.
        //
        // THREE OBJECTS, THREE JOBS: the surface is a mesh with the water shader on it, `WaterPool`
        // owns where the waterline is and the splash when somebody is in it, and `FloatingBalls` owns
        // the drift. None of the three knows what the other two are doing.
        private static WaterPool BuildWaterPool(Transform room)
        {
            GameObject root = new GameObject("Room2_SlideRoom_Pool");
            root.transform.SetParent(room, false);

            WaterPool pool = root.AddComponent<WaterPool>();
            pool.surfaceLocalY = PoolDepth;
            pool.halfExtents = new Vector2(RoomWidth / 2f, RoomDepth / 2f);
            // Everything from the floor to the surface is "in the water". Half a metre of slack under
            // the floor, so a frame in which the controller is a hair through it still counts.
            pool.floorDrop = PoolDepth + 0.5f;

            // --- the surface ----------------------------------------------------------------------
            // PUSHED INTO THE WALLS by a few centimetres. A sheet cut exactly to the room leaves a
            // hairline of floor showing at every wall as soon as the vertex wobble pulls an edge
            // vertex inward, and a gap at the waterline is the one place the eye is guaranteed to look.
            const float wallOverlap = 0.06f;

            GameObject surface = new GameObject("Surface");
            surface.transform.SetParent(root.transform, false);
            surface.transform.localPosition = new Vector3(0f, PoolDepth, 0f);
            // THE SIZE IS IN THE ASSET NAME. `SaveGeneratedMesh` serves from cache, so a shape
            // parameter changed without the name changing gets the OLD mesh back and the change
            // silently does nothing - the same trap `WaterSpill_..._d45` names itself against.
            surface.AddComponent<MeshFilter>().sharedMesh = SaveGeneratedMesh(
                "WaterSheet_SlideRoom_c25",
                () => WaterMeshes.Sheet(RoomWidth + wallOverlap * 2f,
                                        RoomDepth + wallOverlap * 2f, PoolSurfaceCell));

            MeshRenderer surfaceRend = surface.AddComponent<MeshRenderer>();
            surfaceRend.sharedMaterial = MakePoolWaterMaterial();
            surfaceRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            surfaceRend.receiveShadows = false;

            // --- the splash -----------------------------------------------------------------------
            // The tap's spray with the tap taken off it: no rate, no bursts, nothing playing on its
            // own. `WaterPool` moves it to the waterline and calls `Emit`, so one system serves the
            // arrival and every wading step after it.
            GameObject splashGO = new GameObject("Splash");
            splashGO.transform.SetParent(root.transform, false);
            splashGO.transform.localPosition = new Vector3(0f, PoolDepth, 0f);

            ParticleSystem splash = splashGO.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule splashMain = splash.main;
            // PLAYING FOREVER AND EMITTING NOTHING, which is not the contradiction it reads as: a
            // STOPPED system does not simulate, so particles pushed into one by `Emit` are created and
            // then never move. Looping with the emission module switched off is how a system is kept
            // running for a caller that supplies every particle itself.
            splashMain.loop = true;
            splashMain.playOnAwake = true;
            splashMain.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 1.05f);
            splashMain.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 4.6f);
            splashMain.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.09f);
            splashMain.gravityModifier = 1f;
            splashMain.startColor = new ParticleSystem.MinMaxGradient(new Color(0.90f, 0.95f, 1f, 0.92f));
            // World space, or the droplets ride the emitter as it is moved to the next splash.
            splashMain.simulationSpace = ParticleSystemSimulationSpace.World;
            splashMain.maxParticles = 400;

            ParticleSystem.EmissionModule splashEmit = splash.emission;
            // NOTHING ON ITS OWN. Every particle this ever makes comes from an `Emit` call.
            splashEmit.enabled = false;

            // A flat skirt: water thrown up by something dropping into it goes out and over, not up in
            // a column - a narrow cone reads as a fountain.
            ParticleSystem.ShapeModule splashShape = splash.shape;
            splashShape.shapeType = ParticleSystemShapeType.Cone;
            splashShape.angle = 72f;
            splashShape.radius = 0.22f;
            splashShape.rotation = new Vector3(-90f, 0f, 0f);

            ParticleSystem.SizeOverLifetimeModule splashSize = splash.sizeOverLifetime;
            splashSize.enabled = true;
            splashSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f));

            ParticleSystemRenderer splashRend = splashGO.GetComponent<ParticleSystemRenderer>();
            splashRend.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WaterStream.mat");
            splashRend.renderMode = ParticleSystemRenderMode.Billboard;
            splashRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            splashRend.receiveShadows = false;

            pool.splash = splash;
            pool.audioSource = MakeSource(root.transform, "SplashAudio", spatialBlend: 1f, volume: 0.9f);
            // ARRIVING IS A BODY GOING THROUGH THE SURFACE, not water hitting a floor. The three
            // `sfx_water_splash_*` clips were here and played as an effect laid over the room - they
            // are the tap's, and a tap's water lands on tiles from above. This is the wade sound at
            // twice the length, slower to die and with more of it low: the same event, person-sized.
            pool.splashClips = new[]
            {
                MakeWadeClip("sfx_water_plunge", 20260822, seconds: 1.15f, decay: 2.9f,
                             fizzLevel: 0.5f, lowWeight: 3.4f),
            };

            // --- the rings ----------------------------------------------------------------------
            pool.ripples = BuildWaterRipples(root.transform);

            pool.surface = surface.transform;

            // --- what floats on it -------------------------------------------------------------------
            // Three kinds, smallest first, and the count of the small one is what leaves room for the
            // other two to be seen at all (2026-08-19, by request: fewer balls, and beach balls in
            // among them).
            FloatingBalls balls = BuildFloatingBalls(root.transform);

            // THE DUCKS. Rocking and turning, which a plastic sphere cannot show and a duck must - see
            // FloatingBalls' turn fields. Slow: this is still water in a sealed room.
            FloatingBalls ducks = BuildFloatingProps(root.transform, "Ducks", "rubber_duck.glb",
                PoolDuckLength, sinkFraction: 0.34f,
                spots: new[] { new Vector2(-2.35f, 2.60f), new Vector2(1.70f, -0.40f),
                               new Vector2(-0.90f, -3.40f) },
                yaws: new[] { 34f, 205f, 128f },
                idPrefix: DuckItemId, displayName: "RUBBER DUCK", kilograms: WeightDuck,
                icon: DuckIcon());
            if (ducks != null)
            {
                ducks.bobHeight = 0.030f;
                ducks.bobPeriod = 4.2f;
                // An ACCELERATION now, not a radius: against the mooring it settles into a wander of
                // about a third of a metre, and the turning that used to be written here is the
                // rigidbody's own - a duck shouldered by a beach ball turns because it was shouldered.
                // LESS THAN IT WAS. A duck that wanders is charming and is also a thing the player
                // has to chase in water that halves their speed; the drift is now small enough to
                // read as the water moving under it rather than as the duck going somewhere.
                ducks.drift = 0.045f;
                ducks.driftPeriod = 21f;
            }

            // THE BEACH BALLS. Bigger and lighter than anything else on the water, so they sit HIGH -
            // a beach ball is a bag of air and barely breaks the surface - and they wander furthest,
            // because the bigger the sail the more the room's nothing-at-all moves it.
            FloatingBalls beach = BuildFloatingProps(root.transform, "BeachBalls", "beach_ball.glb",
                PoolBeachBallSize, sinkFraction: 0.16f,
                spots: new[] { new Vector2(2.60f, 3.30f), new Vector2(-2.90f, -1.60f) },
                yaws: new[] { 62f, 241f },
                idPrefix: BeachBallItemId, displayName: "BEACH BALL", kilograms: WeightBeachBall,
                icon: BeachBallIcon());
            if (beach != null)
            {
                beach.bobHeight = 0.045f;
                beach.bobPeriod = 5.1f;
                // Furthest, because the bigger the sail the more the room's nothing-at-all moves it.
                // An ACCELERATION now, not a radius - against the mooring it settles into a wander of
                // about half a metre, and the rolling that used to be written here is the rigidbody's
                // own: a beach ball turns because something turned it.
                beach.drift = 0.07f;
                beach.driftPeriod = 26f;
            }

            // EVERY GROUP, NAMED HERE. The pool moves them all when it drains, and gathering them by
            // type at runtime is the thing CLAUDE.md §2 says a cycle must not do - a `FloatingBalls`
            // in some later room would be dragged down with this room's water.
            pool.floats = new[] { balls, ducks, beach };
            return pool;
        }

        // How long a duck is, nose to tail. A real bath duck is about 80mm; this building is oversized
        // throughout (a 220mm ball-pit ball, a 10.5m room) and at bath size these would be specks on a
        // ninety-square-metre pool. 0.42m puts one at about two plastic balls long, which is what reads.
        private const float PoolDuckLength = 0.42f;
        // And a beach ball, which is the biggest thing on the water: three plastic balls across.
        private const float PoolBeachBallSize = 0.66f;

        // ONE KIND OF THING FLOATING ON THE POOL: an imported model, placed at named spots, riding the
        // same `FloatingBalls` component the plastic balls do. The ducks and the beach balls differ by
        // their file, their size, how deep they sit and how they move - so those are the arguments, and
        // the motion is set by the caller on the component this returns.
        //
        // A WRAPPER PER OBJECT, with the model inside it untouched. `FloatingBalls` writes the
        // wrapper's local position and rotation; the model keeps the pose its import gave it, which is
        // the rule every glTF in this project is placed by (see BuildSlide for what overwriting one
        // costs).
        private static FloatingBalls BuildFloatingProps(Transform poolRoot, string name, string file,
                                                        float size, float sinkFraction,
                                                        Vector2[] spots, float[] yaws,
                                                        string idPrefix, string displayName,
                                                        float kilograms, Sprite icon)
        {
            string path = PlayDir + "/" + file;
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null)
            {
                Debug.LogWarning($"[SceneBuilder] {path} is missing - the pool keeps everything else "
                               + $"and loses its {name}.");
                return null;
            }

            ShrinkModelTextures(path, 1024);

            GameObject root = new GameObject(name);
            root.transform.SetParent(poolRoot, false);

            // THE SPOTS ARE WRITTEN DOWN rather than scattered randomly. Two or three objects are too
            // few for a random scatter to look scattered - a pair lands beside each other often enough
            // to matter, and the pool is nine metres across.
            Random.State entry = Random.state;
            Random.InitState(20260819 + name.GetHashCode());

            int floatLayer = EnsureLayer(BalloonLayerName);
            int count = Mathf.Min(spots.Length, yaws.Length);
            var bodies = new Rigidbody[count];
            var homes = new Vector3[count];
            var carriedItems = new CarryableItem[count];

            for (int i = 0; i < count; i++)
            {
                GameObject holder = new GameObject($"{name}_{i}");
                holder.transform.SetParent(root.transform, false);
                holder.layer = floatLayer;

                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source, holder.transform);
                model.name = "Model";
                model.transform.localPosition = Vector3.zero;

                Bounds raw = ModelBounds(holder.transform, model);
                float footprint = Mathf.Max(0.001f, Mathf.Max(raw.size.x, raw.size.z));
                float scale = size / footprint * Random.Range(0.92f, 1.08f);
                model.transform.localScale = Vector3.one * scale;

                // FLOATING, NOT SITTING ON THE SURFACE. Whatever it is displaces some of itself, so
                // the waterline crosses the body - anything resting with its lowest point exactly on
                // the surface reads as an object on a glass table. How much is the caller's, because
                // a duck sits deep and a bag of air does not.
                float sink = raw.size.y * scale * sinkFraction;
                model.transform.localPosition = new Vector3(-raw.center.x * scale,
                                                            -raw.min.y * scale - sink,
                                                            -raw.center.z * scale);

                homes[i] = new Vector3(spots[i].x, PoolDepth, spots[i].y);
                holder.transform.localPosition = homes[i];
                holder.transform.localRotation = Quaternion.Euler(0f, yaws[i], 0f);

                // The collider is a sphere the size of the FOOTPRINT, not of the model. A duck is
                // mostly a body with a head on it, and a collider drawn round the head as well is a
                // duck that fends other things off with a bubble it does not appear to have.
                float radius = footprint * scale * 0.45f;
                (Rigidbody body, SphereCollider solid) = MakeFloatBody(holder, radius, PoolPropMass,
                                                                        upright: true);
                bodies[i] = body;

                // AND IT CAN BE PICKED UP AND CARRIED OUT. The ducks and the beach balls are the only
                // things in this game that are both physics props and carryables, and the handover is
                // in `FloatingBalls`: while a hand has it, or once it is further from home than the
                // pool is wide, its body goes kinematic and the ordinary carryable machinery owns it -
                // including `FallingItem`, so its drop is SCRIPTED and lands in the same place every
                // iteration, which is what room2-7's scale needs of it (CLAUDE.md §4).
                //
                // The reach trigger goes on the HOLDER, which is what `CarryableItem` has to live on -
                // it is the transform that gets parented into a hand, and the physics sphere is a
                // child so the two colliders cannot be confused for each other (see MakeFloatBody).
                //
                // MUCH BIGGER THAN A SHELF ITEM'S, and it has to be. Everything else in this game is
                // picked up off a floor by a player walking at 2.5m/s; these are picked up out of
                // waist-deep water, which halves the walk, while the object itself DRIFTS. At the
                // 0.55m clearance the rest of the game uses, closing the last metre on a duck that is
                // wandering took several attempts - play reported it twice as "잘 안 잡혀". 1.25m is
                // about an arm and a step, and nothing else is close enough to lose the press to.
                SphereCollider reach = holder.AddComponent<SphereCollider>();
                reach.isTrigger = true;
                reach.radius = radius + 1.25f;

                // WHERE THE PROMPT HANGS, AND WHAT THE AIM IS MEASURED AGAINST - above the waterline
                // rather than at the object's centre, which sits ON it. `CarryableItem.HintAnchor` is
                // its own transform by default, and for a duck that point is half under the surface:
                // the disc was drawn inside the water and the aim test was asking about a point the
                // player cannot really see. Everything else in the game rests on a floor, where the
                // two are the same place.
                GameObject aim = new GameObject("Aim");
                aim.transform.SetParent(holder.transform, false);
                aim.transform.localPosition = new Vector3(0f, radius + 0.10f, 0f);

                CarryableItem item = holder.AddComponent<CarryableItem>();
                item.hintAnchor = aim.transform;
                // The physics sphere is also what makes it SOLID, so it is the blocker: switched off
                // while it is in a hand, exactly like a cube's.
                item.blocker = solid;
                // **ONE ID PER OBJECT, NOT ONE PER KIND** (2026-08-28, by request, after play found
                // the beach balls swapping between hands). A shared id makes a SUPPLY, and
                // `ItemRegistry` then resolves it to "any free one" - so a ghost replaying "took a
                // BeachBall" takes whichever of the two is going spare rather than the one it
                // actually took. With two of them adrift on moving water that reads as the pair
                // trading places every iteration.
                //
                // A supply is the right shape for the three pins, where the id names a stock of
                // interchangeable tools and CLAUDE.md §4 says so. It is the wrong shape here: these
                // are two distinct objects a player picks out by eye, and §1.4's whole argument -
                // identity, never position - wants the id to name the OBJECT. The chess pieces
                // already do exactly this (`item.itemId = piece.gameObject.name`).
                //
                // `GhostReplayer.TryTake`'s "any free one" fallback simply stops applying, which is
                // the honest outcome: a past self whose own ball is unavailable does not take a
                // different one.
                item.itemId = $"{idPrefix}_{i}";
                item.displayName = displayName;
                item.icon = icon;
                item.floorY = radius;
                item.handLocalPosition = HandPoseFor(size);
                item.handLocalScale = Vector3.one;
                item.audioSource = MakeSource(holder.transform, "PickupAudio", 1f, 0.8f);
                item.pickupClip = LoadClip(SfxDir, "sfx_item_pickup");
                // THE ONLY CARRYABLES IN THE GAME THAT ARE NOT WHERE THEY WERE PUT - they drift, the
                // plastic balls shove them, and the drain's vortex drags every one of them to the
                // middle of the room. See PoolPropTakeReach and CarryableItem.ghostTakeReach.
                item.ghostTakeReach = PoolPropTakeReach;
                MakeWeighable(item, kilograms);
                carriedItems[i] = item;
            }

            Random.state = entry;

            FloatingBalls floating = root.AddComponent<FloatingBalls>();
            floating.bodies = bodies;
            floating.homes = homes;
            floating.carried = carriedItems;

            Debug.Log($"[SceneBuilder] Room2_6 pool: {count} x {name} at {size:0.00}m, {kilograms:0.0}kg each.");
            return floating;
        }

        // A STILL BODY OF WATER, which is not the same material as a falling one.
        //
        // `WaterStream.mat` is tuned for a column half a metre across dropping two metres: its noise is
        // fine and fast because a stream is moving, and at that scale on a ten-metre pool the surface
        // crawls with a rash of tiny ripples that reads as static. This is the same shader with the
        // noise an order of magnitude broader and ten times slower, more body to it (a metre of water
        // absorbs where a 45mm spill does not), and a softer meeting where it reaches the walls and the
        // player's legs - a hard line around a pair of knees is the tell that the water is a decal.
        private static Material MakePoolWaterMaterial()
        {
            const string path = MaterialsDir + "/PoolWater.mat";
            Shader shader = Shader.Find("IterationRoom/WaterSurface");
            if (shader == null)
            {
                Debug.LogWarning("[SceneBuilder] IterationRoom/WaterSurface is missing - the pool "
                               + "falls back to the stream material.");
                return AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WaterStream.mat");
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;

            mat.SetColor("_BaseColor", new Color(0.78f, 0.88f, 0.93f, 0.16f));
            mat.SetFloat("_Refraction", 0.030f);
            mat.SetFloat("_Reflection", 0.62f);
            mat.SetFloat("_Smoothness", 0.96f);
            mat.SetFloat("_SpecPower", 220f);
            mat.SetFloat("_SpecGain", 2.2f);
            mat.SetFloat("_FresnelPower", 3.2f);
            // MORE BODY THAN THE STREAM HAS, and it is depth that buys it: looking down into 1.2m of
            // water you are looking through 1.2m of water, and a pool as clear as a tap's output is a
            // sheet of glass over a white floor.
            mat.SetFloat("_EdgeAlpha", 0.80f);
            mat.SetFloat("_CoreAlpha", 0.22f);
            mat.SetColor("_DeepColor", new Color(0.30f, 0.50f, 0.60f, 1f));
            mat.SetFloat("_DeepStrength", 0.62f);
            // BROAD AND SLOW. This is the whole difference between a pool and a stream.
            mat.SetFloat("_NoiseScale", 3.6f);
            mat.SetFloat("_NoiseSpeed", 0.22f);
            mat.SetFloat("_NoiseAmount", 0.26f);
            mat.SetFloat("_WobbleAmp", 0.014f);
            mat.SetFloat("_WobbleFreq", 1.6f);
            mat.SetFloat("_WobbleSpeed", 0.45f);
            mat.SetFloat("_DepthFade", 0.25f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // THE BALLS ON THE POOL. The same six hues as room2-7's pit and the opposite construction -
        // three hundred separate objects rather than six merged meshes, because these have to move
        // independently. See FloatingBalls.
        //
        // THEY LOOKED CHEAP ON THE FIRST PASS, and "cheap" was three specific things (2026-08-19):
        //
        //  - **Too bright.** The pit's colours are display colours at full value, and full value under
        //    six ceiling fixtures in a white room is a flat bright patch with no shading left in it.
        //    A real ball has its colour in the MIDTONES and gets its brightness from the highlight.
        //  - **Too uniform.** Six colours across three hundred balls, every one the same shade of its
        //    colour and every one the same size. Nothing manufactured is that consistent, and the eye
        //    reads the repetition long before it reads the objects.
        //  - **Too faceted.** 96 triangles is right for a ball pit seen as a mass from two metres up;
        //    these float at knee height under the player's nose, and at that range a 96-triangle
        //    sphere has a visible polygon silhouette.
        // HOW MANY RINGS CAN BE IN FLIGHT AT ONCE. A ring lives 1.9s and the player makes one every
        // 0.55s of walking, so four covers a walk and the rest is headroom for the entry plus a player
        // turning circles. The cost of the cap is that a ninth ring recycles the oldest, which is the
        // one already almost invisible.
        private const int PoolRippleCount = 8;

        // THE RINGS THE PLAYER LEAVES IN THE WATER. Quads lying on the surface with a ripple painted
        // on them, expanded and faded by `WaterRipples`.
        //
        // DRAWN RATHER THAN SHADED. The water shader already has moving noise in it, but that noise is
        // the same everywhere and answers to nothing - a ripple has to start where the player is. The
        // alternative is feeding the surface shader a list of disturbance centres and ages; this is a
        // texture and eight quads, and it composes with a shader nobody has to touch.
        //
        // A MILLIMETRE ABOVE THE WATER, because two coplanar transparent surfaces is the same flicker
        // the mouth's tunnel just had.
        private static WaterRipples BuildWaterRipples(Transform poolRoot)
        {
            GameObject root = new GameObject("Ripples");
            root.transform.SetParent(poolRoot, false);
            root.transform.localPosition = new Vector3(0f, PoolDepth + 0.012f, 0f);

            Material mat = MakeRippleMaterial();
            Mesh quad = PrimitiveMesh(PrimitiveType.Quad);

            var rings = new Transform[PoolRippleCount];
            var renderers = new Renderer[PoolRippleCount];

            for (int i = 0; i < PoolRippleCount; i++)
            {
                GameObject go = new GameObject($"Ring_{i}");
                go.transform.SetParent(root.transform, false);
                // FLAT, and authored that way once. A quad faces -Z; ninety degrees about X lays it
                // face-up. `WaterRipples` never writes a rotation, precisely so this survives.
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                go.transform.localScale = Vector3.one * 0.5f;

                go.AddComponent<MeshFilter>().sharedMesh = quad;
                MeshRenderer rend = go.AddComponent<MeshRenderer>();
                rend.sharedMaterial = mat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
                rend.enabled = false;

                rings[i] = go.transform;
                renderers[i] = rend;
            }

            WaterRipples ripples = root.AddComponent<WaterRipples>();
            ripples.rings = rings;
            ripples.renderers = renderers;
            return ripples;
        }

        // UNLIT AND TRANSPARENT. A ripple is not a surface being lit - it is the water's own highlight
        // rearranged - so a lit material would put the room's six fixtures on it and make each ring a
        // shiny disc. Unlit also means the per-ring alpha can ride in a `MaterialPropertyBlock` without
        // eight materials, which is what `WaterRipples` fades.
        private static Material MakeRippleMaterial()
        {
            const string path = MaterialsDir + "/WaterRipple.mat";
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) unlit = OpaqueShader();

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(unlit);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = unlit;

            mat.SetTexture("_BaseMap", MakeRippleTexture("WaterRipple", 128));
            mat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));

            // The blend modes AND the keyword - alpha alone does nothing in URP (CLAUDE.md §3).
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 10;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // TWO RINGS, NOT ONE. A single ring reads as a stamp; a bright leading edge with a fainter one
        // chasing it reads as a wave that has been travelling. Both are soft-edged gaussians - a hard
        // ring is a drawn circle, and the eye finds the line before it finds the water.
        private static Texture2D MakeRippleTexture(string name, int size)
        {
            Color[] px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    float lead = Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.085f, 2f));
                    float trail = Mathf.Exp(-Mathf.Pow((r - 0.58f) / 0.13f, 2f)) * 0.42f;
                    // Cut at the edge of the disc, or the outer ring is clipped by the quad and the
                    // ripple has four straight sides.
                    float alpha = r > 1f ? 0f : Mathf.Clamp01(lead + trail);

                    px[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            return WriteTexture(name, size, size, px, TextureWrapMode.Clamp, FilterMode.Bilinear);
        }

        // HANGS A WEIGHT ON A CARRYABLE. One line at each call site, and the weight lives on the
        // object rather than in a table inside the room that reads it - see Weighable.
        private static Weighable MakeWeighable(Component item, float kilograms)
        {
            if (item == null) return null;
            Weighable w = item.gameObject.AddComponent<Weighable>();
            w.kilograms = kilograms;
            return w;
        }

        // ONE VALVE ON A WALL. The model is a handwheel and nothing else, so the WHOLE of it spins -
        // there is no body to hold still (checked: `industrial_valve.glb` is one `Torus001` group of
        // five parts).
        //
        // WHICH WAY IT FACES IS MEASURED, NOT WRITTEN DOWN. A handwheel is a disc, so its axle is its
        // THINNEST axis - found from the bounds and rotated to point out of the wall. That survives a
        // different model, a re-export, or the glTF Z-up correction changing under us, all of which
        // have cost this project a day each before (see the seesaw and the slide).
        private static Valve BuildValve(Transform room, string name, Vector3 wallPoint, float yaw)
        {
            GameObject holder = new GameObject(name);
            holder.transform.SetParent(room, false);
            holder.transform.localPosition = wallPoint;
            // Yaw so local +Z points INTO the room, whichever wall this is.
            holder.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            // BUILT BEFORE THE MODEL, because the model hangs under it - see the note on the reach
            // collider below for why the wheel has to be a descendant of the thing that turns it.
            GameObject spin = new GameObject(name + "Interact");
            spin.transform.SetParent(holder.transform, false);

            string path = PlayDir + "/industrial_valve.glb";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Transform wheel = null;
            Vector3 axle = Vector3.forward;
            float thickness = 0.18f;

            if (source != null)
            {
                ShrinkModelTextures(path, 1024);

                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source, spin.transform);
                model.name = "Wheel";
                model.transform.localPosition = Vector3.zero;

                Bounds raw = ModelBounds(spin.transform, model);
                // The thinnest of the three is the axle. Measured on the model as imported, so the
                // correction below is expressed in the model's own axes.
                axle = raw.size.x <= raw.size.y && raw.size.x <= raw.size.z ? Vector3.right
                     : raw.size.y <= raw.size.z ? Vector3.up : Vector3.forward;
                // TURNED TO FACE THE ROOM, AND THEN TURNED RIGHT ROUND. Aligning the axle to +Z is a
                // choice of AXIS, not of direction - it lands the wheel's face either toward the room
                // or into the wall, and this model comes out the wrong way. The extra half turn is
                // about the room's up axis so the wheel's front is what the player walks up to.
                model.transform.localRotation = Quaternion.AngleAxis(180f, Vector3.up)
                                              * Quaternion.FromToRotation(axle, Vector3.forward)
                                              * model.transform.localRotation;

                // Re-measured AFTER the turn: the fit is of the FACE of the wheel, which is a
                // different pair of axes once it has been stood up.
                raw = ModelBounds(spin.transform, model);
                float across = Mathf.Max(0.001f, Mathf.Max(raw.size.x, raw.size.y));
                float scale = ValveDiameter / across;
                model.transform.localScale = Vector3.one * scale;

                raw = ModelBounds(spin.transform, model);
                thickness = Mathf.Max(0.05f, raw.size.z);
                // Centred on the mount and standing PROUD of the wall by its own depth, so it is a
                // thing bolted on rather than a decal.
                model.transform.localPosition = new Vector3(-raw.center.x, -raw.center.y,
                                                            thickness / 2f + 0.02f - raw.center.z);

                // ONLY THE HANDWHEEL TURNS, not the whole fixture. A valve whose BODY spins with its
                // wheel is a valve nobody has ever seen: the body is bolted to the wall and the red
                // wheel is the part a pair of hands moves.
                //
                // FOUND BY SHAPE, not by name or material. The wheel is a disc, so it is the part with
                // the largest span across the face and the smallest along the axle - which stays true
                // of a re-export that renames every node, and of a different valve model entirely.
                // (Checked against this one: five parts, and the wheel is 1131 x 1131 x 170 where the
                // body is 477 x 477 x 809.)
                wheel = WidestFlattestPart(spin.transform, model) ?? model.transform;
            }
            else
            {
                Debug.LogWarning($"[SceneBuilder] {path} is missing - {name} is an invisible valve.");
            }

            // WHERE THE E DISC HANGS: on the wheel's face, not at the mount inside the wall.
            GameObject anchorGO = new GameObject("HintAnchor");
            anchorGO.transform.SetParent(holder.transform, false);
            anchorGO.transform.localPosition = new Vector3(0f, 0f, thickness + 0.05f);

            // Reach, like every other E fixture: a box in front of the wheel deep enough to stand in.
            //
            // THE REACH IS AN OFFSET ON THE COLLIDER, not on the object - the object sits at the
            // holder's origin so that it can be the WHEEL'S PARENT. That matters for one reason:
            // `MarkReflectionProbeStatic` decides what is a mover by walking UP from each renderer
            // looking for a component that moves it, so a spinning wheel whose `Valve` is a SIBLING
            // reads as scenery and gets baked into the room's reflection mid-turn.
            GameObject interact = spin;
            BoxCollider trigger = interact.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0f, 0.9f);
            trigger.size = new Vector3(1.9f, 2.4f, 2.0f);

            Valve valve = interact.AddComponent<Valve>();
            valve.wheel = wheel;
            valve.hintAnchor = anchorGO.transform;
            // THE AXIS IS THE HOLDER'S, NOT THE MODEL'S, now that what spins is a part INSIDE the
            // model rather than the model itself: that part carries the import's own rotation, and
            // the one thing known about it is that its face has been turned to look into the room.
            valve.spinAxis = wheel != null && wheel.parent != null
                ? wheel.parent.InverseTransformDirection(holder.transform.forward).normalized
                : axle;
            valve.audioSource = MakeSource(holder.transform, "ValveAudio", spatialBlend: 1f, volume: 0.85f);
            valve.turnClip = MakeValveTurnClip("sfx_valve_turn");
            valve.lockClip = MakeValveStopClip("sfx_valve_stop");
            return valve;
        }

        // THE FLATTEST, WIDEST PART OF A MODEL - which for a handwheel is the wheel. Compares each
        // renderer on its own bounds in the given frame: widest across the two long axes, thinnest
        // along the third. Returns null for a model with nothing in it.
        private static Transform WidestFlattestPart(Transform frame, GameObject model)
        {
            Transform best = null;
            float bestScore = float.NegativeInfinity;

            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                Mesh mesh = r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null;
                if (mesh == null) continue;

                Bounds b = ModelBounds(frame, r.gameObject);
                float[] size = { b.size.x, b.size.y, b.size.z };
                System.Array.Sort(size);                       // thinnest first
                if (size[2] < 0.0001f) continue;
                // Flat AND big: the ratio alone would pick a washer, the span alone the body.
                float score = size[2] / Mathf.Max(0.0001f, size[0]) * size[2];
                if (score <= bestScore) continue;

                bestScore = score;
                best = r.transform;
            }

            return best;
        }

        // THE DRAIN IN THE MIDDLE OF THE FLOOR, and what the three valves are for.
        //
        // THREE PARTS AND THEY ARE NOT INTERCHANGEABLE:
        //   - the SUMP, a dark box under the hole. It is what makes the drain a hole rather than a
        //     dark square painted on the floor - you can see down it;
        //   - the GRATE, bars across the top at floor level. SOLID, and that is a safety rule rather
        //     than a decoration: the floor has a real hole in it, and a player who could fall in would
        //     be stuck in a sump they cannot climb out of, in a room with a sixty-second clock;
        //   - the COVER, the one moving part, sliding aside when the valves are all open.
        private static PoolDrain BuildDrain(Transform room, WaterPool pool, Valve[] valves,
                                            Material floorMat, Material propMat)
        {
            GameObject root = new GameObject("Drain2_6");
            root.transform.SetParent(room, false);

            Material dark = MakeColorMaterial("DrainDark", new Color(0.045f, 0.05f, 0.055f));
            Material metal = MakeColorMaterial("DrainGrate", new Color(0.28f, 0.30f, 0.32f));
            SetSmoothness(metal, 0.55f);

            float half = DrainSize / 2f;

            // --- the sump ---------------------------------------------------------------------------
            // HUNG FROM THE FLOOR SLAB'S UNDERSIDE, not run up to its top face - which is the fix
            // `docs/gotchas.md` already records for the tree pit's lip, arrived at again here. The
            // sides used to start at y=0, so their top 0.1m occupied exactly the floor slab's volume
            // and their outer faces sat on exactly the plane of the slab's cut edge: two coplanar
            // surfaces fighting for the same pixels all the way round the hole, which is the flicker
            // play saw once the cover slid off it.
            //
            // Starting them a slab's thickness lower removes the shared plane rather than biasing it.
            // A depth offset only moves a z-fight somewhere else.
            float shaftTop = -WallThickness;
            float shaftDepth = DrainDepth - WallThickness;

            Prim(PrimitiveType.Cube, "Sump_Bottom", root.transform,
                 new Vector3(0f, -DrainDepth - WallThickness / 2f, 0f),
                 new Vector3(DrainSize + 2f * WallThickness, WallThickness, DrainSize + 2f * WallThickness),
                 dark);
            for (int i = 0; i < 4; i++)
            {
                bool alongX = i < 2;
                float sign = i % 2 == 0 ? 1f : -1f;
                float midY = shaftTop - shaftDepth / 2f;
                Vector3 at = alongX ? new Vector3(sign * (half + WallThickness / 2f), midY, 0f)
                                    : new Vector3(0f, midY, sign * (half + WallThickness / 2f));
                Vector3 size = alongX ? new Vector3(WallThickness, shaftDepth, DrainSize + 2f * WallThickness)
                                      : new Vector3(DrainSize, shaftDepth, WallThickness);
                Prim(PrimitiveType.Cube, "Sump_Side_" + i, root.transform, at, size, dark);
            }

            // --- the grate --------------------------------------------------------------------------
            // Seven bars with six gaps between them. The gaps read as a grate from standing height and
            // are far narrower than the player's own capsule.
            const int bars = 7;
            float pitch = DrainSize / bars;
            for (int i = 0; i < bars; i++)
            {
                float x = -half + pitch * (i + 0.5f);
                Prim(PrimitiveType.Cube, "Grate_" + i, root.transform,
                     new Vector3(x, -WallThickness / 2f, 0f),
                     new Vector3(pitch * 0.45f, WallThickness, DrainSize), metal);
            }

            // --- the cover --------------------------------------------------------------------------
            // Sitting ON the floor rather than recessed into it, which needs no second hole and reads
            // as a plate somebody laid over the drain. Its underside is level with the floor's top
            // face - two surfaces that touch and never share a visible plane.
            GameObject cover = Prim(PrimitiveType.Cube, "Cover", root.transform,
                new Vector3(0f, 0.025f, 0f),
                new Vector3(DrainSize + 0.12f, 0.05f, DrainSize + 0.12f), metal);

            // --- what goes down it ------------------------------------------------------------------
            GameObject vortexGO = new GameObject("Vortex");
            vortexGO.transform.SetParent(root.transform, false);
            vortexGO.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            ParticleSystem vortex = vortexGO.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule vm = vortex.main;
            vm.loop = true;
            vm.playOnAwake = false;
            vm.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
            vm.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
            vm.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
            vm.gravityModifier = 1.4f;
            vm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.90f, 0.95f, 1f, 0.85f));
            vm.simulationSpace = ParticleSystemSimulationSpace.World;
            vm.maxParticles = 300;
            ParticleSystem.EmissionModule ve = vortex.emission;
            ve.rateOverTime = 90f;
            ParticleSystem.ShapeModule vs = vortex.shape;
            vs.shapeType = ParticleSystemShapeType.Circle;
            vs.radius = half * 0.9f;
            ParticleSystemRenderer vr = vortexGO.GetComponent<ParticleSystemRenderer>();
            vr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WaterStream.mat");
            vr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // --- the whirlpool ----------------------------------------------------------------------
            // A FUNNEL, not a spinning texture. The dip in the surface is the thing that says the
            // water is going somewhere - a flat sheet that shrinks reads as the water being switched
            // off - so this is real geometry with the water shader on it, rotating and narrowing as
            // the room empties. Hidden until the drain opens; `PoolDrain` owns both.
            GameObject funnel = new GameObject("Funnel");
            funnel.transform.SetParent(root.transform, false);
            funnel.AddComponent<MeshFilter>().sharedMesh = SaveGeneratedMesh(
                "WaterFunnel_r24",
                () => WaterMeshes.Funnel(rings: 14, segments: 24, throat: 0.16f, swirl: 1.35f));
            MeshRenderer funnelRend = funnel.AddComponent<MeshRenderer>();
            funnelRend.sharedMaterial = MakePoolWaterMaterial();
            funnelRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            funnelRend.receiveShadows = false;
            funnel.SetActive(false);

            PoolDrain drain = root.AddComponent<PoolDrain>();
            drain.funnel = funnel.transform;
            drain.valves = valves;
            drain.pool = pool;
            drain.cover = cover.transform;
            // Aside by its own width plus a hand's breadth, so the hole is completely clear.
            drain.coverOpenOffset = new Vector3(DrainSize + 0.2f, 0f, 0f);
            drain.vortex = vortex;
            drain.audioSource = MakeSource(root.transform, "DrainAudio", spatialBlend: 1f, volume: 0.8f);
            // A room emptying is long, low and continuous - so it is the wade sound stretched out and
            // looped rather than a splash. See MakeWadeClip for why nothing in this room is a splash.
            drain.drainClip = MakeWadeClip("sfx_drain_run", 20260823, seconds: 1.6f, decay: 0.9f,
                                           fizzLevel: 0.62f, lowWeight: 3.0f);
            // What the water drags with it. Named by the caller, because the pool knows its float
            // groups and this does not - see BuildPoolRoom.
            drain.floats = pool != null ? pool.floats : null;
            return drain;
        }

        // WHAT EVERYTHING WEIGHS, and what room2-7's scale is looking for. **The player is never told
        // any of this** - the only way to learn a weight is to carry the thing to the scale and put it
        // down, which is what makes that room a measuring instrument rather than a keypad.
        //
        // HOW THE NUMBERS WERE CHOSEN, and they were SOLVED FOR rather than picked (2026-08-19).
        //
        // The room's rule is "bring all of them", so the target is the sum of one of each - and the
        // weights are chosen so that **no other combination in the whole cycle reaches it.**
        //
        //     cube 8.3  +  axe 4.7  +  beach ball 1.3  +  duck 0.4  =  14.7
        //
        // **THE BUCKET IS OUT** (2026-08-29, by request), and taking it out CLOSED THE ONE HOLE THIS
        // PROOF HAD. A partly filled pail is a CONTINUOUS weight - `Weighable` reads `Bucket.Level`,
        // which is anything at all while a bucket sits under a tap - so a player who knew the weights
        // could dial in any total they liked and the room was never single-solution. Every other
        // object here is a fixed number. With no bucket on the pan the exhaustive check is exhaustive
        // again.
        //
        // **THE WEIGHTS DID NOT HAVE TO CHANGE.** Re-run against everything cycle 2 still contains
        // (5 axes, 1 cube, 2 beach balls, 3 ducks), 14.7 has exactly one solution - and the nearest
        // miss is 0.2 rather than the old 0.1, so the room got slightly MORE readable by losing an
        // ingredient. What changed is the target, which the sign and the door both derive.
        //
        // ~~cube 8.3 + full bucket 12.0 + axe 4.7 + beach ball 1.3 + duck 0.4 = 26.7~~
        //
        // That is what the side walls draw and what the number over the door asks for. The player is
        // still told no weights: the sign names the INGREDIENTS and the door names the ANSWER, and
        // which of them add up is the thing to work out on the scale.
        //
        // THE REAL COST IS TRIPS. One object in the hand (CLAUDE.md §4), a 60-second clock, and
        // `ItemRegistry.ReturnAllToOrigin` sweeping everything home at the boundary - so four objects
        // on the pan at once is four past selves each carrying one. Still the largest headcount any
        // room in this game asks for, and one less than it was: the bucket's leg was also the longest,
        // since a full one had to be filled at a tap in room2-2 before it could be carried anywhere.
        private const float WeightDuck = 0.4f;
        private const float WeightBeachBall = 1.3f;
        // ~~The bucket's weights~~ UNUSED since 2026-08-29 - it is no longer `Weighable`. Kept
        // rather than deleted because they are the only record of what a full pail weighed, and the
        // decision that took it off the scale is one somebody may want to reverse.
        private const float WeightBucket = 2.1f;
        private const float WeightBucketWater = 9.9f;       // a full bucket read 12.0
        private const float WeightAxe = 4.7f;
        private const float WeightCube = 8.3f;
        private const float WeightPlayer = 71f;
        private const float WeighTarget = 14.7f;

        // ROOM2-6'S PUZZLE, and the numbers are here for the reason every tuned number is (CLAUDE.md
        // §2): the components own the mechanism, this owns how much of it there is.
        //
        // WHEEL HEIGHT. A valve is turned with both hands at chest height - lower and the player is
        // looking down at their own feet to find it, higher and it is a thing on a wall rather than a
        // thing you operate.
        private const float ValveHeight = 1.45f;
        // How big the wheel is across. Deliberately large: it has to be findable from the far side of
        // a nine-metre room through waist-deep water, and it is the only fixture in here.
        private const float ValveDiameter = 0.62f;
        // THE DRAIN in the middle of the floor. A square hole, because `BuildSlab` subtracts a Rect -
        // the grate over it is what gives it a shape.
        private const float DrainSize = 0.95f;
        // How deep the sump under it is. Only ever seen through the grate, so it is depth enough to
        // read as a shaft and no more.
        private const float DrainDepth = 0.9f;

        // How heavy a floating thing is. All three are light and within a factor of three of each
        // other, so the player's push (`FirstPersonController.pushSpeed`, tuned on the balloons) moves
        // any of them and none of them barges the others aside.
        private const float PoolBallMass = 0.05f;
        private const float PoolPropMass = 0.09f;

        // ONE BODY, ONE COLLIDER, ONE LAYER - everything that floats gets the same treatment.
        //
        // NO GRAVITY. Buoyancy is a spring in `FloatingBalls` rather than gravity fought by a water
        // volume: nothing can sink, nothing can be knocked out of the pool, and there is no settling
        // for a hard shove to get wrong.
        //
        // ON THE BALLOON LAYER, which is not a borrow of convenience. That layer means exactly "a light
        // thing the player wades THROUGH and shoves aside": the CharacterController excludes it, so
        // there is no invisible wall in the pool, and `pushLayers` already contains it, so the push
        // sweep works with nothing rewired. The Balloon COMPONENT is what balloon code looks for, and
        // none of these carry one.
        private static (Rigidbody body, SphereCollider solid) MakeFloatBody(GameObject go, float radius,
                                                                             float mass, bool upright)
        {
            // THE SOLID PART IS A CHILD, and that is load-bearing rather than tidy. The ducks and the
            // beach balls also carry a `CarryableItem`, which takes `GetComponent<Collider>()` on its
            // own object as its REACH trigger - with the physics sphere on the same object, which of
            // the two it picks is a coin toss. Same trap `BuildKeyPlinth` and the symbol cubes
            // document, and the same answer: one collider per object, and the rigidbody is happy to
            // own a collider a level down.
            GameObject solidGO = new GameObject("Solid");
            solidGO.transform.SetParent(go.transform, false);
            solidGO.layer = go.layer;

            SphereCollider col = solidGO.AddComponent<SphereCollider>();
            col.radius = radius;
            col.sharedMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(
                MaterialsDir + "/BalloonRubber.physicMaterial");

            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.useGravity = false;
            // Enough that a shoved float coasts to a stop in a second or so, which is water, and
            // enough damping that the mooring cannot set up a slow oscillation across the pool.
            rb.linearDamping = 1.4f;
            rb.angularDamping = 1.6f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            // UPRIGHT for anything with a top - a duck rolled onto its side by a ball is a dead duck.
            // A plastic ball is a single-coloured sphere, so its rotation is invisible and freezing it
            // is free solver work saved.
            rb.constraints = upright
                ? RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ
                : RigidbodyConstraints.FreezeRotation;
            return (rb, col);
        }

        private static FloatingBalls BuildFloatingBalls(Transform poolRoot)
        {
            int floatLayer = EnsureLayer(BalloonLayerName);
            // A HIGHER-RESOLUTION BALL THAN THE PIT'S, and its own asset - the resolution is in the
            // name because `SaveGeneratedMesh` caches by name (see the note on it). 384 triangles at
            // 300 balls is 115k for the room, against the pit's 554k for its 5,772.
            Mesh ball = SaveGeneratedMesh("PoolBall_r12s16",
                () => LowPolySphereMesh(PoolBallRadius, 12, 16));
            if (ball == null) return null;

            GameObject root = new GameObject("FloatingBalls");
            root.transform.SetParent(poolRoot, false);

            // TWO TONES PER HUE, so twelve materials carry the six colours. The variation is what the
            // eye reads as "a lot of balls" rather than "six balls repeated", and two shades is enough
            // - the tones are half a stop apart, not a different colour.
            Material[] mats = new Material[PlasticBallColours.Length * 2];
            for (int i = 0; i < PlasticBallColours.Length; i++)
            {
                for (int tone = 0; tone < 2; tone++)
                {
                    Color hue = PlasticBallColours[i];
                    // DOWN, not up. The colour is what the ball is made of and the room is what lights
                    // it; a base colour near 1.0 leaves the light nowhere to go and the ball reads as
                    // if it is emitting.
                    //
                    // THE TONE, and it is a scale on a SATURATED hue rather than a wash toward grey -
                    // which is the whole reason 0.55/0.72 read as too bright and 0.30/0.42 as too
                    // dull. Scaling a pure hue keeps the chroma and only takes the light out of it, so
                    // these are plastic in a brightly lit white room: definitely red, definitely blue,
                    // and not glowing.
                    float level = tone == 0 ? 0.52f : 0.68f;
                    Material mat = MakeColorMaterial($"PoolBall_{i}_{tone}", hue * level);
                    // WET PLASTIC. High enough to resolve the ceiling fixtures as distinct shapes in
                    // the reflection - which is the structure that reads as a hard shiny surface, the
                    // same reasoning the escape objects' 0.97 is chosen on (CLAUDE.md §3). A ball that
                    // has just come out of the water is glossier than a dry one, not duller.
                    // A HARD SMALL HIGHLIGHT, NOT A MIRROR. At 0.88 the whole white ceiling sat on
                    // every ball and drank the colour; at 0.62 the highlight is a spot and the rest of
                    // the sphere is its own colour, which is what a wet moulded ball looks like.
                    SetSmoothness(mat, 0.62f);
                    // Three hundred renderers on twelve materials: instancing is what keeps that a
                    // handful of draws rather than three hundred.
                    mat.enableInstancing = true;
                    EditorUtility.SetDirty(mat);
                    mats[i * 2 + tone] = mat;
                }
            }

            // A JITTERED GRID RATHER THAN FREE RANDOM POSITIONS. Uniform random scatter clumps - it
            // puts three balls inside each other and leaves a two-metre hole across the room - and the
            // one thing floating objects reliably do is push each other apart. One ball per cell with a
            // jitter inside it is that spacing, for free and without a relaxation pass.
            float inset = PoolBallRadius * 2f + 0.15f;
            float spanX = RoomWidth - inset * 2f, spanZ = RoomDepth - inset * 2f;
            // Half again as many cells as balls, so which cells are empty is itself part of the
            // scatter - a grid with one ball in every cell is visibly a grid.
            float cell = Mathf.Sqrt(spanX * spanZ / (PoolBallCount * 1.5f));
            int columns = Mathf.Max(1, Mathf.FloorToInt(spanX / cell));
            int rows = Mathf.Max(1, Mathf.FloorToInt(spanZ / cell));
            float cellX = spanX / columns, cellZ = spanZ / rows;

            // A FIXED SEED, like the balloon field's and the ball pit's: the room has to be the same
            // room every time it is built, or a rebuild quietly reshuffles a scene somebody has already
            // looked at.
            Random.State entry = Random.state;
            Random.InitState(20260819);

            int cells = columns * rows;
            int[] order = new int[cells];
            for (int i = 0; i < cells; i++) order[i] = i;
            for (int i = cells - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            int count = Mathf.Min(PoolBallCount, cells);
            var bodies = new Rigidbody[count];
            var homes = new Vector3[count];

            // How high the centre sits: a ball `PoolBallFloat` of the way out of the water has its
            // centre that fraction of a radius above the surface. Written as the arithmetic rather than
            // as a number, so changing how buoyant they look is one constant.
            float centreOffset = PoolBallRadius * (2f * PoolBallFloat - 1f);
            float jitterX = Mathf.Max(0f, cellX * 0.5f - PoolBallRadius);
            float jitterZ = Mathf.Max(0f, cellZ * 0.5f - PoolBallRadius);

            for (int i = 0; i < count; i++)
            {
                int c = order[i] % columns, r = order[i] / columns;
                float x = -spanX / 2f + (c + 0.5f) * cellX + Random.Range(-jitterX, jitterX);
                float z = -spanZ / 2f + (r + 0.5f) * cellZ + Random.Range(-jitterZ, jitterZ);

                GameObject go = new GameObject($"Ball_{i}");
                go.transform.SetParent(root.transform, false);
                go.layer = floatLayer;
                go.AddComponent<MeshFilter>().sharedMesh = ball;
                MeshRenderer rend = go.AddComponent<MeshRenderer>();
                rend.sharedMaterial = mats[Random.Range(0, mats.Length)];
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                // NOT ALL THE SAME BALL. A tenth either way, which is under the threshold at which any
                // one of them looks wrong and over the one at which three hundred identical spheres
                // look printed. It rides in the transform rather than in the mesh, so it is free.
                float scale = Random.Range(0.90f, 1.10f);
                go.transform.localScale = Vector3.one * scale;

                homes[i] = new Vector3(x, PoolDepth + centreOffset * scale, z);
                go.transform.localPosition = homes[i];
                (bodies[i], _) = MakeFloatBody(go, PoolBallRadius * scale, PoolBallMass, upright: false);
            }

            Random.state = entry;

            FloatingBalls floating = root.AddComponent<FloatingBalls>();
            floating.bodies = bodies;
            floating.homes = homes;
            // STILL WATER, AND STILL BALLS. No bob and no drift, which is what lets a settled ball
            // sleep - see FloatingBalls. What moves them is the player walking through, and then the
            // mooring walks them back.
            floating.bobHeight = 0f;
            floating.drift = 0f;

            Debug.Log($"[SceneBuilder] Room2_6 pool: {PoolDepth:0.00}m of water, {count} floating "
                    + $"balls of {PoolBallRadius * 2f:0.00}m on a {columns}x{rows} grid.");
            return floating;
        }

        // ROOM2-7: THE BALL PIT, and the room IS the pit.
        //
        // The prop is a 0.68m box of balls; the room is 8.75 x 10.5. So the model is placed as the
        // thing the balls came out of, and the floor of the room is filled separately - to hip height
        // on a standing player, which is what "up to half the body" comes to at this eye height.
        //
        // THE BALLS ARE GENERATED RATHER THAN TILED FROM THE MODEL, and the reason is arithmetic. Each
        // of the prop's colour clusters is ~3,500 triangles for a 0.62m square of balls; tiling that
        // across this floor three deep is over two million triangles for one room, which is the scale
        // of the stump this project already replaced once for exactly that reason. A generated ball is
        // 96 triangles, and the whole pit comes out around 400k - built as ONE MESH PER COLOUR, so the
        // room costs six draw calls rather than five thousand.
        //
        // NOTHING ABOUT THEM IS SOLID. The player wades: they walk on the real floor with the mass at
        // their waist, which is both what a ball pit feels like and the only version that does not need
        // five thousand colliders. It also keeps them out of the loop entirely - no carryables, no
        // recorded state, nothing to rewind.
        private static void BuildBallPit(Transform room, Material propMat)
        {
            GameObject root = new GameObject("Room2_7_BallPit");
            root.transform.SetParent(room, false);

            // --- the prop -------------------------------------------------------------------------
            string path = PlayDir + "/ball_pit.glb";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source != null)
            {
                ShrinkModelTextures(path, 1024);

                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                model.name = "PitProp";

                Bounds raw = ModelBounds(root.transform, model);
                // A ball pit somebody could climb into rather than a desk toy: 1.9m across, which is
                // where the model's own 0.68m lands when its FOOTPRINT is fitted rather than its
                // height. Measured, so a different prop re-fits itself.
                const float propAcross = 1.9f;
                float scale = propAcross / Mathf.Max(0.001f, Mathf.Max(raw.size.x, raw.size.z));
                model.transform.localScale = Vector3.one * scale;

                // In the corner away from both doorways - the doors are in the Z walls at x=0, so a
                // prop pushed into +X and +Z stands clear of both approaches.
                Vector3 stand = new Vector3(RoomWidth / 2f - 1.6f, 0f, RoomDepth / 2f - 1.7f);
                model.transform.localPosition = new Vector3(stand.x - raw.center.x * scale,
                                                            stand.y - raw.min.y * scale,
                                                            stand.z - raw.center.z * scale);

                Debug.Log($"[SceneBuilder] Ball pit prop: scale {scale:0.000} -> {raw.size.x * scale:0.00}m across");
            }
            else
            {
                Debug.LogWarning($"[SceneBuilder] {path} is missing - room2-7 gets the loose balls only.");
            }

            // --- the fill -------------------------------------------------------------------------
            BuildBallFill(root.transform, propMat);
        }

        // HOW DEEP THE BALLS COME. The player's eye is at 1.6m and their body is 1.8m, so 0.9m is the
        // waist - "half the body", which is what was asked for. It is also the depth at which a
        // standing player can never see the floor of the pit, which is what keeps three layers enough.
        private const float BallFillDepth = 0.9f;
        // 220mm - twice a real ball-pit ball. Deliberately: at 70mm this room needs fifty thousand of
        // them to fill, and a bigger ball reads correctly in a building whose every other dimension is
        // oversized already.
        private const float BallRadius = 0.11f;
        // THREE LAYERS, NOT THE FULL DEPTH. Five would fill the volume honestly and cost 60% more
        // triangles for a thing nobody can see: the mass is only ever looked at from above, because a
        // 1.6m eye is above a 0.9m surface from every position in the room. Each layer is offset by
        // half a pitch from the one under it, so the gaps between three of them line up with nothing.
        private const int BallLayers = 3;

        // The colours the ball-pit prop wears - blue, purple, yellow, green, red, orange. Values live
        // here like every other tuned number in this project, and they are SHARED with the balls
        // floating on room2-5's pool: the two rooms are meant to be the same plastic out of the same
        // building, and two copies of six colours is two copies that can drift apart.
        // SATURATED RATHER THAN BRIGHT, which is the distinction the pool's balls took three passes to
        // land on. "Too bright" and "not distinct enough" sound like opposites and are not: what was
        // wrong both times was CHROMA. A colour near white in one channel and mid in the others is
        // pale - it renders as a bright pastel under this building's flat white light, and pale
        // colours are also the ones hardest to tell apart. These are close to pure hues, with the
        // off-channels pushed down rather than the on-channel pushed up, so they can be darkened for
        // the room (see the tone below) and still read as red, blue, yellow.
        private static readonly Color[] PlasticBallColours =
        {
            new Color(0.05f, 0.28f, 0.95f),   // blue
            new Color(0.60f, 0.06f, 0.80f),   // purple
            new Color(1.00f, 0.80f, 0.02f),   // yellow
            new Color(0.05f, 0.70f, 0.16f),   // green
            new Color(0.92f, 0.05f, 0.07f),   // red
            new Color(1.00f, 0.40f, 0.02f),   // orange
        };

        private static void BuildBallFill(Transform root, Material propMat)
        {
            Color[] colours = PlasticBallColours;

            Mesh ball = SaveGeneratedMesh("BallPitBall", () => LowPolySphereMesh(BallRadius, 6, 8));
            if (ball == null) return;

            // INSET FROM THE WALLS by a ball's own width, so nothing pokes through a wall or through a
            // door slab sitting in its pocket.
            float inset = BallRadius * 2f;
            float halfX = RoomWidth / 2f - inset, halfZ = RoomDepth / 2f - inset;

            // Hexagonal packing: rows offset by half a pitch, which is how balls actually settle and
            // what stops the grid reading as a grid.
            float pitch = BallRadius * 2f;
            float rowPitch = pitch * 0.87f;                 // sqrt(3)/2, the hex row spacing
            float layerDrop = BallRadius * 1.55f;           // layers sit in each other's hollows

            int columns = Mathf.FloorToInt(2f * halfX / pitch);
            int rows = Mathf.FloorToInt(2f * halfZ / rowPitch);

            var byColour = new System.Collections.Generic.List<CombineInstance>[colours.Length];
            for (int i = 0; i < colours.Length; i++) byColour[i] = new System.Collections.Generic.List<CombineInstance>();

            // A FIXED SEED, like the balloon field's: the room has to be the same room every time it is
            // built, or a rebuild quietly reshuffles a scene somebody has already looked at.
            Random.State entry = Random.state;
            Random.InitState(20260817);

            int placed = 0;
            for (int layer = 0; layer < BallLayers; layer++)
            {
                float y = BallFillDepth - BallRadius - layer * layerDrop;
                if (y < BallRadius) break;

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < columns; c++)
                    {
                        // Each layer is shifted half a pitch in both axes from the one above, so no
                        // column of gaps ever runs straight through the mass.
                        float ox = (r % 2 == 0 ? 0f : pitch * 0.5f) + (layer % 2 == 0 ? 0f : pitch * 0.25f);
                        float oz = layer % 2 == 0 ? 0f : rowPitch * 0.5f;

                        float x = -halfX + pitch * 0.5f + c * pitch + ox;
                        float z = -halfZ + rowPitch * 0.5f + r * rowPitch + oz;
                        if (x > halfX || z > halfZ) continue;

                        // Loose balls do not sit level. Only the top layer is jittered upward - the
                        // ones underneath are held down by the ones on top.
                        float jitter = layer == 0 ? Random.Range(-0.35f, 0.55f) * BallRadius
                                                  : Random.Range(-0.2f, 0.2f) * BallRadius;

                        var ci = new CombineInstance();
                        ci.mesh = ball;
                        ci.transform = Matrix4x4.TRS(
                            new Vector3(x, y + jitter, z),
                            Random.rotationUniform,
                            Vector3.one);
                        byColour[Random.Range(0, colours.Length)].Add(ci);
                        placed++;
                    }
                }
            }

            Random.state = entry;

            int tris = 0;
            for (int i = 0; i < colours.Length; i++)
            {
                if (byColour[i].Count == 0) continue;

                Mesh merged = new Mesh { name = $"BallPitFill_{i}" };
                // A merged ball field runs past 65,535 vertices in one colour, and the default index
                // format silently wraps rather than failing - the symptom is a mesh that renders as
                // garbage triangles across the room.
                merged.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                merged.CombineMeshes(byColour[i].ToArray(), true, true);
                merged.RecalculateBounds();

                string meshPath = GeneratedDir + "/BallPitFill_" + i + ".mesh";
                AssetDatabase.DeleteAsset(meshPath);
                if (!Directory.Exists(GeneratedDir)) Directory.CreateDirectory(GeneratedDir);
                AssetDatabase.CreateAsset(merged, meshPath);

                Material mat = MakeColorMaterial($"BallPit_{i}", colours[i]);
                SetSmoothness(mat, 0.72f);

                GameObject go = new GameObject($"Balls_{i}");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;

                tris += merged.triangles.Length / 3;
            }

            AssetDatabase.SaveAssets();

            Debug.Log($"[SceneBuilder] Ball pit: {placed} balls of {BallRadius * 2f:0.00}m in "
                    + $"{BallLayers} layers to {BallFillDepth:0.00}m, {tris} triangles in "
                    + $"{colours.Length} draw calls");
        }

        // A BALL, cheap enough to have five thousand of. Unity's own sphere primitive is 515 triangles,
        // which is 2.7 million for this room; this is 96, and at 220mm across seen from a metre or more
        // the difference is not visible.
        private static Mesh LowPolySphereMesh(float radius, int rings, int segments)
        {
            var verts = new System.Collections.Generic.List<Vector3>();
            var norms = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();

            for (int y = 0; y <= rings; y++)
            {
                float v = (float)y / rings;
                float phi = v * Mathf.PI;
                for (int x = 0; x <= segments; x++)
                {
                    float u = (float)x / segments;
                    float theta = u * Mathf.PI * 2f;
                    Vector3 n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta),
                                            Mathf.Cos(phi),
                                            Mathf.Sin(phi) * Mathf.Sin(theta));
                    norms.Add(n);
                    verts.Add(n * radius);
                }
            }

            int stride = segments + 1;
            for (int y = 0; y < rings; y++)
            {
                for (int x = 0; x < segments; x++)
                {
                    int a = y * stride + x, b = a + stride;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            }

            Mesh mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // THE TREE HALL - room2-3, room2-4 and room2-5 as ONE ROOM: 10.5m across, 30.45m along and
        // 17.57m to the ceiling, at one width and one height throughout. See the constants block.
        //
        // Built at the hall's own origin with Z relative and X absolute, which is the frame the ring
        // already works in for this leg - so both openings land in the walls their neighbours meet.
        private static (Transform root, TreeTrunk trunk, TreeFelled felled,
                        Transform poolRoom, Door poolDoor, PoolDrain poolDrain, Valve[] poolValves,
                        Transform weighRoom, WeighScale weighScale,
                        Transform breakRoom, FinalRoomSequence breakSequence, CycleExit cycleTwoExit)
            BuildTreeHall(Transform parent, float z0, Material floorMat, Material grooveMat,
                          Material panelMat, Material propMat, Material fixtureMat)
        {
            GameObject rootGO = new GameObject("Room2_TreeHall_Root");
            rootGO.transform.SetParent(parent, false);
            rootGO.transform.localPosition = new Vector3(0f, 0f, z0);

            GameObject hallGO = new GameObject("Room2_TreeHall");
            hallGO.transform.SetParent(rootGO.transform, false);
            Transform t = hallGO.transform;

            float spanX = TreeHallEastFace - TreeHallWestFace;      // 30.45
            float centreX = (TreeHallEastFace + TreeHallWestFace) / 2f;

            // Slabs overrun the interior by a wall's depth so they meet the neighbouring rooms' own
            // under the shared dividers - the same reason BuildSlab runs to the room PITCH.
            Rect floorXZ = Rect.MinMaxRect(TreeHallWestFace - WallDepth, TreeHallSouthFace - WallDepth,
                                           TreeHallEastFace + WallDepth, TreeHallNorthFace + WallDepth);
            // THE PIT IS CUT WALL TO WALL - to the walls' FACES, and not one centimetre past them.
            //
            // It used to be cut across the whole floor slab, and the slab deliberately overruns the
            // interior by a `WallDepth` at each end so that it meets the neighbouring rooms' slabs
            // under the shared dividers. Cutting the pit over that overrun takes the floor out from
            // UNDER the hall's own north and south walls, which leaves a 0.125m slot running along the
            // base of each of them with nothing but shaft on the other side - play saw it as the
            // outside of the level showing through at the pit's ends.
            //
            // Stopping at the faces costs nothing the original note was protecting: the ledge it warns
            // about would be a ledge you could stand on INSIDE the room, and this one is buried in the
            // wall's own body where nobody can reach it or see it. The pit is still the full width of
            // the space, and the tree is still the only way across.
            Rect pitXZ = Rect.MinMaxRect(TreePitWestEdge, TreeHallSouthFace,
                                         TreePitEastEdge, TreeHallNorthFace);

            BuildSlabRect(t, "Floor", -WallThickness / 2f, floorXZ, floorMat, pitXZ);

            // ~~THE CEILING FADED TO VOID~~ REVERTED 2026-08-16, by request. The upper walls were
            // darkened in four bands and the ceiling made near-black, so the hall had no findable
            // top. It works, and it was judged the wrong feeling for this room - so the ceiling is an
            // ordinary ceiling again. `DarkenAbove` is gone with it; the panel-display exclusion it
            // needed is documented in docs/gotchas.md, because the trap is real for the next thing
            // that tries to recolour a wall panel.
            BuildSlabRect(t, "Ceiling", TreeHallHeight + WallThickness / 2f, floorXZ, CeilingMaterial());

            // THE NORTH WALL CARRIES BOTH DOORWAYS, which is what a single width bought - they used
            // to be in two walls 0.875m apart. `SubtractRect` takes one hole, so the wall is emitted
            // as two spans split between them; each span is a plain wall with one opening.
            float splitX = (TreeHallEntranceX + TreeHallExitX) / 2f;    // -10.4125, between the two

            BuildPanelWallSpan(t, "Wall_North_East", new Vector3(0f, 0f, TreeHallNorthFace),
                Vector3.right, Vector3.back, splitX, TreeHallEastFace, grooveMat, panelMat,
                Rect.MinMaxRect(TreeHallEntranceX - DoorWidth / 2f, 0f,
                                TreeHallEntranceX + DoorWidth / 2f, DoorHeight), TreeHallHeight);
            // THE WEST SPAN IS SOLID NOW. It carried the way out onto room2-6, and 2026-08-19's
            // second pass deleted that room along with the whole of leg 3 - the way on is the SLIDE,
            // through the south wall and a storey down. A doorway onto nothing is worse than a wall:
            // it is a promise the building cannot keep.
            BuildPanelWallSpan(t, "Wall_North_West", new Vector3(0f, 0f, TreeHallNorthFace),
                Vector3.right, Vector3.back, TreeHallWestFace, splitX, grooveMat, panelMat,
                Rect.zero, TreeHallHeight);

            // THE SOUTH WALL CARRIES ONE OPENING - the slide's mouth at the west corner. It carried a
            // second, the way back out of the room the slide lands in, until that room moved a storey
            // down on 2026-08-19: a same-level doorway stopped making sense the moment the floor on
            // the other side of it was no longer at this floor's height, so it is gone with the room
            // move rather than repositioned. Back to one span until the exit is designed.
            BuildPanelWallSpan(t, "Wall_South", new Vector3(0f, 0f, TreeHallSouthFace),
                Vector3.right, Vector3.forward, TreeHallWestFace, TreeHallEastFace,
                grooveMat, panelMat,
                Rect.MinMaxRect(TreeHallWestFace, 0f, SlideMouthEast, SlideMouthHeight),
                TreeHallHeight);
            BuildPanelWall(t, "Wall_East", new Vector3(TreeHallEastFace, 0f, 0f),
                Vector3.forward, Vector3.left, TreeHallWidth, grooveMat, panelMat, Rect.zero, TreeHallHeight);
            BuildPanelWall(t, "Wall_West", new Vector3(TreeHallWestFace, 0f, 0f),
                Vector3.forward, Vector3.right, TreeHallWidth, grooveMat, panelMat, Rect.zero, TreeHallHeight);

            BuildTreePitShaft(t, pitXZ);

            // --- the tree ---------------------------------------------------------------------------
            // BUILT BEFORE THE LIGHTS, so the lights can be told where its canopy is. Nothing else
            // about the order matters; this does.
            TreeTrunk trunk = BuildTree(t, propMat);

            Bounds canopy = new Bounds(Vector3.zero, Vector3.zero);
            bool hasCanopy = false;
            if (trunk != null)
            {
                foreach (Renderer r in trunk.transform.parent.GetComponentsInChildren<Renderer>(true))
                {
                    if (hasCanopy) canopy.Encapsulate(r.bounds); else { canopy = r.bounds; hasCanopy = true; }
                }
            }

            // --- light ---------------------------------------------------------------------------
            // SINGLE FIXTURES ON THE HALL'S CENTRE LINE, not the 2x2 cluster every other room gets.
            //
            // `BuildCeilingLights` lays four fixtures at xCentre +/- 1.75 and z +/- 2.6, which is a
            // 3.5 x 5.2m footprint sized for an 8.75 x 10.5 room. In a 30m hall that block is both
            // the wrong shape and impossible to place: the near one put a fixture 0.6m THROUGH the
            // east wall, and the far one hung a fixture over the pit's lip - the "odd positions" play
            // reported. A hall this long wants a row, and a row is what this is.
            //
            // ROOM5'S END ONLY, by request. The near ledge and the pit are unlit from above: you walk
            // in under a dark canopy, and the far side of the hole is the lit thing you are heading
            // for. That is the room telling you where to go with light instead of with a sign.
            // ROOM3 IS LIT AGAIN, and where its fixtures can go is decided entirely by the canopy.
            // The crown is 12m across and its underside is 3.4m up, so a fixture hanging at 5.4m
            // anywhere inside that footprint is buried in leaves and lights nothing. What is left is
            // the strip between the east wall and the crown's edge - which is also where the player
            // walks in, so the room lights the way you came from and leaves the tree in shadow.
            //
            // Local x here; the canopy sits at local -1.35..-10.65 (world +1.35..+10.65).
            float[] lightX = { 3.6f, 1.9f, -18.6f, -23.4f };
            foreach (float x in lightX)
            {
                if (x < TreePitEastEdge && x > TreePitWestEdge)
                {
                    Debug.LogError($"[SceneBuilder] tree hall: a light at x={x} hangs over the pit.");
                    continue;
                }

                GameObject fixture = new GameObject($"TreeHall_Fixture_{x:0.0}");
                fixture.transform.SetParent(t, false);
                fixture.transform.localPosition = new Vector3(x, RoomHeight, 0f);

                Prim(PrimitiveType.Cube, "Panel", fixture.transform,
                    new Vector3(0f, -0.02f, 0f), new Vector3(1.4f, 0.04f, 1.4f),
                    fixtureMat, removeCollider: true);

                GameObject lightGO = new GameObject("Light");
                lightGO.transform.SetParent(fixture.transform, false);
                lightGO.transform.localPosition = new Vector3(0f, -0.04f, 0f);
                lightGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Light light = lightGO.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = CeilingSpotAngle;
                light.innerSpotAngle = 45f;
                light.range = 11f;
                // The same intensity every other fixture in the building carries - see
                // BuildCeilingLights for how that number was derived and why it is not re-tuned here.
                light.intensity = CeilingLightIntensity;
                light.shadows = LightShadows.None;

                // The stem up to the real ceiling, which is three times further away than this
                // fixture hangs. Without it a lit square floats with nothing holding it.
                float drop = TreeHallHeight - RoomHeight;
                Prim(PrimitiveType.Cylinder, "Stem", fixture.transform,
                     new Vector3(0f, drop / 2f, 0f), new Vector3(0.06f, drop / 2f, 0.06f),
                     propMat, removeCollider: true);
            }

            BuildReflectionProbe(t, "Room2_TreeHall", 0f, centreX,
                sizeOverride: new Vector3(spanX, TreeHallHeight, TreeHallWidth),
                yCenter: TreeHallHeight * 0.5f);

            // --- what the tree opens, and the five axes ---------------------------------------------
            //
            // THE TREE NO LONGER OPENS A DOOR, AND STILL GATES THE ROOM. `Door2_3` used to hang on
            // this condition in the north wall above; that wall is solid now (see the span above) and
            // the way on is the slide, on the FAR ledge. The pit is what keeps it shut: the tree is
            // the only bridge, so felling it is still the price of getting out of this room, and it is
            // enforced by the hole in the floor rather than by a lock.
            //
            // `TreeFelled` is kept and still handed back as a `RoomCondition`, which is what rewinds it
            // at the top of an iteration - and is what a door built here later would ask.
            GameObject felledGO = new GameObject("TreeFelled");
            felledGO.transform.SetParent(t, false);
            TreeFelled felled = felledGO.AddComponent<TreeFelled>();
            felled.trunk = trunk;

            // SCATTERED, AND TWO OF THEM KNOCKED OVER, for the reason the buckets are: five axes in a
            // row is equipment issued to the player, five lying about is a room somebody left in a
            // hurry. All of them on the NEAR ledge, clear of the entrance and well back from the lip -
            // an axe kicked into the pit is a loss this room must not be able to inflict.
            // ALL FLAT, NONE TIPPED. `tipDegrees` rolled an axe onto its edge, which for a shape this
            // thin stood it up and drove it into the floor - two of the five were planted like
            // grave markers. Scatter is carried entirely by YAW now, which is the only axis that can
            // vary without lifting the object off the ground.
            BuildFireAxe(t, "FireAxe_0", new Vector3(2.30f, 0f, -2.40f), yaw: 37f);
            BuildFireAxe(t, "FireAxe_1", new Vector3(-0.40f, 0f, -3.55f), yaw: -108f);
            BuildFireAxe(t, "FireAxe_2", new Vector3(3.35f, 0f, 1.60f), yaw: 74f);
            BuildFireAxe(t, "FireAxe_3", new Vector3(-1.10f, 0f, 3.30f), yaw: 152f);
            BuildFireAxe(t, "FireAxe_4", new Vector3(-3.20f, 0f, -0.60f), yaw: -21f);

            // --- the far ledge, room2-5 ---------------------------------------------------------------
            //
            // ORDER MATTERS FOR EXACTLY ONE REASON: the ride's path is raycast off whatever is under
            // it, and the last few metres of that path are the landing room's FLOOR. Build the room,
            // then the slide, then measure the ride - anything else measures a hole.
            (Transform poolRoom, Door poolDoor, PoolDrain poolDrain, Valve[] poolValves) =
                BuildPoolRoom(rootGO.transform, floorMat, grooveMat, panelMat, propMat, fixtureMat);
            (Transform weighRoom, WeighScale weighScale) =
                BuildWeighRoom(rootGO.transform, floorMat, grooveMat, panelMat, propMat, fixtureMat);
            // ROOM2-0, one further south again - the last room of the walk, and the `-0` of the
            // cycle by the naming rule: a cycle always ends in its -0 (docs/cycle-design.md SS4a).
            // It was a bare shell for one day; it is the console room now.
            (Transform breakRoom, FinalRoomSequence breakSequence, CycleExit cycleTwoExit) =
                BuildBreakRoom(rootGO.transform,
                    new Vector3(SlideRoomCentreX, SlideRoomFloorY, SlideRoomCentreZ - 2f * RoomPitch),
                    floorMat, grooveMat, panelMat, propMat, fixtureMat);
            BuildSeesaw(t);
            BuildSlide(t);

            // NO WAY BACK. The slide is one-way and the pool room's own door leads ON rather than
            // back - see BuildPoolRoom. A player who rides down without solving it waits out the
            // clock, which is what every room in this game does to somebody who is not finished.

            return (rootGO.transform, trunk, felled, poolRoom, poolDoor, poolDrain, poolValves,
                    weighRoom, weighScale, breakRoom, breakSequence, cycleTwoExit);
        }

        // ROOM2-5: THE SEESAW, and the only thing on the far ledge that IS scenery. A child's thing
        // at full size in a building that has none of them, next to the slide it was placed beside.
        //
        // PLACED BY HAND AND READ BACK, like the slide - see SeesawLocalPosition. Note that it does
        // NOT stand on the floor: the hand placement left it about half a metre up, and that is
        // reproduced here as it was found rather than quietly corrected. Dropping it is one number.
        private static void BuildSeesaw(Transform hall)
        {
            string path = PlayDir + "/seesaw.glb";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null)
            {
                Debug.LogWarning($"[SceneBuilder] {path} is missing - room2-5 loses its seesaw.");
                return;
            }

            ShrinkModelTextures(path, 1024);

            GameObject root = new GameObject("Room2_5_Seesaw");
            root.transform.SetParent(hall, false);
            root.transform.localPosition = SeesawLocalPosition;

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            model.name = "Seesaw";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(SeesawLocalEuler);
            model.transform.localScale = Vector3.one * SeesawLocalScale;

            // SOLID, for the reason the slide is: a seesaw you walk through is a poster. Mesh colliders
            // on the parts as they are - the plank is held up by nothing at its ends, and convex would
            // fill that in and turn the whole thing into one box.
            int solid = 0;
            foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                solid++;
            }

            Debug.Log($"[SceneBuilder] Seesaw: scale {SeesawLocalScale:0.000} at "
                    + $"x={SeesawLocalPosition.x:0.00}, z={SeesawLocalPosition.z:0.00}, "
                    + $"{solid} solid part(s)");
        }

        // A wall between two points along its own axis, rather than one centred on its parent.
        //
        // `BuildPanelWall` takes a centre and a width, which is every wall in the building until a
        // 30m wall needs TWO doorways in it and `SubtractRect` only takes one hole. Splitting the
        // wall into two spans, each with its own opening, is cheaper than teaching the subtraction to
        // take a list - and the seam between them falls inside a groove either way.
        private static void BuildPanelWallSpan(Transform parent, string name, Vector3 faceAtBase,
                                               Vector3 rightDir, Vector3 inward,
                                               float fromAlong, float toAlong,
                                               Material backingMat, Material panelMat,
                                               Rect cutoutAlongHeight, float wallHeight)
        {
            float width = toAlong - fromAlong;
            float centre = (fromAlong + toAlong) / 2f;
            // The cutout arrives in the coordinates the span itself is given in, so it is shifted
            // into the wall's own centred frame here rather than at every call site.
            Rect local = cutoutAlongHeight.width > 0f
                ? Rect.MinMaxRect(cutoutAlongHeight.xMin - centre, cutoutAlongHeight.yMin,
                                  cutoutAlongHeight.xMax - centre, cutoutAlongHeight.yMax)
                : Rect.zero;

            BuildPanelWall(parent, name, faceAtBase + rightDir * centre, rightDir, inward,
                           width, backingMat, panelMat, local, wallHeight);
        }

        // WHAT "BOTTOMLESS" IS MADE OF. There is no kill plane, no fall damage and no Y bound
        // anywhere in this project, so the pit is a shaft deeper than the player can see into with
        // nothing at the end of it - and a floor at the bottom regardless, because the probes clear
        // to SKYBOX and an open-ended hole would glow.
        //
        // What happens to somebody who falls in is NOT decided here. Today they land on that floor,
        // twenty-six metres down, in the dark, with no way out until the iteration ends.
        private static void BuildTreePitShaft(Transform parent, Rect pitXZ)
        {
            Material shaftMat = MakeColorMaterial("TreePitShaft", new Color(0.048f, 0.049f, 0.055f));
            SetSmoothness(shaftMat, 0.08f);

            GameObject shaft = new GameObject("TreePit");
            shaft.transform.SetParent(parent, false);

            float cx = pitXZ.center.x, cz = pitXZ.center.y;
            float halfX = pitXZ.width / 2f, halfZ = pitXZ.height / 2f;
            float outerZ = pitXZ.height + 2f * WallThickness;

            // THE SHAFT HANGS BELOW THE FLOOR SLAB, NOT ALONGSIDE IT - which is what stops the lip
            // flickering. The walls used to run from y=0 down, so their top 0.1m shared exactly the
            // volume the floor slab occupies, and their outer face sat on exactly the plane of the
            // slab's cut edge: two coplanar faces fighting for the same pixels all the way round the
            // hole. Starting at the slab's underside removes the shared plane rather than biasing it.
            float top = -WallThickness;
            float mid = top - TreePitDepth / 2f;

            Prim(PrimitiveType.Cube, "Shaft_East", shaft.transform,
                new Vector3(cx + halfX + WallThickness / 2f, mid, cz),
                new Vector3(WallThickness, TreePitDepth, outerZ), shaftMat);
            Prim(PrimitiveType.Cube, "Shaft_West", shaft.transform,
                new Vector3(cx - halfX - WallThickness / 2f, mid, cz),
                new Vector3(WallThickness, TreePitDepth, outerZ), shaftMat);
            Prim(PrimitiveType.Cube, "Shaft_North", shaft.transform,
                new Vector3(cx, mid, cz + halfZ + WallThickness / 2f),
                new Vector3(pitXZ.width, TreePitDepth, WallThickness), shaftMat);
            Prim(PrimitiveType.Cube, "Shaft_South", shaft.transform,
                new Vector3(cx, mid, cz - halfZ - WallThickness / 2f),
                new Vector3(pitXZ.width, TreePitDepth, WallThickness), shaftMat);
            Prim(PrimitiveType.Cube, "Shaft_Bottom", shaft.transform,
                new Vector3(cx, top - TreePitDepth - WallThickness / 2f, cz),
                new Vector3(pitXZ.width + 2f * WallThickness, WallThickness, outerZ), shaftMat);

            // FALLING IN IS FATAL. The volume starts 2m below the lip rather than at it, so that
            // clipping the edge on the way past is not a death - you have to be in the shaft. It
            // fills the rest of it, because a thin plate can be fallen through between two fixed
            // steps and there is nothing below this to catch anybody.
            GameObject kill = new GameObject("PitKillVolume");
            kill.transform.SetParent(shaft.transform, false);
            kill.transform.localPosition = new Vector3(cx, -(TreePitDepth + 2f) / 2f, cz);
            BoxCollider killBox = kill.AddComponent<BoxCollider>();
            killBox.isTrigger = true;
            killBox.size = new Vector3(pitXZ.width, TreePitDepth - 2f, pitXZ.height);
            kill.AddComponent<KillVolume>();
        }

        // THE TREE, WHICH IS TWO OBJECTS MADE FROM ONE MODEL, AND A NOTCH THAT IS REAL GEOMETRY.
        //
        // `realistic_tree.glb` is a single 16m piece and this room needs a stump that stays and an
        // upper trunk that goes over, so the meshes are CUT at build time. Cutting by triangle
        // centroid leaves a ragged edge, and a ragged edge is what a chopped trunk has, so the cheap
        // way is also the right-looking one.
        //
        // THE NOTCH IS CUT, NOT DRAWN, and that is the second version of it. The first was a dark
        // wedge laid ON the trunk, which reads as something stuck to the tree rather than as wood
        // taken out of it - play called it "a vertical groove". Wood cannot be taken out of a mesh at
        // runtime for the price of a keypress, so all `TreeNotchStages` of it are cut HERE and the
        // stage is switched by enabling one of them. A dark core cylinder sits just inside the bark,
        // invisible until a notch opens onto it, and is what the player sees in the cut.
        //
        // SCALED TO FIT rather than set to a number: the crown is 11.72m across at native size and
        // the hall is 10.5m, so a native tree grows through both side walls. The scale is derived
        // from the measurement so that changing the hall's width cannot silently leave the tree
        // poking through it.
        private static TreeTrunk BuildTree(Transform parent, Material propMat)
        {
            string assetPath = $"{NatureDir}/realistic_tree.glb";
            // BEFORE ANYTHING IS INSTANTIATED. These two models between them carry 645MB of
            // uncompressed texture, which is the stutter this room was reported to have - see
            // ShrinkModelTextures. 1024 is well past what a tree read at three metres needs.
            ShrinkModelTextures(assetPath, 1024);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (source == null)
            {
                Debug.LogError($"[SceneBuilder] {assetPath} is missing - the tree hall has no tree.");
                return null;
            }

            GameObject root = new GameObject("Tree");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(TreeStandX, -TreeSinkDepth, 0f);

            // One throwaway instance so the real ones are PLACED rather than probed. Bounds are taken
            // in the tree's own frame - see ModelBounds for what taking them raw does.
            GameObject probe = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            Bounds raw = ModelBounds(root.transform, probe);
            Object.DestroyImmediate(probe);

            float scale = Mathf.Min(1f, (TreeHallWidth - 2f * TreeWallClearance) / raw.size.z);
            // THE CROWN IS NOT CENTRED ON THE TRUNK - it leans, by 0.33m at this scale - so standing
            // the TRUNK in the middle of the hall spends all the clearance on one side and leaves
            // 20mm on the other. What has to be centred is the thing that nearly touches the walls.
            root.transform.localPosition -= new Vector3(0f, 0f, raw.center.z * scale);
            float lift = -raw.min.y * scale;
            float height = raw.size.y * scale;

            // THREE INSTANCES, and the third is the point. `whole` is never split and never falls:
            // it is the source for the notch stages, which are what the player looks at right up
            // until the tree goes over. `stump` and `faller` are split, and exist only to source the
            // felled pair.
            GameObject whole = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            whole.name = "WholeSource";
            whole.transform.localScale = Vector3.one * scale;
            whole.transform.localPosition = new Vector3(0f, lift, 0f);

            GameObject stump = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            stump.name = "Stump";
            stump.transform.localScale = Vector3.one * scale;
            stump.transform.localPosition = new Vector3(0f, lift, 0f);

            GameObject pivotGO = new GameObject("FallPivot");
            pivotGO.transform.SetParent(root.transform, false);
            pivotGO.transform.localPosition = new Vector3(0f, TreeCutHeight, 0f);

            GameObject faller = (GameObject)PrefabUtility.InstantiatePrefab(source, pivotGO.transform);
            faller.name = "Faller";
            faller.transform.localScale = Vector3.one * scale;
            faller.transform.localPosition = new Vector3(0f, lift - TreeCutHeight, 0f);

            // NEITHER INSTANCE'S OWN ROTATION IS TOUCHED, and that is load-bearing. This model is
            // authored Z-UP and the glTF importer carries the correction inside the prefab, so
            // overwriting the instance's rotation - even with identity - destroys it, and the tree
            // then answers a rotation with a pure translation. The pivot above it is a plain
            // GameObject of ours and turns cleanly. See docs/gotchas.md.
            SplitTreeMeshes(root.transform, stump, TreeCutHeight, keepAbove: false, tag: "stump");
            SplitTreeMeshes(root.transform, faller, TreeCutHeight, keepAbove: true, tag: "faller");

            // `whole` IS A TRUNK SOURCE AND NOTHING ELSE. It is an unsplit copy of the entire tree, so
            // it arrives carrying a second set of branches and a second CROWN - and those sit on the
            // tree's root rather than on the fall pivot, so when the tree went over its canopy stayed
            // hanging in the room. Everything but its trunk is switched off here; the canopy the
            // player sees is `faller`'s, which rides the pivot and goes with it.
            {
                MeshFilter keep = TrunkMeshOf(root.transform, whole, TreeCutHeight);
                int hidden = 0;
                foreach (MeshFilter mf in whole.GetComponentsInChildren<MeshFilter>(true))
                    if (mf != keep) { mf.gameObject.SetActive(false); hidden++; }
                Debug.Log($"[SceneBuilder] Tree: {hidden} duplicate canopy/branch mesh(es) hidden on the whole-trunk source");
            }

            // THE TRUNK'S OWN SIZE AT THE CUT, measured off the split meshes rather than assumed -
            // the notch, the core and the bridge deck are all sized from it.
            float halfWidth = TrunkHalfWidthAt(root.transform, stump, TreeCutHeight);
            float westFace = -halfWidth;
            Debug.Log($"[SceneBuilder] Tree: native {raw.size.y:0.00}m -> scale {scale:0.000} "
                    + $"= {height:0.00}m tall, crown {raw.size.x * scale:0.00}x{raw.size.z * scale:0.00}, "
                    + $"trunk {halfWidth * 2f:0.00}m thick at the {TreeCutHeight:0.00}m cut");

            // --- the notch: TreeNotchStages real cuts, one shown at a time ------------------------
            //
            // A V lying on its side, widest where the axe lands and closing at its apex, deepening
            // stage by stage. Both halves are cut with the same wedge, because the wedge straddles
            // the plane the tree separates on - which is the point: the notch IS where it breaks.
            // --- the notch: TreeNotchStages real cuts, one shown at a time ------------------------
            //
            // A V lying on its side, widest where the axe lands and closing at its apex, deepening
            // stage by stage - the triangular bite an axe actually makes.
            //
            // **THE STANDING TREE IS ONE UNBROKEN MESH.** This is the second version, and the first
            // shipped the tree looking already felled: the stages were built from the SPLIT halves,
            // so even stage zero showed the ragged seam where the centroid test had divided the
            // trunk in two. A tree that is visibly cut before anybody has swung at it gives the whole
            // puzzle away and looks broken.
            //
            // So a stage is now ONE mesh - the whole trunk minus the wedge - and the trunk does not
            // come apart at all until it goes over. The split pair is built too, but it is only
            // switched on at the moment of the fall, which is the moment it should first be visible.
            GameObject stages = new GameObject("NotchStages");
            stages.transform.SetParent(root.transform, false);

            // WHICH MESH THE NOTCH IS IN. Only the trunk is copied: the crown and the branches are
            // metres above the cut and would be `TreeNotchStages` duplicates of the heaviest meshes
            // in the scene - and, in an earlier version, they were switched OFF with the half that
            // owned them, so chopping made the canopy disappear.
            MeshFilter wholeTrunkMesh = TrunkMeshOf(root.transform, whole, TreeCutHeight);
            MeshFilter stumpTrunk = TrunkMeshOf(root.transform, stump, TreeCutHeight);
            MeshFilter fallerTrunk = TrunkMeshOf(root.transform, faller, TreeCutHeight);

            // SEEN FROM BOTH SIDES, which is half of what stands in for exposed heartwood.
            //
            // A cut into a mesh shows its inside, and an inside is back-faces, and back-faces are not
            // drawn - so the notch looked straight through the tree. The other half is the nested
            // fill below; this is what stops a deep cut showing daylight.
            Material barkCut = null;
            if (wholeTrunkMesh != null)
            {
                Material src = wholeTrunkMesh.GetComponent<MeshRenderer>().sharedMaterial;
                barkCut = new Material(src) { name = "TreeBark_Cut" };
                if (barkCut.HasProperty("_Cull")) barkCut.SetFloat("_Cull", 0f);
                if (barkCut.HasProperty("_CullMode")) barkCut.SetFloat("_CullMode", 0f);
                barkCut.doubleSidedGI = true;
                AssetDatabase.CreateAsset(barkCut, $"{MaterialsDir}/TreeBark_Cut.mat");
            }

            float maxDepth = halfWidth * 1.55f;   // past the centre, so the last stage is nearly through

            // WHAT THE CUT LOOKS INTO. A trunk is a SHELL, so a notch opened into it shows the inside
            // of the far wall - hollow, which is what play reported. The fill is the trunk's own mesh,
            // shrunk toward its own centre-line over the notch's height band: the one construction
            // that cannot be the wrong shape here, because this trunk tapers by a third across the
            // band AND leans off the tree's origin, which defeated a cylinder twice.
            //
            // Nested, because the last stage cuts past the centre - one shell would be cut through as
            // well, and the innermost is small enough that the cut never reaches it.
            GameObject fills = new GameObject("TrunkFill");
            fills.transform.SetParent(root.transform, false);
            float fillBand = maxDepth * TreeNotchHalfAngleTan + 0.12f;
            foreach (float shrink in new[] { 0.86f, 0.62f, 0.40f })
            {
                BuildTrunkFill(root.transform, fills.transform, stumpTrunk, $"Fill_Lower_{shrink:0.00}",
                               TreeCutHeight - fillBand, TreeCutHeight, shrink, barkCut);
                BuildTrunkFill(root.transform, pivotGO.transform, fallerTrunk, $"Fill_Upper_{shrink:0.00}",
                               TreeCutHeight, TreeCutHeight + fillBand, shrink, barkCut);
            }

            // The stages themselves: whole trunk, one wedge, no seam.
            var notchStages = new GameObject[TreeNotchStages];
            for (int i = 0; i < TreeNotchStages; i++)
            {
                float depth = maxDepth * (i + 1) / TreeNotchStages;
                notchStages[i] = BuildNotchStage(root.transform, stages.transform, wholeTrunkMesh,
                    $"Notch_{i}", westFace, depth, TreeCutHeight, keepAbove: null, cutMat: barkCut);
            }

            // AND THE PAIR THAT ONLY EXISTS ONCE IT IS DOWN, carved to the deepest stage so the
            // severed faces match the notch that severed them.
            //
            // BOTH HALVES ARE THE TREE'S OWN MESH AGAIN (2026-08-17, by request: "remove the roots,
            // leave only the tree model"). A separate `tree_roots.glb` used to stand at the base as
            // the thing left behind, which meant the room contained two different trees' geometry
            // meeting at the floor. What remains after the cut is now the trunk the player has been
            // chopping, carved by the same wedge that severed it - so the stump and the log are two
            // halves of one object, which is what they are.
            GameObject felledLower = BuildNotchStage(root.transform, stages.transform, stumpTrunk,
                "Felled_Stump", westFace, maxDepth, TreeCutHeight, keepAbove: false, cutMat: barkCut);
            GameObject felledUpper = BuildNotchStage(root.transform, stages.transform, fallerTrunk,
                "Felled_Faller", westFace, maxDepth, TreeCutHeight, keepAbove: true, cutMat: barkCut);
            felledUpper.transform.SetParent(pivotGO.transform, true);

            // Something solid to walk into while it stands. On the STUMP only - the faller is in the
            // air until it is not, and a collider swinging through the room during the fall would
            // shove the player.
            GameObject stumpSolid = new GameObject("StumpBlocker");
            stumpSolid.transform.SetParent(root.transform, false);
            stumpSolid.transform.localPosition = new Vector3(0f, TreeCutHeight / 2f, 0f);
            BoxCollider stumpBox = stumpSolid.AddComponent<BoxCollider>();
            stumpBox.size = new Vector3(halfWidth * 2f, TreeCutHeight, halfWidth * 2f);

            // EVERY PART OF THE FELLED TREE IS SOLID, by request: the trunk, the branches and the
            // crown all take weight once it is down. They were pass-through, so a player crossing
            // walked over an invisible deck with the tree they were supposedly on going through them.
            //
            // Non-convex mesh colliders, built disabled and switched on when the fall lands - never
            // during it, because a mesh collider sweeping through the player is exactly the shove
            // §1.7 keeps ghosts from delivering.
            // WHAT IS SOLID ON THE FELLED TREE, AND WHAT IS NOT - and the split is the whole answer
            // to "crossing it is hard".
            //
            // Everything was solid in the first version, leaves included, and that is what made the
            // crossing a scramble: the canopy is a 10m tangle of round branches, the clear trunk is
            // only 2.4m of a 10.5m span, and falling off is now fatal. Making the leaves solid is also
            // the wrong reading of what foliage IS - you push through leaves, you do not stand on them
            // - and it put a 18,500-vertex mesh collider in the scene for the privilege.
            //
            // So: the TRUNK and the BRANCHES take weight, the LEAVES do not. Crossing is walking the
            // trunk line and pushing through canopy, which is what crossing a fallen tree is.
            var branchColliders = new System.Collections.Generic.List<Collider>();
            int leafMeshes = 0;
            foreach (MeshFilter mf in faller.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Material mat = mf.GetComponent<MeshRenderer>() != null
                    ? mf.GetComponent<MeshRenderer>().sharedMaterial : null;
                // By MATERIAL, not by name: the leaf material is the one the model itself marks as
                // foliage, and a re-export is free to rename the mesh but not to change what it is.
                if (mat != null && mat.name.ToLowerInvariant().Contains("leaves")) { leafMeshes++; continue; }

                MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.enabled = false;
                branchColliders.Add(mc);
            }
            // The severed upper trunk itself, which is the surface most of the crossing happens on.
            if (felledUpper != null)
            {
                MeshFilter mf = felledUpper.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    MeshCollider mc = felledUpper.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.enabled = false;
                    branchColliders.Add(mc);
                }
            }
            Debug.Log($"[SceneBuilder] Tree bridge: {branchColliders.Count} solid part(s), "
                    + $"{leafMeshes} leaf mesh(es) left to push through");

            // THE DECK: one flat walkway the length of the span, and it is not redundant with the
            // colliders above. What has to be reliable is that a player who steps onto a felled tree
            // gets across; bark over a fatal drop is the wrong place to discover a gap between two
            // branches. Its top is level with the log's, so it reads as the log rather than as a
            // plank - and it is WIDE, because the alternative to a wide deck over a fatal pit is
            // dying to a sidestep.
            GameObject bridge = new GameObject("TreeBridge");
            bridge.transform.SetParent(parent, false);
            float minX = TreePitWestEdge - 1.0f, maxX = TreePitEastEdge + 1.0f;
            float deckTop = halfWidth;      // the felled trunk rests with its axis on the floor
            bridge.transform.localPosition = new Vector3((minX + maxX) / 2f, deckTop / 2f, 0f);
            BoxCollider deck = bridge.AddComponent<BoxCollider>();
            deck.size = new Vector3(maxX - minX, deckTop, halfWidth * 5.4f);

            // A RAMP AT EACH LIP, because the deck's top is 0.6m up and the player's step is not.
            // Without them the crossing opens with a jump onto a narrow surface over a fatal drop,
            // which is the least forgiving moment in the room and the least deliberate.
            //
            // A rotated box rather than a slope mesh: the CharacterController walks a collider, and
            // its `slopeLimit` is what decides whether this is a ramp or a wall - about 26 degrees
            // here, which is a walk rather than a climb.
            for (int side = 0; side < 2; side++)
            {
                float lipX = side == 0 ? TreePitEastEdge + 1.0f : TreePitWestEdge - 1.0f;
                float dir = side == 0 ? 1f : -1f;
                const float rampRun = 1.3f;
                GameObject ramp = new GameObject($"TreeBridge_Ramp_{side}");
                ramp.transform.SetParent(bridge.transform, true);
                ramp.transform.localScale = Vector3.one;
                ramp.transform.position = bridge.transform.TransformPoint(
                    new Vector3(lipX - bridge.transform.localPosition.x + dir * rampRun / 2f,
                                deckTop / 2f - deckTop / 2f, 0f));
                ramp.transform.localRotation = Quaternion.Euler(
                    0f, 0f, dir * -Mathf.Atan2(deckTop, rampRun) * Mathf.Rad2Deg);
                BoxCollider rampBox = ramp.AddComponent<BoxCollider>();
                rampBox.size = new Vector3(Mathf.Sqrt(rampRun * rampRun + deckTop * deckTop),
                                           0.12f, halfWidth * 5.4f);
            }
            bridge.SetActive(false);

            // The volume you have to be standing in to swing. Generous on purpose: the player is
            // holding a metre of axe at true size and should not have to hunt for a spot.
            GameObject chop = new GameObject("TreeTrunk");
            chop.transform.SetParent(root.transform, false);
            chop.transform.localPosition = new Vector3(-halfWidth * 0.6f, 1.2f, 0f);
            BoxCollider reach = chop.AddComponent<BoxCollider>();
            reach.isTrigger = true;
            reach.size = new Vector3(halfWidth * 2f + 2.4f, 2.8f, halfWidth * 2f + 2.4f);

            GameObject hint = new GameObject("HintAnchor");
            hint.transform.SetParent(root.transform, false);
            hint.transform.localPosition = new Vector3(westFace - 0.25f, TreeCutHeight + 0.35f, 0f);

            TreeTrunk trunk = chop.AddComponent<TreeTrunk>();
            trunk.axeItemId = CycleTwoToolItemId;
            // THIRTY, down from forty, and it is paying for the swing rather than making the room
            // easier. A blow takes 0.78s to read now instead of 0.52, so forty of them is the same
            // wall of time it always was with fewer of them landing - the count comes down by ten to
            // hold the room where it was. It is still out of one player's reach inside sixty seconds
            // and still comfortable for five pairs of hands, which is the only thing it has to be.
            // TWENTY, down from twenty-five and forty before that (2026-08-19, by request). The
            // number only has to be well past what one pair of hands fits into a minute, which twenty
            // still is; past that, more of them is more waiting rather than more accumulation.
            // `TreeNotchStages` is unchanged at 8, so the notch deepens faster per swing.
            trunk.chopsToFell = 20;
            // WHAT IS SHOWN WHEN, and it is three states rather than two. `wholeTrunk` is the
            // untouched trunk before any chop has landed; `notchStages` are the bitten-but-unbroken
            // trunk while it is being cut; `felledLower`/`felledUpper` are the severed pair, and they
            // are the only ones that show a seam - which they earn, because by then it is severed.
            trunk.wholeTrunk = new[] { wholeTrunkMesh != null ? wholeTrunkMesh.gameObject : null };
            trunk.notchStages = notchStages;
            trunk.felledLower = felledLower;
            trunk.felledUpper = felledUpper;
            // The split halves are only ever sources for the pair above - never shown.
            if (stumpTrunk != null) stumpTrunk.gameObject.SetActive(false);
            if (fallerTrunk != null) fallerTrunk.gameObject.SetActive(false);
            trunk.fallPivot = pivotGO.transform;
            // Over to the WEST, across the pit. Euler Z+90 takes the trunk's own up to -X.
            trunk.fallEuler = new Vector3(0f, 0f, 90f);
            // AND DOWN, by the height of the cut. A tree cut at 1.2m does not stay 1.2m up: the butt
            // slips off the stump as it goes and the log ends up ON the ground. That is also the only
            // way it is crossable - the player's jump clears 0.9m, and a log left at the cut height
            // would have put its walking surface at 1.9m, which is a wall rather than a bridge.
            trunk.fallDrop = TreeCutHeight;
            trunk.bridgeSurface = bridge;
            trunk.branchColliders = branchColliders.ToArray();
            trunk.hintAnchor = hint.transform;
            trunk.audioSource = MakeSource(root.transform, "ChopAudio", spatialBlend: 1f, volume: 0.9f);
            trunk.chopClip = MakeChopClip("sfx_axe_chop");
            trunk.fallClip = LoadClip(SfxDir, "sfx_door_open");

            // DOES IT ACTUALLY REACH? The whole room turns on it, it depends on four numbers that are
            // each free to move, and a tree that lands short is a hall with no way across and no
            // other symptom. Measured off the real geometry, not the nominal height.
            float reachFromCut = height - TreeCutHeight;
            float landsAtX = TreeStandX - reachFromCut;
            if (landsAtX > TreePitWestEdge)
                Debug.LogError($"[SceneBuilder] the felled tree stops at x={landsAtX:0.00}, short of the "
                             + $"far lip at {TreePitWestEdge:0.00} - the pit cannot be crossed.");
            else
                Debug.Log($"[SceneBuilder] Tree bridge: {reachFromCut:0.00}m of tree over a "
                        + $"{TreePitWidth:0.00}m pit, landing {TreePitWestEdge - landsAtX:0.00}m past the far lip.");

            return trunk;
        }

        // The trunk's half-width at a height, taken off the mesh that is actually there. Used for the
        // notch depth, the heartwood radius, the stump's collider and the bridge deck - all of which
        // were magic numbers once and all of which have to move together when the tree is rescaled.
        private static float TrunkHalfWidthAt(Transform frame, GameObject instance, float y,
                                              float band = 0.06f)
        {
            float best = 0f;
            foreach (MeshFilter mf in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                Matrix4x4 toFrame = frame.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (Vector3 v in mf.sharedMesh.vertices)
                {
                    Vector3 p = toFrame.MultiplyPoint3x4(v);
                    if (Mathf.Abs(p.y - y) > band) continue;
                    best = Mathf.Max(best, new Vector2(p.x, p.z).magnitude);
                }
            }
            return best > 0.01f ? best : 0.7f;
        }

        // ONE STAGE OF THE NOTCH: a copy of the trunk with a wedge of wood genuinely gone.
        //
        // Only the mesh the notch passes through is copied - the crown and the branches are metres
        // away and would be `TreeNotchStages` wasted duplicates of the heaviest meshes in the scene.
        private static GameObject BuildNotchStage(Transform frame, Transform parent, MeshFilter chosen,
                                                  string name, float westFace, float depth,
                                                  float cutY, bool? keepAbove, Material cutMat)
        {
            GameObject stage = new GameObject(name);
            stage.transform.SetParent(parent, false);
            if (chosen == null) return stage;

            Matrix4x4 toFrame = frame.worldToLocalMatrix * chosen.transform.localToWorldMatrix;
            Mesh cut = CarveNotch(chosen.sharedMesh, toFrame, westFace, depth, cutY, keepAbove);
            if (cut == null) return stage;

            // The stage sits in the SAME pose as the mesh it copies, so the carved mesh lands exactly
            // on top of the whole one it replaces.
            stage.transform.SetPositionAndRotation(chosen.transform.position, chosen.transform.rotation);
            stage.transform.localScale = chosen.transform.lossyScale;

            stage.AddComponent<MeshFilter>().sharedMesh = SaveGeneratedMesh(
                $"tree_notch_{name}_{depth:0.000}".Replace(' ', '_'), () => cut);
            stage.AddComponent<MeshRenderer>().sharedMaterial =
                cutMat != null ? cutMat : chosen.GetComponent<MeshRenderer>().sharedMaterial;
            stage.SetActive(false);
            return stage;
        }

        // A SHRUNKEN COPY OF THE TRUNK over one band of its height, so a cut into the trunk has
        // something behind it. See the call site for why this shape and not a primitive.
        //
        // Shrunk in X and Z about the band's own centroid and NOT in Y, so it keeps the trunk's
        // taper and lean and simply sits inside the bark.
        private static void BuildTrunkFill(Transform frame, Transform parent, MeshFilter chosen,
                                           string name, float fromY, float toY, float shrink,
                                           Material mat)
        {
            if (chosen == null || chosen.sharedMesh == null) return;

            Matrix4x4 toFrame = frame.worldToLocalMatrix * chosen.transform.localToWorldMatrix;
            Mesh band = FilterTriangles(chosen.sharedMesh, (a, b, c) =>
            {
                float y = toFrame.MultiplyPoint3x4((a + b + c) / 3f).y;
                return y >= fromY && y <= toY;
            });
            if (band == null) return;

            // The centroid is taken in the FRAME the shrink has to happen in, then carried back into
            // the mesh's own space - the model is authored Z-up and its local axes are not the room's.
            Matrix4x4 fromFrameM = toFrame.inverse;
            Vector3[] verts = band.vertices;
            Vector3 centre = Vector3.zero;
            for (int i = 0; i < verts.Length; i++) centre += toFrame.MultiplyPoint3x4(verts[i]);
            centre /= Mathf.Max(1, verts.Length);

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 p = toFrame.MultiplyPoint3x4(verts[i]);
                p.x = centre.x + (p.x - centre.x) * shrink;
                p.z = centre.z + (p.z - centre.z) * shrink;
                verts[i] = fromFrameM.MultiplyPoint3x4(p);
            }
            band.vertices = verts;
            band.RecalculateNormals();
            band.RecalculateBounds();

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(chosen.transform.position, chosen.transform.rotation);
            go.transform.localScale = chosen.transform.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = SaveGeneratedMesh(
                $"tree_fill_{name}_{fromY:0.00}".Replace(' ', '_'), () => band);
            go.AddComponent<MeshRenderer>().sharedMaterial =
                mat != null ? mat : chosen.GetComponent<MeshRenderer>().sharedMaterial;
        }

        // The mesh the cut passes through - the trunk, found by which mesh's middle is nearest the
        // cut height rather than by name, because the name is the exporter's business.
        private static MeshFilter TrunkMeshOf(Transform frame, GameObject instance, float cutY)
        {
            MeshFilter best = null;
            float nearest = float.MaxValue;
            foreach (MeshFilter mf in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                Matrix4x4 m = frame.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                float dy = Mathf.Abs(m.MultiplyPoint3x4(mf.sharedMesh.bounds.center).y - cutY);
                if (dy < nearest) { nearest = dy; best = mf; }
            }
            return best;
        }

        // The trunk minus a horizontal V, and minus everything on the wrong side of the cut plane.
        //
        // The V is widest where the axe lands and closes at its apex `depth` in, which is the shape a
        // real notch has and the reason it reads as deepening rather than as a slot: as the apex
        // travels in, the OPENING gets taller as well as deeper.
        // `keepAbove` NULL means keep both sides - one unbroken trunk with a bite out of it, which
        // is what the tree looks like for every chop but the last. Only the felled pair asks for a
        // side, and only because it has genuinely come apart by then.
        private static Mesh CarveNotch(Mesh src, Matrix4x4 toFrame, float westFace, float depth,
                                       float cutY, bool? keepAbove)
        {
            return FilterTriangles(src, (a, b, c) =>
            {
                Vector3 p = toFrame.MultiplyPoint3x4((a + b + c) / 3f);
                if (keepAbove.HasValue && (p.y >= cutY) != keepAbove.Value) return false;

                float into = p.x - westFace;                       // 0 at the bark, growing inward
                if (into < 0f || into > depth) return true;        // outside the wedge's reach
                float opening = (depth - into) * TreeNotchHalfAngleTan;   // the V, closing at its apex
                return Mathf.Abs(p.y - cutY) > opening;
            });
        }

        // Keeps the triangles a predicate accepts, remapping the vertices it actually uses.
        //
        // THE REMAP IS NOT AN OPTIMISATION. Carrying the whole vertex array over was tried first and
        // is wrong in a way nothing renders: `RecalculateBounds` measures EVERY vertex, referenced or
        // not, so each half came back claiming the whole tree's bounds - never culled, and unable to
        // answer whether the cut had happened at all.
        private static Mesh FilterTriangles(Mesh src, System.Func<Vector3, Vector3, Vector3, bool> keep)
        {
            Vector3[] verts = src.vertices;
            Vector3[] normals = src.normals;
            Vector2[] uvs = src.uv;
            Vector4[] tangents = src.tangents;
            int[] tris = src.triangles;

            bool hasNormals = normals != null && normals.Length == verts.Length;
            bool hasUvs = uvs != null && uvs.Length == verts.Length;
            bool hasTangents = tangents != null && tangents.Length == verts.Length;

            var remap = new System.Collections.Generic.Dictionary<int, int>();
            var newVerts = new System.Collections.Generic.List<Vector3>();
            var newNormals = new System.Collections.Generic.List<Vector3>();
            var newUvs = new System.Collections.Generic.List<Vector2>();
            var newTangents = new System.Collections.Generic.List<Vector4>();
            var kept = new System.Collections.Generic.List<int>(tris.Length);

            for (int i = 0; i < tris.Length; i += 3)
            {
                if (!keep(verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]])) continue;
                for (int c = 0; c < 3; c++)
                {
                    int old = tris[i + c];
                    if (!remap.TryGetValue(old, out int fresh))
                    {
                        fresh = newVerts.Count;
                        remap[old] = fresh;
                        newVerts.Add(verts[old]);
                        if (hasNormals) newNormals.Add(normals[old]);
                        if (hasUvs) newUvs.Add(uvs[old]);
                        if (hasTangents) newTangents.Add(tangents[old]);
                    }
                    kept.Add(fresh);
                }
            }

            if (kept.Count == 0) return null;

            Mesh mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(newVerts);
            if (hasNormals) mesh.SetNormals(newNormals);
            if (hasUvs) mesh.SetUVs(0, newUvs);
            if (hasTangents) mesh.SetTangents(newTangents);
            mesh.SetTriangles(kept, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Replaces every mesh under `instance` with just the half of it on one side of `cutY`,
        // measured in `frame`'s space. A mesh with nothing left is switched off rather than left as
        // an empty renderer.
        private static void SplitTreeMeshes(Transform frame, GameObject instance, float cutY,
                                            bool keepAbove, string tag)
        {
            foreach (MeshFilter mf in instance.GetComponentsInChildren<MeshFilter>())
            {
                Mesh src = mf.sharedMesh;
                if (src == null) continue;
                if (!src.isReadable)
                {
                    Debug.LogError($"[SceneBuilder] {src.name} is not readable - the tree cannot be cut.");
                    continue;
                }

                Matrix4x4 toFrame = frame.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Mesh built = FilterTriangles(src, (a, b, c) =>
                    (toFrame.MultiplyPoint3x4((a + b + c) / 3f).y >= cutY) == keepAbove);

                if (built == null)
                {
                    mf.gameObject.SetActive(false);
                    continue;
                }

                // THE CUT HEIGHT IS IN THE ASSET NAME on purpose. `SaveGeneratedMesh` serves a second
                // call from the asset the first one wrote, so without it a changed TreeCutHeight
                // would silently keep building the old cut.
                mf.sharedMesh = SaveGeneratedMesh(
                    $"tree_{tag}_{src.name}_{cutY:0.000}".Replace(' ', '_'), () => built);
            }
        }

        // ONE OF FIVE, and five is what makes this room's accumulation possible at all - see
        // ItemRegistry on an id naming a SUPPLY. Each axe still obeys the one-object rule and
        // returns to its own origin; what is forbidden is two holders of one axe, never five axes
        // of one id.
        private static CarryableItem BuildFireAxe(Transform parent, string name, Vector3 localPosition,
                                                  float yaw)
        {
            // A real fire axe. The model is already authored at 0.817m, so this is barely a
            // correction - but it is here rather than assumed, because a different axe would need it.
            const float wantedLength = 0.90f;

            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>($"{ToolsDir}/fire_axe.glb");
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            model.name = "Visual";
            model.transform.localPosition = Vector3.zero;

            Bounds raw = ModelBounds(root.transform, model);
            float longest = Mathf.Max(raw.size.x, Mathf.Max(raw.size.y, raw.size.z));
            float modelScale = wantedLength / Mathf.Max(0.0001f, longest);
            model.transform.localScale = Vector3.one * modelScale;

            Vector3 size = raw.size * modelScale;
            model.transform.localPosition = new Vector3(
                -raw.center.x * modelScale, -raw.min.y * modelScale, -raw.center.z * modelScale);

            BoxCollider reach = root.AddComponent<BoxCollider>();
            reach.isTrigger = true;
            reach.center = new Vector3(0f, size.y / 2f, 0f);
            reach.size = size + new Vector3(0.35f, 0.35f, 0.35f);

            GameObject solid = new GameObject("Blocker");
            solid.transform.SetParent(root.transform, false);
            BoxCollider block = solid.AddComponent<BoxCollider>();
            block.center = new Vector3(0f, size.y / 2f, 0f);
            block.size = size;

            CarryableItem item = root.AddComponent<CarryableItem>();
            item.blocker = block;
            item.itemId = CycleTwoToolItemId;
            item.displayName = "FIRE AXE";
            item.icon = AxeIcon();
            item.floorY = 0f;
            item.handLocalPosition = HandPoseFor(wantedLength);
            item.handLocalScale = Vector3.one;
            // Three of the five are knocked over, and without this putting one down restored the
            // pose it was BUILT in - see the buckets, which found this first.
            item.restsUpright = true;
            item.audioSource = MakeSource(root.transform, "PickupAudio", 1f, 0.85f);
            item.pickupClip = LoadClip(SfxDir, "sfx_item_pickup");
            MakeWeighable(item, WeightAxe);

            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return item;
        }

        // AN AXE, AND THE POINT IS THAT IT IS NOT A HAMMER.
        //
        // Two things separate the shapes at icon size, and neither is detail:
        //   - **SYMMETRY.** A hammer's head sits centred on its haft; an axe's hangs off ONE SIDE.
        //   - **THE EDGE IS A CURVE.** A blade is a crescent - convex where it cuts, and swept back
        //     to a point at each end. A straight-ended block is a mallet however it is attached.
        //
        // Built in the AXE'S OWN FRAME - `along` up the haft, `side` across it - so the shape is
        // described once and the 45-degree lie of it is a rotation applied to the samples. The first
        // version stacked rectangles to fake the flare, which is what left it reading as a block.
        private static Sprite AxeIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 mid = new Vector2(0.5f, 0.5f);
            const float turn = 45f * Mathf.Deg2Rad;
            float cos = Mathf.Cos(turn), sin = Mathf.Sin(turn);

            // World point -> axe frame. x is across the haft (the blade hangs at +x), y is up it.
            System.Func<Vector2, Vector2> local = pt =>
            {
                Vector2 d = pt - mid;
                return new Vector2(d.x * cos + d.y * sin, -d.x * sin + d.y * cos);
            };
            // ...and back, for placing things by hand in the axe's own terms.
            System.Func<float, float, Vector2> world = (x, y) =>
                mid + new Vector2(x * cos - y * sin, x * sin + y * cos);

            // THE HAFT, with a swell at the butt - the knob that stops a hand sliding off is most of
            // what says "handle" at this size.
            icon.Capsule(world(0f, -0.375f), world(0f, 0.255f), 0.042f);
            icon.Disc(world(0f, -0.395f), 0.062f);

            // THE HEAD. Everything to one side of the haft:
            //   - the EYE, the block the haft passes through, squared off;
            //   - the BIT, flaring out and ending in a convex edge struck from a centre well behind
            //     it, which is what gives the curve its shallow, weapon-like sweep;
            //   - the BEARD, a bite taken out under the bit where a real axe is cut away.
            icon.Shape(pt =>
            {
                Vector2 l = local(pt);
                if (l.x < -0.055f) return false;

                // The eye: a short block around the haft.
                bool eye = l.x <= 0.075f && l.y >= 0.115f && l.y <= 0.315f;

                // The bit: half-height grows with reach, so it flares.
                float reach = Mathf.InverseLerp(0.055f, 0.315f, l.x);
                float half = Mathf.Lerp(0.105f, 0.185f, reach);
                bool bit = l.x >= 0.055f
                        && Mathf.Abs(l.y - 0.215f) <= half
                        // The cutting edge, convex: struck from a long way back so the arc is shallow.
                        && (l - new Vector2(-0.42f, 0.215f)).sqrMagnitude <= 0.735f * 0.735f;

                return eye || bit;
            });

            // The beard, cut out: the underside of the bit is hollowed where it meets the haft.
            icon.Disc(world(0.105f, 0.010f), 0.115f, -1f);
            return SaveSprite(icon, "icon_axe");
        }
    }
}
