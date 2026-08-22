using UnityEngine;

namespace IterationRoom
{
    // THE CAMERA IN ROOM3-2N AND THE SCREENS THAT SHOW IT.
    //
    // **IT IS NOT DECORATION, IT IS THE ONLY WAY THE PUZZLE IS LEGIBLE.** The levers are in three
    // rooms and the stairs they raise are in a fourth, and a lever is HELD - so the person pulling it
    // can never be the person on the stairs, and without a screen they cannot see what their pull did
    // either. The feed is what closes that gap: pull the red lever, watch the red panels come out of
    // the wall of a room you are not standing in, and learn the shape of the climb before spending an
    // iteration on it.
    //
    // **ONE CAMERA, THREE SCREENS, AND THAT IS A COST DECISION.** A camera rendering to a texture
    // draws the whole scene again, every frame, on top of the one the player is looking through. One
    // feed shared by all three monitors is one extra render rather than three, and there is nothing a
    // second angle would show that a high wide one does not - the room is one staircase.
    //
    // **AND IT ONLY RENDERS WHILE SOMEBODY IS LOOKING.** The camera is disabled and driven by hand
    // from here, at a fixed rate, and only while the player is near one of the screens. Standing in
    // room3-1 or halfway up the stairs costs nothing at all. A CCTV feed that updates a few times a
    // second is also more like a CCTV feed than a perfect one is.
    public class CctvFeed : MonoBehaviour
    {
        public Camera feedCamera;
        // Every screen showing this feed. They share one texture, so this is a list of renderers to
        // point at it rather than a list of things to render.
        public Renderer[] screens;

        // **BIG ENOUGH TO READ, AND SHAPED LIKE THE GLASS IT IS DRAWN ON** (2026-08-21: play called
        // the picture unreadable). Two separate faults, and 512x288 was only the first of them.
        //
        // 1. RESOLUTION. 512 across was chosen for a 0.72m monitor. The screens are 2.6m now, so the
        //    same texture is stretched over thirteen times the area and every edge in the room came
        //    apart into stairsteps. `SceneBuilder` sets this from the monitor's size.
        // 2. ASPECT. The model's glass is 2.04:1 and the texture was 16:9, so the picture was
        //    squeezed 13% horizontally - which reads as "bad quality" long before anyone works out
        //    that it is the wrong shape. The height is derived from the measured mesh rather than
        //    written down, so a different monitor model cannot reintroduce it.
        public int textureWidth = 1024;
        public int textureHeight = 502;
        // **SUPERSAMPLING RATHER THAN MSAA, and that is a deliberate choice of the cheaper risk.** A
        // CCTV picture is mostly long straight edges - panel grooves, stair treads, the corner of a
        // mezzanine - and those are what aliases. MSAA on a render texture is the direct answer, but
        // URP takes its sample count from the pipeline asset and not from the target, so a target
        // asking for 4 is at best ignored and at worst a mismatch. 1600 across is already about twice
        // the pixels the screen occupies on a 1080p display, and a bilinear tap over four samples is
        // an antialiased one. Set this to 2/4/8 to try the other way; it is wired.
        public int antiAliasing = 1;

        // How close a player has to be to a screen for the feed to be worth drawing. **Bigger than
        // the room's own diagonal on purpose**: the whole point of a 2.6m screen is that it can be
        // read from wherever you are standing, and a range that stopped short of that would leave the
        // player looking at the last frame it drew before they backed away.
        public float viewerRange = 14f;
        // Frames per second for the feed itself. Deliberately not the game's rate: this is a security
        // monitor, and a slightly steppy one reads as the right thing rather than as a window.
        public float refreshRate = 10f;

        private RenderTexture target;
        private float nextRender;

        // **NO TWO FEEDS RENDER ON THE SAME FRAME, EVER.** Three cameras in one room, all in range of
        // the player at once, all due at the same instant because they were all created in the same
        // frame - so a third of the time the game paid for three whole extra renders of the room
        // between two of its own. Play called it "cycle 3 has started stuttering".
        //
        // A frame stamp shared by every feed in the game is the cheapest possible fix and it caps the
        // worst case rather than improving the average: whatever else is going on, at most ONE extra
        // scene render happens per frame. A feed that loses the race simply tries again next frame,
        // which costs it a fraction of a refresh interval nobody can see at 10fps.
        private static int lastRenderFrame = -1;

        private void Awake()
        {
            if (feedCamera == null) return;

            // BUILT AT RUNTIME, not saved as an asset. A `RenderTexture` asset would be one more
            // generated file for `SceneBuilder` to keep in step, and every setting on it is derived
            // from the monitor it is drawn on rather than authored.
            //
            // 24-bit depth rather than 16. The room is 21m deep and the camera looks the length of it
            // from a corner, and at 16 bits the coloured steps and the wall they sit in were close
            // enough in depth to swap places as the camera resolved - the same shimmer the corridor
            // had, arriving second-hand through a monitor.
            target = new RenderTexture(textureWidth, textureHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "CctvFeed",
                filterMode = FilterMode.Bilinear,
                // Clamped, or the bilinear tap at the very edge of the picture wraps round and puts a
                // sliver of the opposite wall along the frame.
                wrapMode = TextureWrapMode.Clamp,
                // No mips. The screen is looked at very nearly head on and at roughly one size, so a
                // mip chain would only ever cost memory and hand back a blurrier frame at an angle.
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(1, antiAliasing)), 1, 8),
            };
            target.Create();

            feedCamera.targetTexture = target;
            // Has to be allowed on the camera as well, or the target's samples are resolved from a
            // single-sampled render and the setting above buys nothing.
            feedCamera.allowMSAA = target.antiAliasing > 1;
            // Driven by hand below. Left enabled it would render every frame whether or not anyone
            // was in the building to see it.
            feedCamera.enabled = false;

            if (screens == null) return;
            foreach (Renderer screen in screens)
            {
                if (screen == null) continue;
                // `material` rather than `sharedMaterial`: this instantiates, so three screens sharing
                // one source material do not fight over whose texture it holds. They all point at the
                // same RenderTexture, which is the thing that is actually shared.
                screen.material.mainTexture = target;
                if (screen.material.HasProperty("_BaseMap")) screen.material.SetTexture("_BaseMap", target);
                if (screen.material.HasProperty("_EmissionMap")) screen.material.SetTexture("_EmissionMap", target);
            }
        }

        private void OnDestroy()
        {
            if (feedCamera != null) feedCamera.targetTexture = null;
            if (target == null) return;
            target.Release();
            Destroy(target);
        }

        private void Update()
        {
            if (feedCamera == null || target == null) return;
            if (Time.unscaledTime < nextRender) return;
            if (lastRenderFrame == Time.frameCount) return;
            if (!ViewerWatching()) return;

            lastRenderFrame = Time.frameCount;
            nextRender = Time.unscaledTime + 1f / Mathf.Max(1f, refreshRate);
            // `Render` rather than enabling the camera, so the cost lands exactly on the frames that
            // asked for it and the rate above means something.
            feedCamera.Render();
        }

        // **NEAR IS NOT ENOUGH - IT HAS TO BE ON SCREEN.** Range alone kept all three feeds of a lever
        // room alive the whole time the player was in it, including while they stood with their back
        // to every one of them. `PlayerLookup.OnScreen` is the same frustum test every E prompt in the
        // building is gated on, so "the feed is running" and "you can see the feed" are the same
        // question asked once - and a player who turns round gets a fresh frame within a tenth of a
        // second, which is one refresh interval and is not something anyone can catch.
        //
        // `OnScreen` rather than `InView`: the occlusion half of `InView` is a `RaycastAll` per call,
        // and this is asked of up to nine screens every frame. A feed drawn for a screen that turns
        // out to be behind a wall costs one render; nine rays a frame costs every frame.
        private bool ViewerWatching()
        {
            Collider player = PlayerLookup.Collider;
            if (player == null || !player.enabled || screens == null) return false;

            Vector3 at = player.bounds.center;
            foreach (Renderer screen in screens)
            {
                if (screen == null) continue;
                if ((screen.transform.position - at).sqrMagnitude >= viewerRange * viewerRange) continue;
                if (PlayerLookup.OnScreen(screen.transform.position)) return true;
            }
            return false;
        }
    }
}
