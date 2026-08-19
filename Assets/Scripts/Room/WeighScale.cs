using UnityEngine;

namespace IterationRoom
{
    // ROOM2-7'S RULE: a platform scale with a digital readout, and a number it has to reach.
    //
    // WHAT MAKES IT A PUZZLE IS THAT THE PLAYER IS NOT TOLD ANY OF THE WEIGHTS. There is no label on
    // an axe and no note on a wall; the only way to learn what anything weighs is to carry it here and
    // put it down. The room is therefore a MEASURING instrument first and a lock second, and the first
    // thing most players will weigh is themselves - which is exactly the tutorial, since standing on
    // it makes the number jump and come back.
    //
    // POLLED WITH AN OVERLAP, not by trigger callbacks. Same reason as everywhere else in this
    // project (see `PlayerLookup.InReach`): objects arrive here by being teleported home at the top of
    // an iteration, by a ghost releasing them, and by `FallingItem` scripting a drop - and half of
    // those never generate the enter/exit pairs a trigger needs to stay honest. An overlap asked every
    // frame cannot get out of step with the world.
    //
    // WHAT COUNTS: everything resting in the pan, the player if they are standing in it, and whatever
    // the PLAYER is holding while they do. A person on a scale with an axe in their hands weighs
    // themselves and the axe - a real scale cannot tell the difference and neither does this one, and
    // it gives the player a way to read two things at once without putting either down.
    //
    // A GHOST'S HANDS ARE EMPTY as far as this is concerned, and the asymmetry is the room's rule
    // rather than an oversight: a past self has no collider (CLAUDE.md §1.7) so it can never be in the
    // pan at all, and something it is carrying past is not something it is weighing. What a ghost CAN
    // do is put an object DOWN in the pan, which is how this room is solved.
    //
    // **IT LATCHES.** `Door` re-reads its condition every frame and shuts when it lapses, which is
    // right for pads and wrong here for a specific reason: the player's own weight is part of what
    // this can read, so a target that happens to include them is a target they cannot walk away from.
    // Latched, the scale means "this reading HAPPENED" - and `ResetCondition` at the top of every
    // iteration is what stops that from being permanent.
    public class WeighScale : RoomCondition
    {
        [Header("The pan")]
        // The volume that counts as "on the scale", in this transform's space. Generous upward: things
        // stack, and a bucket standing on an axe is still on the scale.
        public Vector3 panCentre = new Vector3(0f, 0.6f, 0f);
        public Vector3 panSize = new Vector3(1.6f, 1.2f, 1.6f);

        [Header("What it is looking for")]
        public float targetKilograms = 23f;
        // Half a display digit. Anything tighter and floating-point sums of five objects start to
        // matter; anything looser and two different answers read as the same one.
        public float tolerance = 0.05f;

        [Header("The player")]
        // A person is a weight like any other, and knowing their own is how most players will work out
        // what this room does. Nothing else in the game needs this number.
        public float playerKilograms = 71f;
        // AND WHATEVER THEY ARE HOLDING. Standing on a scale with an axe in your hands weighs you AND
        // the axe - the scale has no way to tell the difference, and neither does a real one. It is
        // also the only way the player can weigh two things at once without putting either down, so
        // it is a verb rather than a technicality: carry one, stand on the scale, read the sum.
        //
        // The GHOSTS are the exception, and the asymmetry is deliberate rather than an oversight: a
        // past self has no collider (CLAUDE.md §1.7) so it can never be on the pan, and an object in
        // its hands is being carried past rather than weighed. What a ghost CAN do is put something
        // down on the scale, which is the whole point of the room.
        public bool weighWhatThePlayerCarries = true;

        [Header("The readout")]
        // THE MODEL'S OWN DISPLAY PANEL, painting the number into its texture - see PanelDigits for
        // why neither a canvas over the panel nor quads on it was the right object for the job.
        public PanelDigits display;
        // Lit when the reading is the one the door wants. The colour is the only feedback that says
        // "that is it" - the door is behind the player when they are looking at the scale.
        public Color idleColour = new Color(0.25f, 1f, 0.45f);
        public Color acceptedColour = new Color(1f, 0.92f, 0.35f);

        public AudioSource audioSource;
        public AudioClip acceptClip;

        // What it says right now, and whether it has ever said the right thing this iteration.
        public float Reading { get; private set; }
        private bool latched;
        private readonly Collider[] hits = new Collider[32];

        public override bool Satisfied => latched;

        private void Update()
        {
            Reading = Weigh();
            Show(Reading);

            if (latched) return;
            if (Mathf.Abs(Reading - targetKilograms) > tolerance) return;

            latched = true;
            if (audioSource != null && acceptClip != null) audioSource.PlayOneShot(acceptClip);
        }

        private float Weigh()
        {
            Vector3 centre = transform.TransformPoint(panCentre);
            Vector3 half = panSize * 0.5f;

            int count = Physics.OverlapBoxNonAlloc(centre, half, hits, transform.rotation,
                                                   ~0, QueryTriggerInteraction.Ignore);
            float total = 0f;
            for (int i = 0; i < count; i++)
            {
                Collider hit = hits[i];
                if (hit == null) continue;

                // THE PLAYER, weighed off their own collider - the one PlayerLookup already resolves,
                // so this cannot drift out of step with what counts as the player elsewhere.
                if (PlayerLookup.Collider != null && hit == PlayerLookup.Collider)
                {
                    total += playerKilograms;
                    total += CarriedKilograms();
                    continue;
                }

                // Anything else is weighed through its object rather than its collider: a carryable's
                // reach trigger, its blocker and its mesh are three colliders on one thing, and
                // `GetComponentInParent` collapses them to the one component that has a weight.
                Weighable weighable = hit.GetComponentInParent<Weighable>();
                if (weighable == null) continue;

                CarryableItem carried = weighable.GetComponent<CarryableItem>();
                if (carried != null && carried.IsCarried) continue;

                total += weighable.Kilograms;
            }

            return total;
        }

        // WHAT IS IN THE PLAYER'S HANDS, weighed with them.
        //
        // Asked of `PlayerHand` rather than swept for: an object in a hand is parented under the
        // camera and is nowhere near the pan, so no overlap could ever find it, and the hand is the
        // one thing that knows what it has.
        private float CarriedKilograms()
        {
            if (!weighWhatThePlayerCarries) return 0f;

            PlayerHand hand = PlayerLookup.Hand;
            if (hand == null) return 0f;

            CarryableItem held = hand.Held;
            if (held == null) return 0f;

            Weighable weighable = held.GetComponent<Weighable>();
            return weighable != null ? weighable.Kilograms : 0f;
        }

        private void Show(float kilograms)
        {
            if (display == null) return;

            // The digits go from green to amber when the reading is the one the door wants - the only
            // feedback that says "that is it", since the door is behind the player when they are
            // looking at the scale. Set before the value, so the repaint it forces is the one that
            // draws the new number.
            Color wanted = latched ? acceptedColour : idleColour;
            if (wanted != lastInk) { lastInk = wanted; display.SetInk(wanted); }

            display.Show(kilograms);
        }

        private Color lastInk = Color.clear;

        // The loop rewinding. The latch is world state exactly like a door's position: left standing,
        // the room would begin every later iteration already solved, on top of ghosts re-delivering
        // the objects that solved it.
        public override void ResetCondition()
        {
            latched = false;
            Reading = 0f;
            // The colour goes back with it, and the panel is BLANKED rather than zeroed: an
            // unattended scale is dark, and a 0.0 sitting there says the instrument is reading
            // nothing rather than that nothing is on it.
            lastInk = idleColour;
            if (display != null) { display.SetInk(idleColour); display.Blank(); }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(panCentre, panSize);
        }
    }
}
