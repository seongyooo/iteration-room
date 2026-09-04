using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // **THE BUILDING GOING OUT, ROOM BY ROOM** (rewritten 2026-09-04, by request).
    //
    // The title screen looks down a corridor of six rooms. This takes them out one at a time and
    // LEAVES them out: each room stutters two or three times and then goes dark for good, the next
    // one starts, and when the last is gone the whole corridor comes back and it begins again.
    //
    // The frames are CUMULATIVE - `roomFrames[k]` is the building with k+1 rooms already dark - so
    // showing one is the whole state of it, and nothing has to be layered or ordered. That is also
    // why a fit can cut between two of them: frame k-1 and frame k differ by exactly one room.
    //
    // WHICH room each stage takes is the capture's business, not this component's (it fails from the
    // far end inward - see `SceneBuilder.CaptureMenuBackground` for why that direction). Here a
    // frame is just stage k of a failure that ends with everything out.
    //
    // **UNSCALED TIME, because the menu is not the game.** `Time.timeScale` is zero whenever the
    // pause menu is up and this screen is reachable from there; on a scaled clock the whole sequence
    // would freeze exactly when the player is looking at it longest.
    [RequireComponent(typeof(Image))]
    public class MenuFlicker : MonoBehaviour
    {
        // The legacy single dark frame. Still driven when no per-room set was built, so a menu made
        // by an older build - or by one that could not render - flickers instead of sitting still.
        private Image dark;

        // One per stage of the failure, cumulative and in order. Empty falls back to `dark`.
        public Image[] roomFrames;

        // How long a room holds steady before its turn. Per room and random inside the range, so the
        // corridor does not go out on a metronome.
        public Vector2 calmSeconds = new Vector2(3f, 5f);

        // How many times a room stutters before it goes for good, and how long each cut lasts.
        public Vector2Int stutters = new Vector2Int(2, 3);
        public Vector2 cutSeconds = new Vector2(0.05f, 0.14f);

        // The dark beat at the end of a room's turn, before the next room starts.
        public Vector2 settleSeconds = new Vector2(0.35f, 0.9f);

        // How long the whole corridor stays black before the lights come back.
        public Vector2 blackoutSeconds = new Vector2(1.2f, 2.2f);

        // **AND SOMETIMES THE WHOLE THING GOES AT ONCE.** One run in this many is a cascade: no calm
        // between rooms and one cut each, so the corridor snaps out end to end. It is the same
        // sequence at a different speed rather than a second behaviour, which is what keeps it
        // reading as the same fault having a worse moment.
        public int cascadeOneRunIn = 3;
        public Vector2 cascadeSeconds = new Vector2(0.06f, 0.13f);

        private float until;
        private int settled = -1;      // last room index that is dark for good; -1 is a whole corridor
        private int cutsLeft;
        private bool showingNext;      // mid-stutter, is the room being taken currently out
        private bool cascading;

        private int RoomCount => roomFrames != null ? roomFrames.Length : 0;

        private void Awake()
        {
            dark = GetComponent<Image>();
            Restore();
            cascading = Random.Range(0, Mathf.Max(1, cascadeOneRunIn)) == 0;
        }

        private void Update()
        {
            if (Time.unscaledTime < until) return;

            if (RoomCount == 0) { LegacyToggle(); return; }

            // The corridor is fully dark - hold it, then bring everything back and start again.
            if (settled >= RoomCount - 1)
            {
                Restore();
                cascading = Random.Range(0, Mathf.Max(1, cascadeOneRunIn)) == 0;
                return;
            }

            int taking = settled + 1;

            if (cutsLeft > 0)
            {
                cutsLeft--;
                showingNext = !showingNext;
                Show(showingNext ? taking : settled);

                if (cutsLeft == 0)
                {
                    // A turn always ENDS with the room out - it is going for good, so the last cut
                    // is the one that keeps it.
                    Show(taking);
                    settled = taking;
                    showingNext = false;

                    // **THE LAST ROOM'S BEAT IS THE BLACKOUT.** Holding after the restore instead
                    // would put the lights back the same frame the corridor went out and then wait
                    // on a picture nobody was looking at.
                    bool wasTheLast = settled >= RoomCount - 1;
                    until = Time.unscaledTime + (wasTheLast
                        ? Random.Range(blackoutSeconds.x, blackoutSeconds.y)
                        : cascading
                            ? Random.Range(cascadeSeconds.x, cascadeSeconds.y)
                            : Random.Range(settleSeconds.x, settleSeconds.y));
                    return;
                }

                until = Time.unscaledTime + (cascading
                    ? Random.Range(cascadeSeconds.x, cascadeSeconds.y)
                    : Random.Range(cutSeconds.x, cutSeconds.y));
                return;
            }

            // Start the next room's turn: a calm stretch, then its stutter.
            cutsLeft = cascading ? 1 : Random.Range(stutters.x, stutters.y + 1) * 2 - 1;
            showingNext = false;
            until = Time.unscaledTime + (cascading
                ? Random.Range(cascadeSeconds.x, cascadeSeconds.y)
                : Random.Range(calmSeconds.x, calmSeconds.y));
        }

        // Everything on. `settled` of -1 means no frame is showing at all, which is the lit still
        // underneath doing the work.
        private void Restore()
        {
            settled = -1;
            cutsLeft = 0;
            showingNext = false;
            Show(-1);
        }

        // One frame at a time, and every other one cleared - a frame left up because an index moved
        // mid-stutter is exactly the kind of state that only shows on the fifth loop.
        private void Show(int index)
        {
            for (int i = 0; i < RoomCount; i++)
            {
                Image frame = roomFrames[i];
                if (frame == null) continue;
                Color c = frame.color;
                c.a = i == index ? 1f : 0f;
                frame.color = c;
            }
        }

        // What this component was before the corridor: one still, on and off.
        private void LegacyToggle()
        {
            if (dark == null) return;
            Color c = dark.color;
            bool wasOut = c.a > 0.5f;
            c.a = wasOut ? 0f : 1f;
            dark.color = c;
            until = Time.unscaledTime + (wasOut
                ? Random.Range(calmSeconds.x, calmSeconds.y)
                : Random.Range(cutSeconds.x, cutSeconds.y));
        }
    }
}
