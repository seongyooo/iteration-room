using System.Collections;
using UnityEngine;

namespace IterationRoom
{
    // The nightstand drawer. Press E in range to slide it open; it holds the balloon tool.
    //
    // The nightstand is built from primitives, carcass and drawer together, so this slides into a
    // real opening rather than off a solid face. SceneBuilder owns every number involved.
    public class Drawer : GhostInteractable, IInteractHintTarget
    {
        public Transform drawerBody;
        public Vector3 openLocalOffset = new Vector3(0f, 0f, -0.3f);
        public float openDuration = 0.45f;

        public AudioSource audioSource;
        public AudioClip openClip;
        // Shutting it again, when there is a clip for it. There is not today, so the open clip is
        // played back at a lower pitch instead - a drawer going in is the same runners in the same
        // carcass, which is exactly the case where pitching one clip is honest rather than lazy.
        public AudioClip closeClip;
        public float closePitch = 0.88f;

        // WHETHER E CAN ALSO SHUT IT. Off by default, and that default is the careful one.
        //
        // A closable drawer makes the press a TOGGLE, and a toggle replayed by several ghosts is
        // order-dependent in a way an idempotent Open() is not: two past selves that both recorded a
        // pull would open it and then shut it again, and anything inside gates on `IsFullyOpen`. That
        // is harmless for a drawer with nothing in it and a real hazard for the one holding the pin
        // Room2's whole accumulation runs on - so cycle 1's nightstand stays open-only and cycle 2's
        // chest, which is empty, opens and shuts.
        //
        // Turning it on for a drawer that HOLDS something means accepting that a ghost can close it
        // on another ghost's errand.
        public bool canClose;

        public bool IsOpen { get; private set; }

        // Set only once the slide has finished, and the pickup inside gates on it. Without the
        // delay, the single E press that opens the drawer can also be seen by the tool's own
        // Update on the same frame - script execution order decides whether opening a drawer
        // silently pockets what is in it, which reads as the item being unobtainable half the time.
        public bool IsFullyOpen { get; private set; }

        // Opening is an instant, but a ghost advances its timeline by elapsed time and can skip
        // several recorded frames in a single tick - a one-frame signal would eventually be missed.
        // Stretched over a handful of frames instead. Keeping it short means two deliberate pulls
        // still record as two.
        public float openPulseDuration = 0.15f;

        private Collider trigger;
        private bool playerInRange;
        private Vector3 closedLocalPos;
        private float openPulseUntil = -1f;

        public override bool PlayerSignal => Time.time < openPulseUntil;

        // Only while it is shut, UNLESS it can be shut again - then the press means something in both
        // states. Once an open drawer's contents are what matters, the press that counts is the one
        // on the object inside, so a takeable nearer than this wins the prompt and the press with it
        // (see PlayerLookup.TakeableIsNearer, added for the taps standing over their buckets).
        // On screen as well as in reach, like every other E fixture - see PlayerLookup.InView.
        // NEAREST-WINS, ACROSS DRAWERS AS WELL AS AGAINST TAKEABLES.
        //
        // A chest has three of these stacked 0.2m apart, all of them in range at once, and each one
        // used to answer the press purely on its own proximity - so which drawer opened came down to
        // script execution order, which is stable and arbitrary. Play reported it as "the top drawer
        // is always the one that gets picked". This is the same rule `ItemRegistry.NearestTakeable`
        // gives the carryables, for the same reason: the press and the disc must never disagree, and
        // the disc is drawn over the nearest anchor.
        public bool WantsInteractHint =>
            playerInRange && (canClose || !IsOpen)
            && NearestDrawer() == this
            && PlayerLookup.InView(HintAnchor)
            && !PlayerLookup.TakeableIsNearer(HintAnchor);
        public Transform HintAnchor => drawerBody != null ? drawerBody : transform;

        // Only the rising edge means anything: the fall is the recorded pulse expiring, not anyone
        // shutting the drawer. Routed straight to Open() rather than through RegisterPlayerOpen, so
        // a ghost's replayed pull is not re-recorded as if the player had done it. Route it back
        // through the player's path and every iteration inherits the last one's pulls, so the
        // drawer opens earlier and earlier until it opens by itself on frame one.
        public override void SetGhostSignal(GhostReplayer ghost, bool active)
        {
            if (!active) return;
            // What is reproduced is the PULL, not the resulting state - the honest reading of "record
            // the attempt, re-evaluate the condition" for a toggle, and the same choice `WaterTap`
            // makes. A past self reaches for the handle, and what that does depends on how it finds
            // the drawer.
            if (canClose) Toggle(); else Open();
        }

        private void Awake()
        {
            trigger = GetComponent<Collider>();
            if (drawerBody != null) closedLocalPos = drawerBody.localPosition;
        }

        // Range is polled rather than driven by OnTriggerEnter/Exit, for the reason documented on
        // FloorButton: the loop teleports the player by disabling and re-enabling
        // the CharacterController inside one frame, so the exit callback never arrives and
        // playerInRange would stay true for the rest of the run.
        // Every drawer in the scene, so a chest does not have to be told about its own siblings and a
        // second chest in another room cannot be dragged into the comparison by an anchor list built
        // at the wrong time. Registered on enable like the carryables are.
        private static readonly System.Collections.Generic.List<Drawer> all =
            new System.Collections.Generic.List<Drawer>();

        private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        private void OnDisable() => all.Remove(this);

        // The one whose anchor is nearest the EYE, which is what the prompt is measured against too.
        // Only drawers that are actually in reach are considered, so a nearer one two rooms away
        // cannot silence this one.
        private Drawer NearestDrawer()
        {
            Camera eye = PlayerLookup.Eye;
            if (eye == null) return this;

            Vector3 from = eye.transform.position;
            Drawer best = null;
            float bestSqr = float.MaxValue;
            foreach (Drawer d in all)
            {
                if (d == null || !d.playerInRange) continue;
                if (!(d.canClose || !d.IsOpen)) continue;
                Transform anchor = d.HintAnchor;
                if (anchor == null || !PlayerLookup.InView(anchor)) continue;

                float sqr = (anchor.position - from).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = d;
            }
            return best;
        }

        private void FixedUpdate()
        {
            Collider playerCollider = PlayerLookup.Collider;

            playerInRange = playerCollider != null
                && playerCollider.enabled
                && trigger != null
                && trigger.bounds.Intersects(playerCollider.bounds);
        }

        private void Update()
        {
            if (!playerInRange) return;
            if (IsOpen && !canClose) return;
            // The bed spawn is close enough to the nightstand that a key held down through the
            // wake-up would otherwise open this before the player can see the room.
            if (LoopManager.Instance != null && !LoopManager.Instance.AcceptsInput) return;

            if (!GameInput.InteractPressed) return;
            // Checked as well as claimed - see PlayerLookup.InteractTaken. Checking stops a second
            // fixture answering the same press; claiming stops PlayerHand reading it as "put down".
            if (PlayerLookup.InteractTaken) return;

            PlayerLookup.ClaimInteract();
            RegisterPlayerOpen();
        }

        // The player's own pull, and only theirs, raises the pulse PlayerRecorder samples.
        private void RegisterPlayerOpen()
        {
            openPulseUntil = Time.time + openPulseDuration;
            if (canClose) Toggle(); else Open();
        }

        public void Toggle()
        {
            if (IsOpen) Shut(); else Open();
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            StopAllCoroutines();
            StartCoroutine(Slide(closedLocalPos + openLocalOffset, fullyOpenAtEnd: true));

            if (audioSource != null && openClip != null)
            {
                audioSource.pitch = 1f;
                audioSource.PlayOneShot(openClip);
            }
        }

        // PUSHED SHUT BY SOMEBODY, which is a different event from `Close()` below. This one slides,
        // it makes a noise, and it happens while the player is watching; that one snaps and is silent
        // because it is the loop rewinding behind closed eyelids.
        //
        // `IsFullyOpen` drops on the FIRST frame of the slide rather than the last: whatever is in the
        // drawer stops being reachable the moment the front starts moving, which is the same
        // conservative edge `Slide` already applies at the opening end.
        public void Shut()
        {
            if (!IsOpen) return;
            IsOpen = false;
            IsFullyOpen = false;
            StopAllCoroutines();
            StartCoroutine(Slide(closedLocalPos, fullyOpenAtEnd: false));

            AudioClip clip = closeClip != null ? closeClip : openClip;
            if (audioSource != null && clip != null)
            {
                audioSource.pitch = closeClip != null ? 1f : closePitch;
                audioSource.PlayOneShot(clip);
            }
        }

        // Snapped shut by the loop at the top of every iteration, while the eyelids are still
        // closed. Deliberately silent, like Door.Close(): this is the loop rewinding world state
        // behind a black screen, not somebody shutting a drawer.
        public void Close()
        {
            StopAllCoroutines();
            IsOpen = false;
            IsFullyOpen = false;
            openPulseUntil = -1f;
            if (drawerBody != null) drawerBody.localPosition = closedLocalPos;
        }

        // Runs both ways now. `end` is where the front is going and `fullyOpenAtEnd` is what to say
        // about it on arrival - shutting has already set that false before the slide began.
        private IEnumerator Slide(Vector3 end, bool fullyOpenAtEnd)
        {
            if (drawerBody == null)
            {
                IsFullyOpen = fullyOpenAtEnd;
                yield break;
            }

            Vector3 start = drawerBody.localPosition;
            float t = 0f;
            while (t < openDuration)
            {
                t += Time.deltaTime;
                drawerBody.localPosition = Vector3.Lerp(start, end, t / openDuration);
                yield return null;
            }
            drawerBody.localPosition = end;
            IsFullyOpen = fullyOpenAtEnd;
        }
    }
}
