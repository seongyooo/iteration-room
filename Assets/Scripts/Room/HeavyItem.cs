using UnityEngine;

namespace IterationRoom
{
    // ROOM7'S RULE: a load two people can move and one cannot.
    //
    // **IT IS NOT A `CarryableItem`, AND THAT IS THE WHOLE DESIGN.** CLAUDE.md §1.2 says every
    // carryable is in exactly one of five states and has at most one holder, ever. A thing two people
    // carry has two holders, which would break the one invariant the project is least willing to bend.
    //
    // So nothing holds it. It has no owner and is never picked up: it simply MOVES while enough
    // people are near it, in whatever direction they are pushing it from. Custody never exists, so
    // there is nothing for the loop to put back but a position - which `ResetLoad` does outright.
    //
    // A ghost counts toward the two without knowing it does. There is no signal here and nothing
    // recorded: a past self that walked this route walks it again, and if it happens to be beside the
    // load while the living player is too, the load moves. That is the loosest coupling in the game
    // between a ghost and a puzzle, and it is what makes this room feel like moving furniture with
    // somebody rather than operating a machine.
    public class HeavyItem : MonoBehaviour
    {
        // How many bodies it takes. Two is the whole idea; three would be the same idea slower.
        public int handsNeeded = 2;

        // How close counts as being on it.
        public float gripRadius = 1.35f;

        // Slower than walking, so moving it is visibly work rather than carrying something invisible.
        public float speed = 1.1f;

        // Where it has to end up, and how near is near enough.
        public Transform socket;
        public float seatedRadius = 0.8f;

        public Transform ghostParent;

        public AudioSource audioSource;
        public AudioClip dragClip;

        public bool Seated { get; private set; }

        private Vector3 home;
        private bool dragging;

        private void Awake() => home = transform.position;

        // Who is on it right now - the living player, plus any ghost standing within reach. Ghosts
        // are found by transform rather than by signal because there is nothing to record: being
        // beside a thing is not an interaction.
        private int Hands
        {
            get
            {
                int n = 0;
                Collider p = PlayerLookup.Collider;
                if (p != null && p.enabled && Near(p.bounds.center)) n++;

                if (ghostParent == null) return n;
                for (int i = 0; i < ghostParent.childCount; i++)
                {
                    Transform ghost = ghostParent.GetChild(i);
                    if (ghost.gameObject.activeInHierarchy && Near(ghost.position)) n++;
                }
                return n;
            }
        }

        private bool Near(Vector3 world)
        {
            Vector3 d = world - transform.position;
            d.y = 0f;
            return d.sqrMagnitude < gripRadius * gripRadius;
        }

        private void Update()
        {
            bool running = LoopManager.Instance == null || LoopManager.Instance.AcceptsInput;
            if (Seated || !running) { Quiet(); return; }

            if (Hands < handsNeeded) { Quiet(); return; }

            // PUSHED FROM WHERE THE PLAYER IS, which is the only direction that needs no controls.
            // Asking for a key would make this a machine to operate; walking into it makes it a thing
            // to shove, and a past self shoving from its own side is exactly what the room is about.
            Collider player = PlayerLookup.Collider;
            if (player == null) { Quiet(); return; }

            Vector3 push = transform.position - player.bounds.center;
            push.y = 0f;
            if (push.sqrMagnitude < 0.0001f) { Quiet(); return; }

            transform.position += push.normalized * (speed * Time.deltaTime);

            if (!dragging)
            {
                dragging = true;
                if (audioSource != null && dragClip != null) { audioSource.clip = dragClip; audioSource.loop = true; audioSource.Play(); }
            }

            if (socket == null) return;
            Vector3 toSocket = socket.position - transform.position;
            toSocket.y = 0f;
            if (toSocket.sqrMagnitude > seatedRadius * seatedRadius) return;

            Seated = true;
            transform.position = new Vector3(socket.position.x, transform.position.y, socket.position.z);
            Quiet();
        }

        private void Quiet()
        {
            if (!dragging) return;
            dragging = false;
            if (audioSource != null && audioSource.isPlaying) audioSource.Stop();
        }

        // The loop rewinding. There is no custody to unwind - only where it ended up.
        public void ResetLoad()
        {
            Seated = false;
            transform.position = home;
            Quiet();
        }
    }

    // The room's rule, which is only "the load is home".
    public class Haul : RoomCondition
    {
        public HeavyItem load;

        public override bool Satisfied => load != null && load.Seated;
        public override void ResetCondition() => load?.ResetLoad();
    }
}
