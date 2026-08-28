using UnityEngine;

namespace IterationRoom
{
    // THE BEAM. Fired from room3-2E, bent by whatever mirrors are being held along the way, and it
    // has to arrive in room3-2N.
    //
    // **NOTHING ABOUT IT IS RECORDED OR REPLAYED, and that is the point.** It is recomputed from
    // scratch every frame out of where the mirrors are right now - and a ghost's position and yaw
    // come off its own timeline, so the beam a past self bends is reproduced exactly without a single
    // byte in `RecordedFrame` and without one bit of the 32 signals. It is DERIVED, in the same sense
    // `FallingItem` derives a fall from a release rather than simulating one.
    //
    // **THE GATES AND THE CORRIDOR ARE PART OF THE OPTICS**, which is the best thing about it. A gate
    // is only open while a past self stands on its pad, and the corridor is a solid block unless
    // somebody is holding the north pad - so the light needs exactly the same people the player does,
    // and letting go of that pad does not close a door on the beam, it fills twenty-three metres of
    // corridor in front of it. The one mechanism this cycle already had turns out to be a shutter.
    public class LaserBeam : MonoBehaviour
    {
        // Where it starts and which way it points. The direction is FLATTENED - see `Mirror` for why
        // the whole system lives in one horizontal plane.
        public Transform muzzle;
        public float range = 60f;

        // **A HARD CAP, and unlike the mirrors' own reflections this one cannot be left to physics.**
        // Two mirrors facing each other bounce a ray between them for ever, and there is no
        // brightness falloff in a line renderer to stop it. Twelve is far more than any route needs
        // and cheap enough not to think about.
        public int maxBounces = 12;

        // One per possible segment: `maxBounces` reflections need `maxBounces + 1` runs of beam.
        // Built by `SceneBuilder` rather than pooled here, so the scene shows what it contains.
        public LineRenderer[] segments;
        // The bright spot where the beam lands. The whole aiming loop is watching this move.
        public Transform terminalDot;

        public AudioSource audioSource;
        public AudioClip hitClip;

        // Everything the beam is allowed to stop on. Triggers are excluded at the query rather than
        // by layer - every reach volume and pad range in this building is a trigger sitting in open
        // air, and counted as geometry they would block the light everywhere.
        public LayerMask blockers = ~0;

        private static readonly RaycastHit[] hits = new RaycastHit[16];
        private LaserReceiver litLastFrame;

        private void LateUpdate()
        {
            // **AFTER EVERYTHING HAS MOVED.** `Mirror.LateUpdate` levels each face and the hand
            // anchors have already followed the camera, so by here every mirror is where it will be
            // drawn. Computed in `Update` the beam would be one frame behind the mirror in the
            // player's own hands, which is the one place a frame of lag is visible.
            // **EVERY MIRROR IS BROUGHT UP TO DATE FIRST, because `LateUpdate` order between two
            // components is arbitrary.** Left to Unity, this traced against wherever the glass was
            // last frame roughly half the time - which is a lag nobody would report as a lag, only as
            // "the aiming feels loose". `Sync` is idempotent and there are five of them.
            foreach (Mirror mirror in Mirror.All)
                if (mirror != null && mirror.isActiveAndEnabled) mirror.Sync();

            Vector3 origin = muzzle != null ? muzzle.position : transform.position;
            Vector3 dir = Flat(muzzle != null ? muzzle.forward : transform.forward);

            int used = 0;
            LaserReceiver reached = null;

            for (int bounce = 0; bounce <= maxBounces; bounce++)
            {
                float travel = range;
                Vector3 end = origin + dir * travel;

                // 1. THE WORLD. Nearest solid thing that is not a person.
                float wall = NearestBlocker(origin, dir, travel);

                // 2. THE MIRRORS, which physics cannot see.
                Mirror bent = null;
                bool bentFront = false;
                float mirrorAt = wall;
                foreach (Mirror mirror in Mirror.All)
                {
                    if (mirror == null || !mirror.isActiveAndEnabled) continue;
                    if (!mirror.Raycast(origin, dir, mirrorAt, out float d, out bool front)) continue;
                    mirrorAt = d;
                    bent = mirror;
                    bentFront = front;
                }

                travel = Mathf.Min(wall, mirrorAt);
                end = origin + dir * travel;

                if (used < segments.Length) Draw(segments[used++], origin, end);

                // The silvered side bends it; the back stops it dead. A mirror held the wrong way
                // round is the one way a mirror gets IN the way, and you can see exactly where.
                if (bent != null && bentFront)
                {
                    // **THE FLATTENING BELONGS TO THE MIRROR, NOT TO THE BEAM** (2026-08-28). It is a
                    // guard against a hand: every pane a person aims is levelled every frame, and
                    // re-flattening after the bounce means a degree of drift in an import rotation
                    // cannot put the light in the ceiling twenty metres later. A pane built to leave
                    // the horizontal plane is the one thing that guard must not touch, so it is asked
                    // of the mirror rather than assumed of all of them.
                    Vector3 bounced = Vector3.Reflect(dir, bent.Normal);
                    dir = bent.KeepsBeamLevel ? Flat(bounced) : bounced.normalized;
                    origin = end;
                    continue;
                }

                reached = ReceiverAt(end);
                break;
            }

            for (int i = used; i < segments.Length; i++)
                if (segments[i] != null) segments[i].enabled = false;

            if (terminalDot != null)
            {
                bool any = used > 0;
                terminalDot.gameObject.SetActive(any);
                if (any) terminalDot.position = segments[used - 1].GetPosition(1);
            }

            if (reached != null) reached.Illuminate();
            if (reached != litLastFrame)
            {
                litLastFrame = reached;
                if (reached != null && audioSource != null && hitClip != null)
                    audioSource.PlayOneShot(hitClip);
            }
        }

        // **PEOPLE ARE NOT OPAQUE.** Ghosts have no colliders so they are already transparent to it,
        // and the living player is excluded here so the two behave the same. The alternative - a beam
        // you must not stand in - is a fine idea for a room you cross once and a bad one for a
        // building whose corridor is twenty-three metres long and whose light runs down the middle of
        // it.
        private float NearestBlocker(Vector3 origin, Vector3 dir, float maxDistance)
        {
            int count = Physics.RaycastNonAlloc(origin, dir, hits, maxDistance, blockers,
                                                QueryTriggerInteraction.Ignore);
            float nearest = maxDistance;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider == null) continue;
                if (hits[i].collider.transform.root.CompareTag("Player")) continue;
                if (hits[i].distance < nearest) nearest = hits[i].distance;
            }
            return nearest;
        }

        private static LaserReceiver ReceiverAt(Vector3 point)
        {
            foreach (LaserReceiver receiver in LaserReceiver.All)
            {
                if (receiver == null || !receiver.isActiveAndEnabled) continue;
                if (receiver.Covers(point)) return receiver;
            }
            return null;
        }

        private void Draw(LineRenderer line, Vector3 from, Vector3 to)
        {
            if (line == null) return;
            line.enabled = true;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
        }

        // Levels a direction. Flattening at each bounce rather than trusting the mirrors to be
        // exactly upright means one degree of drift in a model's import rotation cannot send the beam
        // into the ceiling twenty metres later.
        //
        // **IT IS ABOUT DIRECTION, NEVER ABOUT HEIGHT** - a distinction that only started mattering
        // in 2026-08-28, when a held pane stopped being pinned to one world Y and started sitting at
        // its holder's own head height. A segment leaving a levelled pane is horizontal; WHICH
        // horizontal plane it is on is wherever whoever is holding the thing happens to be standing,
        // and a beam crossing the floor simply does not meet a mirror held on deck A. That is the
        // architecture doing the work, and it is the point of letting the height go.
        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized;
        }
    }
}
