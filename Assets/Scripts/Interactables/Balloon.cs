using UnityEngine;

namespace IterationRoom
{
    // One pink balloon. Physical while it falls and while the player wades through it, but its
    // identity is what the loop actually records - see RecordedTimeline.PopEvent.
    public class Balloon : MonoBehaviour
    {
        // Stable across iterations. BalloonField hands these out in spawn order and the order is
        // fixed, so balloon 17 is the same balloon in every iteration even though physics puts it
        // somewhere slightly different each time. This is the whole reason a ghost can reliably
        // re-pop what it popped.
        public int id;

        // Exactly one balloon in the field carries the key. Chosen from the field's fixed seed, so
        // it is the same balloon every run - learning which one it is, is progress the player keeps.
        public bool holdsKey;

        // Balloons do not fall at g. Unity has no per-body gravity scale, so the Rigidbody has
        // useGravity off and this is applied by hand instead - which also lets the damping stay low
        // enough for them to stay lively and bounce, rather than having to be damped into treacle
        // just to slow the drop down.
        public float fallAcceleration = 2.2f;

        public AudioSource audioSource;
        public AudioClip popClip;

        public bool IsPopped { get; private set; }

        private Renderer[] renderers;
        private Collider bodyCollider;
        private Rigidbody body;

        private bool resolved;

        // Resolved on demand rather than in Awake. Awake order between objects is undefined, and
        // BalloonField resets the whole pool from its own Awake - reached first, every reference
        // here would still be null and the reset would silently do nothing.
        private void EnsureRefs()
        {
            if (resolved) return;
            resolved = true;
            renderers = GetComponentsInChildren<Renderer>(true);
            bodyCollider = GetComponent<Collider>();
            body = GetComponent<Rigidbody>();
        }

        private void Awake()
        {
            EnsureRefs();
        }

        private void FixedUpdate()
        {
            if (IsPopped || body == null || body.isKinematic) return;
            body.AddForce(Vector3.down * fallAcceleration, ForceMode.Acceleration);
        }

        // Parked out of play: the GameObject stays active so a pop sound can finish and so Awake
        // has already run by the time the field first resets. Only the parts that make it a
        // balloon are switched off.
        public void SetInPlay(bool inPlay)
        {
            EnsureRefs();

            if (renderers != null)
                foreach (Renderer r in renderers)
                    if (r != null) r.enabled = inPlay;

            if (bodyCollider != null) bodyCollider.enabled = inPlay;

            if (body != null)
            {
                body.isKinematic = !inPlay;
                if (inPlay)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }
        }

        public void Respawn(Vector3 position, Quaternion rotation)
        {
            IsPopped = false;
            transform.SetPositionAndRotation(position, rotation);
            SetInPlay(false);
        }

        public void Release()
        {
            if (IsPopped) return;
            SetInPlay(true);
        }

        public void Pop()
        {
            if (IsPopped) return;
            IsPopped = true;
            SetInPlay(false);

            if (audioSource != null && popClip != null) audioSource.PlayOneShot(popClip);
        }
    }
}
