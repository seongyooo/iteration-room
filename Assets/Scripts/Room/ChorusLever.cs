using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // A lever that goes down when pulled and COMES BACK UP on its own, a few seconds later.
    //
    // That one property is the whole puzzle. A floor pad is held down by standing on it, so where the
    // player is decides everything and the answer is a matter of position - which is the lesson
    // room1-1 already teaches. A lever that returns makes the answer a matter of WHEN: three of them
    // have to be down at the same moment, and one person cannot be at three levers at once no matter
    // how they stand.
    //
    // So this is the first fixture in the game that a past self cannot help with merely by being
    // somewhere. It has to have pulled at the right time.
    //
    // A HOLD, RECORDED AS A LEVEL, exactly as CLAUDE.md §1.5 demands. What gets sampled is "this
    // lever is down", which is true for `holdSeconds` after a pull - comfortably longer than the
    // several recorded frames a ghost can skip in one tick. Recording the PULL as a one-frame pulse
    // would let a ghost step straight over it, which is the failure `Drawer` stretches its pulse to
    // avoid.
    public class ChorusLever : GhostInteractable, IInteractHintTarget
    {
        // How long it stays down. Long enough that three pulls can be made to overlap by a player
        // who plans, short enough that they cannot be made to overlap by a player who wanders.
        public float holdSeconds = 4f;

        // The arm, which swings about its own pivot. Down fast, back slowly - a lever that returns as
        // fast as it was pulled reads as a switch flicking rather than as something sprung.
        public Transform arm;
        public float downAngle = 62f;
        public float pullSeconds = 0.12f;
        public float returnSeconds = 0.55f;

        // Lit while down, so which levers are currently up is readable from across the room. Without
        // it the player is timing something they cannot see the state of.
        public Renderer lampRenderer;
        public Color upColour = new Color(0.42f, 0.45f, 0.52f);
        public Color downColour = new Color(1f, 0.62f, 0.15f);
        public float lampEmission = 2.4f;

        public AudioSource audioSource;
        public AudioClip pullClip;
        public AudioClip releaseClip;

        // Proximity, polled rather than trusted to trigger callbacks - the loop teleports the player
        // by disabling the controller inside one frame, so an exit callback can simply never arrive.
        public float reach = 1.6f;
        public Transform hintAnchor;

        // DOWN BECAUSE THE PLAYER PULLED IT, as a countdown rather than a flag, so the level the
        // recorder samples has a real duration.
        private float playerHeldUntil;
        private readonly HashSet<GhostReplayer> ghostsHolding = new HashSet<GhostReplayer>();

        private float blend;
        private bool wasDown;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public bool IsDown => Time.time < playerHeldUntil || ghostsHolding.Count > 0;

        // ONLY THE PLAYER'S OWN PULL. A ghost's hold must never feed back into what is being
        // recorded, or the signal compounds every iteration and a lever pulled once stays down for
        // the rest of the run.
        public override bool PlayerSignal => Time.time < playerHeldUntil;

        public override void SetGhostSignal(GhostReplayer ghost, bool active)
        {
            if (active) ghostsHolding.Add(ghost);
            else ghostsHolding.Remove(ghost);
        }

        public bool WantsInteractHint =>
            Running && !IsDown && PlayerInRange && PlayerLookup.InView(HintAnchor);

        public Transform HintAnchor => hintAnchor != null ? hintAnchor : transform;

        private bool Running => LoopManager.Instance == null || LoopManager.Instance.AcceptsInput;

        private bool PlayerInRange
        {
            get
            {
                Collider player = PlayerLookup.Collider;
                if (player == null || !player.enabled) return false;
                Vector3 d = player.bounds.center - transform.position;
                return d.sqrMagnitude < reach * reach;
            }
        }

        // The loop rewinding. A lever caught mid-swing at the boundary would come up in full view on
        // the first frame of the next iteration, which is the machinery of the loop showing through.
        public void ResetLever()
        {
            playerHeldUntil = 0f;
            ghostsHolding.Clear();
            blend = 0f;
            wasDown = false;
            Apply();
        }

        private void Update()
        {
            if (Running && !IsDown && PlayerInRange
                && Input.GetKeyDown(KeyCode.E) && !PlayerLookup.InteractTaken)
            {
                // Claimed only because it is being acted on. A fixture that claims a press it then
                // refuses is a deadlock - see PlayerLookup.ClaimInteract.
                PlayerLookup.ClaimInteract();
                Pull();
            }

            bool down = IsDown;
            float seconds = Mathf.Max(0.01f, down ? pullSeconds : returnSeconds);
            blend = Mathf.MoveTowards(blend, down ? 1f : 0f, Time.unscaledDeltaTime / seconds);
            Apply();

            if (down == wasDown) return;
            wasDown = down;
            // Only the edges make a sound, and only while the clock runs: the loop releases every
            // ghost during the reset, and a rack of levers letting go behind the closed eyelids is
            // the loop showing through rather than a sound the room makes.
            if (LoopManager.Instance != null && !LoopManager.Instance.IterationRunning) return;
            AudioClip clip = down ? pullClip : releaseClip;
            if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
        }

        private void Pull() => playerHeldUntil = Time.time + holdSeconds;

        private void Apply()
        {
            if (arm != null)
                arm.localRotation = Quaternion.Euler(Mathf.SmoothStep(0f, downAngle, blend), 0f, 0f);

            if (lampRenderer == null) return;
            Color c = Color.Lerp(upColour, downColour, blend);
            var block = new MaterialPropertyBlock();
            lampRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            block.SetColor(EmissionId, c * (lampEmission * blend));
            lampRenderer.SetPropertyBlock(block);
        }
    }
}
