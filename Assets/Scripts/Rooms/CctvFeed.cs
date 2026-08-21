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

        public int textureWidth = 512;
        public int textureHeight = 288;

        // How close a player has to be to a screen for the feed to be worth drawing. Generous enough
        // that walking into the room starts it before anyone is reading it.
        public float viewerRange = 6f;
        // Frames per second for the feed itself. Deliberately not the game's rate: this is a security
        // monitor, and a slightly steppy one reads as the right thing rather than as a window.
        public float refreshRate = 12f;

        private RenderTexture target;
        private float nextRender;

        private void Awake()
        {
            if (feedCamera == null) return;

            // BUILT AT RUNTIME, not saved as an asset. A `RenderTexture` asset would be one more
            // generated file for `SceneBuilder` to keep in step, and this one has no settings worth
            // persisting - it is a size and a depth buffer.
            target = new RenderTexture(textureWidth, textureHeight, 16, RenderTextureFormat.ARGB32)
            {
                name = "CctvFeed",
                filterMode = FilterMode.Bilinear,
            };
            target.Create();

            feedCamera.targetTexture = target;
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
            if (!ViewerNear()) return;

            nextRender = Time.unscaledTime + 1f / Mathf.Max(1f, refreshRate);
            // `Render` rather than enabling the camera, so the cost lands exactly on the frames that
            // asked for it and the rate above means something.
            feedCamera.Render();
        }

        private bool ViewerNear()
        {
            Collider player = PlayerLookup.Collider;
            if (player == null || !player.enabled || screens == null) return false;

            Vector3 at = player.bounds.center;
            foreach (Renderer screen in screens)
            {
                if (screen == null) continue;
                if ((screen.transform.position - at).sqrMagnitude < viewerRange * viewerRange) return true;
            }
            return false;
        }
    }
}
