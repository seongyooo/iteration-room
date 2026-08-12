using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // Replays a recorded timeline from a previous iteration. Ghosts never read
    // input or physics - they scrub through recorded frames and report their
    // recorded floor-button hold state to the shared FloorButton each frame.
    public class GhostReplayer : MonoBehaviour
    {
        // The rig. Every clip is SCRUBBED rather than played: animator.speed is pinned at 0 and the
        // normalized time is written every frame. That is what preserves the property the old
        // primitive limbs had - the walk advances with DISTANCE TRAVELLED, not with time, so a
        // ghost's stride length is constant instead of its legs spinning faster the quicker it
        // moves. Only position and yaw are recorded, so the walk has to be inferred, and inferring
        // it from distance is the only way it stays honest.
        public Animator animator;
        public string walkState = "Walk";
        public string idleState = "Idle";
        public string swingState = "Swing";

        // Metres of travel per full walk cycle. Too small and the ghost skates; too large and it
        // moon-walks. Tune against the clip by eye - the foot should look planted while it is down.
        public float metresPerCycle = 1.55f;
        // TURNING COUNTS AS TRAVEL. Yaw is recorded but nothing was ever driven from it, so a ghost
        // standing on a pad and looking around used to play the idle clip while its whole body
        // rotated rigidly underneath - a statue on a turntable, and in Room3 four of them do it for
        // forty seconds at a stretch. Rather than sourcing a turn-in-place clip (which this rig
        // does not have, and which is Generic so nothing external retargets onto it), the fix
        // extends the rule already in use: the walk advances with distance, and a turn is distance.
        //
        // Metres of stride credited per radian of yaw - roughly the circle the feet trace shuffling
        // round on the spot, so about half a stance width. Raise it and a turning ghost paddles;
        // drop it to zero and turning stops driving the walk entirely, which is the old behaviour.
        public float turnRadius = 0.2f;
        // Degrees per second, and it exists because the recorded yaw is the CAMERA's. A player
        // flicking the mouse turns hundreds of degrees in a frame, and uncapped that lands as a
        // single enormous phase jump - legs blurring through several strides in one tick. Past this
        // the body still snaps round (it is following the recording) but the feet stop keeping up,
        // which is both what a real fast turn looks like and unnoticeable at that speed anyway.
        public float maxTurnRate = 360f;

        // Below this the ghost is treated as standing, and plays the idle clip rather than freezing
        // mid-stride with its legs apart - which is what scrubbing a walk cycle to a stop looks
        // like. Hysteresis on top, so a ghost creeping along the threshold does not flicker.
        // Measured against combined travel, so it doubles as the dead zone for turning: at
        // turnRadius 0.2 a 20 deg/s look-around comes to 0.07 m/s and stays idle, while a 90 deg/s
        // turn comes to 0.31 m/s and shuffles.
        public float walkThreshold = 0.25f;
        public float idleCycleSeconds = 4.17f;

        // How long a ghost stays mid-swing after one of its recorded pops fires. Purely
        // presentational: without it a balloon bursts near a ghost that is just standing there.
        public float popSwingDuration = 0.9f;

        // A TOOL-SHAPED ACTION NEEDS THE TOOL, for a past self exactly as for the living player.
        // This is the general rule the room now runs on, not a special case for balloons: an
        // interaction performed WITH an object is gated on that object being in the hand, and
        // taking the object away stops the interaction. See docs/decisions.md for what it costs.
        public bool requirePopTool = true;
        public string popToolItemId = "Tool";

        // Where a carried item rides. Parented under the rig's right hand by SceneBuilder, so an
        // item follows the arm through the walk cycle rather than floating beside the ghost.
        public Transform carryAnchor;

        private RecordedTimeline recording;
        private List<RecordedFrame> timeline;
        private List<PopEvent> pops;
        private List<CarryEvent> carries;
        private int popCursor;
        private int carryCursor;
        // What this ghost is holding right now. Never more than one holder per item in the whole
        // game - this list is one end of that invariant, CarryableItem.HeldByGhost is the other.
        private readonly List<CarryableItem> held = new List<CarryableItem>();
        // Which of `held` is in the hand. Empty string is empty hands.
        private string equippedId = string.Empty;

        private float swingUntil = -1f;
        // Shared with the PlayerRecorder that produced the timeline - an interactable's index here
        // is its bit in RecordedFrame.signals.
        private GhostInteractable[] interactables;
        private int cursor;
        private uint activeSignals;
        private bool finished;
        private Vector3 lastPosition;
        private float lastYaw;
        private float walkPhase;
        private float smoothedSpeed;
        private bool walking;
        private Renderer[] renderers;
        private int walkHash, idleHash, swingHash;

        // "This ghost has that item, in its hand, right now." Deliberately verified against the ITEM
        // rather than trusting equippedId, because equippedId is a cached answer and the thing it
        // describes can be taken away without asking: PlayerHand.Take pulls an item straight out of
        // a ghost's hands. Trusting the cache let a ghost keep popping balloons for the rest of the
        // iteration with the pin visibly in the living player's hand.
        private bool HoldingEquipped(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || equippedId != itemId) return false;
            for (int i = 0; i < held.Count; i++)
                if (held[i] != null && held[i].itemId == itemId && held[i].HeldByGhost == this) return true;
            return false;
        }

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            walkHash = Animator.StringToHash(walkState);
            idleHash = Animator.StringToHash(idleState);
            swingHash = Animator.StringToHash(swingState);

            // Pinned at zero for the whole run. Every state is driven by an explicit normalized
            // time below; letting the animator advance on its own would put the walk back on a
            // clock and undo the entire point of recording distance.
            if (animator != null)
            {
                animator.speed = 0f;
                // The ghost's position comes from the recorded timeline. Root motion would fight
                // it - the clips have none baked in, but a future clip that did would drag the
                // ghost off its own recording.
                animator.applyRootMotion = false;
                // These are the only figures in the room, so they should not stop animating when
                // the camera looks away - a ghost frozen mid-step in the corner of the eye is
                // worse than the cost of updating seven skinned meshes.
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        // Every timeline's frame 0 is the bed spawn, because recording starts the instant the
        // wake-up hands control back - so ResetPlayback parks every ghost ever made on the exact
        // spot the player wakes up on. Without hiding them, opening your eyes means looking
        // through a stack of dark figures standing inside you, every single iteration.
        public void SetVisible(bool visible)
        {
            if (renderers == null) return;
            foreach (Renderer r in renderers)
                if (r != null) r.enabled = visible;
        }

        public void Init(RecordedTimeline recorded, GhostInteractable[] ghostInteractables)
        {
            recording = recorded;
            timeline = recorded != null ? recorded.frames : null;
            pops = recorded != null ? recorded.pops : null;
            carries = recorded != null ? recorded.carries : null;
            interactables = ghostInteractables;
            ResetPlayback();
        }

        public void ResetPlayback()
        {
            cursor = 0;
            popCursor = 0;
            carryCursor = 0;
            // Everything this ghost was holding goes back where SceneBuilder put it. The loop calls
            // ReleaseCarried before the balloon field resets so the ordering is already right by the
            // time this runs; this is the belt to that braces, for any other caller.
            ReleaseCarried();
            swingUntil = -1f;
            if (timeline != null && timeline.Count > 0)
            {
                transform.position = timeline[0].position;
                transform.rotation = Quaternion.Euler(0f, timeline[0].yaw, 0f);
            }
            lastPosition = transform.position;
            // Seeded alongside the position, or the first tick after a reset reads the whole
            // difference between where this ghost finished and where it starts as one turn, and
            // the walk phase jumps by however many strides that comes to.
            lastYaw = transform.eulerAngles.y;
            walkPhase = 0f;
            smoothedSpeed = 0f;
            walking = false;
            finished = false;
            // Release everything before replaying from the top, so a ghost that ended the previous
            // pass standing on a button doesn't leave it stuck on.
            ApplySignals(0u);
        }

        public void Tick(float elapsedLoopTime)
        {
            if (timeline == null || timeline.Count == 0) return;

            // Drained before the end-of-timeline check below, not after: a tick can jump past
            // several recorded frames at once, and the last pops of a recording sit right up
            // against its final frame. Checked first, a single long frame would retire the ghost
            // with its closing pops never fired, and those balloons would stay up for good.
            DrainPops(elapsedLoopTime);
            DrainCarries(elapsedLoopTime);

            // Once the recording runs out this ghost is done: it lets go of everything and leaves.
            //
            // Holding the last frame instead - which is what clamping the cursor does on its own -
            // makes ending a cycle early strictly better than seeing it out. Quit at t=5s while
            // standing on the floor pad and that final frame's signal would stay applied from t=5
            // to t=60 of every future iteration: five seconds of your time buying a 55-second hold,
            // which deletes the "spend a whole iteration on this" premise the puzzle is built on.
            if (elapsedLoopTime > timeline[timeline.Count - 1].time)
            {
                if (!finished)
                {
                    finished = true;
                    ApplySignals(0u);
                    // PUT DOWN, not taken away. A ghost can still be holding something here: its
                    // recorded surrender may have FAILED because another ghost already opened the
                    // door, and then it carries the key to the end of its timeline. Vanishing with
                    // it would delete the only key in the game for the rest of the iteration. Short
                    // timelines make this ordinary rather than exotic - EndCycleControl truncates
                    // recordings, so a ghost made from an iteration ended at t=5 retires at t=5 of
                    // every iteration after it.
                    DropCarried();
                    SetVisible(false);
                }
                return;
            }

            while (cursor < timeline.Count - 1 && timeline[cursor + 1].time <= elapsedLoopTime)
                cursor++;

            RecordedFrame frame = timeline[cursor];
            transform.position = frame.position;
            transform.rotation = Quaternion.Euler(0f, frame.yaw, 0f);
            ApplySignals(frame.signals);

            // Ghosts are deterministic, so once one of them has walked into Room2 the balloons come
            // down at the same instant every iteration - which is what keeps every later recording
            // aligned with the field it was made against.
            if (BalloonField.Instance != null) BalloonField.Instance.TriggerIfInside(transform.position);

            SwingLimbs();
        }

        // Replays this ghost's pops by balloon identity, so it bursts exactly the balloons the
        // player did - wherever physics has carried them this iteration. Popping an already-burst
        // balloon is a no-op, which is what happens when the living player gets to one first.
        private void DrainPops(float elapsedLoopTime)
        {
            if (pops == null) return;

            // GATED ON THIS GHOST HOLDING THE TOOL, in the hand - the same test BalloonTool applies
            // to the living player (hand.Holding, not hand.Has). Take the pin off a past self and
            // its pops stop, which is the point of the rule.
            //
            // THE COST IS REAL AND WAS ACCEPTED KNOWINGLY. There is one pin, so at most one entity
            // in the room can pop at a time: the earliest-recorded taker wins it, holds it to the
            // end of its timeline (there is no socket to surrender a tool to), and every other
            // ghost's recorded pops silently do nothing. Room2's accumulation - every past
            // iteration's pops replaying so the field shrinks - does not survive that.
            //
            // The unblocking change is a SUPPLY of pins rather than one, which makes the rule free.
            // See docs/decisions.md. To revert, set requirePopTool false; nothing else changes.
            while (popCursor < pops.Count && pops[popCursor].time <= elapsedLoopTime)
            {
                bool armed = !requirePopTool || HoldingEquipped(popToolItemId);
                // The cursor advances either way. A pop that could not happen did not happen; it is
                // not owed later, and holding it back would fire it at whatever unrelated moment
                // the ghost next picked the tool up.
                popCursor++;
                if (!armed) continue;

                if (BalloonField.Instance != null) BalloonField.Instance.PopById(pops[popCursor - 1].balloonId);
                swingUntil = Time.time + popSwingDuration;
            }
        }

        // Replays this ghost's pickups and hand-overs. NEITHER end is simply applied - both are
        // re-evaluated against the world as it is right now, which is the difference between this
        // and the KeyRevealed bug that made "ghosts cannot carry" a rule in the first place. That
        // version re-evaluated a WEAKER fact (has the key's balloon burst?) than the one that
        // actually enabled the action (is the key in this hand?). These check the real one.
        private void DrainCarries(float elapsedLoopTime)
        {
            if (carries == null) return;

            while (carryCursor < carries.Count && carries[carryCursor].time <= elapsedLoopTime)
            {
                CarryEvent e = carries[carryCursor];
                carryCursor++;

                if (e.kind == CarryKind.Take) TryTake(e.itemId);
                else if (e.kind == CarryKind.Equip) ApplyEquip(e.itemId);
                else TrySurrender(e.itemId);
            }
        }

        // Which of this ghost's items is out. Everything else it carries is stowed - still held,
        // still rewound, just not rendered - because two objects at one anchor is two objects
        // inside each other.
        private void ApplyEquip(string itemId)
        {
            equippedId = itemId;
            for (int i = 0; i < held.Count; i++)
                if (held[i] != null) held[i].SetGhostStowed(held[i].itemId != itemId);
        }

        private void TryTake(string itemId)
        {
            // Asks for a FREE one rather than for the object: an id can be a supply now (three pins
            // live in the drawer), and a ghost is entitled to one of them, not to a particular one.
            // The freedom test is inside that lookup - IsFreeForGhost, not IsAvailable, because a
            // ghost must never lift something out of the living player's pocket and must never pull
            // one back out of a lock it is already seated in. The traffic only goes the other way:
            // the player can take from a ghost.
            //
            // Null means the supply is exhausted, which is a legitimate outcome. With three pins the
            // player and two past selves can pop at once; a third ghost reaching for a fourth pin
            // finds none and does not pop, and nothing anywhere has to special-case that.
            CarryableItem item = ItemRegistry.FindFreeForGhost(itemId);
            if (item == null) return;

            // THE COMPLETED-ERRAND RULE, and it applies ONLY to items with a destination.
            //
            // Its job is arbitration: several ghosts want the one key, and a recording that fetched
            // it and fumbled must not rob the recording that fetched it and delivered. That needs a
            // notion of "finished", which needs somewhere to finish - a socket.
            //
            // The pin has no socket and is never surrendered, so every pin errand is unfinished by
            // that measure and the rule would silently forbid ghosts to hold it at all. That is
            // exactly the bug this scoping fixes: a rule about scarcity was blocking an item that
            // was never contended for in a way that mattered.
            if (ItemRegistry.FindSocket(itemId) != null
                && (recording == null || !recording.Delivers(itemId))) return;

            // The condition that ENABLED the pickup, re-evaluated - same rule as everywhere else.
            // The pin lives inside the nightstand drawer, and a ghost must no more take it through
            // a shut drawer than the player can. Its own replayed pull is what opens it, so in
            // practice this passes; it is here so that stops being a coincidence.
            if (item.requiresOpenDrawer != null && !item.requiresOpenDrawer.IsFullyOpen) return;

            item.AttachToGhost(this, carryAnchor != null ? carryAnchor : transform);
            held.Add(item);
            // Straight into the hand, mirroring PlayerHand.Take - and the recording's own Equip
            // event lands in the same breath anyway, so this only covers the frame between them.
            ApplyEquip(itemId);
        }

        private void TrySurrender(string itemId)
        {
            CarryableItem item = null;
            for (int i = 0; i < held.Count; i++)
                if (held[i] != null && held[i].itemId == itemId) { item = held[i]; break; }

            // Not holding it means the player took it back somewhere between the recorded pickup and
            // here. The hand-over simply does not happen, and the door it would have opened stays
            // shut - which is the honest outcome and the whole reason both ends are conditional.
            if (item == null) return;

            // And it has to be the one IN HAND, the same condition the player's own unlock is gated
            // on now that Tab exists. A ghost cannot put a key in a lock out of its pocket any more
            // than the living player can.
            if (!HoldingEquipped(itemId)) return;

            IItemSocket socket = ItemRegistry.FindSocket(itemId);
            // A socket that refuses (Room2's lock is already open, because another ghost got there
            // first) leaves the item in this ghost's hands. That is the case DropCarried covers.
            if (socket == null || !socket.AcceptFromGhost(item)) return;

            held.Remove(item);
        }

        // Everything down where it stands, still in play.
        private void DropCarried()
        {
            for (int i = 0; i < held.Count; i++)
                if (held[i] != null) held[i].DropAt(held[i].transform.position);
            held.Clear();
            equippedId = string.Empty;
        }

        // Everything back to where SceneBuilder put it. This is the loop rewinding, not the ghost
        // putting something down, so it goes to the origin rather than to the floor.
        public void ReleaseCarried()
        {
            for (int i = 0; i < held.Count; i++)
                if (held[i] != null) held[i].ReturnToOrigin();
            held.Clear();
            equippedId = string.Empty;
        }

        // A ghost must not take anything into the grave. A carried item is PARENTED to the carry
        // anchor, so destroying the ghost destroys the item with it - and there is exactly one of
        // each object, so that is a soft-locked run rather than a cosmetic loss.
        //
        // Nothing destroys a ghost mid-run today, which is why this went unnoticed; the GHOST RESET
        // (next steps §2) is precisely a feature that destroys ghosts, and it would have shipped
        // straight into this. Found by a test harness tearing down its own ghost.
        // Called by PlayerHand when the living player takes something out of this ghost's hands.
        // `toWorld` false means someone else has already taken charge of the object - this ghost
        // only has to stop believing it holds it.
        public void ReleaseItem(CarryableItem item, bool toWorld)
        {
            if (item == null) return;
            held.Remove(item);
            // The hand is empty now, whoever took it. HoldingEquipped would catch a stale id on its
            // own, but leaving one set would still show up anywhere else that reads equippedId.
            if (equippedId == item.itemId) equippedId = string.Empty;
            if (toWorld) item.DropAt(item.transform.position);
        }

        // Only recorded position and yaw exist, so the walk is inferred from how far the ghost
        // moved - both ways it can move. Phase advances with travel rather than time, which keeps
        // the stride length constant instead of the legs spinning faster the quicker it goes, and
        // makes a turn on the spot come out as a shuffle in step with the rotation.
        private void SwingLimbs()
        {
            if (animator == null) return;

            float dt = Time.deltaTime;

            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;
            delta.y = 0f;
            float distance = delta.magnitude;

            // DeltaAngle rather than a subtraction: yaw is an euler angle and wraps, and a ghost
            // crossing 0/360 would otherwise register a 360-degree spin in one frame.
            float yaw = transform.eulerAngles.y;
            float turned = Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw));
            lastYaw = yaw;
            // Capped per frame, not per second, so the limit holds at any frame rate.
            turned = Mathf.Min(turned, maxTurnRate * dt);
            float turnDistance = turned * Mathf.Deg2Rad * turnRadius;

            // The two are simply added. Walking a curve is both at once and wants a slightly longer
            // stride than the straight line the position delta measures, which is exactly what
            // falls out of this - no state to pick between, no blend, nothing to get out of sync.
            float travel = distance + turnDistance;

            float speed = dt > 0f ? travel / dt : 0f;
            // Smoothed, because a ghost's position comes from a scrubbed timeline and can arrive in
            // uneven steps - the raw per-frame speed jitters across the walk threshold.
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, 1f - Mathf.Exp(-8f * dt));
            walking = smoothedSpeed > (walking ? walkThreshold * 0.6f : walkThreshold);

            // Travel, not time. This is the whole reason the animator is scrubbed by hand.
            walkPhase += travel / Mathf.Max(0.01f, metresPerCycle);

            // A recorded pop takes the whole body for the length of the swing. The old primitive
            // ghost could throw one arm out while the rest of it kept walking; a rigged figure
            // cannot be half-posed without an avatar mask and a second layer, and a ghost that
            // stops to swing reads perfectly well.
            if (Time.time < swingUntil)
            {
                float t = 1f - (swingUntil - Time.time) / Mathf.Max(0.01f, popSwingDuration);
                animator.Play(swingHash, 0, Mathf.Clamp01(t));
                return;
            }

            if (walking) animator.Play(walkHash, 0, Mathf.Repeat(walkPhase, 1f));
            // Idle is the one clip with nothing to sync to, so it runs on the clock - but still
            // scrubbed, so animator.speed can stay at zero and there is one rule for everything.
            else animator.Play(idleHash, 0, Mathf.Repeat(Time.time / Mathf.Max(0.01f, idleCycleSeconds), 1f));
        }

        // Reports only the bits that actually changed, so each interactable sees clean edges: a
        // hold button gets one add and one remove, and a one-touch button fires once per rise
        // rather than every frame the pulse is up.
        private void ApplySignals(uint signals)
        {
            if (interactables != null)
            {
                int count = Mathf.Min(interactables.Length, 32);
                for (int i = 0; i < count; i++)
                {
                    uint bit = 1u << i;
                    bool now = (signals & bit) != 0u;
                    if (now == ((activeSignals & bit) != 0u)) continue;
                    if (interactables[i] != null) interactables[i].SetGhostSignal(this, now);
                }
            }

            activeSignals = signals;
        }

        private void OnDestroy()
        {
            ApplySignals(0u);

            // A ghost must not take anything into the grave. A carried item is PARENTED to the carry
            // anchor, so destroying the ghost destroys the item with it - and there is exactly one
            // of each object, so that is a soft-locked run rather than a cosmetic loss.
            //
            // Nothing destroys a ghost mid-run today, which is why this went unnoticed; the GHOST
            // RESET (next steps §2) is precisely a feature that destroys ghosts, and it would have
            // shipped straight into this. Found by a test harness tearing down its own ghost.
            //
            // Skipped during scene teardown: everything is being destroyed anyway, and reparenting
            // into a half-unloaded hierarchy is asking for trouble.
            if (gameObject.scene.isLoaded) ReleaseCarried();
        }
    }
}
