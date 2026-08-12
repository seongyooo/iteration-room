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

        public static Collider Collider => Resolve() ? playerCollider : null;
        public static PlayerHand Hand => Resolve() ? playerHand : null;

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
