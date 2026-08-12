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
    // Four walls, not one, and that is not decoration. There is no single wall the player is
    // reliably looking at in any of these rooms, and a message on the wrong one is a message nobody
    // reads. Covering all four is affordable because each of these retires.
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

        // NOT BEFORE THIS ITERATION. 0 means no gate.
        //
        // The end-cycle sign uses it, and it is the whole reason that sign can live in Room1: told
        // this in iteration 1 the player has never yet watched a clock run out and has nothing to
        // apply it to, so the instruction is noise. By iteration 2 they have sat through sixty
        // seconds of dead time exactly once, which is the moment it becomes an offer rather than a
        // fact. It used to hang in Room3 for the same reason expressed as distance rather than as
        // time - the walk through two solved rooms - and Room1 is the better address for it because
        // it is where every iteration BEGINS: the player is standing still, facing a wall, with
        // nothing to do yet.
        public int showFromIteration;

        // Seconds of qualifying time before the PA line, NOT before the sign.
        //
        // Needed only because the loop announces itself. NarrationDirector.Speak is Stop() + Play(),
        // so announcements replace each other rather than stacking, and a room whose sign lights up
        // at the top of an iteration would cut "Iteration N, 60 seconds remaining." off mid-word.
        // Room3's never had to care - nobody arrives there in the first seconds of a cycle.
        //
        // Accumulated rather than reset on leaving, so a player who ducks in and out still gets it.
        public float announceDelay;

        private float alpha;
        private float insideTime;

        // Set instead of using the leave-once rule below, and it changes when this retires: the sign
        // stays up until the player has actually POPPED something, however many visits that takes.
        //
        // Room2's pictogram needs this and Room3's message did not, and the difference is who each
        // one is for. Skipping an iteration is learned in one reading. But the player this pictogram
        // exists for is the one who walked past the drawer, arrived with empty hands, could not burst
        // anything and went back out to look - and under the leave-once rule they would return to a
        // blank wall, having been shown the answer at the one moment they could not use it.
        public BalloonTool retireOnPop;

        // The same rule for the end-cycle sign: retire it once the player has actually held N.
        //
        // TWO CONCRETE FIELDS RATHER THAN ONE ABSTRACTION, and that is Unity's serialisation talking
        // rather than a design preference - a delegate does not survive being written into a scene,
        // and a scene here is build output. If a third source of this ever appears, an interface
        // with a MonoBehaviour field is the move, not a fourth bool.
        //
        // It matters more here than for the pictogram. The leave-once rule would be actively wrong
        // in Room1: the player wakes up in this room and can be out of it in under three seconds, so
        // "shown until they leave" can retire a sign nobody has read. Retiring against the ACTION
        // means the room keeps offering until the offer is taken.
        public EndCycleControl retireOnEndCycle;

        // Shown on the FIRST visit only - the walls and the announcement both. Once the player has
        // been in and left, this never lights again. Not used when either retire-on-action field is.
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

            if (inside)
            {
                insideTime += Time.deltaTime;
                if (!announced && insideTime >= announceDelay)
                {
                    announced = true;
                    narration?.AnnounceManualTermination();
                }
            }

            // THE ACTION RETIRES IT FIRST. A sign that has been seen has taught nothing; one whose
            // action has been performed has nothing left to say. Same rule as the left-click prompt
            // and the Tab hint.
            if ((retireOnPop != null && retireOnPop.HasPopped)
                || (retireOnEndCycle != null && retireOnEndCycle.UseCount > 0))
                retired = true;

            // AND, FOR EVERYTHING EXCEPT THE PICTOGRAM, on the way out once it has actually been up.
            // The two halves of that are both necessary and they answer opposite failures:
            //
            // - on the way OUT rather than on arrival, because the sign's one chance to be read is
            //   the whole time the player is in the room, and a timer would race someone who walked
            //   in and turned straight round;
            // - only once `announced`, which is the delay above already elapsed, because Room1 is
            //   the room the player WAKES UP IN and can be out of in under three seconds. Without
            //   the gate the leave-rule retires a sign nobody read.
            //
            // Together they also stop it nagging. N is optional, so a player who reads it and never
            // uses it would otherwise be shown four wall-sized signs at the top of every remaining
            // iteration - which is exactly the failure this file's own note warns about.
            //
            // The pictogram is excluded because it must persist until a pop, whatever it costs: the
            // player it exists for is the one who arrives empty-handed and walks back out to look.
            else if (retireOnPop == null && announced && !inside) retired = true;

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
            // Nothing to say while the player has no control. For Room2's pictogram that guard is
            // slack - the bed is nowhere near it - but for a sign in Room1 it is the whole thing:
            // the loop parks the player in THIS room for the wake-up, so without it the walls would
            // light up under closed eyelids. It also covers the ending, where nothing should come
            // up behind the scrim.
            if (LoopManager.Instance != null && !LoopManager.Instance.IterationRunning) return false;

            // Too early to be worth saying. Checked here rather than in Update so it gates the
            // announcement and the sign together, and so the delay timer does not run either.
            if (showFromIteration > 0 && LoopManager.Instance != null
                && LoopManager.Instance.IterationNumber < showFromIteration) return false;

            Collider playerCollider = PlayerLookup.Collider;
            if (playerCollider == null) return false;

            return Mathf.Abs(playerCollider.transform.position.z - roomCenterZ) < halfDepth;
        }
    }
}
