using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // ROOM4'S RULE: one person holds the pawl while another turns the wheel.
    //
    // **THE FIRST EXPLICIT DIVISION OF LABOUR IN THE GAME.** Every other room asks several people to
    // do the same thing at once - stand on pads, pull levers, swing axes. This asks them to do
    // DIFFERENT things at once, and neither is any use alone: turning with nobody on the pawl winds
    // straight back to nothing, and holding the pawl while nobody turns holds nothing.
    //
    // THE UNWIND IS ALSO THE SAFETY NET. A count that could be overshot and never brought back would
    // poison the room for the rest of the cycle - a ghost would replay the wrong turn forever with
    // nothing able to subtract from it. Here the wrong state is not a state at all: let go and it is
    // simply gone, and the next iteration starts from zero like the one before. That is the same
    // property `CountPad`'s wrap-at-ten buys, arrived at from the other direction.
    public class Ratchet : RoomCondition
    {
        public RatchetPawl pawl;
        public RatchetCrank crank;

        // How many notches. Eight is more than one iteration's worth of turning with a partner, so
        // the room accumulates across past selves rather than being solved in one visit.
        public int notchesNeeded = 8;

        // How fast it gives back when nobody is holding the pawl. Fast enough to be unmistakable,
        // slow enough that a moment's fumble is not a whole iteration lost.
        public float unwindPerSecond = 3.5f;

        // The core's axle, which turns with the count. This is what the window is for: the room's
        // progress is visible as a thing moving inside the machine rather than as a number.
        public Transform axle;
        public float degreesPerNotch = 45f;

        // What the room pays out once it is round. Driven by `Requested` - see RewardPlinth.
        public RewardPlinth payout;

        private float notches;
        private bool latched;

        public int Notches => Mathf.FloorToInt(notches);
        public override bool Satisfied => latched;

        // A turn only counts while the pawl is held. Asked by the crank before it accepts a press,
        // so the refusal happens at the press rather than being silently discarded after it.
        public bool PawlHeld => pawl != null && pawl.Held;

        public void Advance()
        {
            if (latched || !PawlHeld) return;
            notches = Mathf.Min(notches + 1f, notchesNeeded);
            if (notches >= notchesNeeded) Latch();
        }

        private void Latch()
        {
            latched = true;
            if (payout != null) payout.Requested = true;
        }

        private void Update()
        {
            // LATCHED ONCE ROUND, so the room stays answered for the iteration. The alignment that
            // finishes it is a single press, and a door that shut again the instant the pawl was
            // released would be asking the player to be at it already.
            if (!latched && !PawlHeld && notches > 0f)
                notches = Mathf.Max(0f, notches - unwindPerSecond * Time.deltaTime);

            if (axle == null) return;
            axle.localRotation = Quaternion.Euler(0f, notches * degreesPerNotch, 0f);
        }

        public override void ResetCondition()
        {
            latched = false;
            notches = 0f;
            if (payout != null) payout.Requested = false;
            if (axle != null) axle.localRotation = Quaternion.identity;
            pawl?.ResetPawl();
            crank?.ResetCrank();
        }
    }

    // The lever somebody has to keep hold of. A plain hold, and the simplest half of the room.
    public class RatchetPawl : GhostInteractable
    {
        public float reach = 1.5f;
        public Transform arm;
        public float downAngle = 48f;
        public Renderer lampRenderer;
        public Color idleColour = new Color(0.45f, 0.47f, 0.52f);
        public Color heldColour = new Color(0.3f, 1f, 0.55f);

        private readonly HashSet<GhostReplayer> ghosts = new HashSet<GhostReplayer>();
        private bool playerHolding;
        private float blend;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public bool Held => playerHolding || ghosts.Count > 0;

        // HELD BY STANDING THERE, not by keeping a key down. A hold that needed a button pressed
        // would be a hold the recorder samples as a level anyway, with an input to explain on top -
        // and this room already has one thing to explain.
        public override bool PlayerSignal => playerHolding;

        public override void SetGhostSignal(GhostReplayer ghost, bool active)
        {
            if (active) ghosts.Add(ghost); else ghosts.Remove(ghost);
        }

        public void ResetPawl()
        {
            playerHolding = false;
            ghosts.Clear();
            blend = 0f;
            Apply();
        }

        private void FixedUpdate()
        {
            bool running = LoopManager.Instance == null || LoopManager.Instance.AcceptsInput;
            Collider player = PlayerLookup.Collider;
            bool near = running && player != null && player.enabled
                        && (player.bounds.center - transform.position).sqrMagnitude < reach * reach;
            playerHolding = near;
        }

        private void Update()
        {
            blend = Mathf.MoveTowards(blend, Held ? 1f : 0f, Time.deltaTime / 0.22f);
            Apply();
        }

        private void Apply()
        {
            if (arm != null)
                arm.localRotation = Quaternion.Euler(Mathf.SmoothStep(0f, downAngle, blend), 0f, 0f);
            if (lampRenderer == null) return;
            Color c = Color.Lerp(idleColour, heldColour, blend);
            var block = new MaterialPropertyBlock();
            lampRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            block.SetColor(EmissionId, c * (2.2f * blend));
            lampRenderer.SetPropertyBlock(block);
        }
    }

    // The wheel somebody has to turn, one press at a time.
    //
    // AN INSTANT, RECORDED AS A STRETCHED LEVEL - the trick `Drawer` documents. A ghost advances by
    // elapsed time and can skip several recorded frames in one tick, so a one-frame pulse is a press
    // a past self will eventually step straight over. The pulse is held long enough to be sampled
    // whatever the frame rate, and the rising edge is what the ratchet acts on.
    public class RatchetCrank : GhostInteractable, IInteractHintTarget
    {
        public Ratchet ratchet;
        public float reach = 1.6f;
        public float pulseDuration = 0.15f;
        public Transform wheel;
        public Transform hintAnchor;

        public AudioSource audioSource;
        public AudioClip turnClip;
        public AudioClip refusedClip;

        private float pulseUntil;
        private float spin;

        public override bool PlayerSignal => Time.time < pulseUntil;

        public override void SetGhostSignal(GhostReplayer ghost, bool active)
        {
            // Rising edge only, and routed straight at the ratchet rather than through the player's
            // path - a ghost's turn must not be recorded again as if the player had made it.
            if (active) ratchet?.Advance();
        }

        public void ResetCrank()
        {
            pulseUntil = 0f;
            spin = 0f;
            if (wheel != null) wheel.localRotation = Quaternion.identity;
        }

        public bool WantsInteractHint =>
            Running && ratchet != null && !ratchet.Satisfied && PlayerInRange
            && PlayerLookup.InView(HintAnchor);

        public Transform HintAnchor => hintAnchor != null ? hintAnchor : transform;

        private bool Running => LoopManager.Instance == null || LoopManager.Instance.AcceptsInput;

        private bool PlayerInRange
        {
            get
            {
                Collider p = PlayerLookup.Collider;
                return p != null && p.enabled
                       && (p.bounds.center - transform.position).sqrMagnitude < reach * reach;
            }
        }

        private void Update()
        {
            if (Running && PlayerInRange && Input.GetKeyDown(KeyCode.E) && !PlayerLookup.InteractTaken)
            {
                // Claimed either way, because either way this press was answered here - a turn or a
                // refusal are both this fixture acting. What it must not do is claim a press it
                // ignores entirely, which is the deadlock ClaimInteract warns about.
                PlayerLookup.ClaimInteract();
                Turn();
            }

            // The wheel keeps the angle the count implies, so a wheel that unwinds is visibly
            // giving back rather than merely reading a smaller number.
            if (wheel == null || ratchet == null) return;
            spin = Mathf.Lerp(spin, ratchet.Notches * 40f, 1f - Mathf.Exp(-8f * Time.deltaTime));
            wheel.localRotation = Quaternion.Euler(0f, 0f, -spin);
        }

        private void Turn()
        {
            if (ratchet == null) return;

            if (!ratchet.PawlHeld)
            {
                // Refused, and said so. A press that does nothing and makes no sound reads as a
                // broken fixture rather than as an unmet condition.
                if (audioSource != null && refusedClip != null) audioSource.PlayOneShot(refusedClip);
                return;
            }

            pulseUntil = Time.time + pulseDuration;
            ratchet.Advance();
            if (audioSource != null && turnClip != null) audioSource.PlayOneShot(turnClip);
        }
    }
}
