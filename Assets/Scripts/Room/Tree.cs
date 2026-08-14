using UnityEngine;

namespace IterationRoom
{
    // ROOM3'S RULE: enough people swinging at the same moment, and the tree comes down.
    //
    // **THE THRESHOLD IS A HEADCOUNT, AND NOTHING IS STORED** - the decision `docs/decisions.md`
    // settled for this room. The tree does not remember being 40% cut. Every iteration it stands
    // again, every ghost that chopped chops again, and the moment enough of them are swinging at once
    // it falls. Iteration N has N-1 past selves, so the count rises on its own; once it is enough, the
    // tree comes down that iteration **and every iteration after**, because the ghosts always redo it.
    //
    // That is what makes it accumulation without un-rewound state, which is the property every room in
    // this game has to have. Stored damage would be world the loop does not take back.
    //
    // The cost, stated plainly: **a threshold of N cannot be met before iteration N.** At five this is
    // the longest room in cycle 2 by some way, and that is deliberate - it is the room the whole design
    // has been promising, and the image it exists for is five past selves swinging at once while the
    // living player walks through them.
    public class Tree : RoomCondition
    {
        public ChopStation[] stations;

        // How many have to be swinging AT THE SAME MOMENT. One number, and it is this room's length.
        public int choppersNeeded = 5;

        // The whole tree, hinged at its base. Falls away from both doorways - see SceneBuilder for
        // why east is the only direction that clears them.
        public Transform hinge;
        public float fallAngle = 84f;
        public float fallSeconds = 1.6f;

        public AudioSource audioSource;
        public AudioClip fallClip;

        // LATCHED FOR THE ITERATION. The overlap that fells it may last a second; a room that
        // re-blocked itself the moment one ghost walked off would be asking the player to be at the
        // door inside that second as well, which is a sprint bolted onto the puzzle rather than a
        // harder version of it. The loop takes it back at the boundary like everything else.
        private bool felled;
        private float blend;

        public override bool Satisfied => felled;

        // How many are swinging right now. Read by the room's own sign so the player can see the
        // count rising rather than guessing at it.
        public int Choppers
        {
            get
            {
                if (stations == null) return 0;
                int n = 0;
                foreach (ChopStation station in stations)
                    if (station != null && station.Chopping) n++;
                return n;
            }
        }

        private void Update()
        {
            if (!felled && Choppers >= choppersNeeded)
            {
                felled = true;
                if (audioSource != null && fallClip != null) audioSource.PlayOneShot(fallClip);
            }

            if (hinge == null) return;
            blend = Mathf.MoveTowards(blend, felled ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, fallSeconds));
            // Eased IN rather than smoothed at both ends: a tree gives slowly and then all at once,
            // and a symmetrical curve reads as machinery being lowered.
            float t = blend * blend;
            hinge.localRotation = Quaternion.Euler(0f, 0f, -fallAngle * t);
        }

        public override void ResetCondition()
        {
            felled = false;
            // Snapped rather than left to swing back up: this happens behind the closed eyelids, and
            // a tree standing itself up in view is the machinery of the loop showing through.
            blend = 0f;
            if (hinge != null) hinge.localRotation = Quaternion.identity;
            if (stations == null) return;
            foreach (ChopStation station in stations) station?.ResetStation();
        }
    }
}
