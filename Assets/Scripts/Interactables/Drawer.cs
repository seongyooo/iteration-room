using System.Collections;
using UnityEngine;

namespace IterationRoom
{
    // The nightstand drawer. Press E in range to slide it open; it holds the balloon tool.
    //
    // The drawer is generated geometry rather than a node on the model: nightstand.glb bakes its
    // whole body into a single mesh (Nightstand_Nightstand_0), so there is nothing in it to pull
    // out. SceneBuilder sizes this box from the model's own measured front face instead.
    public class Drawer : GhostInteractable, IInteractHintTarget
    {
        public Transform drawerBody;
        public Vector3 openLocalOffset = new Vector3(0f, 0f, -0.3f);
        public float openDuration = 0.45f;

        public AudioSource audioSource;
        public AudioClip openClip;

        public bool IsOpen { get; private set; }

        // Set only once the slide has finished, and the pickup inside gates on it. Without the
        // delay, the single E press that opens the drawer can also be seen by the tool's own
        // Update on the same frame - script execution order decides whether opening a drawer
        // silently pockets what is in it, which reads as the item being unobtainable half the time.
        public bool IsFullyOpen { get; private set; }

        // Opening is an instant, but a ghost advances its timeline by elapsed time and can skip
        // several recorded frames in a single tick - a one-frame signal would eventually be missed.
        // Stretched over a handful of frames instead, exactly as DoorButton does.
        public float openPulseDuration = 0.15f;

        private Collider trigger;
        private Collider playerCollider;
        private bool playerInRange;
        private Vector3 closedLocalPos;
        private float openPulseUntil = -1f;

        public override bool PlayerSignal => Time.time < openPulseUntil;

        // Only while it is shut. Once it is open the press that matters is the one on the tool
        // inside, and that carries its own prompt.
        public bool WantsInteractHint => playerInRange && !IsOpen;
        public Transform HintAnchor => drawerBody != null ? drawerBody : transform;

        // Only the rising edge means anything: the fall is the recorded pulse expiring, not anyone
        // shutting the drawer. Routed straight to Open() rather than through RegisterPlayerOpen, so
        // a ghost's replayed pull is not re-recorded as if the player had done it - see DoorButton
        // for what that feedback loop does over a few iterations.
        public override void SetGhostSignal(GhostReplayer ghost, bool active)
        {
            if (active) Open();
        }

        private void Awake()
        {
            trigger = GetComponent<Collider>();
            if (drawerBody != null) closedLocalPos = drawerBody.localPosition;
        }

        // Range is polled rather than driven by OnTriggerEnter/Exit, for the reason documented on
        // FloorButton and DoorButton: the loop teleports the player by disabling and re-enabling
        // the CharacterController inside one frame, so the exit callback never arrives and
        // playerInRange would stay true for the rest of the run.
        private void FixedUpdate()
        {
            if (playerCollider == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) playerCollider = player.GetComponent<Collider>();
            }

            playerInRange = playerCollider != null
                && playerCollider.enabled
                && trigger != null
                && trigger.bounds.Intersects(playerCollider.bounds);
        }

        private void Update()
        {
            if (!playerInRange || IsOpen) return;
            // The bed spawn is close enough to the nightstand that a key held down through the
            // wake-up would otherwise open this before the player can see the room.
            if (LoopManager.Instance != null && !LoopManager.Instance.AcceptsInput) return;

            if (Input.GetKeyDown(KeyCode.E)) RegisterPlayerOpen();
        }

        // The player's own pull, and only theirs, raises the pulse PlayerRecorder samples.
        private void RegisterPlayerOpen()
        {
            openPulseUntil = Time.time + openPulseDuration;
            Open();
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            StopAllCoroutines();
            StartCoroutine(Slide());

            if (audioSource != null && openClip != null) audioSource.PlayOneShot(openClip);
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

        private IEnumerator Slide()
        {
            if (drawerBody == null)
            {
                IsFullyOpen = true;
                yield break;
            }

            Vector3 start = drawerBody.localPosition;
            Vector3 end = closedLocalPos + openLocalOffset;
            float t = 0f;
            while (t < openDuration)
            {
                t += Time.deltaTime;
                drawerBody.localPosition = Vector3.Lerp(start, end, t / openDuration);
                yield return null;
            }
            drawerBody.localPosition = end;
            IsFullyOpen = true;
        }
    }
}
