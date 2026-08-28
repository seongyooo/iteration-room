using UnityEngine;

namespace IterationRoom
{
    // A FLOOR THAT COMES DOWN WHEN THE LIGHT IS ON IT, AND GOES BACK UP WHEN THE LIGHT GOES.
    //
    // Room3-2N is three storeys tall with two decks and, until this, no way onto either. This is the
    // way, and the direction it runs is the whole design: **raised is its resting state.** The beam
    // pulls it DOWN to you; the moment the beam is interrupted it climbs back to the top, carrying
    // whatever is standing on it.
    //
    // **THAT IS WHY IT RUNS THIS WAY ROUND RATHER THAN THE OBVIOUS ONE.** A lift that rises while
    // powered would need the beam held for the whole ride, which is a past self standing still and
    // the player watching them do it. Powered-DOWN means the ride is what happens when a past self
    // LETS GO - so the player steps on at the bottom while the light is held, and goes up on the
    // recording running out. The cycle's premise is that a past self has to be somewhere for you to
    // get anywhere; this is that premise pointed at a wall you cannot climb.
    //
    // **NOTHING HERE IS RECORDED, and it does not cost a signal bit.** The lift is a pure function of
    // whether a receiver is lit, a receiver is a pure function of where the beam lands, and the beam
    // is recomputed every frame from where the mirrors are - which for a ghost comes off its own
    // timeline. So a past self who held the light holds it again, exactly, with nothing in
    // `RecordedFrame` describing any of it. Same argument as `LaserBeam`'s, one link further along.
    public class BeamLift : MonoBehaviour
    {
        // The slab that moves. A child, so this object can stay at the shaft's origin and the two
        // heights below can be plain local Y.
        public Transform panel;

        // **ANY ONE OF THEM CALLS IT, and there are two for the reason a real lift has a button on
        // each floor.** The low plate is reachable by a level beam off the floor; the high one is not
        // reachable at all except by the pane that leaves the horizontal plane. Once the player is on
        // the deck the low plate is five metres below them, so without the second one the lift
        // is a one-way trip.
        public LaserReceiver[] calls;

        // Where it rests (top) and where the beam pulls it (bottom), as local Y on `panel`.
        public float raisedY;
        public float loweredY;

        // Metres a second. Slow enough to step onto and off, fast enough that the climb to deck A is
        // not most of an iteration - the whole budget is sixty seconds, and 5.4m at this rate is 2.4.
        public float speed = 2.2f;

        // WHO IS STANDING ON IT. A thin trigger sitting just above the deck, inset from its edges so
        // somebody standing BESIDE the panel at the top is not dragged along with it. Polled rather
        // than driven by callbacks, for the reason every other volume in this building is: `Teleport`
        // disables the controller inside one frame, so exit callbacks are not reliable here.
        public Collider rideVolume;

        public AudioSource audioSource;
        public AudioClip moveClip;

        private bool Called
        {
            get
            {
                if (calls == null) return false;
                foreach (LaserReceiver r in calls)
                    if (r != null && r.Lit) return true;
                return false;
            }
        }

        // **AFTER THE BEAM.** `LaserBeam` traces in its own `LateUpdate` and the receivers latch in
        // `Update`, so what is read here is one frame behind the light. On a slab moving at 2.2m/s
        // that is 3cm of lag and nobody can see it; computed in `Update` instead, the lift would be
        // reacting to where the mirrors were before the player turned.
        private void LateUpdate()
        {
            if (panel == null) return;

            // **STILL WHILE THE CLOCK IS STOPPED**, like every other moving thing in the building.
            // The loop sweeps the mirrors home during the blackout, so every receiver goes dark at
            // the boundary - left running, the lift would spend the wake-up climbing back to the top
            // in full view. `ResetLift` snaps it instead, which is both instant and silent.
            if (LoopManager.Instance != null && !LoopManager.Instance.IterationRunning)
            {
                Quieten();
                return;
            }

            float target = Called ? loweredY : raisedY;
            Vector3 local = panel.localPosition;
            if (Mathf.Abs(local.y - target) < 0.0005f)
            {
                Quieten();
                return;
            }

            // MEASURED IN WORLD SPACE, not taken from the local step. What the player has to be moved
            // by is how far the deck actually went, and local Y is only the same number while nothing
            // above this is rotated - which is true today and is not a thing to rely on.
            float before = panel.position.y;
            local.y = Mathf.MoveTowards(local.y, target, speed * Time.deltaTime);
            panel.localPosition = local;
            float travelled = panel.position.y - before;

            // **THE RIDER IS MOVED EXPLICITLY.** A `CharacterController` does not inherit the motion
            // of the collider under it - it is not a rigidbody and nothing pushes it - so a platform
            // that just moves slides out from under the player and drops them. Asking the controller
            // to make the same step is the whole of carrying somebody.
            if (travelled != 0f && PlayerLookup.InReach(rideVolume))
                PlayerLookup.Controller?.Carry(Vector3.up * travelled);

            // ONCE PER TRIP, on the rising edge of motion, exactly as `CrushingBarrier` announces
            // itself - and with the same clip, because it is the same event: a slab that travels.
            // Retriggered every frame it would be a buzz; looped it would need stopping in three
            // places, one of which is the blackout.
            if (!moving && audioSource != null && moveClip != null)
                audioSource.PlayOneShot(moveClip);
            moving = true;
        }

        private bool moving;

        private void Quieten() => moving = false;

        // SNAPPED, NOT DRIVEN, at the top of an iteration - the same treatment `CrushingBarrier` gets
        // and for the same two reasons: a slab left partway is world state the loop forgot to rewind,
        // and letting it travel there under its own power would be heard through the blackout.
        public void ResetLift()
        {
            if (panel == null) return;
            Vector3 local = panel.localPosition;
            local.y = raisedY;
            panel.localPosition = local;
            Quieten();
        }
    }
}
