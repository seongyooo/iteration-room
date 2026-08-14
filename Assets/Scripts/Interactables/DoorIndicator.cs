using UnityEngine;

namespace IterationRoom
{
    // The lamp above the door: one block split into a red half and a green half, only one of which
    // is ever lit.
    //
    // It tracks the *condition*, not the door. Green means "everything this door needs is true
    // right now" - which turns the lamp into the puzzle's readout. The player can see the moment a
    // ghost steps onto a pad from across the room, rather than having to walk over and find out.
    //
    // This matters most in Room3, where the condition is TWO pads held at once: standing at the
    // door watching the lamp stay red tells you that only one of your past selves has arrived, and
    // that is information the room has no other way to give.
    public class DoorIndicator : MonoBehaviour
    {
        public Renderer redHalf;
        public Renderer greenHalf;

        // Kept only for a lamp with no door wired to it. When there IS a door, its own `HeldOpen`
        // answers instead - the lamp is the readout for exactly the condition the door uses, and a
        // lamp saying "go" on a rule the door does not use is worse than no lamp. That mattered the
        // moment a door could be held by something other than pads: room2-1's is held by a number
        // lock, and a lamp still asking about pads would have sat red through a solved room.
        public FloorButton[] requiredFloorButtons;
        // Room2's door is a key door with no pad behind it, so the lamp reads the lock instead.
        // Exactly one of these two is wired per door; both being null just leaves the lamp red.
        public KeyLock keyLock;
        public Door door;

        public Color redColor = new Color(1f, 0.09f, 0.07f);
        public Color greenColor = new Color(0.13f, 1f, 0.3f);
        public float litEmission = 3.2f;
        // How far the unlit half drops. Not to black: a dead lamp half should still read as a lens.
        public float unlitScale = 0.1f;

        private MaterialPropertyBlock block;

        private bool green;
        private bool applied;

        // Lazily, not in Awake. A MaterialPropertyBlock is not serialized, so a script reload
        // while play mode is running nulls this without Awake ever running again - and every
        // SetPropertyBlock call after that throws. Editor-only, but recompiling mid-session while
        // tuning something is precisely when it happens.
        private MaterialPropertyBlock Block => block ??= new MaterialPropertyBlock();

        private void Start()
        {
            Apply(true);
        }

        private void Update()
        {
            bool nowGreen = (door != null && (door.IsOpen || door.HeldOpen))
                         || (door == null && FloorButton.AllActive(requiredFloorButtons))
                         || (keyLock != null && keyLock.CanOpen);

            if (applied && nowGreen == green) return;
            green = nowGreen;
            Apply(false);
        }

        private void Apply(bool force)
        {
            applied = true;
            SetHalf(redHalf, redColor, !green);
            SetHalf(greenHalf, greenColor, green);
        }

        private void SetHalf(Renderer half, Color color, bool lit)
        {
            if (half == null) return;

            MaterialPropertyBlock b = Block;
            half.GetPropertyBlock(b);
            b.SetColor(LitPropertyIds.BaseColor, lit ? color : color * unlitScale);
            // Emission carries the "lit" read - albedo alone just looks like coloured plastic.
            // Pushed past 1 so it clears the deliberately high bloom threshold.
            b.SetColor(LitPropertyIds.EmissionColor, lit ? color * litEmission : Color.black);
            half.SetPropertyBlock(b);
        }
    }
}
