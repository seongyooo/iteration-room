using UnityEngine;

namespace IterationRoom
{
    // Swinging the tool that came out of the nightstand drawer. Lives on the player.
    //
    // Not a GhostInteractable: a signal is a level sampled into one bit, and there are far more
    // balloons than bits. Pops go into the timeline as events carrying the balloon's id instead
    // (RecordedTimeline), which is what lets a ghost burst exactly the balloons the player did.
    public class BalloonTool : MonoBehaviour
    {
        public Camera playerCamera;
        public PlayerHand hand;
        public PlayerRecorder recorder;
        public string requiredItemId = "Tool";

        // Deliberately generous, and an overlap sphere rather than a raycast: in a room packed with
        // balloons, having to line one up in the crosshair turns a physical act into a shooting
        // gallery. Swinging at a cluster should burst something.
        public float reach = 1.15f;
        public float grabRadius = 0.5f;

        public float swingDuration = 0.24f;
        public float swingAngle = 62f;

        public AudioSource audioSource;
        public AudioClip swingClip;

        // Whether the player has ever burst a balloon. Set by the POP, not by the click, and never
        // cleared - not at the loop boundary either. Its only job is retiring the left-click prompt,
        // and a control the player has actually used does not need teaching again sixty seconds
        // later. A ghost's pops do not count: they go through GhostReplayer, and watching a past
        // self do it is not the same as having done it.
        public bool HasPopped { get; private set; }

        // Whether the left-click prompt wants showing at all. Lives here rather than in
        // ControlHintDisplay because it is the same condition Update gates the click on - a prompt
        // for a click that would be ignored teaches the wrong thing.
        public bool WantsSwingHint =>
            !HasPopped && hand != null && hand.Holding(requiredItemId)
            && (LoopManager.Instance == null || LoopManager.Instance.AcceptsInput);

        private float swingStarted = -99f;
        private Quaternion restRotation;
        private CarryableItem animating;

        // Reused, because FindTarget is now called every frame by the prompt as well as on click, and
        // the allocating Physics.OverlapSphere would have made that a per-frame garbage source.
        //
        // 32 has to be a CEILING, not a guess: OverlapSphereNonAlloc fills the buffer and silently
        // stops, so an undersized one would drop balloons from the search and could drop the nearest.
        // Room2's field is 70 balloons carrying one collider each, and as built no two sit within the
        // 0.5m grabRadius of one another - so the number only has to cover a cluster physics and the
        // player have pushed together, plus the room's own walls and floor. 32 is well past that.
        private readonly Collider[] overlap = new Collider[32];

        private void Update()
        {
            if (hand == null || playerCamera == null) return;
            if (LoopManager.Instance != null && !LoopManager.Instance.AcceptsInput) return;
            // Holding, not Has: with Tab in the game you can be carrying the pin and holding the
            // key, and swinging the key at a balloon should do nothing. This is also what makes the
            // recorded pop honest - it can only exist if the pin was in the hand that made it.
            if (!hand.Holding(requiredItemId)) return;

            if (GameInput.UsePressed) Swing();
            AnimateHeld();
        }

        // The balloon a swing from here would burst: the one nearest the centre of the swing sphere.
        // Public and shared with the prompt on purpose - the prompt hangs on this balloon, so if the
        // two ever disagreed the game would be pointing at one balloon and popping another.
        public Balloon FindTarget()
        {
            if (playerCamera == null) return null;

            Vector3 centre = playerCamera.transform.position + playerCamera.transform.forward * reach;
            int hits = Physics.OverlapSphereNonAlloc(centre, grabRadius, overlap);

            Balloon best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < hits; i++)
            {
                Balloon b = overlap[i].GetComponentInParent<Balloon>();
                if (b == null || b.IsPopped) continue;

                float sqr = (b.transform.position - centre).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = b;
            }

            return best;
        }

        private void Swing()
        {
            swingStarted = Time.time;
            if (audioSource != null && swingClip != null) audioSource.PlayOneShot(swingClip);

            Balloon best = FindTarget();
            if (best == null) return;

            // Only a swing that actually burst something retires the prompt. A miss is exactly the
            // case where the player still needs telling.
            HasPopped = true;

            int id = best.id;
            if (BalloonField.Instance != null) BalloonField.Instance.Pop(best);
            // Recorded after the pop, and by id: the ghost re-pops this same balloon next iteration
            // wherever physics has left it.
            recorder?.RecordPop(id);
        }

        // Drives the held item directly rather than through an Animator - it is one bone and one
        // arc, and CarryableItem already parks the rest pose on the hand anchor.
        private void AnimateHeld()
        {
            CarryableItem held = hand.Held;
            if (held == null) return;

            if (animating != held)
            {
                animating = held;
                restRotation = Quaternion.Euler(held.handLocalEuler);
            }

            float t = (Time.time - swingStarted) / swingDuration;
            if (t < 0f || t > 1f)
            {
                held.transform.localRotation = restRotation;
                return;
            }

            // Out fast, back slow: a chop, not a metronome.
            float arc = Mathf.Sin(Mathf.Pow(t, 0.55f) * Mathf.PI);
            held.transform.localRotation = restRotation * Quaternion.Euler(-swingAngle * arc, 0f, 0f);
        }
    }
}
