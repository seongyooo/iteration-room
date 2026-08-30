using UnityEngine;

namespace IterationRoom
{
    // WHERE THE LADDER GOES, AND WHAT HAPPENS ONCE IT IS THERE.
    //
    // Room3-2N is three storeys tall and deck B is the top of it. There is a hole in the ceiling
    // above deck B and room3-0 on the other side of it, and **no way up** — that is the state the
    // room ships in. The ladder is carried in from room3-2S and stood in this spot with a left
    // click; from then on the hole is a way through.
    //
    // TWO THINGS IN ONE COMPONENT, and they are the same thing: the socket that accepts the ladder,
    // and the volume that turns a player standing in it into a climbing one. Splitting them would
    // mean a volume that has to ask a socket whether it is armed, every frame, across a reference
    // that exists only to answer that question.
    //
    // AN `IItemSocket`, so a past self replays it for free. A ghost's recorded surrender of "Ladder"
    // resolves through `ItemRegistry` to this, and the ghost stands it up without knowing what a
    // ladder is — which is what makes "somebody already brought it" the ordinary state of the room
    // after the first iteration that did.
    // A `RoomCondition`, so the loop rewinds it with every other room rule and nothing has to
    // remember to call it: `Cycle.ResetRooms` walks `conditions` after the item sweep, which is
    // exactly the order this wants - the sweep takes the ladder back to room3-2S and then this
    // forgets that it was ever standing here. `Satisfied` is "there is a ladder in it", which is
    // what a door would ask if one ever cared.
    public class LadderMount : RoomCondition
    {
        public string acceptedItemId = "Ladder";

        // Where an accepted ladder ends up, parented at local identity: this transform's frame
        // decides how it stands, so the ladder arrives square to the shaft however it was carried.
        public Transform seat;

        // THE CLIMB, as two heights in world space. `bottomY` is deck B's surface and `topY` is
        // room3-0's floor; between them the player goes up and down instead of walking.
        public float bottomY;
        public float topY;

        // **WHICH WAY IS UP THE LADDER, and it is not `Vector3.up`** (2026-08-31). The ladder LEANS
        // now - its foot stands out on deck B and its head rests against room3-0's west wall, which
        // is what a ladder does and what a vertical one in the middle of a shaft never looked like.
        //
        // Everything about the climb that used to be an axis-aligned assumption reads this instead:
        // the volume is a cylinder about the SLANT rather than about a vertical line through the
        // mark, and `FirstPersonController` moves along it rather than straight up. A unit vector,
        // in world space, pointing from the foot to the head - `SceneBuilder` measures it off the
        // same numbers that place the ladder, so the two cannot disagree.
        public Vector3 climbDirection = Vector3.up;

        // WHERE THE LADDER'S AXIS IS at a given height. The foot is this transform; every point up
        // the ladder is that plus the slant, and how far along depends only on how much height has
        // been gained. Horizontal-only in effect, which is all the range test wants.
        public Vector3 AxisAt(float y)
        {
            Vector3 dir = climbDirection.sqrMagnitude > 0.0001f ? climbDirection : Vector3.up;
            // A ladder lying flat has no "at this height" - guarded rather than trusted, because a
            // divide by a rounding error here would send the volume to infinity.
            if (Mathf.Abs(dir.y) < 0.05f) return transform.position;
            return transform.position + dir * ((y - transform.position.y) / dir.y);
        }
        // How wide of the shaft's centre counts as being on the ladder. Generous enough that a
        // player who walked into the hole is climbing rather than falling, tight enough that
        // somebody crossing deck B beside it is not grabbed.
        public float radius = 0.85f;

        // WHERE YOU STEP OFF AT THE TOP. Not optional and not a detail: the top of the ladder is the
        // middle of a hole, and a climb that simply ended there would leave the player standing on
        // nothing and drop them straight back down it. These are points on room3-0's floor, clear of
        // the opening, and reaching the top puts the player on one of them.
        //
        // **SEVERAL, AND THE NEAREST WINS** (2026-08-31). One fixed point meant the step-off always
        // travelled to the same corner of the room however the player had come - so somebody who
        // stepped into the mouth from the south and climbed straight back out was carried across
        // the opening to the east lip, which is a metre and three quarters of sideways travel and
        // reads as being shoved. Reported from play as "walking at the ladder teleports me
        // backwards". Picking the lip nearest to where they actually are makes the step-off the
        // shortest honest move rather than a fixed one.
        //
        // Authored rather than found: the mouth is in a corner, so two of its four sides have only
        // 40cm of floor before a wall and are not places anybody can be put down.
        public Transform[] dismounts;

        // How long the step-off takes. Long enough to read as the last movement of the climb, short
        // enough that it is not a cutscene.
        public float stepOffSeconds = 0.32f;

        // **HOW FAR BELOW THE TOP THE LADDER ACTUALLY CATCHES YOU.** Acquiring at `topY` itself
        // means a player merely WALKING over the mouth is grabbed, and then forward - which on a
        // ladder means up - carries them straight back out again. With a margin they have to be in
        // the shaft to be on the ladder, and the hold below runs to `topY`, so this is hysteresis
        // and not a dead band: nothing flickers at the boundary.
        public float grabBelow = 0.35f;

        // AND HOW FAR DOWN COUNTS AS HAVING CLIMBED. The step-off is for somebody arriving at the
        // top of a climb; a player who dipped into the mouth and came straight back up has not made
        // one, and moving them anywhere would be the shove this is meant to remove. They simply
        // stand back on the lip they came from, which is where they already are.
        public float commitDrop = 0.9f;

        // **WHERE THE PROMPT IS ASKED ABOUT, AND IT IS NOT THE MARK.**
        //
        // `LadderPlacer` ends in `PlayerLookup.InView`, which casts a ray from the eye to this point
        // and refuses the press if anything is in the way. The mount's own origin is ON deck B's
        // surface - so anything RESTING on the mark stands squarely in that ray and the left click
        // is silently refused, with the prompt gone and nothing saying why. Play found it with a
        // Bedlam block scattered onto the mark (`SceneBuilder.BedlamShelves` now keeps that square
        // clear), but the build is not the only thing that can put an object there: the player can
        // put one down on it, and so can a past self.
        //
        // Lifted into the shaft at chest height it is a point in open air, which nothing on the deck
        // can stand in front of. The disc lands where a person would look to judge the spot, too,
        // rather than at their own feet.
        public Transform hintAnchor;
        public Transform HintAnchor => hintAnchor != null ? hintAnchor : transform;

        public FirstPersonController player;

        public AudioSource audioSource;
        public AudioClip installClip;

        // Whether the ladder is standing here. The loop rewinds it like everything else: the item
        // sweep takes the ladder back to room3-2S and `ResetMount` forgets that it was ever here.
        public bool Installed { get; private set; }

        // Whether the player got properly onto the ladder this time - see `commitDrop`.
        private bool committed;

        private CarryableItem installed;
        // ONE socket object, kept. `ItemRegistry.UnregisterSocket` matches by reference, so building
        // a fresh one in `OnDisable` would deregister nothing and leave a dead entry behind every
        // time a cycle goes to sleep.
        private IItemSocket socket;

        private void OnEnable()
        {
            if (socket == null) socket = new DelegateItemSocket(acceptedItemId, Accept);
            ItemRegistry.RegisterSocket(socket);
        }

        private void OnDisable()
        {
            if (socket != null) ItemRegistry.UnregisterSocket(socket);
        }

        // Asked BEFORE the ladder leaves anybody's hands, so a hand-over is only ever begun when it
        // can be finished — the rule every socket in this game follows. False once one is standing
        // here, which is the honest outcome a second ghost's delivery gets.
        public bool CanAccept(string itemId) => !Installed && itemId == acceptedItemId;

        public bool Accept(CarryableItem item)
        {
            if (item == null || !CanAccept(item.itemId) || seat == null) return false;

            item.InsertInto(seat);
            installed = item;
            Installed = true;
            if (audioSource != null && installClip != null) audioSource.PlayOneShot(installClip);
            return true;
        }

        // POLLED, not driven by trigger callbacks — the reason `FloorButton` documents and every
        // volume in this building follows: the loop's teleport disables and re-enables the
        // `CharacterController` inside one frame, so an exit callback never arrives and the player
        // would still be climbing a ladder in another room.
        private void Update()
        {
            if (player == null) return;

            bool running = LoopManager.Instance == null || LoopManager.Instance.AcceptsInput;
            if (!Installed || !running)
            {
                Release();
                return;
            }

            // NOT WHILE ONE IS IN PROGRESS. The step-off moves the player across this very volume,
            // and a grab halfway through it would put them back on the ladder they are leaving.
            if (player.Mantling) return;

            Vector3 at = player.transform.position;
            // AGAINST THE SLANT, not against the mark. On a leaning ladder the axis has moved a
            // metre and a half sideways by the time it reaches the top, so a cylinder about the foot
            // would let go of a climber halfway up.
            Vector3 axis = AxisAt(at.y);
            float dx = at.x - axis.x, dz = at.z - axis.z;
            bool near = dx * dx + dz * dz <= radius * radius;

            if (!near || at.y < bottomY - 0.6f)
            {
                Release();
                committed = false;
                return;
            }

            // OFF THE TOP AND ONTO THE FLOOR. Checked before the grab below, so a player who climbs
            // out is put down rather than caught again by the volume they are leaving.
            if (at.y >= topY)
            {
                // **ONLY FOR SOMEBODY WHO CLIMBED.** See `commitDrop`: a player who never went down
                // the shaft is standing on the lip already and has nowhere to be put.
                if (player.Ladder == this && committed) StepOff(at);
                Release();
                committed = false;
                return;
            }

            // ON THE LADDER. Acquired only below `grabBelow` and held all the way to `topY` - see
            // that field for why the two thresholds differ.
            if (player.Ladder == this || at.y <= topY - grabBelow) player.Ladder = this;
            if (at.y <= topY - commitDrop) committed = true;
        }

        // THE NEAREST PLACE THERE IS FLOOR. Straight-line distance in plan, because every candidate
        // is on the same storey and the one to prefer is the one the player is already beside.
        private void StepOff(Vector3 at)
        {
            Transform best = null;
            float bestSqr = float.MaxValue;

            if (dismounts != null)
            {
                foreach (Transform spot in dismounts)
                {
                    if (spot == null) continue;
                    float ex = spot.position.x - at.x, ez = spot.position.z - at.z;
                    float sqr = ex * ex + ez * ez;
                    if (sqr >= bestSqr) continue;
                    bestSqr = sqr;
                    best = spot;
                }
            }

            if (best != null) player.StepOffLadder(best.position, stepOffSeconds);
        }

        private void Release()
        {
            if (player != null && player.Ladder == this) player.Ladder = null;
        }

        public override bool Satisfied => Installed;

        // The top of an iteration, AFTER `ItemRegistry.ReturnAllToOrigin` - the sweep is what puts
        // the ladder back in room3-2S, and this is what forgets that it was ever standing here.
        public override void ResetCondition()
        {
            Installed = false;
            installed = null;
            committed = false;
            Release();
        }
    }
}
