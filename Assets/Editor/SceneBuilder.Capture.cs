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
    // FILMING THE PROJECT'S OWN PICTURES: the title screen's background, the darkened frame under
    // it, the per-cycle previews, the app icon and the press shot. Everything here RENDERS, so all
    // of it bails on a -nographics build and says so in the log - see CLAUDE.md 5.
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {

        // Renders the player camera to a PNG for the title screen to sit behind. It is literally
        // the first frame of the game - the view down the room from the foot of the bed, which is
        // exactly what the Editor's Game view shows before Play is pressed.
        //
        // Regenerated on every build that can render, so the menu can never advertise a room that
        // no longer exists. Under `-nographics` there is no device to render with: the capture is
        // SKIPPED and the previous PNG stands, because a stale background is a far better outcome
        // than a failed build. A frame that renders but comes back WRONG is held to the same rule,
        // and the tripwire below is what makes "wrong" something this can actually tell.
        // A BOX-FILTERED DOWNSCALE, which is the second half of supersampling and the half that
        // actually removes the aliasing - averaging the samples is what turns four hard pixels into
        // one soft edge. Done in C# rather than by a bilinear blit because a blit at exactly 2:1
        // samples pixel CENTRES and can miss half the detail it is supposed to be averaging.
        private static Texture2D Downsample(Texture2D src, int width, int height)
        {
            int sx = src.width / width, sy = src.height / height;
            if (sx <= 1 && sy <= 1) return src;

            Color[] source = src.GetPixels();
            Color[] outPixels = new Color[width * height];
            float inv = 1f / (sx * sy);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float r = 0f, g = 0f, b = 0f;
                    for (int j = 0; j < sy; j++)
                    {
                        int row = (y * sy + j) * src.width + x * sx;
                        for (int i = 0; i < sx; i++)
                        {
                            Color c = source[row + i];
                            r += c.r; g += c.g; b += c.b;
                        }
                    }
                    outPixels[y * width + x] = new Color(r * inv, g * inv, b * inv, 1f);
                }
            }

            Texture2D result = new Texture2D(width, height, TextureFormat.RGB24, false);
            result.SetPixels(outPixels);
            result.Apply();
            return result;
        }

        // **TWO FRAMES, ONE POSE.** The lit room, and the same room with the fixtures on one side
        // switched off - `MenuFlicker` cross-cuts between them on the title screen.
        //
        // The pair only works because NOTHING moves between the two exposures: same camera, same
        // position, same lens, same frame. Anything that shifted - a drifting prop, a re-framed
        // camera - would read as a jump cut rather than as a light going out, which is the one way
        // this effect can look broken rather than absent.
        // WHICH ROOM STANDS FOR EACH CYCLE on the title screen's RECORD page (2026-08-31, by
        // request). Named rather than derived: "the room that is most this cycle" is a judgement, and
        // the three below are the ones the player remembers each cycle by - the balloons, the tree,
        // and the three-storey hall the ladder climbs.
        //
        // Cycle 4 has no entry because cycle 4 has nothing in it yet. A cycle with no preview simply
        // does not get a row, which is the same rule as a cycle nobody has finished.
        // **ONE ROOM FOR EVERY ROW** (2026-08-31, by request: unify them all on room1-1).
        //
        // It was a room per cycle - the balloon room, then the taps, then room3-2N - and each choice
        // was sound in itself. What defeated it is that a build-time render happens before a scene's
        // probe and lighting data are loaded, and cycles 2 and 3 came back with colours the game does
        // not have. Substituting a flat reflection fixed the WALLS, which are 0.85 smoothness and so
        // are almost entirely reflection; the dark and emissive details - grooves, door leaves,
        // ceiling panels - still read wrong, and they are lit by ambient rather than by reflection.
        //
        // Room1 is the one room that comes out right, every build, in the core scene where the shot
        // is honest. So there is one shot, and every row uses it.
        //
        // **What that costs, stated**: the preview no longer tells one cycle from another, which was
        // the point of having a picture at all. The row's heading does that job now. If the per-cycle
        // shot is ever wanted back, the thing to fix first is the lighting data, not the framing -
        // see `CaptureCyclePreview`.
        private const string RecordPreviewPath = RecordDir + "/Cycle1.png";
        private static readonly string[] CyclePreviewRooms = { "Room1" };

        private const string RecordDir = "Assets/Textures/Record";

        // A THUMBNAIL OF ONE CYCLE, RENDERED AT BUILD TIME.
        //
        // Called from `SplitCyclesIntoScenes` while that cycle's own scene is ACTIVE, which is what
        // makes the shot match the game - see the note at the call site.
        //
        // **ONE FRAMING RULE FOR ALL OF THEM, so they read as a set rather than as three
        // photographs.** Stand on the room's own centre line, 2m up, a bit back from the middle along
        // whichever horizontal axis is LONGEST, and look down that axis. The rooms are different
        // shapes, so a fixed direction would point at a side wall in one of them; the longest axis is
        // the one a room is meant to be seen down.
        private static void CaptureCyclePreview(Cycle cycle, int index)
        {
            if (index >= CyclePreviewRooms.Length) return;
            if (cycle == null || cycle.worldRoot == null) return;

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogWarning("[SceneBuilder] No graphics device (-nographics): cycle previews NOT "
                               + "captured, keeping whatever is on disk. Rebuild with a device.");
                return;
            }

            Directory.CreateDirectory(RecordDir);

            // Woken for the shot and put back exactly as it was. Cycles ship asleep, and a disabled
            // renderer renders nothing - the same trap the probe bake documents.
            bool wasAwake = cycle.worldRoot.gameObject.activeSelf;
            cycle.worldRoot.gameObject.SetActive(true);

            GameObject camGO = new GameObject("PreviewCamera");
            Camera cam = camGO.AddComponent<Camera>();
            cam.fieldOfView = 52f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 120f;

            try
            {
                Transform room = FindChildByName(cycle.worldRoot, CyclePreviewRooms[index]);
                if (room == null)
                {
                    Debug.LogError($"[SceneBuilder] cycle {index + 1} has no room called "
                                 + $"'{CyclePreviewRooms[index]}' to preview.");
                    return;
                }

                Bounds b = SolidBounds(room);
                bool alongZ = b.size.z >= b.size.x;
                Vector3 dir = alongZ ? Vector3.forward : Vector3.right;
                float length = alongZ ? b.size.z : b.size.x;

                // 38% back from the middle: far enough to have the room in front of you, near enough
                // that the far wall is not the whole picture.
                Vector3 eye = b.center - dir * (length * 0.38f);
                // **EYE HEIGHT OFF THE ROOM'S OWN TRANSFORM, NOT OFF ITS BOUNDS.** A room object sits
                // on its floor, so `position.y + 2` is standing height in every one of them.
                // `b.min.y` is the lowest MESH, which in the tree hall is the bottom of a 33m pit -
                // the first run of this put the camera inside that hole looking at its wall.
                eye.y = room.position.y + 2.0f;

                // **THE REFLECTION IS SUBSTITUTED FOR THE SHOT, and this is the fix rather than a
                // dodge.**
                //
                // These walls are 0.85 smoothness - they are almost entirely reflection - and a
                // build-time render happens BEFORE the scene's probe data is loaded. CLAUDE.md
                // already records that about this project's other capture: "`CaptureMenuBackground`
                // renders before probe data loads". Whatever is in the reflection slot at that
                // moment is not what the game will use, and it is not even stable: two builds a few
                // minutes apart, with no material or lighting change between them, gave cycle 2's
                // tap room red walls and then BLUE ones. Play reported the room as white, which it
                // is.
                //
                // A white cubemap is not a lie about the room - it is the answer the probe would
                // give once loaded. Every wall, floor and ceiling in this building is white, so what
                // a probe in the middle of one captures IS a white surround. The floor (0.6) and the
                // props barely moved between those two builds, which is the same fact from the other
                // side: only the mirror-like surfaces were reading the empty slot.
                //
                // Swapped for the frame and put back, so nothing outside this method sees it.
                DefaultReflectionMode wasMode = RenderSettings.defaultReflectionMode;
                Texture wasCustom = RenderSettings.customReflectionTexture;
                Cubemap flat = new Cubemap(1, TextureFormat.RGBA32, false);
                foreach (CubemapFace face in System.Enum.GetValues(typeof(CubemapFace)))
                {
                    if (face == CubemapFace.Unknown) continue;
                    flat.SetPixel(face, 0, 0, new Color(0.78f, 0.78f, 0.80f));
                }
                flat.Apply();
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = flat;

                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(dir, Vector3.up));
                try
                {
                    CaptureMenuFrame(cam, $"{RecordDir}/Cycle{index + 1}.png", 640, 360);
                }
                finally
                {
                    RenderSettings.defaultReflectionMode = wasMode;
                    RenderSettings.customReflectionTexture = wasCustom;
                    Object.DestroyImmediate(flat);
                }

                Debug.Log($"[SceneBuilder] Cycle {index + 1} preview: {CyclePreviewRooms[index]} from "
                        + $"{eye}, looking {(alongZ ? "+Z" : "+X")} down {length:0.#}m, with a flat "
                        + "reflection standing in for the probe (see the note above).");
            }
            finally
            {
                Object.DestroyImmediate(camGO);
                cycle.worldRoot.gameObject.SetActive(wasAwake);
            }
        }

        // The preview sprite for a cycle, or null when there is no file - which is what a cycle with
        // no distinctive room gets, and what every cycle gets on a `-nographics` build before the
        // first real one.
        // The same frame for every row - see `CyclePreviewRooms`. The parameter is kept so the day
        // per-cycle shots come back, this is the only line that changes.
        private static Sprite CyclePreviewSprite(int cycle)
        {
            string path = RecordPreviewPath;
            if (!File.Exists(path)) return null;

            if (AssetImporter.GetAtPath(path) is TextureImporter importer
                && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void CaptureMenuBackground(Camera cam, Transform room)
        {
            CaptureMenuFrame(cam, MenuBackgroundPath);

            // **THE LIGHTS ONLY, NEVER THE EMISSIVE PANELS.** A fixture's glowing face wears
            // `CeilingFixture`, which is ONE material shared by every fixture in the building - dim it
            // and every ceiling in the game goes with it. The `Light` components are per-object and
            // are what actually paints the walls and floor, which is the whole of what this shot is
            // of: the framing deliberately excludes the ceiling (see the note at the call site), so
            // the panels are barely in frame anyway.
            var doused = new System.Collections.Generic.List<Light>();
            if (room != null)
                foreach (Light light in room.GetComponentsInChildren<Light>(true))
                {
                    // ONE SIDE OF THE ROOM. `BuildCeilingLights` lays a 2x2 grid either side of the
                    // room's centreline, so everything at negative x is the left-hand pair - which is
                    // the side the menu's column of text sits over, so the fault is behind the words
                    // rather than out on the open right where the eye is resting.
                    if (!light.enabled || light.transform.position.x >= 0f) continue;
                    light.enabled = false;
                    doused.Add(light);
                }

            try { CaptureMenuFrame(cam, MenuBackgroundDarkPath); }
            finally { foreach (Light light in doused) light.enabled = true; }

            Debug.Log($"[SceneBuilder] Menu background: two frames, {doused.Count} fixture(s) doused "
                    + "for the dark one. Zero would mean the flicker has nothing to show.");
        }

        // THE WALL AND NOTHING ELSE (2026-09-02, by request), which makes the icon a MARK rather
        // than a photograph - and a mark is the only thing that survives being drawn 32 pixels wide
        // in a taskbar.
        //
        // **THE FIRST ATTEMPT WAS A PHOTOGRAPH AND IT FAILED FOR A REASON ALREADY WRITTEN DOWN.**
        // It was the menu camera with a narrower lens, on the theory that the doorway is the one
        // dark shape in a white room. The doorway is a CLOSED DOOR of almost exactly the wall's own
        // value, the black grid outweighed it, and the bed and nightstand were sliced by the bottom
        // edge. The note at the menu capture's call site had already recorded the same failure from
        // the other end - a square shot of a wall "came back as wallpaper ... a surface that is in
        // shade at that angle so the room stopped being bright". Both attempts were photographs of a
        // room that has no silhouette in it.
        //
        // What is left when the furniture goes is the thing the room is actually made of: a grid of
        // pale panels in black grooves. Two decisions make that read:
        //
        // 1. **A CLEAN WALL.** Room1's EAST wall - no doorway (that is north), no controls wall
        //    (south), no furniture in front of it. The camera turns its back on everything.
        // 2. **CENTRED ON A GROOVE INTERSECTION, NOT ON A PANEL.** The cell is 1.75 x 1.3519, so it
        //    is not square and a single panel centred in a square frame reads as a letterbox. An
        //    intersection is symmetrical by construction: the cross lands dead centre whatever the
        //    cell aspect is. `GridCellWidth`/`GridCellHeight` divide both wall spans exactly, so
        //    z = 0 and y = 2 x GridCellHeight are both on the grid - these are derived, not tuned.
        //
        // The frame is stated in METRES OF WALL rather than as a lens angle, because that is what
        // decides the picture; the fov follows from the standoff.
        //
        // **HOW WIDE WAS DECIDED BY MEASUREMENT, NOT BY TASTE.** A groove is `GridLineThickness`,
        // 0.05m, so how bold the mark is depends entirely on how much wall is in frame - and the
        // icon is drawn at 32 pixels in a taskbar. The first framing was 2.0m of half-extent, which
        // put the centre cross plus the next groove out on all four sides in frame and looked well
        // at full size; downsampled, it measured 0.05/4.0 = 1.25% of the width, i.e. **0.4 pixels**
        // at 32, and turned to grey mush. Sizes 48 and up were fine, 16 and 32 were not.
        //
        // 0.7m is the answer to that: 0.05/1.4 = 3.6% of the width, 1.1 pixels at 32, and the mark
        // is one groove intersection - a bold cross on a pale panel - which is legible at every
        // size an icon is ever drawn at. What it costs is the panelling: at this range only the
        // centre cross is in frame, because its neighbours are a whole cell away (1.75 and 1.3519).
        // **Raising this number back toward 1.4 brings the neighbours in and takes the small sizes
        // out again** - that trade is the whole of this constant, and it has been measured once.
        //
        // It also puts the frame at y 2.00 to 3.40, which happens to drop the two specular hotspots
        // the wider version caught - the ceiling fixtures reflected in an 0.85-smoothness wall,
        // which sit up at about y = 4.27.
        // ONE PRESS SHOT OF ROOM1, deliberately NOT the title screen's picture.
        //
        // The menu background is square to the north wall from the back of the room, which is a
        // designed surface rather than a view - that is what it is for, and it is why it also makes
        // the itch.io cover. A store page that used it twice would be showing the same picture
        // twice, so this one is composed the other way: **a player's eye, at the player's own field
        // of view**, standing where somebody who just got out of the bed would stand.
        //
        // 60 degrees is `FirstPersonController`'s own fov. The menu shot deliberately runs narrower
        // (41.3) because a 60 bows the wall grid at the corners - here that bowing is wanted, since
        // it is exactly what the game looks like in the hand. A screenshot that is easier on the eye
        // than the game is a lie about the game.
        //
        // SQUARE TO THE NORTH WALL (2026-09-02, by request), the same facing the menu uses and for
        // the same reason it uses it: a wall parallel to the image plane does not converge at all,
        // so every groove projects perfectly horizontal or vertical. An angled shot was written
        // first and replaced.
        //
        // What still separates this from the title screen's picture is the OBSERVER, not the aim.
        // The menu stands 9.85m back at 2.0m with a 41.3-degree lens - a camera height and a lens
        // chosen for a composition. This stands at `standingEyeHeight`, 1.6m, at 60 degrees, close
        // enough that the bed and the nightstand fill the lower frame. Same room, same facing, a
        // person's eye instead of a camera's.
        private static void CaptureRoom1Shot(Camera cam)
        {
            if (cam == null)
                return;

            Vector3 previousPos = cam.transform.position;
            Quaternion previousRot = cam.transform.rotation;
            float previousFov = cam.fieldOfView;
            try
            {
                // Dead centre in X, like the menu: a grid is symmetrical and a picture of one that
                // is nearly-but-not-quite centred reads as a mistake. Level, because pitching would
                // put the wall into convergence and throw away what square buys.
                const float eyeY = 1.6f;                  // FirstPersonController.standingEyeHeight
                Vector3 eye = new Vector3(0f, eyeY, -2.2f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(Vector3.forward, Vector3.up));
                cam.fieldOfView = 60f;                    // the player's own, not the menu's 41.3
                CaptureMenuFrame(cam, Room1ShotPath, 1920, 1080, TextureImporterType.Default, "Room1 press shot");
            }
            finally
            {
                cam.transform.SetPositionAndRotation(previousPos, previousRot);
                cam.fieldOfView = previousFov;
            }
        }

        private static void CaptureAppIcon(Camera cam)
        {
            if (cam == null)
                return;

            // Half the wall the square frame covers, in metres. THIS is the framing knob: raise it
            // to pull back and take in more cells, lower it to sit closer to one intersection.
            const float iconHalfExtent = 0.7f;
            const float iconStandoff = 3.5f;

            float previousFov = cam.fieldOfView;
            Vector3 previousPos = cam.transform.position;
            Quaternion previousRot = cam.transform.rotation;
            try
            {
                // Room1 is centred on the origin, so its east wall's interior face is at +X. The
                // aim point is the intersection two rows up (mid-wall) at z = 0, which is a column
                // boundary because 10.5 divides by 1.75 exactly.
                float wallX = RoomWidth / 2f;
                Vector3 aim = new Vector3(wallX, GridCellHeight * 2f, 0f);
                cam.transform.SetPositionAndRotation(aim - Vector3.right * iconStandoff,
                                                     Quaternion.LookRotation(Vector3.right, Vector3.up));
                cam.fieldOfView = 2f * Mathf.Atan2(iconHalfExtent, iconStandoff) * Mathf.Rad2Deg;

                // 1024 square: the largest icon Windows or macOS asks for, and Unity downscales the
                // rest from it. Written as a Default texture rather than a Sprite because it is not
                // drawn by anything in the game - `PlayerSettings.SetIcons` is its only consumer.
                CaptureMenuFrame(cam, AppIconPath, 1024, 1024, TextureImporterType.Default, "App icon");
            }
            finally
            {
                cam.fieldOfView = previousFov;
                cam.transform.SetPositionAndRotation(previousPos, previousRot);
            }
        }

        private static void CaptureMenuFrame(Camera cam, string path,
                                             int width = 1920, int height = 1080,
                                             TextureImporterType importAs = TextureImporterType.Sprite,
                                             string label = "Menu background")
        {
            if (cam == null)
            {
                Debug.LogWarning($"[SceneBuilder] No player camera; {label} not captured.");
                return;
            }

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogWarning($"[SceneBuilder] No graphics device (-nographics): keeping the existing "
                               + $"{label}. Rebuild from the Editor to refresh it.");
                return;
            }


            // RENDERED AT 2x AND DOWNSAMPLED. The room is a grid of thin black grooves on white, which
            // is the worst case for aliasing there is: at 1:1 every line crawls and breaks up, and the
            // menu advertised the game as a jaggy mess. Supersampling fixes it regardless of what MSAA
            // the pipeline asset happens to be set to, which is the reason to do it this way rather
            // than by asking the RenderTexture for anti-aliasing.
            const int ss = 2;
            int rw = width * ss, rh = height * ss;

            RenderTexture rt = new RenderTexture(rw, rh, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = cam.targetTexture;
            CameraClearFlags previousFlags = cam.clearFlags;
            Color previousBackground = cam.backgroundColor;
            int previousMask = cam.cullingMask;
            Texture2D shot = null;
            byte[] png = null;
            float stray = 1f;

            try
            {
                cam.targetTexture = rt;

                // The tripwire. The camera clears to Skybox in play, and the rooms are sealed boxes
                // with the camera INSIDE one, so a correct frame is geometry edge to edge and the
                // clear never shows - which means the clear colour reaching the PNG is proof that
                // something did not draw, or that the camera is not where it should be. Clearing to
                // magenta instead makes that proof readable. It cannot change a good frame, because
                // a good frame contains none of it.
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = MenuCaptureTripwire;

                // **NO BODY IN THE TITLE SHOT** (2026-08-23, by request). This is the player's own
                // camera, and the caller MOVES it across the room to frame the north wall - but the
                // body stays with the player root, so it does not come along. It stands wherever the
                // spawn left it and walks straight into the frame. Before the player had a body this
                // capture was of an empty room, which is what the title screen is composed as.
                //
                // All three layers, not just the world one. The first-person body is headless and
                // armless by design, so what it contributes to a shot taken from across the room is a
                // legless torso - worse than the whole person, not better. And the shadow-only
                // instance would leave a person-shaped shadow on the floor with nobody casting it.
                cam.cullingMask &= ~((1 << EnsureLayer(PlayerBodyViewLayer))
                                   | (1 << EnsureLayer(PlayerBodyWorldLayer))
                                   | (1 << EnsureLayer(PlayerBodyShadowLayer)));

                shot = new Texture2D(rw, rh, TextureFormat.RGB24, false);

                // Rendered up to TWICE. The frame this has been seen to get wrong was the first one
                // after the URP asset was rewritten earlier in the same build: URP drops and
                // rebuilds its pipeline instance when its asset changes, and a request submitted
                // into that window came back as bare skybox with every lit surface missing. A
                // second request is enough, and costs one frame on the build that needs it.
                const int attempts = 2;
                for (int attempt = 1; attempt <= attempts && png == null; attempt++)
                {
                    // URP does not support a bare Camera.Render() from arbitrary code - a render
                    // request is the supported route, and it is also what actually runs the volume
                    // stack (tonemapping, bloom, vignette) the room's look is tuned against. The
                    // fallback is there for a pipeline that does not advertise the request.
                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, request))
                        RenderPipeline.SubmitRenderRequest(cam, request);
                    else
                        cam.Render();

                    RenderTexture.active = rt;
                    shot.ReadPixels(new Rect(0f, 0f, rw, rh), 0, 0);
                    shot.Apply();

                    stray = StrayClearFraction(shot);
                    if (stray <= MenuCaptureMaxStray)
                        png = Downsample(shot, width, height).EncodeToPNG();
                    else if (attempt < attempts)
                        Debug.LogWarning($"[SceneBuilder] {label} attempt {attempt}: {stray:P1} of the "
                                       + "frame is clear colour, so the room did not render. Retrying.");
                }
            }
            finally
            {
                // Restored in a finally: leaving a target texture on the player camera would mean
                // the game renders into a RenderTexture instead of the screen, and the saved scene
                // would carry it. The clear flags go back for the same reason - the room is sealed
                // so it would never show, but the scene should not carry a setting made for a
                // one-off capture.
                cam.targetTexture = previousTarget;
                cam.clearFlags = previousFlags;
                cam.backgroundColor = previousBackground;
                // Or the saved scene ships a player who cannot see their own legs.
                cam.cullingMask = previousMask;
                RenderTexture.active = previousActive;
                if (shot != null) Object.DestroyImmediate(shot);
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            // Nothing written, so the previous PNG stands - same call as `-nographics`. An ERROR
            // rather than a warning because this one already shipped once: a skybox went to itch.io
            // behind the title screen and the build reported success.
            if (png == null)
            {
                Debug.LogError($"[SceneBuilder] {label} NOT captured - {stray:P1} of the frame came back "
                             + "clear colour on both attempts. The previous PNG is kept. Build again from the "
                             + "Editor; if it persists, the camera is no longer inside the room.");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, png);

            // A FRAME THAT IS NOT AN ASSET DOES NOT GO THROUGH THE IMPORTER, and must not: a press
            // screenshot is written outside Assets/ precisely so it is not imported, not given a
            // .meta, and not carried into every player build as a texture nothing draws.
            if (!path.StartsWith("Assets/"))
            {
                Debug.Log($"[SceneBuilder] {label} captured to {path}");
                return;
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = importAs;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 2048;
                // UNCOMPRESSED, and this is what the softness was. The default is DXT at quality 50,
                // and block compression is at its worst on exactly this image: huge smooth gradients
                // across a wall and floor, where 4x4 blocks of two interpolated endpoints show as
                // banding and mush. It is one full-screen sprite on a menu - about 8MB uncompressed,
                // which is nothing to spend on the first thing anybody sees.
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            Debug.Log($"[SceneBuilder] {label} captured to {path}");
        }

        // What fraction of the frame is still wearing the camera's clear colour. Compared with a
        // tolerance rather than for equality because the readback is sRGB-converted, and only on the
        // two channels that separate magenta from anything the room contains.
        private static float StrayClearFraction(Texture2D shot)
        {
            Color32 want = MenuCaptureTripwire;
            Color32[] pixels = shot.GetPixels32();
            int stray = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                if (Mathf.Abs(p.r - want.r) < 56 && Mathf.Abs(p.g - want.g) < 56 && Mathf.Abs(p.b - want.b) < 56)
                    stray++;
            }
            return pixels.Length == 0 ? 1f : (float)stray / pixels.Length;
        }
    }
}
