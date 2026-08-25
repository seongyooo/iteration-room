using UnityEngine;

namespace IterationRoom
{
    // The one player in the game, resolved once and cached rather than re-found by every
    // interactable that wants to poll range against it.
    //
    // Every fixture that polls the player instead of trusting trigger callbacks - see FloorButton
    // for why - used to carry its own copy of "if the cached collider is null, FindGameObjectWithTag
    // and fetch components off it". Six copies of the same lookup meant a fix to one (FloorButton's
    // own history has a real bug in exactly this kind of code) had to be found and reapplied in the
    // other five, and every new room's fixtures were liable to paste a seventh. This is that lookup,
    // once.
    //
    // A plain static rather than a MonoBehaviour singleton, in the company of ItemRegistry,
    // ChessBoard.Instance and BalloonField.Instance - this project already resolves its handful of
    // one-of-a-kind objects this way. Unity's fake-null keeps this safe across an Editor session
    // that plays more than once without a domain reload: the cached Collider compares equal to null
    // again the instant the scene that held it is torn down, so the next FixedUpdate re-resolves it.
    public static class PlayerLookup
    {
        private static Collider playerCollider;
        private static PlayerHand playerHand;
        private static FirstPersonController playerController;
        private static Camera playerEye;

        public static Collider Collider => Resolve() ? playerCollider : null;
        public static PlayerHand Hand => Resolve() ? playerHand : null;
        // Resolved here with the rest of the player, so a room fixture that has to hold the player
        // still for a moment does not go hunting for them itself - see FirstPersonController.
        public static FirstPersonController Controller => Resolve() ? playerController : null;

        // Resolved on its own rather than inside Resolve(), so a scene that somehow has a player
        // without a camera still answers Collider and Hand.
        public static Camera Eye
        {
            get
            {
                if (playerEye != null) return playerEye;

                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player == null) return null;

                playerEye = player.GetComponentInChildren<Camera>();
                return playerEye;
            }
        }

        // ONE PRESS, ONE ACTION. Every fixture polls E for itself in its own Update, and Unity's
        // script execution order is arbitrary, so two of them answering the same press is decided by
        // a coin toss - play found it as picking a key up and putting it in the lock with one press.
        //
        // A fixture must CHECK this before acting and CLAIM it when it does, and the two halves are
        // equally load-bearing: claiming without checking is what let the second fixture through,
        // and checking without claiming lets `PlayerHand.LateUpdate` read the press as "put it down"
        // and throw the object on the floor.
        //
        // CLAIM ONLY WHEN YOU ACT. A fixture that claims a press it then refuses is a deadlock: a
        // lock that eats every E because the player has the wrong key means they can never pick up
        // the right one while standing next to it.
        // THE PROXIMITY POLL EVERY FIXTURE RUNS, in one place.
        //
        // Eight fixtures carried a byte-identical `FixedUpdate` - resolve the player's collider, check
        // it is enabled, intersect it with our own trigger's bounds - and four of them null-checked
        // the trigger while four did not. That is the same duplication this class already exists to
        // remove one level down (see the note above on six copies of the player lookup): the fix is
        // not that any one copy was wrong, it is that a change to the test has to be found and
        // reapplied in eight places and a new room's fixture is liable to paste a ninth.
        //
        // POLLED, NEVER `OnTriggerEnter`. `Teleport` disables and re-enables the CharacterController
        // inside one frame, so a player standing in a volume when the iteration ended never generates
        // the exit callback - see FloorButton, where that shipped as a pad stuck on forever.
        //
        // `enabled` on the player's collider is what says they are actually in the world; it is off
        // for exactly the frames the teleport takes.
        public static bool InReach(Collider trigger)
        {
            Collider playerCollider = Collider;
            return trigger != null
                && playerCollider != null
                && playerCollider.enabled
                && trigger.bounds.Intersects(playerCollider.bounds);
        }

        public static bool InteractTaken => Hand != null && Hand.InteractedThisFrame;

        public static void ClaimInteract()
        {
            PlayerHand hand = Hand;
            if (hand != null) hand.MarkInteract();
        }

        // WHAT THE PLAYER IS LOOKING AT WINS THE PRESS. Changed 2026-08-17, by request, and it
        // replaces "nearest to the eye" everywhere the game arbitrates between two things E could act
        // on.
        //
        // WHY DISTANCE WAS THE WRONG MEASURE. Reach is a VOLUME, and volumes overlap by design: each
        // bay of cycle 2's chest answers E through a 1.7 x 1.0 x 1.4m trigger, so both drawers and the
        // cube standing on the top are all in range at once from where a player stands to use any of
        // them. Nearest-to-the-eye then hands the press to whichever anchor happens to sit closest to
        // the face - the upper drawer front, every time - so the cube could not be picked up and the
        // lower bay could not be pulled. Play reported exactly that.
        //
        // The measure is the ANGLE OFF THE CROSSHAIR instead: how far the thing's own prompt anchor is
        // from the centre of the screen. That is the question the player is actually asking with their
        // head, and it is the one measure that can tell two overlapping volumes apart.
        //
        // RETURNED IN VIEWPORT UNITS with the horizontal corrected for aspect, so a degree sideways
        // counts the same as a degree up - uncorrected, a wide screen makes everything to the left and
        // right look closer to the centre than it is. Half the screen HEIGHT is 0.5, so the numbers
        // read as "fraction of the way to the top edge".
        //
        // `float.MaxValue` for anything behind the camera: `WorldToViewportPoint` mirrors those onto
        // the screen, and a mirrored point can land dead centre.
        public static float AimOffset(Vector3 worldPoint)
        {
            Camera cam = Eye;
            if (cam == null) return float.MaxValue;

            Vector3 v = cam.WorldToViewportPoint(worldPoint);
            if (v.z <= 0f) return float.MaxValue;

            float dx = (v.x - 0.5f) * Mathf.Max(0.0001f, cam.aspect);
            float dy = v.y - 0.5f;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        public static float AimOffset(Transform anchor) =>
            anchor != null ? AimOffset(anchor.position) : float.MaxValue;

        // HOW CLOSE TO EQUALLY CENTRED COUNTS AS EQUAL, in the same viewport units - about 3% of the
        // screen's height, roughly a degree and a half at this field of view.
        //
        // It exists because two things genuinely stacked on one another cannot be told apart by angle:
        // a pin lying in an open drawer is behind the drawer front, both are under the crosshair, and
        // whichever is a hair nearer the centre would win by an amount the player cannot see or aim.
        // Inside the band the older rule decides - NEARER WINS - which is what "the things clustered
        // together at a similar distance" asks for, and it keeps the closer of two overlapping objects
        // reachable rather than making it a contest of sub-pixel aim.
        public const float AimTieBand = 0.03f;

        // The comparison itself, in one place, because three call sites have to agree on it: the press
        // (`ItemRegistry.AimedTakeable`), the shared arbiter every fixture's press ends in
        // (`IsAimedAt`) and the prompt disc, which is drawn from that same arbiter. If they ever
        // disagreed the game would draw a mark on one thing and act on another, which is the fault
        // this whole family of rules exists to prevent.
        public static bool BetterAim(float offset, float distanceSqr, float bestOffset, float bestDistanceSqr)
        {
            if (offset >= float.MaxValue) return false;
            if (bestOffset >= float.MaxValue) return true;

            // Clearly better aimed at - the head has chosen.
            if (offset < bestOffset - AimTieBand) return true;
            // Clearly worse.
            if (offset > bestOffset + AimTieBand) return false;
            // Equally centred: the near one.
            return distanceSqr < bestDistanceSqr;
        }

        // WHICH ONE THING THE PRESS IS FOR - asked of EVERYTHING at once, and the answer both the
        // press and the prompt disc are taken from.
        //
        // Claiming decides that only ONE thing answers a press; it cannot decide WHICH, because the
        // fixtures claim in whatever order Unity happens to run their `Update`s. The predecessor of
        // this (`TakeableIsNearer`) settled only TAKEABLE vs FIXTURE - the bucket standing under a
        // tap - and left FIXTURE vs FIXTURE entirely to that coin toss. Cycle 2's chest of drawers is
        // two fixtures a hand's width apart with overlapping reach volumes, so the coin toss WAS the
        // interaction: the upper bay answered every press, the lower one could not be pulled, and the
        // cube standing on the top could not be picked up. Play reported all three.
        //
        // ONE SCAN PER FRAME over every fixture that wants the press and every takeable in reach,
        // cached on the frame count. Every fixture polls E in its own Update and would otherwise
        // repeat the whole scan - and worse, two of them could get different answers in one frame if
        // anything moved between their Updates, which is the disagreement all of this exists to stop.
        private static IInteractHintTarget[] hintTargets;
        private static Transform aimedAnchor;
        private static int aimedFrame = -1;

        // Assigned by `ControlHintDisplay.SetTargets`, so the disc and this cannot be looking at two
        // different sets. It is the display's list because that is who gathers it (`CycleBinding`
        // rebuilds it per cycle); it is used here because the PRESS needs the same answer.
        public static void SetHintTargets(IInteractHintTarget[] targets) => hintTargets = targets;

        // The single winner: the anchor of whatever E is for right now, or null when E is for nothing.
        public static Transform AimedAnchor
        {
            get
            {
                if (aimedFrame == Time.frameCount) return aimedAnchor;
                aimedFrame = Time.frameCount;
                aimedAnchor = ComputeAimedAnchor();
                return aimedAnchor;
            }
        }

        private static Transform ComputeAimedAnchor()
        {
            Camera cam = Eye;
            if (cam == null) return null;

            Vector3 eye = cam.transform.position;
            Transform best = null;
            float bestOffset = float.MaxValue;
            float bestSqr = float.MaxValue;

            if (hintTargets != null)
            {
                foreach (IInteractHintTarget target in hintTargets)
                {
                    // NOTHING IN HERE MAY ASK THIS QUESTION BACK. `WantsInteractHint` is a fixture's
                    // own eligibility - in reach, on screen, in a state where E would do something -
                    // and must never include the arbitration, or this scan calls itself. That is why
                    // the tap and the switch lost their `TakeableIsNearer` clause when this arrived.
                    //
                    // TESTED THROUGH `Object`, NOT THROUGH THE INTERFACE. Unity's fake-null lives on
                    // `UnityEngine.Object`'s == operator, and an interface reference does not use it -
                    // so a destroyed fixture held in this list reads as a perfectly good target and
                    // throws on the property access. This list is static and survives a cycle swap,
                    // which is exactly where a destroyed entry would come from.
                    if (target == null || (target is Object dead && dead == null)) continue;
                    if (!target.WantsInteractHint) continue;

                    Transform anchor = target.HintAnchor;
                    if (anchor == null) continue;

                    float offset = AimOffset(anchor.position);
                    float sqr = (anchor.position - eye).sqrMagnitude;
                    if (!BetterAim(offset, sqr, bestOffset, bestSqr)) continue;

                    bestOffset = offset; bestSqr = sqr; best = anchor;
                }
            }

            // And the takeables, from the live registry rather than from the gathered list - an item
            // is registered by existing, where the hint list is a snapshot taken per cycle.
            CarryableItem item = ItemRegistry.AimedTakeable(eye);
            if (item != null)
            {
                Transform anchor = item.HintAnchor != null ? item.HintAnchor : item.transform;
                float offset = AimOffset(anchor.position);
                float sqr = (anchor.position - eye).sqrMagnitude;
                if (BetterAim(offset, sqr, bestOffset, bestSqr)) best = anchor;
            }

            return best;
        }

        // **THE WHOLE RULE FOR AN E PRESS, IN ONE CALL: the press goes exactly where the disc is.**
        //
        // Two halves, and every fixture needs both. `WantsInteractHint` is this fixture's own answer -
        // in reach, ON SCREEN AND NOT BEHIND ANYTHING (`InView`), and in a state where E would do
        // something. `IsAimedAt` is which of the eligible ones the player is pointing at.
        //
        // IT IS ONE CALL BECAUSE THREE FIXTURES HAD ONLY THE SECOND HALF. `Drawer`, `KeyLock` and
        // `CarryableItem` polled E off `playerInRange` alone and never asked their own hint property,
        // so a press could open a drawer through a wall or with your back to it - and the aim scan
        // does not close that on its own, because an angle off the crosshair is happily measured
        // through solid geometry. Stated separately it was got wrong three times out of eight; stated
        // here it cannot be got half-right.
        public static bool PressGoesTo(IInteractHintTarget target)
        {
            if (target == null) return false;
            if (!target.WantsInteractHint) return false;
            return IsAimedAt(target.HintAnchor);
        }

        // "Is this the thing the press is for?" - the arbitration half of the rule above. Prefer
        // `PressGoesTo`, which is that AND the fixture's own eligibility; this is public for the
        // prompt, which has already asked the eligibility question when it gathered its candidates.
        //
        // FAILS OPEN FOR ANYTHING THE SCAN CANNOT SEE - but only if it is genuinely IN VIEW. A fixture
        // missing from both the hint list and the registry would otherwise be silently dead: it would
        // lose a contest it was never entered in, and nothing about the room would look wrong. So an
        // anchor that did not win is judged directly against whatever did - with `InView` applied
        // first, because the scan's candidates all passed it inside their own `WantsInteractHint` and
        // a fallback that skipped it would be the one path into the game where a press acts on
        // something behind a wall. A fixture that IS in the list and lost fails this too, as it should.
        public static bool IsAimedAt(Transform anchor)
        {
            if (anchor == null) return false;

            // NO EYE, NO PRESS - checked before anything else, and it is the fail-closed half of this.
            // Asked after the winner it would answer "nobody won, so you did", which hands an
            // unarbitrated press to every overlapping fixture at once. Every other lookup in this file
            // fails the same way for the same reason (see InView).
            Camera cam = Eye;
            if (cam == null) return false;

            Transform best = AimedAnchor;
            if (best == null || best == anchor) return true;

            if (!InView(anchor)) return false;

            Vector3 eye = cam.transform.position;
            return BetterAim(AimOffset(anchor.position), (anchor.position - eye).sqrMagnitude,
                             AimOffset(best.position), (best.position - eye).sqrMagnitude);
        }

        // ON SCREEN. Every fixture in the game answers E on PROXIMITY - a polled volume around it -
        // which is right for "am I close enough to touch this" and says nothing at all about whether
        // the player can see it. So a press could take an item behind you, or work a recess you had
        // turned your back on, purely because you were standing near it.
        //
        // The rule is stated against the PROMPT rather than against the object, and every caller
        // passes its own HintAnchor: the interaction is available exactly when its prompt disc would
        // be on screen. That is a rule the player can see the whole of - if there is no disc, the
        // press does nothing, and the disc is missing precisely because the thing is not in view.
        //
        // A point rather than the object's bounds, for the same reason: the disc is drawn at a point,
        // and testing anything else would let the press and the prompt disagree - which is the one
        // thing ControlHintDisplay and AimedTakeable were both built to prevent.
        //
        // NO OCCLUSION TEST. This asks whether the thing is in the camera's frustum, not whether a
        // wall stands in the way. Nothing in this project raycasts, and a line-of-sight check would
        // have to be told to ignore the fixture's own collider, the player's, and the drawer front
        // the pin is deliberately sitting behind - three exceptions, each of which breaks a real
        // pickup if it is wrong. The gap it leaves is small: reach volumes extend well under a metre
        // past a fixture, so being close enough to press E through a wall barely happens.
        //
        // Fails CLOSED when there is no camera, matching ItemRegistry.AimedTakeable: no eye, no
        // press.
        public static bool InView(Transform anchor) =>
            anchor != null && InFrustum(anchor.position) && !Occluded(anchor.position, anchor);

        // No owner to forgive, so nothing is ignored but triggers and the player. For callers that
        // have a point rather than an object.
        public static bool InView(Vector3 worldPoint) =>
            InFrustum(worldPoint) && !Occluded(worldPoint, null);

        // FRUSTUM ONLY, NO RAY. For callers that need "is this on screen" many times a frame and
        // cannot pay for a `RaycastAll` per candidate - `CctvFeed` asks it of every screen it drives,
        // every frame, to decide whether the feed is worth rendering at all. An interaction prompt
        // must use `InView`; a cost decision must not.
        public static bool OnScreen(Vector3 worldPoint) => InFrustum(worldPoint);

        private static bool InFrustum(Vector3 worldPoint)
        {
            Camera cam = Eye;
            if (cam == null) return false;

            Vector3 v = cam.WorldToViewportPoint(worldPoint);
            // z is distance ALONG the view axis: negative means behind the camera, where the x/y of
            // a viewport point are mirrored and would otherwise read as perfectly on screen.
            return v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
        }

        // ON SCREEN IS NOT THE SAME AS VISIBLE, and this half was missing until 2026-08-14.
        //
        // The frustum test alone was deliberate and right while the building was one corridor: a
        // fixture in the room ahead is behind a door, and a door is either shut - so the fixture is
        // out of range anyway - or open, so you can see it. Rooms that WRAP broke that, and play
        // reported an E prompt hanging in mid-air past a wall.
        //
        // **AN OBJECT'S OWN BODY IS NOT A BLOCKER, and getting that wrong broke the cube room.** The
        // first version stopped the ray half a metre short of the target, on the theory that a
        // fixture sits at its own hint anchor. A glass cube is a METRE across with a solid collider
        // on it, so half a metre short is still inside the cube: every cube occluded itself and none
        // could be picked up. The forgiveness has to be "this collider belongs to the thing being
        // looked at" - which is exact - rather than a distance, which is a guess about size.
        //
        // TRIGGERS ARE IGNORED, and that is essential rather than tidy. Every carryable's reach
        // volume, every pad's range and every doorway trigger is a trigger collider sitting in open
        // air; counted as geometry they would occlude everything behind them, including themselves.
        private static bool Occluded(Vector3 to, Transform owner)
        {
            Camera cam = Eye;
            if (cam == null) return true;

            Vector3 from = cam.transform.position;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.05f) return false;

            Transform ownerRoot = owner != null ? owner.root : null;

            // **NonAlloc, BECAUSE THIS RUNS PER CANDIDATE PER FRAME.** `RaycastAll` returns a fresh
            // array every call, and this is asked of every fixture and every takeable the player is
            // standing near - so in a room like the chess board it was allocating a dozen arrays a
            // frame, for the garbage collector to hand back as a hitch later. The buffer is reused and
            // the cast is otherwise identical.
            //
            // The cap can in principle drop a hit, and it does not matter here: this returns on the
            // FIRST occluder it finds, so losing one only changes the answer if all `occluders.Length`
            // of the hits that came back were the owner or the player. Sized far past that.
            int count = Physics.RaycastNonAlloc(from, delta / distance, occluders, distance,
                                                ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider c = occluders[i].collider;
                if (c == null) continue;
                Transform t = c.transform;
                // The thing being looked at cannot hide itself.
                if (ownerRoot != null && t.root == ownerRoot) continue;
                // Nor can the player, whose own capsule the camera sits inside.
                if (t.root.CompareTag("Player")) continue;
                return true;
            }

            return false;
        }

        // Shared by every occlusion test in the building. Static because `Occluded` is, and one
        // buffer is safe here for the reason it usually is not: the cast, the scan and the answer all
        // happen inside one synchronous call with nothing re-entering it.
        private static readonly RaycastHit[] occluders = new RaycastHit[32];

        private static bool Resolve()
        {
            if (playerCollider != null) return true;

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return false;

            playerCollider = player.GetComponent<Collider>();
            playerHand = player.GetComponent<PlayerHand>();
            playerController = player.GetComponent<FirstPersonController>();
            return playerCollider != null;
        }
    }
}
