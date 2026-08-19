using UnityEngine;

namespace IterationRoom
{
    // THE TREE IN THE CYCLE-2 HALL, and the first thing in this game that one pair of hands cannot
    // finish.
    //
    // NOTHING IS STORED. The notch is cut back to nothing at the top of every iteration and cut
    // again from scratch - `docs/decisions.md` *"accumulated work is RE-PERFORMED, never stored"*.
    // What accumulates is not damage to the tree but the number of past selves swinging at it inside
    // one sixty seconds: iteration N has N of them, so the tree falls on the iteration that first
    // musters enough axes, and on every iteration after, because the ghosts always redo it.
    //
    // That is why there is no persistence here to go looking for, and why there must not be. A tree
    // that remembered being 40% cut would be the first piece of un-rewound state in the project, and
    // ghost possession was built precisely so that would never be needed.
    //
    // A CHOP IS AN EVENT, NOT A LEVEL (CLAUDE.md §1.5). The recorded signal is a stretched pulse on
    // the swing and the notch deepens on the RISING EDGE only, so a ghost that skips several recorded
    // frames in one tick still lands exactly one chop rather than none or twelve.
    //
    // A TOOL-SHAPED ACTION REQUIRES THE TOOL, for ghosts as for the player (CLAUDE.md §4). Both ends
    // check possession live - `PlayerHand.Holding` here, `GhostReplayer.HoldingEquipped` at replay -
    // so taking an axe out of a past self's hands stops that past self chopping from that moment on.
    // Never cache the answer.
    [RequireComponent(typeof(Collider))]
    // NOT an `IInteractHintTarget`, and that is deliberate rather than an omission. The E prompt list
    // is gathered from that interface (`CycleBinding.RebindHints`), so implementing it put an E disc
    // on the tree - and E does nothing here. Chopping is a LEFT CLICK, so the tree asks for the swing
    // disc instead (`WantsSwingHint`, read by ControlHintDisplay), which is the mark that matches the
    // button. `CanChop` below is the internal gate the two prompts and the press all share.
    public class TreeTrunk : GhostInteractable
    {
        // The wire value, matching SceneBuilder's CycleTwoToolItemId. Five axes share it, which is
        // what makes five simultaneous choppers possible at all - see ItemRegistry on an id naming a
        // SUPPLY rather than an object.
        public string axeItemId = "Tool2";

        // How many swings fell it. This is the ONLY difficulty knob, and it is deliberately more than
        // one player can land alone inside sixty seconds - that is the whole puzzle.
        public int chopsToFell = 40;

        // THE NOTCH IS GEOMETRY, NOT A DECAL, and the trunk has THREE states rather than two.
        //
        // `wholeTrunk` - untouched, and it is one unbroken mesh. Shown until the first chop lands.
        // `notchStages` - the same unbroken trunk with a real triangular wedge cut out of it, one
        //   per step, deepening. Still ONE piece: a tree being cut is not a tree in two halves.
        // `felledLower` / `felledUpper` - the severed pair, switched on at the moment it goes over
        //   and not one frame before. They are the only ones that show a seam, which they earn.
        //
        // The middle state used to be built from the split halves, which shipped the tree looking
        // already felled before anybody had swung at it - the seam was visible at zero chops.
        public GameObject[] wholeTrunk;
        public GameObject[] notchStages;
        public GameObject felledLower;
        public GameObject felledUpper;

        // The part that goes over. Everything above the cut hangs off `fallPivot`, which sits AT the
        // cut - so the fall is a rotation about the stump's own top rather than the whole tree
        // sliding sideways.
        public Transform fallPivot;
        public Vector3 fallEuler = new Vector3(0f, 0f, 90f);
        // AND IT COMES DOWN AS WELL AS OVER. A tree cut at 1.2m does not stay 1.2m up - the butt
        // slips off the stump as it goes. Without this the felled log's walking surface sits at 1.9m,
        // which the player's 0.9m jump cannot reach: a wall rather than a bridge.
        public float fallDrop = 1.2f;
        public float fallDuration = 2.4f;

        // The bridge, and everything on the felled tree that takes weight with it. Both off until it
        // is down: a walkable surface floating over a fatal drop is the one bug this room must not
        // ship with, and a mesh collider sweeping through the player mid-fall is the shove §1.7
        // keeps ghosts from delivering.
        public GameObject bridgeSurface;
        public Collider[] branchColliders;

        public Transform hintAnchor;

        public AudioSource audioSource;
        public AudioClip chopClip;
        public AudioClip fallClip;

        // Stretched over a handful of frames, like Drawer.openPulseDuration and LightSwitch's - a
        // ghost advances by elapsed time and would skip a one-frame pulse outright.
        public float chopPulseDuration = 0.2f;

        // One swing takes a moment. Without this a held button would file forty chops in forty frames
        // and the accumulation the room is built on would never be felt.
        public float chopCooldown = 0.5f;

        public bool HasFallen { get; private set; }
        public int Chops { get; private set; }

        private Collider trigger;
        private PlayerHand hand;
        private bool playerInRange;
        private float pulseUntil = -1f;
        private float nextChopAllowed;
        private Quaternion standingRotation;
        private Vector3 standingPosition;

        public override bool PlayerSignal => Time.time < pulseUntil;

        // Whether a swing would actually land: the tree still standing, an axe in the hand, and the
        // trunk on screen. Everything - the press, the prompt - goes through this one answer.
        // NO AIM ARBITRATION, and its absence is deliberate. `PlayerLookup.IsAimedAt` settles which
        // thing an E PRESS is for, because every E fixture answers the same button. Chopping answers
        // the LEFT button, so it competes with nothing - and an earlier version that did consult the
        // E arbitration let the four axes lying on the floor take the tree's prompt, which is why no
        // overlay appeared.
        public bool CanChop =>
            playerInRange && !HasFallen
            && hand != null && hand.Holding(axeItemId)
            && PlayerLookup.InView(HintAnchor);

        public Transform HintAnchor => hintAnchor != null ? hintAnchor : transform;

        // THE LEFT-CLICK DISC, which is a different prompt from the E one and asks a different
        // question. `CanChop` above is what makes the swing legal; this is what puts the
        // mark on screen, and it retires once the player has clearly learned the control - the same
        // shape BalloonTool.WantsSwingHint has, and for the same reason: a prompt that never goes
        // away stops being an instruction and becomes furniture.
        public int hintForFirstChops = 6;
        public bool WantsSwingHint => CanChop && Chops < hintForFirstChops;

        // The ghost's own hand is the gate, checked at the moment of the swing rather than trusted
        // from the recording - CLAUDE.md §1.3, and the same shape as GhostReplayer.requirePopTool.
        public override void SetGhostSignal(GhostReplayer ghost, bool active)
        {
            if (!active) return;
            if (ghost == null || !ghost.HoldingEquipped(axeItemId)) return;
            // The past self's arm moves too. Asked of the ghost for the same reason the player's is
            // asked of the hand: whoever poses the carried object animates it.
            ghost.Swing();
            Chop();
        }

        private void Awake()
        {
            trigger = GetComponent<Collider>();
            if (fallPivot != null)
            {
                standingRotation = fallPivot.localRotation;
                standingPosition = fallPivot.localPosition;
            }
            ApplyNotch();
            SetCrossable(false);
        }

        private void Start()
        {
            hand = PlayerLookup.Hand;
        }

        private void FixedUpdate()
        {
            playerInRange = PlayerLookup.InReach(trigger);
        }

        // LEFT CLICK, NOT E. Swinging an axe is "do the thing this object is FOR", which is what
        // `UsePressed` means everywhere else in this game - the balloon tool swings on it and the
        // chess placer places on it. E stays what E is: pick the axe up, put the axe down.
        //
        // AND THAT REMOVES A WHOLE CLASS OF ARBITRATION. Nothing here claims the interact press or
        // marks the hand, because it is not taking one: E over the tree with an axe in hand now
        // reaches `PlayerHand` and puts the axe down, which is the right answer and was impossible
        // without walking away first.
        private void Update()
        {
            if (!CanChop) return;
            if (LoopManager.Instance != null && !LoopManager.Instance.AcceptsInput) return;
            if (Time.time < nextChopAllowed) return;

            if (!GameInput.UsePressed) return;

            pulseUntil = Time.time + chopPulseDuration;
            nextChopAllowed = Time.time + chopCooldown;
            // The axe moves. Asked of the HAND rather than done here - see PlayerHand.Swing for why
            // the thing that owns the held object's pose is the thing that animates it.
            hand.Swing();
            Chop();
        }

        // The one place a swing lands, whoever swung. Ghosts arrive through SetGhostSignal and the
        // living player through Update, and neither gets a path the other does not have.
        private void Chop()
        {
            if (HasFallen) return;

            Chops++;
            ApplyNotch();
            Play(chopClip);

            if (Chops >= chopsToFell) Fell();
        }

        // Which of the three states is showing. The stage index rises with the chop count, so the
        // wedge deepens by whole steps - eight across forty swings, so five swings buy a visible bite.
        private void ApplyNotch()
        {
            int stages = notchStages != null ? notchStages.Length : 0;

            // -1 is "no notch at all", which is the untouched tree rather than a very shallow cut.
            int show = HasFallen || Chops <= 0 || chopsToFell <= 0 || stages == 0
                ? -1
                : Mathf.Min(stages - 1, Chops * stages / chopsToFell);

            if (wholeTrunk != null)
                foreach (GameObject go in wholeTrunk)
                    if (go != null) go.SetActive(show < 0 && !HasFallen);

            for (int i = 0; i < stages; i++)
                if (notchStages[i] != null) notchStages[i].SetActive(i == show);

            // The severed pair replaces both of the above the instant it is down, and never appears
            // while the tree is standing - a visible seam is the one thing that gives the puzzle away.
            if (felledLower != null) felledLower.SetActive(HasFallen);
            if (felledUpper != null) felledUpper.SetActive(HasFallen);
        }

        private void Fell()
        {
            HasFallen = true;
            // The swap to the severed pair happens HERE, on the frame it lets go - so the trunk comes
            // apart as it starts to move rather than at some point during the swing.
            ApplyNotch();
            Play(fallClip);
            StopAllCoroutines();
            StartCoroutine(Falling());
        }

        private System.Collections.IEnumerator Falling()
        {
            if (fallPivot == null) yield break;

            Quaternion start = standingRotation;
            Quaternion end = Quaternion.Euler(fallEuler) * standingRotation;
            Vector3 down = standingPosition + Vector3.down * fallDrop;

            for (float e = 0f; e < fallDuration; e += Time.deltaTime)
            {
                // Eased IN, not out: a tree lets go slowly and arrives fast, and a linear sweep reads
                // as a door opening rather than as something giving way.
                float k = Mathf.Clamp01(e / fallDuration);
                float eased = k * k;
                fallPivot.localRotation = Quaternion.Slerp(start, end, eased);
                fallPivot.localPosition = Vector3.Lerp(standingPosition, down, eased);
                yield return null;
            }

            fallPivot.localRotation = end;
            fallPivot.localPosition = down;
            // AFTER the swing, never during it.
            SetCrossable(true);
        }

        private void SetCrossable(bool on)
        {
            if (bridgeSurface != null) bridgeSurface.SetActive(on);
            if (branchColliders == null) return;
            foreach (Collider c in branchColliders)
                if (c != null) c.enabled = on;
        }

        // The loop rewinding, called from TreeFelled.ResetCondition with the other room resets and
        // AFTER the item sweep. Silent and instant, like Door.Close() - this happens behind shut
        // eyelids, not as a tree standing itself back up in front of anybody.
        public void ResetTree()
        {
            StopAllCoroutines();
            HasFallen = false;
            Chops = 0;
            pulseUntil = -1f;
            nextChopAllowed = 0f;
            if (fallPivot != null)
            {
                fallPivot.localRotation = standingRotation;
                fallPivot.localPosition = standingPosition;
            }
            SetCrossable(false);
            ApplyNotch();
        }

        private void Play(AudioClip clip)
        {
            if (audioSource == null || clip == null) return;
            // Scattered, so five axes landing at once are not audibly one recording played five
            // times - which is exactly what this room puts on screen.
            audioSource.pitch = Random.Range(0.92f, 1.09f);
            audioSource.PlayOneShot(clip);
        }
    }
}
