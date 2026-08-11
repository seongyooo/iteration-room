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
    // This matters most in Room3, where the condition is THREE pads held at once: standing at the
    // door watching the lamp stay red tells you that not all of your past selves have arrived, and
    // that is information the room has no other way to give.
    public class DoorIndicator : MonoBehaviour
    {
        public Renderer redHalf;
        public Renderer greenHalf;

        // All of them must be held - the same rule the door itself uses, via FloorButton.AllActive,
        // so the two cannot drift apart.
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
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

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
            bool nowGreen = (door != null && door.IsOpen)
                         || FloorButton.AllActive(requiredFloorButtons)
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
            b.SetColor(BaseColorId, lit ? color : color * unlitScale);
            // Emission carries the "lit" read - albedo alone just looks like coloured plastic.
            // Pushed past 1 so it clears the deliberately high bloom threshold.
            b.SetColor(EmissionId, lit ? color * litEmission : Color.black);
            half.SetPropertyBlock(b);
        }
    }
}
