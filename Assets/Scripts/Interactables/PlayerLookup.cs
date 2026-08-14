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
        private static Camera playerEye;

        public static Collider Collider => Resolve() ? playerCollider : null;
        public static PlayerHand Hand => Resolve() ? playerHand : null;

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
        public static bool InteractTaken => Hand != null && Hand.InteractedThisFrame;

        public static void ClaimInteract()
        {
            PlayerHand hand = Hand;
            if (hand != null) hand.MarkInteract();
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
        // thing ControlHintDisplay and NearestTakeable were both built to prevent.
        //
        // NO OCCLUSION TEST. This asks whether the thing is in the camera's frustum, not whether a
        // wall stands in the way. Nothing in this project raycasts, and a line-of-sight check would
        // have to be told to ignore the fixture's own collider, the player's, and the drawer front
        // the pin is deliberately sitting behind - three exceptions, each of which breaks a real
        // pickup if it is wrong. The gap it leaves is small: reach volumes extend well under a metre
        // past a fixture, so being close enough to press E through a wall barely happens.
        //
        // Fails CLOSED when there is no camera, matching ItemRegistry.NearestTakeable: no eye, no
        // press.
        public static bool InView(Transform anchor) => anchor != null && InView(anchor.position);

        // How close to the target a blocker may be and still not count as blocking. It exists to
        // spare the target ITSELF: most fixtures have a solid collider at the very point their hint
        // anchor sits, so a strict linecast would report every prompt in the game as occluded by the
        // thing it is pointing at.
        //
        // Wide enough to clear an object's own body, narrow enough that a wall in front of it is
        // still a wall - the thinnest wall here is 0.1m and the nearest a fixture ever sits to one is
        // further than this.
        private const float OcclusionSlack = 0.5f;

        public static bool InView(Vector3 worldPoint)
        {
            Camera cam = Eye;
            if (cam == null) return false;

            Vector3 v = cam.WorldToViewportPoint(worldPoint);
            // z is distance ALONG the view axis: negative means behind the camera, where the x/y of
            // a viewport point are mirrored and would otherwise read as perfectly on screen.
            if (v.z <= 0f || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) return false;

            return !Occluded(cam.transform.position, worldPoint);
        }

        // ON SCREEN IS NOT THE SAME AS VISIBLE, and this half was missing.
        //
        // The frustum test alone was deliberate and it was right while the building was one corridor:
        // a fixture in the room ahead is behind a door, and a door is either shut - so the fixture is
        // not in range anyway - or open, so you can see it. Rooms that WRAP, with a fixture one wall
        // away at a diagonal, broke that: play reported an E prompt floating in mid-air beyond a
        // wall, which is a promise the game cannot keep.
        //
        // TRIGGERS ARE IGNORED, and that is essential rather than tidy. Every carryable's reach
        // volume, every pad's range and every doorway trigger is a trigger collider sitting in open
        // air; counted as geometry they would occlude everything behind them, including themselves.
        private static bool Occluded(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance <= OcclusionSlack) return false;

            // Stopped short of the target by the slack, so the object's own body is never the
            // blocker - which is cheaper and steadier than raycasting the whole way and then
            // deciding which hits to forgive.
            return Physics.Raycast(from, delta / distance, distance - OcclusionSlack,
                                   ~0, QueryTriggerInteraction.Ignore);
        }

        private static bool Resolve()
        {
            if (playerCollider != null) return true;

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return false;

            playerCollider = player.GetComponent<Collider>();
            playerHand = player.GetComponent<PlayerHand>();
            return playerCollider != null;
        }
    }
}
