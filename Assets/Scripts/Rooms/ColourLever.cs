using UnityEngine;

namespace IterationRoom
{
    // WHICH SET OF PANELS A LEVER DRIVES. Three colours, fixed pairings, and the pairing is the
    // puzzle's whole vocabulary - room3-2S is red, room3-2E is blue, room3-2W is yellow, and a lever
    // never touches a colour that is not its own.
    public enum PanelColour
    {
        Red,
        Blue,
        Yellow,
    }

    // A LEVER THAT IS HELD, NOT SWITCHED. Pull it and every panel of its colour comes out of the wall
    // in room3-2N; let go and they all go back, immediately.
    //
    // **THIS IS THE FIRST HELD INTERACTION IN THE GAME, and it is what makes the room a room.** Every
    // other E fixture in the building is a press: a drawer opens, a key goes in a lock, a cube seats.
    // A press can be made by the person who benefits from it. A HOLD cannot - the lever is in one room
    // and the stairs it raises are in another, so whoever is holding it is not the one climbing. One
    // person cannot solve any part of this room. That is `FloorButton`'s trick (Room3's two pads) with
    // a hand instead of a foot, and with three of them.
    //
    // **THE GRAB IS A PRESS; THE HOLD IS A STATE.** CLAUDE.md SS1.2 requires every fixture that answers
    // E to go through `PlayerLookup.PressGoesTo` on the rising edge, so two fixtures cannot both take
    // one press. That still happens here, exactly once, when the lever is taken hold of. After that
    // the lever asks only whether the key is still down and whether the player is still near - it does
    // NOT re-enter the arbitration every frame, because it is no longer competing for anything.
    public class ColourLever : GhostInteractable, IInteractHintTarget
    {
        public PanelColour colour;

        // What the hand is on. Rotated between two poses so the lever reads as pulled rather than
        // merely lit - see `Apply`.
        public Transform handle;
        public float restAngle = -25f;
        public float pulledAngle = 35f;
        // Fast enough to feel like a mechanism and not an animation. The PANELS are what the player is
        // watching on a monitor in another room, and those are near-instant; this is just the hand.
        public float throwDuration = 0.12f;

        public Renderer indicator;
        public Color offColour = new Color(0.25f, 0.25f, 0.27f);
        public Color onColour = Color.red;

        public AudioSource audioSource;
        public AudioClip pullClip;
        public AudioClip releaseClip;

        // How far the player can drift before the lever is pulled out of their hand. Generous: letting
        // go by accident while watching a monitor across the room is not a puzzle, it is a slip.
        public float holdRange = 2.2f;
        public Transform hintAnchor;

        public bool PlayerHolding { get; private set; }

        // A HOLD RECORDS THE LEVEL DIRECTLY, exactly as `FloorButton` does: the ghost keeps hold for
        // precisely as long as the player did, which is the whole mechanic. Nothing about this is an
        // instant, so nothing about it is a `CarryEvent` (CLAUDE.md SS1.5).
        public override bool PlayerSignal => PlayerHolding;

        private readonly System.Collections.Generic.HashSet<GhostReplayer> ghostsHolding
            = new System.Collections.Generic.HashSet<GhostReplayer>();

        // **THE PANELS ASK THIS, RATHER THAN THE LEVER PUSHING THEM.** A lever that drove a list would
        // need that list wired at build time and kept in step as panels are added or moved, and the
        // spec says the layout is going to be retuned by play. A panel knowing its own colour and
        // asking "is my colour up" is one fact per panel and no wiring at all.
        public bool IsActive => PlayerHolding || ghostsHolding.Count > 0;

        private static readonly System.Collections.Generic.List<ColourLever> all
            = new System.Collections.Generic.List<ColourLever>();

        private void OnEnable() => all.Add(this);
        private void OnDisable() => all.Remove(this);

        // Any lever of this colour being held raises the colour. There is only one of each today; the
        // question is asked this way so a second lever for the same colour - a thing the puzzle may
        // well want - needs no change anywhere else.
        public static bool ColourActive(PanelColour colour)
        {
            foreach (ColourLever lever in all)
                if (lever != null && lever.colour == colour && lever.IsActive) return true;
            return false;
        }

        private float thrown;
        private bool wasActive;

        public Transform HintAnchor => hintAnchor != null ? hintAnchor : transform;

        // ELIGIBILITY ONLY, and it must never consult the arbitration - the aim scan polls this of
        // every fixture, so a fixture that asked back would recurse (CLAUDE.md SS1.2).
        public bool WantsInteractHint =>
            !PlayerHolding
            && LoopManager.Instance != null && LoopManager.Instance.AcceptsInput
            && WithinReach()
            && PlayerLookup.InView(HintAnchor);

        // Measured off the player's COLLIDER rather than a transform, because that is the handle
        // `PlayerLookup` actually exposes - and it is the same one `FloorButton` measures its pad
        // against, so "near enough" means the same thing in both rooms.
        private bool WithinReach()
        {
            Collider player = PlayerLookup.Collider;
            if (player == null || !player.enabled) return false;
            return (player.bounds.center - HintAnchor.position).sqrMagnitude < holdRange * holdRange;
        }

        private void Update()
        {
            bool accepting = LoopManager.Instance == null || LoopManager.Instance.AcceptsInput;

            if (PlayerHolding)
            {
                // Three ways to lose it, and all three are the same rule: the hand is no longer on the
                // lever. Letting the loop take it is what stops a hold surviving a teleport to the bed.
                if (!accepting || !GameInput.InteractHeld || !WithinReach()) SetHolding(false);
            }
            else if (accepting && GameInput.InteractPressed && PlayerLookup.PressGoesTo(this))
            {
                // The one moment this competes with anything. Claimed, so `PlayerHand.LateUpdate` does
                // not read the same press as "put down" and throw whatever is held on the floor.
                PlayerLookup.ClaimInteract();
                SetHolding(true);
            }

            Apply();
        }

        private void SetHolding(bool holding)
        {
            if (PlayerHolding == holding) return;
            PlayerHolding = holding;
            Announce();
        }

        public override void SetGhostSignal(GhostReplayer ghost, bool active)
        {
            if (active) ghostsHolding.Add(ghost);
            else ghostsHolding.Remove(ghost);
            Announce();
        }

        // The loop's rewind. A lever left thrown is world state the loop forgot, and because ghosts
        // are released before this runs, letting it fall back on its own would be heard.
        public void ResetLever()
        {
            PlayerHolding = false;
            ghostsHolding.Clear();
            thrown = 0f;
            wasActive = false;
            Apply();
        }

        private void Announce()
        {
            bool active = IsActive;
            if (active == wasActive) return;
            wasActive = active;

            // Silent unless the clock is running, for the reason `FloorButton.PlayStateChange` gives:
            // the loop releases every ghost during the reset, and a rack of levers letting go behind
            // the closed eyelids is the machinery of the loop showing through.
            if (audioSource == null) return;
            if (LoopManager.Instance != null && !LoopManager.Instance.IterationRunning) return;

            AudioClip clip = active ? pullClip : releaseClip;
            if (clip != null) audioSource.PlayOneShot(clip);
        }

        private void Apply()
        {
            bool active = IsActive;

            float step = throwDuration > 0f ? Time.deltaTime / throwDuration : 1f;
            thrown = Mathf.MoveTowards(thrown, active ? 1f : 0f, step);

            if (handle != null)
                handle.localRotation = Quaternion.Euler(Mathf.Lerp(restAngle, pulledAngle, thrown), 0f, 0f);

            if (indicator != null)
                indicator.material.color = active ? onColour : offColour;
        }
    }
}
