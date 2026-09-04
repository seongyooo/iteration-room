using System.Collections.Generic;
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
    // WHAT HAPPENS TO A SCENE ONCE ITS OBJECTS EXIST: the surface detail every material gets,
    // splitting the cycles into their own scenes, and the two bakes. `MarkReflectionProbeStatic`
    // walks the scene, so a new room is included without being told - but a new MOVER has to be
    // added to `MovesDuringPlay` or it bakes into the reflection in a pose it does not hold.
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {

        // tiling is in repeats across one face. URP/Lit drives the normal map's UVs from _BaseMap's
        // transform, not _BumpMap's, so setting the scale on _BumpMap does nothing at all.
        private static void ApplySurfaceDetail(Material mat, Texture2D normalMap, float bumpScale, Vector2 tiling, float smoothness)
        {
            if (normalMap == null) return;

            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_BumpMap", normalMap);
            mat.SetFloat("_BumpScale", bumpScale);
            mat.SetTextureScale("_BaseMap", tiling);
            // Raised off the old 0.03: at that roughness there is almost no specular response, so
            // the relief only shows in diffuse shading and barely reads. Still well short of the
            // 0.5 default that mirrors the skybox onto the walls.
            SetSmoothness(mat, smoothness);

            // **AND THE SMOOTHNESS VARIES ACROSS IT NOW** - see `MakeSmoothnessMap`. Every surface
            // that gets a normal map gets this, because the two are halves of one idea: the normal
            // map breaks the light up, this breaks up how sharply it comes back.
            //
            // Applied HERE rather than at each call site so a new surface cannot get one without the
            // other - the same reason `AddFallingToEveryCarryable` is a sweep.
            ApplyWear(mat);

            EditorUtility.SetDirty(mat);
        }

        // The smoothness variation, on one material.
        //
        // **THE KEYWORD IS THE WHOLE OF IT.** Assigning `_MetallicGlossMap` and stopping is the exact
        // shape of mistake this project has recorded twice already - transparency needing
        // `_SURFACE_TYPE_TRANSPARENT` as well as the blend modes, and `_BumpScale` only being
        // drivable because `_NORMALMAP` was already compiled in. URP's `SampleMetallicSpecGloss` is
        // wrapped in `#ifdef _METALLICSPECGLOSSMAP`: without it the texture is set, costs memory, and
        // is never read.
        //
        // ONE TEXTURE PER METALLIC VALUE, cached by name, because the map carries metallic in its red
        // channel - two materials that differ only in colour share one, and a metal and a non-metal
        // cannot.
        // **THE ROUND BLOB ON THE WALLS IS A PUNCTUAL LIGHT'S SPECULAR LOBE, AND THIS IS THE ONE
        // DIAL THAT REMOVES IT WITHOUT COSTING ANYTHING ELSE** (2026-09-04, by request).
        //
        // A ceiling fixture is TWO objects: a square emissive panel, and a `Light` standing in for
        // what it emits (`BuildCeilingLights`). A glossy wall therefore shows TWO reflections of the
        // same fixture at once, and play could see both - the panel's REAL reflection, square,
        // arriving through the reflection probe as a mirror image of actual geometry, and the
        // Light's specular highlight, which is ROUND however square the thing throwing it is,
        // because a point source's lobe has no shape of its own. The round one is brighter and sits
        // on top of the square one.
        //
        // **THE SQUARE CANNOT BE MADE BY THE LIGHT, AND DOES NOT NEED TO BE.** URP says so in as
        // many words - `// Rect area light is baked only in URP` - so a rectangle light contributes
        // to the lightmap and never to a real-time highlight; and a cookie shapes the POOL a light
        // throws, not the lobe, because the highlight subtends too small an angle from the source
        // for the mask to vary across it. What is wanted is already on screen: turning the punctual
        // specular off leaves the probe's reflection of the actual panel, which is square because
        // the panel is.
        //
        // **WHAT THIS DOES NOT TURN OFF: the light.** `_SPECULARHIGHLIGHTS_OFF` drops the specular
        // term only - the diffuse half of every direct light is untouched, so the room is lit
        // exactly as brightly as before. Environment reflections are a separate keyword and stay on,
        // which is the whole point: this is the thing `WallSmoothness` 0.65 could not do, since
        // roughness blurs the wall's mirroring of the room and the highlight together.
        private static void NoDirectSpecular(Material mat)
        {
            if (mat == null) return;
            // Both halves, as ever: the float is what the inspector and any material copy read, the
            // keyword is what the shader actually branches on. Setting one without the other is the
            // trap `_SURFACE_TYPE_TRANSPARENT` and `_METALLICSPECGLOSSMAP` are both recorded under.
            if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 0f);
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            EditorUtility.SetDirty(mat);
        }

        private static void ApplyWear(Material mat)
        {
            if (mat == null) return;

            float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;

            // Named off the metallic value so the cache key and the file name are the same fact.
            Texture2D wear = MakeSmoothnessMap($"SurfaceWear_{Mathf.RoundToInt(metallic * 100f)}",
                                               256, metallic, WearFloor);

            if (wear == null) return;

            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetTexture("_MetallicGlossMap", wear);
            // The alpha channel of the metallic map, not of the base map. It is URP's default, and
            // stating it is cheap against a material that arrives with the other one set.
            mat.SetFloat("_SmoothnessTextureChannel", 0f);
            // The wear tiles on its OWN scale, much broader than the grain - `_BaseMap`'s tiling is
            // tuned to make the normal map read as plaster, and wear at that rate is glitter.
            mat.SetTextureScale("_MetallicGlossMap", Vector2.one);
        }

        // A glossy surface is only as good as what it has to reflect, and the only reflection
        // source here was the procedural sky - which is blue, and which the room can't even see.
        // Raising smoothness without this paints a blue cast over every white panel; that is the
        // exact failure the old 0.03-smoothness-everywhere rule existed to dodge.
        //
        // Baked, and baked right here as part of the build. A Realtime probe was tried first, on
        // the reasoning that SceneBuilder rebuilds the scene every run so baked data would always
        // be stale - but a realtime probe renders nothing until play mode, so `probe.texture` came
        // back EMPTY and the glossy walls had nothing at all to reflect. Baking it during Build()
        // solves both: the cubemap is a real asset, and it is regenerated in lockstep with the
        // geometry it captures. It is two small cubemaps, so it costs no meaningful build time.
        //
        // Box projection matters more than usual for a room this shape - it reprojects the cubemap
        // onto the box bounds, so a reflected wall stays put on the wall instead of sliding around
        // as the camera moves.
        // xCenter and the swap flag are both for Room2's side rooms: they sit off the chain's axis, and
        // their long dimension runs along X where every other room's runs along Z - so the probe's box
        // has to be turned with the room or box projection reflects the wrong walls.
        // `sizeOverride` is for the one room that is not a standard shell - the tree hall is 21m of
        // run and 17.5m of headroom, and a probe boxed to a normal room would box-project the
        // reflection off walls that are nowhere near where it thinks they are. Left default, every
        // other room behaves exactly as before.
        private static void BuildReflectionProbe(Transform parent, string roomName, float zCenter,
                                                float xCenter = 0f, bool longAxisIsX = false,
                                                Vector3 sizeOverride = default, float yCenter = -1f)
        {
            GameObject go = new GameObject(roomName + "_ReflectionProbe");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(
                xCenter, yCenter >= 0f ? yCenter : RoomHeight * 0.5f, zCenter);

            ReflectionProbe probe = go.AddComponent<ReflectionProbe>();
            // CUSTOM, NOT BAKED, and this one word is the whole bug. A Baked probe does not carry
            // its cubemap: the reference lives in the SCENE'S LIGHTING DATA ASSET, which this build
            // never generates, so `bakedTexture` was set in memory by the bake and came back NULL
            // the moment the scene was saved and reopened. A Custom probe holds the cubemap in a
            // field on the component, which serialises with everything else. Verified by reopening
            // the saved scene and reading it back - see BakeReflectionProbes.
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
            probe.boxProjection = true;
            Vector3 roomSize = sizeOverride != default
                ? sizeOverride
                : (longAxisIsX
                    ? new Vector3(RoomDepth, RoomHeight, RoomWidth)
                    : new Vector3(RoomWidth, RoomHeight, RoomDepth));
            // **PADDED PAST THE WALLS THIS BOX IS SUPPOSED TO ENCLOSE, ON PURPOSE.** Sized to exactly
            // RoomWidth/RoomHeight/RoomDepth, this box's faces land EXACTLY on the floor slab's top,
            // the ceiling, and the inner face of every wall - the same "nothing may be exactly the
            // size of the hole it sits in" trap as a coplanar panel, just in a bounding box instead of
            // a mesh. A wall PANEL sits with its face on that boundary and its thickness (WallDepth)
            // behind it, and the FLOOR SLAB sits with its top surface on y=0 and its thickness below
            // it - so both renderers' bounds straddle the box face rather than sitting inside it, and
            // Unity's automatic probe assignment reads that as partly or wholly OUTSIDE this probe,
            // falling back to the scene's default reflection (the blue procedural skybox) instead.
            // Confirmed by substituting a loud magenta cubemap for the fallback and rebuilding: the
            // floor and the two side walls picked it up (fully and partially), the far wall did not -
            // exactly the renderers sitting on this box's un-padded faces.
            probe.size = roomSize + Vector3.one * ProbeBoxMargin;

            // **NO BLEND DISTANCE, AND THIS ONE HAS BEEN OUT AND BACK.**
            //
            // `blendDistance` reaches a metre PAST a probe's box so two probes can crossfade where
            // they overlap. In a house that is what you want. Here the rooms are a corridor of sealed
            // cells one thin divider apart, and a metre crosses it easily: measured, 31 of room2-1's
            // wall panels sit inside the LIT room next door's reach.
            //
            // It was set to 0 on that measurement plus a theory - that this was why a dark room
            // showed bright patches - and the theory was wrong. The patches were the probes being
            // baked at full ambient against a room played at a fifth of it
            // (`ProbeLightSwap.darkIntensity`). So it was reverted, on the reasoning that a change
            // with a cost and no demonstrated benefit should not stay.
            //
            // **The revert is what demonstrated the benefit.** Play, immediately: "the square light
            // the walls were reflecting has gone". Blending mixes this room's cubemap with the next
            // room's, and an average of two rooms has no sharp ceiling panel in it - which is the
            // whole thing `NoDirectSpecular` was added to let the walls show. Back to 0, now with a
            // reason that was observed rather than argued.
            //
            // What it still costs is what it always cost: reflections change abruptly at a doorway
            // rather than crossfading. That is the right trade here - the rooms genuinely have
            // different light, and a smeared square is worse than a hard cut nobody is looking at.
            probe.blendDistance = 0f;

            // 512, not 256: at the wall smoothness used here the reflection is sharp enough that a
            // 256 cubemap shows the ceiling fixtures as vague smears rather than panels.
            probe.resolution = 512;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.nearClipPlane = 0.05f;
            probe.farClipPlane = 40f;

            // NOT BAKED HERE. It used to be, and the reference did not survive to the saved scene -
            // see BakeReflectionProbes, called at the end of Build(), for what went wrong and why
            // last is the right place for it.
        }

        // Bake every probe in the scene, whatever built it. Walking the scene rather than taking a
        // list means a room added later cannot be forgotten - which is exactly how this would break
        // again, and quietly, since an unbaked probe looks like a slightly flat room rather than
        // like an error.
        //
        // `-nographics` cannot render, and a bake is a render: the whole pass is skipped there. That
        // leaves the previous .exr files in place, which is the same bargain CaptureMenuBackground
        // makes - stale reflection data is a far better outcome than a failed build.
        // MOVES EACH CYCLE OUT INTO ITS OWN SCENE, and then proves nothing was left pointing across the
        // gap. See CycleSceneLoader for why the cycles are split at all (build time, not frame time).
        //
        // The `Cycle` component's own GameObject becomes the PARENT of that cycle's world and its wall
        // panels, so one move takes everything and the scene has exactly one root. That is also what
        // `CycleSceneLoader` looks for on the way back in - a scene whose root carries a `Cycle`.
        //
        // WHAT THIS DELIBERATELY DOES NOT DO is repair the references it is about to break. Unity drops
        // a serialized reference across a scene boundary silently, and the answer to that is not to
        // hunt them here - it is `CycleBinding`, which re-establishes every one of them at runtime and
        // was written and proved BEFORE this method existed. All this does is check the work: anything
        // still crossing after the move is something CycleBinding does not yet know about, and the
        // build says so rather than shipping a null nobody will meet until they play that room.
        private static void SplitCyclesIntoScenes(Cycle[] cycles, WallPanelDisplay[] displays)
        {

            for (int i = 0; i < cycles.Length; i++)
            {
                Cycle cycle = cycles[i];
                if (cycle == null) continue;

                if (cycle.worldRoot != null) cycle.worldRoot.SetParent(cycle.transform, true);
                if (i < displays.Length && displays[i] != null)
                    displays[i].transform.SetParent(cycle.transform, true);

                string name = CycleSceneNames[i];
                Scene cycleScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                EditorSceneManager.MoveGameObjectToScene(cycle.gameObject, cycleScene);

                // **THE NEW SCENE HAS UNITY'S LIGHTING, NOT THIS GAME'S, AND NOTHING INHERITS IT.**
                // `RenderSettings` is per scene and these are born empty, so without this a cycle
                // runs on skybox ambient nobody chose - see `ApplyEnvironment` for what that cost.
                // The write goes to the ACTIVE scene, so it has to be made active first and put back
                // after, or the settings land on whichever scene happened to be active.
                Scene wasActive = SceneManager.GetActiveScene();
                SceneManager.SetActiveScene(cycleScene);
                ApplyEnvironment();
                EnsureProbeVolume(cycleScene);

                // **AND THE RECORD PAGE'S THUMBNAIL, HERE, WHILE THIS SCENE IS THE ACTIVE ONE.**
                //
                // It was taken before the split, out of the core scene, and the shots came back with
                // colours the game does not have - cycle 2's floor green and its grooves orange.
                // `RenderSettings` is PER SCENE and rendering uses the ACTIVE one, so a shot taken
                // out there was lit by the core scene's environment while the game lights that room
                // with the settings `ApplyEnvironment` has just written one line above.
                //
                // This is the only moment both facts hold at once: the cycle is still loaded, and it
                // already has its own lighting. A line later it is a file.
                CaptureCyclePreview(cycle, i);

                SceneManager.SetActiveScene(wasActive);
                EditorSceneManager.SaveScene(cycleScene, CycleScenePath(name));
                Debug.Log($"[SceneBuilder] Cycle scene '{name}' written to {CycleScenePath(name)}");
            }

            CrossSceneReferenceCheck.WriteReport();
        }

        // EVERYTHING CYCLE 2 IS, behind one call - the shell, its own wall display, the console at the
        // end of the ring and the `Cycle` component that names all of it.
        //
        // Extracted from `Build` so there is a way to build this cycle WITHOUT building the rest of the
        // game (see RebuildCycleTwo). That only became possible once the cycles were split into scenes
        // of their own: `shaker`, `hand` and `narration` all live in the core scene, and until
        // `CycleBinding` started re-establishing them at runtime they had to be present at build time.
        // They are still passed when the full build calls this, because passing the real thing costs
        // nothing - but NULL is now a legal answer, and that is the whole of why a cycle can be rebuilt
        // on its own.
        private static (Cycle cycle, WallPanelDisplay display) AssembleCycleTwo(
            Transform cycleTwoRoot, Transform bedSpawn, Door[] doors, RoomCondition[] conditions,
            GhostInteractable[] signals, ParticleSystem[] gasEmitters, Transform[] rooms,
            Transform[] loweredRooms, FinalRoomSequence finalRoom,
            Material floorMat, Material propMat, Texture2D testCard, Texture2D staticNoise,
            CameraShaker shaker, PlayerHand hand, NarrationDirector narration)
        {
            // ONE DISPLAY PER CYCLE. The ERROR spreading from a console means *this bed's cycle is
            // over*, so a panel in a cycle the player has not reached has no business failing - and
            // the gather is by name, which would otherwise sweep up every storey at once.
            var panels = new System.Collections.Generic.List<Renderer>();
            foreach (Renderer r in cycleTwoRoot.GetComponentsInChildren<Renderer>())
            {
                if (r.transform.parent == null || !r.transform.parent.name.EndsWith("_Panels")) continue;
                panels.Add(r);
            }
            WallPanelDisplay display = MakeWallPanelDisplay(
                "WallPanelDisplay_Cycle2", panels.ToArray(), testCard, staticNoise);

            GameObject cycleTwoGO = new GameObject("Cycle2");
            Cycle cycleTwo = cycleTwoGO.AddComponent<Cycle>();
            cycleTwo.bedSpawnPoint = bedSpawn;
            cycleTwo.doors = doors;
            cycleTwo.drawers = cycleTwoRoot.GetComponentsInChildren<Drawer>(true);
            // Shut at the top of every iteration with the drawers. A tap left running is world state
            // the loop would otherwise forget to rewind - and the most visible kind there is.
            cycleTwo.taps = cycleTwoRoot.GetComponentsInChildren<WaterTap>(true);
            cycleTwo.buckets = cycleTwoRoot.GetComponentsInChildren<Bucket>(true);
            cycleTwo.bucketStands = cycleTwoRoot.GetComponentsInChildren<BucketStand>(true);
            // THE FOUR WALL EMITTERS, BY NAME RATHER THAN BY TYPE. `CycleBinding.PointGasAt` used to
            // gather every ParticleSystem under the cycle instead, which swept the tap sprays in with
            // them - see Cycle.gasEmitters. The builder knows exactly which four these are, so it says
            // so here and nothing has to guess at runtime.
            cycleTwo.gasEmitters = gasEmitters;
            // Its own array, numbered from ZERO - where cycle 1's pad is also bit 0. Legal because
            // every ghost is destroyed at the boundary, so no surviving timeline refers to cycle 1's
            // bits, and the recorder is repointed at this array when the cycle starts.
            CheckGhostSignals("Cycle 2", signals);
            cycleTwo.ghostInteractables = signals;
            cycleTwo.conditions = conditions;

            // AND CYCLE 2 CAN BE FINISHED AGAIN. It shipped with `finalRoom` null for a day - a
            // supported state that `Cycle.Complete`, `LoopManager` and `CycleBinding` all guard, and
            // an honest way to say "this cycle has no end yet". room2-0 is that end: four pedestals,
            // four billiard balls, and the same break cycle 1 has.
            //
            // The three things it needed are all here now, which is what the old comment listed as
            // outstanding: a room0, a console in it, and carryables wearing the ids its recesses
            // declare. The ids are the nine billiard balls rather than the ShardA/B/C the old console
            // asked for and nothing ever wore.
            cycleTwo.finalRoom = finalRoom;
            if (finalRoom != null)
            {
                // THIS CYCLE'S PANELS, not the building's. The ERROR spreading out from a console
                // means *this bed's cycle is over*, and the display gathered above is the one built
                // from this root's own panel groups.
                finalRoom.wallPanels = display;
                // Both may be null on the cycle-2-only rebuild path, where there is no player and no
                // PA to point at - `CycleBinding` re-establishes them at runtime, which is the whole
                // reason that path is allowed to pass null for them at all.
                finalRoom.cameraShaker = shaker;
                finalRoom.narration = narration;
                if (finalRoom.slots != null)
                    foreach (FinalSlot slot in finalRoom.slots)
                        if (slot != null) slot.hand = hand;

                CheckCycleFinishable("Cycle 2", cycleTwoRoot, finalRoom);
            }

            cycleTwo.worldRoot = cycleTwoRoot;
            cycleTwo.wallPanels = display;

            // Everything down there stands on a floor one storey below zero, and every carryable has to
            // be told so or it falls through it.
            SetFloorBase(cycleTwoRoot, -StoreyDrop);

            // ...EXCEPT THE THREE THE SLIDE LANDS IN, which are a further `SlideRoomFloorY` down again.
            // A second pass rather than a smarter first one: the sweep above is a statement about the
            // CYCLE and this is a statement about three rooms inside it, and running them in this
            // order is what lets the second overwrite the first for exactly the objects it names.
            //
            // Left unsaid, `floorBaseY` is 3.7m too high for anything living down there - which is a
            // value that is simply untrue in the scene, whether or not anything currently reads it.
            // What used to read it was every drop (see `CarryableItem.DropAt`), and the symptom was a
            // duck put down on the water hopping 3.7m into the air first.
            if (loweredRooms != null)
                foreach (Transform room in loweredRooms)
                    SetFloorBase(room, -StoreyDrop + SlideRoomFloorY);

            return (cycleTwo, display);
        }

        // CYCLE 3, GATHERED ONTO ONE OBJECT. The same shape as `AssembleCycleTwo` and much less of it,
        // because the cycle is one sealed room: no doors to shut, no conditions to rewind, no final
        // room to finish. All three are supported states rather than gaps - see `Cycle.Complete`,
        // which is false forever without a `finalRoom`, and `LoopManager`, which simply keeps
        // iterating.
        // Whether a transform sits anywhere under an object with this name. Used to hold one room's
        // panels out of its cycle's display - by ancestry rather than by position, because a room is
        // a subtree and its panels are three levels down inside it.
        // Whether a wall panel sits behind one of room3-0's way-down arrows. Measured against the
        // sign's own position rather than listed by name: the signs are built before this runs and
        // moving one should take its exclusion with it.
        private static bool BehindAWayDownSign(Renderer panel)
        {
            if (wayDownHint == null) return false;
            foreach (CanvasGroup sign in wayDownHint)
            {
                if (sign == null) continue;
                // The sign is 1.5m across; a cell is 1.75. One cell's reach either way covers the
                // panel it is printed on and its neighbours, which is what the eye reads as "behind
                // the arrow".
                if ((panel.bounds.center - sign.transform.position).sqrMagnitude
                    < WayDownSignReach * WayDownSignReach) return true;
            }
            return false;
        }

        private const float WayDownSignReach = 1.9f;

        private static bool IsUnder(Transform t, string ancestor)
        {
            for (Transform at = t; at != null; at = at.parent)
                if (at.name == ancestor) return true;
            return false;
        }

        private static (Cycle cycle, WallPanelDisplay display) AssembleCycleThree(
            Transform root, Transform bedSpawn, ParticleSystem[] gasEmitters,
            GhostInteractable[] signals, Texture2D testCard, Texture2D staticNoise,
            CrushingBarrier northBarrier, BeamLift[] lifts)
        {
            // ONE DISPLAY PER CYCLE, gathered by parent name exactly as cycle 2's is: the ERROR
            // spreading from a console means *this bed's cycle is over*, so a panel in a cycle the
            // player has not reached has no business failing.
            // **AND ROOM3-2N IS LEFT OUT OF IT** (2026-09-01, by request). Its four walls carry the
            // evaluation at the end of the game (`EvaluationBoard`), and the ERROR wave was washing
            // red static across the same surfaces the report is printed on - two things shouting on
            // one wall is neither of them.
            //
            // What that costs: room3-2N's panels no longer glitch when the cycle breaks. The player
            // is in room3-0 for that, one storey up, where the wave still plays across every wall -
            // so the beat is not lost, it is confined to the room the player is standing in.
            var panels = new System.Collections.Generic.List<Renderer>();
            int excluded = 0;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                if (r.transform.parent == null || !r.transform.parent.name.EndsWith("_Panels")) continue;
                if (IsUnder(r.transform, "Room3_2N")) { excluded++; continue; }
                // **AND THE PANELS BEHIND ROOM3-0'S ARROWS** (2026-09-01, by request: only the red
                // arrow on those, no ERROR under it). The arrow is a canvas ON the wall and the wave
                // is the wall itself, so without this the one thing in the room that says where to go
                // is printed over red static.
                if (BehindAWayDownSign(r)) { excluded++; continue; }
                panels.Add(r);
            }
            WallPanelDisplay display = MakeWallPanelDisplay(
                "WallPanelDisplay_Cycle3", panels.ToArray(), testCard, staticNoise);
            Debug.Log($"[SceneBuilder] Cycle 3 panel display: {panels.Count} panel(s), {excluded} in "
                    + "room3-2N held out so the evaluation has its walls to itself.");

            GameObject go = new GameObject("Cycle3");
            Cycle cycle = go.AddComponent<Cycle>();
            cycle.bedSpawnPoint = bedSpawn;
            // FOUR GATES, EIGHT LEAVES, TWO `Door`s EACH SIDE OF EACH WALL - gathered rather than
            // listed, because a gate is built as a pair of calls and naming sixteen of them here is
            // sixteen chances to miss one. They all have to be shut by the loop like any other door:
            // a leaf left open is a wall with a hole in it at the top of the next iteration.
            cycle.doors = root.GetComponentsInChildren<Door>(true);
            cycle.barriers = northBarrier != null ? new[] { northBarrier } : new CrushingBarrier[0];
            cycle.lifts = lifts ?? new BeamLift[0];
            cycle.drawers = root.GetComponentsInChildren<Drawer>(true);
            cycle.gasEmitters = gasEmitters;
            CheckGhostSignals("Cycle 3", signals);
            cycle.ghostInteractables = signals;
            // GATHERED BY TYPE, not listed. A room rule joins this array by existing, which is the
            // same reason `doors` and `drawers` above are gathered - and it is what `ResetRooms`
            // walks at the top of every iteration, so a rule missing from here is a puzzle that
            // stays solved through the rewind. Room3-2N's cube is the first entry cycle 3 has.
            cycle.conditions = root.GetComponentsInChildren<RoomCondition>(true);
            // **AND CYCLE 3 CAN BE FINISHED, since 2026-08-30.** room3-0 is off deck B, two storeys
            // up inside room3-2N, and it takes the one object the Bedlam cube pays out. Found rather
            // than threaded down through the shell's return, for the same reason the cube is: there
            // is exactly one, and another member on that tuple is another thing to keep in step.
            cycle.finalRoom = root.GetComponentInChildren<FinalRoomSequence>(true);
            cycle.worldRoot = root;
            cycle.wallPanels = display;

            // WHAT FINISHING ROOM3-2N'S CUBE COSTS. Built here rather than in the shell because it
            // needs the cycle's own `WallPanelDisplay`, which is assembled a few lines above out of
            // every panel in every room - and turning all of them red at once is half of what this
            // sequence is. The cube is found rather than threaded down through the shell's return:
            // there is exactly one, and a seventh member on that tuple would be a seventh thing to
            // keep in step for no gain.
            FacilityFailure failure = BuildFacilityFailure(
                root, root.GetComponentInChildren<BedlamCube>(true), display);
            // AND THE BREAK PLAYS IT INSTEAD OF ITS OWN DRESSING. Cycle 3 does not say "containment
            // failure" and glitch its panels; it comes apart and says something else. See
            // `FinalRoomSequence.facilityFailure`.
            if (cycle.finalRoom != null) cycle.finalRoom.facilityFailure = failure;

            // Everything down here stands on a floor two storeys below zero, and every carryable has
            // to be told so or it falls through it. Nothing is carryable in this room yet except the
            // chest's cube, which is exactly the kind of thing that would fall through it.
            SetFloorBase(root, CycleThreeFloorY);

            return (cycle, display);
        }

        private static void SleepCycle(Transform root)
        {
            if (root == null) return;
            root.gameObject.SetActive(false);
            Debug.Log($"[SceneBuilder] {root.name} starts asleep; LoopManager wakes it at the boundary.");
        }

        // **THE WALLS ARE MOSTLY WHAT THEY REFLECT, AND NOTHING SAID WHEN THEY STOPPED.**
        //
        // `PanelWhite` is authored once with its gloss and then referred to from a dozen places, so
        // any one of them calling `MakeColorMaterial` instead of `FindColorMaterial` republishes it
        // matte - and a matte white wall is not an error, an exception or a missing asset. It is a
        // room that looks very slightly flat, in every room at once, discoverable only by eye. It
        // shipped that way for a day and was found by play, not by anything here.
        //
        // Run BEFORE the probes bake, deliberately: the probes capture these walls, so a flattened
        // material would otherwise be baked into twenty cubemaps as well.
        private static void CheckWallSmoothness()
        {
            Material panel = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsDir}/PanelWhite.mat");
            if (panel == null || !panel.HasProperty("_Smoothness")) return;

            float smoothness = panel.GetFloat("_Smoothness");
            if (Mathf.Approximately(smoothness, WallSmoothness)) return;

            Debug.LogError($"[SceneBuilder] PanelWhite came out of the build at {smoothness:0.###} "
                         + $"smoothness, not {WallSmoothness}. Something re-authored it after "
                         + "ApplySurfaceDetail - look for a MakeColorMaterial(\"PanelWhite\", ...) "
                         + "that should be FindColorMaterial. Every wall in the game is flat until "
                         + "it is fixed; see the note on FindColorMaterial.");
        }

        private static void BakeReflectionProbes()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Debug.Log("[SceneBuilder] Reflection probes NOT baked (-nographics); existing cubemaps left in place.");
                return;
            }

            int flagged = MarkReflectionProbeStatic();

            Directory.CreateDirectory(TexturesDir);
            ReflectionProbe[] probes = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(
                FindObjectsInactive.Include);

            int wired = 0;
            foreach (ReflectionProbe probe in probes)
            {
                // The name carries the room, and the room names the cubemap - so a rebuild
                // overwrites the same asset rather than accumulating one per build.
                string roomName = probe.name.Replace("_ReflectionProbe", string.Empty);
                string path = $"{TexturesDir}/{roomName}_Reflection.exr";
                if (!Lightmapping.BakeReflectionProbe(probe, path)) continue;

                // THE ASSIGNMENT IS THE POINT, not the bake. The bake writes the .exr and that part
                // always worked - seven correct cubemaps sat on disk for as long as this was broken.
                // What was missing was a reference that survives the save, and on a Custom probe
                // that is this field. Imported explicitly first: the file has just been written
                // underneath the AssetDatabase, and loading it without the import returns null.
                AssetDatabase.ImportAsset(path);
                Cubemap cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                if (cube == null) continue;
                probe.customBakedTexture = cube;
                wired++;
            }

            // Reported as a count against a count, because the failure this exists to catch is a
            // probe that ends up with no cubemap - which renders as a room that looks very slightly
            // flat, and nothing else. If this ever says anything but n/n, the metal will go black.
            Debug.Log($"[SceneBuilder] Reflection probes baked and wired: {wired}/{probes.Length} " +
                      $"({flagged} renderers reflection-probe-static)");

            BakeSwitchRoomLitProbe(probes);
        }

        // **THE ONE ROOM THAT NEEDS TWO OF THESE.** Room2-1 spends the entire build dark - see
        // `BuildCycleTwoShell`, which douses its fixtures and swaps their glowing faces the moment it
        // has finished building them - so the pass above can only photograph an unlit room. That is
        // the right reflection while the room is dark and the wrong one the moment the player
        // finishes its puzzle, which is what play reported: the lights come on and the walls still
        // have no ceiling in them.
        //
        // So the room is turned back on, photographed a second time, and put back. Both cubemaps go
        // to `ProbeLightSwap`, which picks between them at runtime on the room's own lit condition.
        //
        // **THIS RUNS AFTER THE MAIN PASS ON PURPOSE**: it needs `customBakedTexture` to already
        // hold the dark bake, which is what it hands over as the dark half of the pair.
        private static void BakeSwitchRoomLitProbe(ReflectionProbe[] probes)
        {
            if (Room2OneLights == null || Room2OnePanels == null
                || Room2OneFixtureLit == null || Room2OneFixtureOff == null) return;

            ReflectionProbe probe = null;
            foreach (ReflectionProbe p in probes)
                if (p != null && p.name == "Room2_1_ReflectionProbe") probe = p;

            if (probe == null)
            {
                Debug.LogWarning("[SceneBuilder] Room2_1 has no reflection probe to bake a lit "
                               + "variant for; its walls will reflect an unlit room once its lights "
                               + "are switched on.");
                return;
            }

            Cubemap dark = probe.customBakedTexture as Cubemap;

            // Lit exactly the way the switches will light it: the `Light` components back on, and the
            // panels handed back to the material every other room's fixtures already wear.
            foreach (Light l in Room2OneLights) if (l != null) l.enabled = true;
            foreach (Renderer r in Room2OnePanels) if (r != null) r.sharedMaterial = Room2OneFixtureLit;

            Cubemap lit = null;
            string path = $"{TexturesDir}/Room2_1_Reflection_Lit.exr";
            try
            {
                if (Lightmapping.BakeReflectionProbe(probe, path))
                {
                    AssetDatabase.ImportAsset(path);
                    lit = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                }
            }
            finally
            {
                // Back to dark, and back to the cubemap the room ships in - the runtime component
                // takes it from here.
                foreach (Light l in Room2OneLights) if (l != null) l.enabled = false;
                foreach (Renderer r in Room2OnePanels) if (r != null) r.sharedMaterial = Room2OneFixtureOff;
                if (dark != null) probe.customBakedTexture = dark;
            }

            ProbeLightSwap swap = probe.gameObject.GetComponent<ProbeLightSwap>()
                                  ?? probe.gameObject.AddComponent<ProbeLightSwap>();
            swap.probe = probe;
            swap.dark = dark;
            swap.lit = lit;
            swap.litWhen = Room2OneLitWhen;
            // The same fraction the ambient is scaled by, so the reflection stays in step with the
            // room it is a reflection OF - see `Room2OneDarkFraction`.
            swap.darkIntensity = Room2OneDarkFraction;

            // Said as a comparison, because the whole point is that the two differ: a lit bake with
            // no pixels over 1.0 would mean the fixtures did not come back on for the shot, and the
            // swap would be switching between two identical dark rooms.
            Debug.Log($"[SceneBuilder] Room2_1 lit probe: {(lit == null ? "FAILED" : "baked")}, "
                    + $"dark {(dark == null ? "MISSING" : "held")}, condition "
                    + $"{(Room2OneLitWhen == null ? "MISSING" : "wired")}.");
        }

        // WHAT A BAKED PROBE CAPTURES IS STATIC GEOMETRY, AND NOTHING IN THIS SCENE WAS STATIC.
        //
        // This is the bottom of the reflection-probe bug and the last of three separate faults on
        // one feature. The other two - baking before SetupLighting invalidated it, and a Baked probe
        // keeping its cubemap in lighting data this build never generates - were both real, and
        // fixing them still left the objects mirroring a brown default skybox. The reason is that
        // `Lightmapping.BakeReflectionProbe` renders only renderers flagged ReflectionProbeStatic,
        // SceneBuilder creates every object from script and never flagged one, so all seven probes
        // were faithfully capturing an empty world. Proved by assigning the cubemap as the GLOBAL
        // reflection and getting the same brown: the probe was applying, its contents were sky.
        //
        // ONLY the ReflectionProbeStatic flag is set. ContributeGI would put this scene into
        // lightmapping it does not use, and BatchingStatic would change how it draws - neither is
        // being asked for, and a static flag set for the wrong reason is very hard to notice later.
        //
        // MOVERS ARE EXCLUDED, by the components that move them rather than by name. A door baked
        // open, or a plinth baked at the height SceneBuilder authors it at rather than the sunk
        // position it starts the game in, would put a thing in the reflection that is not in the
        // room. Everything else - shell, panels, fixtures, furniture, board - is genuinely fixed.
        // **THE OCCLUSION BAKE, AND IT BAKES EVERY SCENE AT ONCE BECAUSE THE GAME LOADS THEM THAT
        // WAY.**
        //
        // The first version opened each cycle on its own and baked it alone. It produced real data -
        // 113KB, 241KB, 261KB, 28KB - and culled NOTHING, which play confirmed by toggling the
        // camera's own Occlusion Culling checkbox and seeing the draw calls not move.
        //
        // The reason is the scene the camera is in. `CycleSceneLoader` loads a cycle ADDITIVELY, so
        // `IterationRoom` stays the ACTIVE scene for the whole run - and baking the cycles separately
        // left that scene with `m_OcclusionCullingData: {fileID: 0}`. Occlusion data for an additive
        // set has to be baked with the set loaded together, which is also the only way the cells can
        // agree about a camera standing in one scene looking at another.
        //
        // So: the core scene, then all four cycles on top of it, every root awake, one `Compute`.
        // The cycles sit at different depths in the world - cycle 2 a storey under cycle 1, cycle 3
        // under that - so baking them together costs a taller volume rather than an overlapping one.
        //
        // **THE ROOTS HAVE TO BE AWAKE.** Cycles ship asleep and a disabled renderer is invisible to
        // the bake - the same trap `CaptureCyclePreview` documents for the camera and the probe bake
        // documents for lightmaps. Woken for the bake and put back exactly as they were found.
        private static void BakeOcclusionCulling()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogWarning("[SceneBuilder] No graphics device (-nographics): occlusion NOT "
                               + "baked, keeping whatever is on disk. Rebuild with a device.");
                return;
            }

            Scene core = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var scenes = new List<Scene> { core };
            foreach (string name in CycleSceneNames)
            {
                string path = CycleScenePath(name);
                if (File.Exists(path))
                    scenes.Add(EditorSceneManager.OpenScene(path, OpenSceneMode.Additive));
            }

            var woken = new List<GameObject>();
            foreach (Cycle cycle in UnityEngine.Object.FindObjectsByType<Cycle>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cycle.worldRoot == null) continue;
                if (cycle.worldRoot.gameObject.activeSelf) continue;
                cycle.worldRoot.gameObject.SetActive(true);
                woken.Add(cycle.worldRoot.gameObject);
            }

            // **OVER THE PANEL, DELIBERATELY: ONLY THE BACKING SLABS MAY OCCLUDE.**
            //
            // A wall here is two layers - chamfered panels in front, a solid backing slab behind -
            // and the black grid the room is made of is that backing seen through the `GrooveDepth`
            // gaps between panels. Ten millimetres of gap.
            //
            // Dropping this to 1 let the 1.75 x 1.35m panels in as occluders, and `smallestHole`
            // 0.25 then swallowed those 10mm gaps: Umbra read the panelled face as one solid sheet,
            // concluded the backing behind it was never visible, and culled it. Play found it
            // immediately - the black grooves in room1-1 turned into the procedural sky, blue at the
            // top of the wall and warm at the bottom, because the gap was now a hole through the
            // building. A hole big enough to fix that is 0.01, which is not a bake anyone can afford.
            //
            // So the threshold goes back ABOVE the panel. The backing slabs are the whole wall minus
            // its doorway - metres across - and they clear this easily, so the building still
            // occludes itself; what it no longer does is hide its own second layer.
            StaticOcclusionCulling.smallestOccluder = 2f;
            StaticOcclusionCulling.smallestHole = 0.25f;
            StaticOcclusionCulling.backfaceThreshold = 100f;

            System.DateTime started = System.DateTime.Now;
            StaticOcclusionCulling.Compute();
            double seconds = (System.DateTime.Now - started).TotalSeconds;

            foreach (GameObject go in woken) go.SetActive(false);
            foreach (Scene scene in scenes) EditorSceneManager.SaveScene(scene);

            // **MEASURED OFF THE FILES, NOT OFF `StaticOcclusionCulling.umbraDataSize`.** That
            // property reported 0 for every scene on a batchmode build that had in fact just written
            // real data - the number said the bake had failed while the bake had worked. The assets
            // on disk are what ship, so they are what gets reported.
            var sizes = new List<string>();
            foreach (Scene scene in scenes)
            {
                string dir = Path.Combine(Path.GetDirectoryName(scene.path),
                                          Path.GetFileNameWithoutExtension(scene.path));
                string data = Path.Combine(dir, "OcclusionCullingData.asset");
                float kb = File.Exists(data) ? new FileInfo(data).Length / 1024f : 0f;
                sizes.Add($"{scene.name} {kb:0.#}KB");
            }

            Debug.Log($"[SceneBuilder] Occlusion baked for {scenes.Count} scene(s) TOGETHER in "
                + $"{seconds:0.#}s, {woken.Count} root(s) woken: {string.Join(", ", sizes)}. "
                + "IterationRoom at 0KB would mean the ACTIVE scene has no data and none of this "
                + "reaches the camera - which is exactly how the first version failed.");
        }

        private static int MarkReflectionProbeStatic()
        {
            int flagged = 0;
            int skipped = 0;
            int backings = 0;
            int gateLeaves = 0;
            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Include);

            foreach (Renderer r in renderers)
            {
                if (MovesDuringPlay(r.transform)) continue;

                // **CONTRIBUTE GI AS WELL, since 2026-08-25** - and the paragraph above, which used to
                // say ONLY the reflection flag is set, was right at the time and is what changed.
                //
                // Without this there is no bounce light anywhere in the building: a white room lit by
                // four downlights and a constant ambient term, which is most of why it reads as a
                // whitebox. Real white walls throw most of their light back at each other.
                //
                // The flag is inert until something bakes. It marks this renderer as a surface light
                // BOUNCES OFF and is BLOCKED BY - see `BakeLighting`.
                //
                // **BUT NOT ON A MESH WITH NO TRIANGLES, AND THAT IS NOT A NICETY - IT IS WHY CYCLE1
                // COULD NOT BE BAKED.** `chess.glb` ships a mesh named `Material3` with no triangle
                // sub-mesh at all. It bounces nothing and blocks nothing, so leaving it out costs the
                // bake exactly nothing; putting it IN took the Editor down. APV's Virtual Offset pass
                // builds a ray tracing acceleration structure out of every ContributeGI renderer, and
                // `HardwareRayTracingAccelStruct.AddInstance` refuses a mesh with no triangle topology
                // and then registers a zero handle for it anyway - so the second such mesh throws
                // `ArgumentException: An item with the same key has already been added. Key: 0`. That
                // aborts `DefaultVirtualOffset.Initialize` half-built, `Step()` NREs on what it left
                // behind, and the wreckage surfaces much later in `FinalizeBake`, where the real
                // exception is swallowed by a `catch` and the cleanup after it throws out of the bake
                // delegate instead - so `done` is never set and Unity calls the delegate again every
                // tick, forever. 29,757 exceptions and a hung Editor, with the only readable cause
                // 30,000 lines above the noise.
                //
                // **The chess pieces are in Room2West, which is why CYCLE1 ALONE crashed** while
                // IterationRoom and Cycle3 baked in seconds. A scene-shaped symptom with an
                // asset-shaped cause. `docs/gotchas.md`.
                bool bouncesLight = HasTriangles(r);
                if (!bouncesLight) skipped++;

                // **AND IT IS AN OCCLUDER, WHICH IS THE WHOLE OF THE OCCLUSION BAKE'S INPUT.**
                //
                // Measured on 2026-09-04, in cycle 1's first room: facing a blank wall the frame
                // submits 3.6k triangles and 77 draw calls; turning to face the DOOR in that wall
                // submits 988.8k triangles, 2,074 set-pass calls and 2,890 draw calls, and the
                // frame time goes from 8.1ms to 18.9ms. What is on screen at that moment is a
                // closed door. The building is one straight corridor with every doorway on the same
                // centre line, so looking down its axis puts all six rooms inside the frustum - and
                // frustum culling was the only culling this project had.
                //
                // A mover reaches this line never: the loop above skips anything `MovesDuringPlay`
                // returns true for, which is the same predicate the reflection bake uses and for
                // the same reason. That deliberately leaves the DOOR LEAVES out of the occluder set,
                // so a doorway is treated as permanently open. Conservative in the safe direction:
                // it culls less than it could, never more than it should.
                //
                // A mesh with no triangles gets neither flag. It occludes nothing and hides behind
                // nothing, and this is the same guard that keeps it out of the GI bake.
                // **THE DARK LAYER BEHIND THE PANELS IS AN OCCLUDER BUT NEVER AN OCCLUDEE, and that
                // exception is the whole reason the first attempt at this had to be reverted.**
                //
                // A wall here is two layers: chamfered panels in front, a solid backing slab 0.06m
                // behind them, and `BuildPanelWall` gives that backing the GROOVE material - so the
                // black grid this whole building is made of is not a separate object, it IS the
                // backing seen through the 10mm gaps between panels.
                //
                // Umbra voxelises at metre scale. At that granularity the panel and the backing are
                // inside the same voxel, so the backing reads as buried in solid geometry and is
                // culled as never visible. Play found it at once: room1-1's black grid turned into
                // the procedural sky, blue at the top of the wall and warm at the bottom, because
                // the groove had become a hole through the building.
                //
                // Taking it out of the occludee set costs almost nothing - there are a handful of
                // slabs per wall, which is why the same bug moved the draw call count by so little
                // that it read as "occlusion is not running here at all". It stays an OCCLUDER,
                // because it is the solid sheet that actually hides the next room.
                bool isGateLeaf = MovesForOcclusion(r.transform);

                bool isBacking = false;
                foreach (Material mat in r.sharedMaterials)
                    if (mat != null && mat.name.StartsWith("GrooveDark")) { isBacking = true; break; }

                StaticEditorFlags occlusion = isGateLeaf
                    ? 0
                    : StaticEditorFlags.OccluderStatic
                      | (isBacking ? 0 : StaticEditorFlags.OccludeeStatic);

                StaticEditorFlags flags = bouncesLight
                    ? StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.ContributeGI
                      | occlusion
                    : StaticEditorFlags.ReflectionProbeStatic;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags);
                if (isBacking) backings++;
                if (isGateLeaf) gateLeaves++;

                // **AND IT TAKES ITS GI FROM PROBES, NOT FROM A LIGHTMAP.** That is not a quality
                // compromise here, it is the only option: a lightmap needs a second UV set, and every
                // piece of this building is generated from script - Unity's primitives have no UV2
                // and a generated mesh has whatever it was given. Adaptive Probe Volumes need none,
                // which is why the bake goes that way.
                //
                // It also buys something lightmaps cannot: the ghosts, the carryables and the
                // player's own body sample the same volumes, so a past self walking through a room
                // is lit by that room instead of by a global constant.
                //
                // `receiveGI` is a `MeshRenderer` property, not a `Renderer` one - a skinned mesh has
                // no such choice to make, because it is never lightmapped in the first place. The
                // cast is the test, so nothing here needs to also ask what kind of renderer it is.
                if (r is MeshRenderer mesh) mesh.receiveGI = ReceiveGI.LightProbes;
                flagged++;
            }

            // **SAID OUT LOUD, because the failure it prevents has no other symptom.** A triangle-less
            // mesh that slips back into the GI set does not warn - it hangs the Editor twenty minutes
            // later inside Unity's own code. A count here means the next one is a line in the build
            // log rather than an afternoon.
            if (skipped > 0)
                Debug.Log($"[SceneBuilder] {skipped} renderer(s) held out of ContributeGI - no "
                        + "triangles to bounce light off. See MarkReflectionProbeStatic.");

            Debug.Log($"[SceneBuilder] Occlusion flags: {backings} backing renderer(s) held out of "
                + "the occludee set so the grooves cannot be culled, and "
                + $"{gateLeaves} moving renderer(s) out of occlusion entirely (see "
                + "MovesForOcclusion) so nothing keeps hiding a room after it has slid away. Zero "
                + "of either is a regression: the first turns room1-1's black grid into sky, the "
                + "second does the same to whatever is past a cycle 3 gate or under a lift.");
            return flagged;
        }

        // Whether any probe volume in the project has actually been baked - the question the URP
        // asset's probe system now follows. See `ConfigureUrpAsset`.
        //
        // **REFLECTION, RELUCTANTLY, AND FAILING SAFE.** `ProbeVolumeBakingSet.HasBeenBaked()` is
        // `internal`, the same wall `BakeLighting.EnsureBakingSet` runs into and answers the same way:
        // look the member up, and report rather than assume when it is gone. A Unity version that
        // renames it makes this return false, which pins URP to legacy probes - the state that renders
        // correctly with no bake. The wrong answer in the safe direction.
        // **RETURNS FALSE ON PURPOSE: APV IS OFF** (2026-08-26, after two days of trying it).
        //
        // Everything needed to switch it back on is intact - the volumes are built, the bake works,
        // the data is on disk. This one line is the switch. What it is NOT is an accident, so the
        // reasoning has to live where the switch is:
        //
        // **1. It never lit the walls.** With ambient at zero, so bounce was the only indirect light,
        // the walls measured 13 of 255 and the ceiling 9, against a floor at 100. Converted to
        // linear that is 4.6% of the floor, where form-factor maths for a room this shape says
        // 25-30%. Switching APV on adds +101 to the floor and +166 to a dynamic prop while giving
        // the walls +2 - it piles light where there is already too much. Raising Virtual Offset past
        // the 25mm panels (the best hypothesis for why) changed nothing.
        //
        // **2. The menu background can never match the game while it is on.** `CaptureMenuBackground`
        // renders during the build, before any probe data is loaded, and its output is provably
        // identical with APV on and off. Measured on the same room: capture 81/89/141 against
        // in-game 114/72/181. The title screen would show a room the game does not render, and every
        // lighting tweak widens the gap. **This is the blocker to solve first if APV is retried.**
        //
        // **3. Play preferred it off, three separate times.** That is the strongest evidence here and
        // it outranks the theory.
        //
        // The `LegacyLightProbes` path this falls back to is what the building has always rendered
        // with: direct light from the fixtures plus the Trilight ambient constant.
        private static bool AnyBakedProbeVolumes()
        {
            return false;
#pragma warning disable 162
            MethodInfo baked = typeof(ProbeVolumeBakingSet).GetMethod("HasBeenBaked",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (baked == null)
            {
                Debug.LogWarning("[SceneBuilder] ProbeVolumeBakingSet.HasBeenBaked is gone - this "
                               + "Unity version has moved the API. Assuming no baked probe data.");
                return false;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:ProbeVolumeBakingSet"))
            {
                var set = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (set != null && baked.Invoke(set, null) is true) return true;
            }

            return false;
#pragma warning restore 162
        }

        // Whether a renderer has a surface the GI bake can actually use.
        //
        // **ASKED OF THE SUB-MESH TOPOLOGY, not the triangle count**, because that is the question
        // the ray tracing structure asks: a mesh can carry lines or points, report plenty of
        // vertices, and still have nothing to intersect. `GetTopology` reads the sub-mesh descriptor
        // rather than the index buffer, so it does not need the mesh to be readable - which matters,
        // since the imported `.glb` meshes are not.
        //
        // A renderer with no mesh at all (a line, a trail, a particle system) answers false, which is
        // the right answer for the same reason: nothing for light to bounce off.
        private static bool HasTriangles(Renderer r)
        {
            Mesh mesh = null;
            if (r is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
            else if (r.TryGetComponent(out MeshFilter filter)) mesh = filter.sharedMesh;

            if (mesh == null) return false;

            for (int i = 0; i < mesh.subMeshCount; i++)
                if (mesh.GetTopology(i) == MeshTopology.Triangles) return true;

            return false;
        }

        // Walks up, because the thing that moves is usually a parent of the thing that renders - a
        // door's slab, a plinth's key, a ghost's mesh.
        // **OCCLUSION ASKS A STRICTER QUESTION THAN LIGHTING DOES, AND IT NEEDS ITS OWN LIST.**
        //
        // `MovesDuringPlay` answers "should this bake into the GI and the probes", and it was tuned
        // for that gentler consumer: getting it wrong there means a reflection is slightly off.
        // Getting it wrong HERE means geometry disappears - an occluder baked in a pose it does not
        // hold keeps hiding whatever was behind it after it has moved away.
        //
        // Play found the first one on 2026-09-05: standing on a pad in cycle 3 turned the room
        // beyond the gate into skybox. `WallWhileShut` makes `MovesDuringPlay` answer FALSE on
        // purpose, so a shut gate bakes as the wall it is at rest - a good argument about a RESTING
        // state, and occlusion is not about a resting state.
        //
        // The rest of this list is everything else that moves a renderer and was never in that one,
        // because it never needed to be: the two beam lifts run their decks 5.21m, the barrier
        // crushes, the cable car flies, the tree comes apart, the chess board splits, the slide
        // carries. **The asymmetry is what decides the membership rule**: holding something out of
        // occlusion costs a few draw calls, and leaving something in that should not be costs a room
        // that is not there. So this errs wide, and anything new that moves belongs in it.
        private static bool MovesForOcclusion(Transform t)
        {
            while (t != null)
            {
                if (t.GetComponent<WallWhileShut>() != null) return true;
                if (t.GetComponent<BeamLift>() != null) return true;
                if (t.GetComponent<CrushingBarrier>() != null) return true;
                if (t.GetComponent<CableCarRide>() != null) return true;
                if (t.GetComponent<TreeTrunk>() != null) return true;
                if (t.GetComponent<ChessReward>() != null) return true;
                if (t.GetComponent<SlideRide>() != null) return true;
                t = t.parent;
            }
            return false;
        }

        private static bool MovesDuringPlay(Transform t)
        {
            while (t != null)
            {
                // BEFORE the Door test, because a gate leaf carries both and this is the exception
                // to that rule rather than a separate case - see WallWhileShut for the argument.
                if (t.GetComponent<WallWhileShut>() != null) return false;

                if (t.GetComponent<CarryableItem>() != null) return true;
                if (t.GetComponent<Door>() != null) return true;
                if (t.GetComponent<RewardPlinth>() != null) return true;
                if (t.GetComponent<Balloon>() != null) return true;
                if (t.GetComponent<Drawer>() != null) return true;
                if (t.GetComponent<GhostReplayer>() != null) return true;
                // The cover over a cycle's way out. Authored closed and slid aside at the boundary,
                // so baked in it would leave a shut floor in every reflection of a room the player is
                // standing in with it open.
                //
                // **UNLESS IT NEVER OPENS** - see `CycleExit.neverOpens`. Room3-2N's floor cover is
                // cut and stays shut, and holding it out of the bake left it unlit in a lit room.
                CycleExit exit = t.GetComponent<CycleExit>();
                if (exit != null) return !exit.neverOpens;
                // ROOM2-5'S POOL, both halves of it. The balls drift, so a baked one is a ball frozen
                // where it was authored rather than where it floats; and the water itself is a
                // transparent surface that is reflecting the probe - baking it in would put the room's
                // reflection of the water inside the water's reflection of the room.
                if (t.GetComponent<WaterPool>() != null) return true;
                if (t.GetComponent<FloatingBalls>() != null) return true;
                // ROOM2-6'S VALVES AND ITS DRAIN. The wheels turn, the cover slides aside and the
                // funnel appears - all three baked into a probe would put a shut drain and a still
                // wheel in the reflection of a room the player has just changed.
                if (t.GetComponent<Valve>() != null) return true;
                if (t.GetComponent<PoolDrain>() != null) return true;
                // The gas. A particle system baked into a probe would put vapour in the reflection
                // of a room that has not been gassed yet.
                if (t.GetComponent<ParticleSystem>() != null) return true;
                if (t.CompareTag("Player")) return true;
                t = t.parent;
            }
            return false;
        }
    }
}
