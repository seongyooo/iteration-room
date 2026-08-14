using System.Collections;
using UnityEngine;

namespace IterationRoom
{
    // Press E in range to turn the tap: it twists a quarter turn and the water stream shows or
    // hides with it. Range is polled rather than driven by trigger callbacks - see FloorButton for
    // why. Lives on its own trigger object rather than on the imported tap mesh, since the mesh's
    // wrapper carries the model's own baked import rotation and is not a frame worth reasoning about.
    //
    // Not yet a GhostInteractable - this is a first pass proving the visual and the toggle, not
    // wired into the loop/ghost system.
    [RequireComponent(typeof(Collider))]
    public class WaterTap : MonoBehaviour, IInteractHintTarget
    {
        public Transform tapVisual;
        public GameObject waterStream;
        public float turnAngle = 45f;
        public float turnDuration = 0.25f;

        public bool IsOn { get; private set; }

        private Collider trigger;
        private bool playerInRange;
        private Quaternion offRotation;
        private Quaternion onRotation;

        public bool WantsInteractHint => playerInRange && PlayerLookup.InView(HintAnchor);
        public Transform HintAnchor => tapVisual != null ? tapVisual : transform;

        private void Awake()
        {
            trigger = GetComponent<Collider>();

            if (tapVisual != null)
            {
                offRotation = tapVisual.rotation;
                onRotation = Quaternion.AngleAxis(turnAngle, Vector3.up) * offRotation;
            }

            if (waterStream != null) waterStream.SetActive(false);
        }

        private void FixedUpdate()
        {
            Collider playerCollider = PlayerLookup.Collider;

            playerInRange = playerCollider != null
                && playerCollider.enabled
                && trigger.bounds.Intersects(playerCollider.bounds);
        }

        private void Update()
        {
            if (!playerInRange) return;
            if (LoopManager.Instance != null && !LoopManager.Instance.AcceptsInput) return;

            if (!Input.GetKeyDown(KeyCode.E)) return;
            if (PlayerLookup.InteractTaken) return;

            PlayerLookup.ClaimInteract();
            Toggle();
        }

        private void Toggle()
        {
            IsOn = !IsOn;
            if (waterStream != null) waterStream.SetActive(IsOn);

            if (tapVisual != null)
            {
                StopAllCoroutines();
                StartCoroutine(Turn(IsOn ? onRotation : offRotation));
            }
        }

        private IEnumerator Turn(Quaternion target)
        {
            Quaternion start = tapVisual.rotation;
            for (float e = 0f; e < turnDuration; e += Time.deltaTime)
            {
                tapVisual.rotation = Quaternion.Slerp(start, target, Mathf.Clamp01(e / turnDuration));
                yield return null;
            }
            tapVisual.rotation = target;
        }
    }
}
