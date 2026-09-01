using UnityEngine;

namespace IterationRoom
{
    // FALLING IN THE PIT KILLS YOU, and dying is the iteration ending.
    //
    // That equivalence is the whole design of this: the loop already has exactly one way to take the
    // player out of the world and put them back in bed, and it is the thing that happens sixty times
    // a run. Death does not need a screen, a respawn point or a state of its own - it needs the
    // iteration to end early, which `LoopManager.EndCycleEarly` already does and which the player has
    // already learned the shape of by pressing N.
    //
    // THE RECORDING ENDS HERE TOO, and that is correct rather than a cost to be worked around. A past
    // self that fell in the pit stops doing anything at the moment it fell, because that is what
    // happened. `GhostReplayer.Tick` releases whatever it was carrying when its timeline runs out, so
    // an axe carried into the pit comes back to the room rather than being lost with the ghost -
    // which is CLAUDE.md §1.2's "timeline ends -> dropped" clause doing its job unmodified.
    //
    // A TRIGGER, NOT A Y THRESHOLD. A plane at a height would be a rule about the whole building, and
    // this building has two storeys and a service void between them; a volume is a rule about one
    // hole. It sits well below the lip so that clipping a corner on the way past is not fatal - you
    // have to actually be in the shaft.
    [RequireComponent(typeof(Collider))]
    public class KillVolume : MonoBehaviour
    {
        // Guards against the same fall being reported twice - `OnTriggerEnter` and `OnTriggerStay`
        // both fire while the player is inside, the blackout takes half a second, and the iteration
        // takes a frame or two to turn over after that.
        //
        // RE-ARMED OFF THE ITERATION COUNT rather than by anything calling a reset. A volume that has
        // to be remembered in `Cycle.ResetRooms` is a volume that is fatal exactly once per run the
        // first time somebody adds a second one and forgets - and this one is buried inside the pit
        // shaft, which is not where anybody goes looking.
        private int spentOnIteration = -1;

        private void OnEnable() => spentOnIteration = -1;

        private void OnTriggerEnter(Collider other) => Claim(other);

        // ALSO ON STAY, because a fast enough fall can pass through a trigger between two fixed
        // steps without ever generating an Enter - and the one thing this must never do is let a
        // player through into the empty space under the level.
        private void OnTriggerStay(Collider other) => Claim(other);

        private void Claim(Collider other)
        {
            if (PlayerLookup.Collider == null || other != PlayerLookup.Collider) return;

            LoopManager loop = LoopManager.Instance;
            // Only while the loop is actually running. During the wake-up, the pause menu or a cycle
            // break the player is not playing, and ending an iteration that has not begun would be a
            // reset on top of a reset.
            if (loop == null || !loop.AcceptsInput) return;
            if (spentOnIteration == loop.IterationNumber) return;

            spentOnIteration = loop.IterationNumber;
            StartCoroutine(Blackout(loop));
        }

        // THE SIGHT GOES BEFORE THE ITERATION DOES.
        //
        // Ending the iteration on contact was the first version and it reads as a teleport: one frame
        // you are falling, the next you are in bed, and nothing says you died. The eyelids are what
        // this game already uses to take a player out of the world - they close on the gas at every
        // boundary - so death borrows them and means the same thing it always means.
        //
        // UNSCALED TIME, because `CloseEyes` runs on it and because a pause during the fall must not
        // strand the player mid-blackout with the iteration never ending.
        public float blackoutDuration = 0.55f;

        private System.Collections.IEnumerator Blackout(LoopManager loop)
        {
            WakeUpSequence eyes = loop.wakeUpSequence;
            if (eyes != null) yield return eyes.CloseEyes();
            else yield return new WaitForSecondsRealtime(blackoutDuration);

            // Still falling while the lids shut, which is the point - the drop is what is being felt.
            // `EndCycleEarly` then takes the ordinary path: teleport to the bed, reset the rooms, and
            // the wake-up opens the eyes again.
            loop.EndCycleEarly(LoopManager.EndReason.Killed);
        }

        // Kept for a caller that wants to arm it by hand. Nothing needs to: see `spentOnIteration`.
        public void ResetVolume() => spentOnIteration = -1;
    }
}
