using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // A place to stand and swing at the tree, and one of several around it.
    //
    // A HOLD, like a floor pad, with one extra condition: THE AXE HAS TO BE IN THE HAND. That gate is
    // the rule CLAUDE.md §4 states generally - an interaction performed *with* an object is gated on
    // that object being held, for ghosts exactly as for the player - and it is what stops the room
    // being solved by five people standing in the right places.
    //
    // THE GHOST'S HALF IS RE-EVALUATED EVERY FRAME, not trusted from the recording. A past self that
    // had an axe when the recording was made may not have one now: the living player can take it
    // straight out of its hands, and another ghost can reach it first. Asking `HoldingEquipped` on
    // every count is the §1.3 re-evaluation - the condition checked is the one that actually enabled
    // the action, not a weaker fact that correlates with it.
    public class ChopStation : GhostInteractable
    {
        public string axeItemId = "Axe";

        // Standing on the mark, the same test FloorButton uses. Derived from the visible disc by
        // SceneBuilder so the mark and the volume cannot drift apart.
        public float activationRadius = 0.55f;
        public float footClearance = 0.4f;

        public Renderer markRenderer;
        public Color idleColour = new Color(0.45f, 0.47f, 0.52f);
        public Color activeColour = new Color(1f, 0.55f, 0.2f);
        public float activeEmission = 2.2f;

        // Resolved rather than wired. `PlayerLookup` exists precisely so a fixture does not have to
        // be handed the player - and here it also sidesteps an ordering problem: cycle 2's rooms are
        // built before the player is, so there is no hand to wire at the time this is made.
        private PlayerHand Hand => PlayerLookup.Hand;

        private bool playerChopping;
        private readonly HashSet<GhostReplayer> ghostsHere = new HashSet<GhostReplayer>();

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        // SOMEBODY IS SWINGING HERE. The player counts only while holding the axe; a ghost counts
        // only while it still has one.
        public bool Chopping
        {
            get
            {
                if (playerChopping) return true;
                foreach (GhostReplayer ghost in ghostsHere)
                    if (ghost != null && ghost.HoldingEquipped(axeItemId)) return true;
                return false;
            }
        }

        // ONLY THE PLAYER'S OWN STATE, or the signal compounds every iteration.
        //
        // The axe is part of what gets recorded rather than checked only on replay, so a recording
        // made without one never becomes a chop at all - which is the honest thing for it to be.
        public override bool PlayerSignal => playerChopping;

        public override void SetGhostSignal(GhostReplayer ghost, bool active)
        {
            if (active) ghostsHere.Add(ghost);
            else ghostsHere.Remove(ghost);
            Apply();
        }

        public void ResetStation()
        {
            playerChopping = false;
            ghostsHere.Clear();
            Apply();
        }

        // Polled, not driven by trigger callbacks - the loop teleports the player by disabling the
        // controller inside one frame, so an exit callback can simply never arrive.
        private void FixedUpdate()
        {
            bool now = Running && OnMark && Hand != null && Hand.Holding(axeItemId);
            if (now == playerChopping) return;
            playerChopping = now;
            Apply();
        }

        private void Update() => Apply();

        private bool Running => LoopManager.Instance == null || LoopManager.Instance.AcceptsInput;

        private bool OnMark
        {
            get
            {
                Collider player = PlayerLookup.Collider;
                if (player == null || !player.enabled) return false;

                Bounds b = player.bounds;
                Vector3 mark = transform.position;
                float dx = b.center.x - mark.x, dz = b.center.z - mark.z;
                if (dx * dx + dz * dz > activationRadius * activationRadius) return false;

                float feet = b.min.y;
                return feet >= mark.y - footClearance && feet <= mark.y + footClearance;
            }
        }

        private void Apply()
        {
            if (markRenderer == null) return;
            Color c = Chopping ? activeColour : idleColour;
            var block = new MaterialPropertyBlock();
            markRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            block.SetColor(EmissionId, c * (Chopping ? activeEmission : 0f));
            markRenderer.SetPropertyBlock(block);
        }
    }
}
