using UnityEngine;

namespace IterationRoom
{
    // The room telling the player something, on all four of Room3's walls at once.
    //
    // This exists because the walls are displays. That was established long before there was
    // anything to display - the panel grid boots at the top of every iteration, which is what makes
    // it a screen rather than tiling - and teaching a control on it is the most in-fiction option
    // the project has. The alternative was another disc on the HUD, which would have said the same
    // thing in the game's voice rather than the facility's.
    //
    // Room3 is where it belongs rather than Room1, and the reason is the trek: by the time a player
    // is setting up four pads, every iteration costs a walk through two solved rooms, and the dead
    // time at the end of each one is the most expensive thing in the run. Told this in Room1 they
    // would have no use for it yet.
    //
    // Four walls, not one, and that is not decoration. The player arrives through the south wall
    // with their back to it, walks a diagonal to a pad, and turns to face the door - there is no
    // single wall they are reliably looking at, and a message on the wrong one is a message nobody
    // reads. Covering all four is affordable because it is only ever shown once.
    public class PanelMessage : MonoBehaviour
    {
        // One per wall, each a world-space canvas hung just proud of the panelling.
        public CanvasGroup[] faces;
        public NarrationDirector narration;

        // The room this belongs to, tested against the player's Z. Room3 spans the full width, so
        // there is no X test worth making - being at this Z at all means being in the room.
        public float roomCenterZ;
        public float halfDepth = 5.25f;

        public float fadeSpeed = 1.4f;

        private Transform player;
        private float alpha;

        // Shown on the FIRST visit only - the walls and the announcement both. Once the player has
        // been in and left, this never lights again.
        //
        // That is the opposite call to the one ControlHintDisplay's prompts settled on, and the
        // difference is what the two are teaching. E is a control used constantly, at a different
        // fixture every time, so a prompt that retires leaves the player hunting; this is a control
        // learned once and then simply known. It is also four wall-sized signs rather than a small
        // disc, so it is impossible to miss on the one visit it gets - and impossible to ignore if
        // it came back every sixty seconds for the rest of the run.
        //
        // Plain fields rather than PlayerPrefs, so they reset when you press Play again, which is
        // what you want while this is still being tested.
        private bool announced;
        private bool retired;

        private void Update()
        {
            bool inside = PlayerInside();

            if (inside && !announced)
            {
                announced = true;
                narration?.AnnounceManualTermination();
            }

            // Retired on the way OUT, not on arrival: the sign stays up for as long as the player
            // is in the room the first time, which is the only chance it gets to be read. Killing
            // it on a timer instead would race a player who walked in and immediately turned round.
            if (announced && !inside) retired = true;

            // Unscaled, so a message caught mid-fade does not sit frozen under the pause overlay -
            // the same reason ControlHintDisplay uses it.
            bool show = inside && !retired;
            alpha = Mathf.MoveTowards(alpha, show ? 1f : 0f, fadeSpeed * Time.unscaledDeltaTime);

            if (faces == null) return;
            foreach (CanvasGroup face in faces)
                if (face != null) face.alpha = alpha;
        }

        private bool PlayerInside()
        {
            // Nothing to say while the player has no control: the loop parks them at the bed for
            // the wake-up, which is nowhere near this room, but the guard also covers the ending -
            // where the walls should not light up behind the scrim.
            if (LoopManager.Instance != null && !LoopManager.Instance.IterationRunning) return false;

            if (player == null)
            {
                GameObject go = GameObject.FindGameObjectWithTag("Player");
                if (go == null) return false;
                player = go.transform;
            }

            return Mathf.Abs(player.position.z - roomCenterZ) < halfDepth;
        }
    }
}
