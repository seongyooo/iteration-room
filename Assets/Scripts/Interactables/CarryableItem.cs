using UnityEngine;

namespace IterationRoom
{
    // Something the player can pick up with E: the balloon tool in the nightstand drawer, and the
    // key a balloon gives up.
    //
    // Range is polled rather than driven by trigger callbacks, for the reason documented on
    // FloorButton - the loop's teleport disables and re-enables the CharacterController inside one
    // frame, so an exit callback never arrives and the item would stay grabbable from across the room.
    [RequireComponent(typeof(Collider))]
    public class CarryableItem : MonoBehaviour
    {
        // What this counts as once carried. PlayerHand keys on it and KeyLock asks for "Key".
        public string itemId = "Tool";

        // What the HUD calls it. Separate from itemId because the id is a wire value other scripts
        // match on (KeyLock asks for "Key") and renaming it for the player's benefit would break them.
        public string displayName = "TOOL";

        // True for the tool, which is shown in the hand. The key is pocketed: picking it up must
        // not knock the tool out of view, since you need the tool to have got the key at all.
        public bool showInHand = true;

        // Optional gate. The tool sits inside the drawer, so it cannot be taken through a shut one.
        public Drawer requiresOpenDrawer;

        public Vector3 handLocalPosition = new Vector3(0.28f, -0.24f, 0.42f);
        public Vector3 handLocalEuler = new Vector3(12f, -8f, 18f);

        public AudioSource audioSource;
        public AudioClip pickupClip;

        public bool IsCarried { get; private set; }

        private PlayerHand hand;
        private Collider trigger;
        private Collider playerCollider;
        private bool playerInRange;

        private Transform originParent;
        private Vector3 originLocalPosition;
        private Quaternion originLocalRotation;

        private void Awake()
        {
            trigger = GetComponent<Collider>();
            originParent = transform.parent;
            originLocalPosition = transform.localPosition;
            originLocalRotation = transform.localRotation;
        }

        private void FixedUpdate()
        {
            if (IsCarried) return;

            if (playerCollider == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    playerCollider = player.GetComponent<Collider>();
                    hand = player.GetComponent<PlayerHand>();
                }
            }

            playerInRange = playerCollider != null
                && playerCollider.enabled
                && trigger != null
                && trigger.bounds.Intersects(playerCollider.bounds);
        }

        private void Update()
        {
            if (IsCarried || !playerInRange || hand == null) return;
            if (LoopManager.Instance != null && !LoopManager.Instance.IterationRunning) return;

            // IsFullyOpen, not IsOpen, so the same E press cannot both open the drawer and empty it.
            if (requiresOpenDrawer != null && !requiresOpenDrawer.IsFullyOpen) return;

            if (Input.GetKeyDown(KeyCode.E)) hand.Take(this);
        }

        public void AttachTo(Transform anchor)
        {
            IsCarried = true;
            if (trigger != null) trigger.enabled = false;

            transform.SetParent(anchor, false);
            transform.localPosition = handLocalPosition;
            transform.localRotation = Quaternion.Euler(handLocalEuler);
            SetVisible(true);
            PlayPickup();
        }

        // Carried but not shown - the key goes straight in a pocket.
        public void Pocket()
        {
            IsCarried = true;
            if (trigger != null) trigger.enabled = false;
            SetVisible(false);
            PlayPickup();
        }

        public void ReturnToOrigin()
        {
            IsCarried = false;
            if (trigger != null) trigger.enabled = true;

            transform.SetParent(originParent, false);
            transform.localPosition = originLocalPosition;
            transform.localRotation = originLocalRotation;
            SetVisible(true);
        }

        // Out of play until a balloon gives it up. Not SetActive(false): Awake has to have run for
        // originParent to be captured, and an inactive object never gets there.
        public void Hide()
        {
            SetVisible(false);
            if (trigger != null) trigger.enabled = false;
        }

        public void RevealAt(Vector3 worldPosition)
        {
            if (IsCarried) return;
            transform.position = worldPosition;
            SetVisible(true);
            if (trigger != null) trigger.enabled = true;
        }

        private void SetVisible(bool visible)
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                r.enabled = visible;
        }

        private void PlayPickup()
        {
            if (audioSource != null && pickupClip != null) audioSource.PlayOneShot(pickupClip);
        }
    }
}
