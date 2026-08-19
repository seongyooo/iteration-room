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
        // states.
        //
        // ELIGIBILITY ONLY, and that is the whole of what changed here on 2026-08-17. This used to
        // end in `NearestDrawer() == this`, a private nearest-wins between the bays of a chest, and
        // it answered the right question with the wrong measure: NEAREST, so the top bay won from
        // every standing position and the bottom one could not be pulled at all.
        //
        // It is worse than redundant under the shared arbiter (`PlayerLookup.AimedAnchor`): a drawer
        // that says it does not want the press is not a CANDIDATE, so the lower bay would have been
        // hidden from the scan that was supposed to be able to choose it. A fixture states what it
        // could do; which one the press is for is decided in one place, for everything at once.
        public bool WantsInteractHint =>
            playerInRange && (canClose || !IsOpen)
            && PlayerLookup.InView(HintAnchor);
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
        private void FixedUpdate()
        {
            playerInRange = PlayerLookup.InReach(trigger);
        }

        private void Update()
        {
            // The bed spawn is close enough to the nightstand that a key held down through the
            // wake-up would otherwise open this before the player can see the room.
            if (LoopManager.Instance != null && !LoopManager.Instance.AcceptsInput) return;

            // IN REACH, ON SCREEN, PULLABLE, AND THE THING BEING LOOKED AT - all four in one call.
            // The first three were spelled out here as `playerInRange` and the open/close test, which
            // is `WantsInteractHint` MINUS its `InView`: this drawer could be pulled with your back
            // to it or through the wall behind it, and had been able to since it was built.
            if (!PlayerLookup.PressGoesTo(this)) return;

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
