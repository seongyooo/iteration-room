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

        private float swingStarted = -99f;
        private Quaternion restRotation;
        private CarryableItem animating;

        private void Update()
        {
            if (hand == null || playerCamera == null) return;
            if (LoopManager.Instance != null && !LoopManager.Instance.AcceptsInput) return;
            // Holding, not Has: with Tab in the game you can be carrying the pin and holding the
            // key, and swinging the key at a balloon should do nothing. This is also what makes the
            // recorded pop honest - it can only exist if the pin was in the hand that made it.
            if (!hand.Holding(requiredItemId)) return;

            if (Input.GetMouseButtonDown(0)) Swing();
            AnimateHeld();
        }

        private void Swing()
        {
            swingStarted = Time.time;
            if (audioSource != null && swingClip != null) audioSource.PlayOneShot(swingClip);

            Vector3 centre = playerCamera.transform.position + playerCamera.transform.forward * reach;

            Balloon best = null;
            float bestSqr = float.MaxValue;
            foreach (Collider c in Physics.OverlapSphere(centre, grabRadius))
            {
                Balloon b = c.GetComponentInParent<Balloon>();
                if (b == null || b.IsPopped) continue;

                float sqr = (b.transform.position - centre).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = b;
            }

            if (best == null) return;

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
