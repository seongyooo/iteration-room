using UnityEngine;

namespace IterationRoom
{
    // **THE CONTROLS, STENCILLED ON THE WALL THE PLAYER WAKES UP FACING, FOR ONE ITERATION.**
    //
    // This is what is left of the calibration room (removed 2026-08-31, by request). That room was a
    // whole shell of its own that the player walked through before the clock ever started: it taught
    // the controls, set the mouse sensitivity, and cost a minute of standing still before the game
    // began. The teaching was worth keeping and the room was not - so the pictograms moved onto
    // room1-1's south wall, which is the wall the wake-up leaves the player looking at, and the
    // sensitivity went to the settings page where it was already adjustable.
    //
    // WHY NOT `PanelMessage`, which is the other sign-on-a-wall in this game. Two reasons, and the
    // first is decisive: `PanelMessage.Update` calls `narration.AnnounceManualTermination()` by name
    // - it is the N sign with a second user, not a general fixture - and a controls wall has nothing
    // to announce. The second is that its retire rules are all about a message being *read once*,
    // where this is a reference you should be able to turn back to for as long as it is up.
    //
    // **ITERATION 1 ONLY**, which is `lastIteration`. The wall is scenery the moment it has been
    // read: a player who has walked and looked and pressed E has learned all of it, and a sign that
    // came back every sixty seconds for the rest of the run would be the nagging `PanelMessage`'s own
    // notes warn about. It is also why nothing here retires on leaving the room - within iteration 1
    // you can walk out, realise you did not read it, and walk back.
    public class ControlsWall : MonoBehaviour
    {
        // Every face this sign is painted on. One, today - the wall the player wakes facing - and an
        // array because that is what `PanelMessage` learned: a sign worth putting up is usually worth
        // putting on more than one wall, and finding that out later should not change the component.
        public CanvasGroup[] faces;

        // The room, as the same two numbers `PanelMessage` uses: a centre and a half-depth on Z.
        // Cheaper than a trigger and immune to the loop's teleport, which is the reason every volume
        // in this building is polled rather than driven by collider callbacks (see `FloorButton`).
        public float roomCenterZ;
        public float halfDepth = 5.25f;

        // The last iteration this is shown in. One means "the first, and never again".
        public int lastIteration = 1;

        public float fadeSpeed = 3f;

        private float alpha;

        private void Update()
        {
            bool show = Showing();

            // Unscaled, so a sign caught mid-fade does not sit frozen under the pause overlay - the
            // same reason `PanelMessage` and `ControlHintDisplay` use it.
            alpha = Mathf.MoveTowards(alpha, show ? 1f : 0f, fadeSpeed * Time.unscaledDeltaTime);

            if (faces == null) return;
            foreach (CanvasGroup face in faces)
                if (face != null) face.alpha = alpha;
        }

        private bool Showing()
        {
            LoopManager loop = LoopManager.Instance;

            // **NOTHING WHILE THE PLAYER HAS NO CONTROL, and here that is the whole thing rather than
            // a guard.** The loop parks the player in this very room for the wake-up, eyes shut - so
            // without this the wall lights up behind closed lids and the first thing the player sees
            // is a sign already fading in. It covers the ending for the same reason.
            if (loop != null && !loop.IterationRunning) return false;
            if (loop != null && loop.IterationNumber > lastIteration) return false;

            Collider player = PlayerLookup.Collider;
            if (player == null) return false;

            return Mathf.Abs(player.transform.position.z - roomCenterZ) < halfDepth;
        }
    }
}
