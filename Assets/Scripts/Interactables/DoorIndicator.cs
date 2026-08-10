using UnityEngine;

namespace IterationRoom
{
    // The lamp above the door: one block split into a red half and a green half, only one of which
    // is ever lit.
    //
    // It tracks the *condition*, not the door. Green means "the floor button is held right now, so
    // this door will open if you press it" - which turns the lamp into the puzzle's readout. The
    // player can see the moment a ghost steps onto the pad from across the room, instead of having
    // to walk over and try the button to find out.
    public class DoorIndicator : MonoBehaviour
    {
        public Renderer redHalf;
        public Renderer greenHalf;

        public FloorButton requiredFloorButton;
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

        private void Awake()
        {
            block = new MaterialPropertyBlock();
        }

        private void Start()
        {
            Apply(true);
        }

        private void Update()
        {
            bool nowGreen = (door != null && door.IsOpen)
                         || (requiredFloorButton != null && requiredFloorButton.IsActive);

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

            half.GetPropertyBlock(block);
            block.SetColor(BaseColorId, lit ? color : color * unlitScale);
            // Emission carries the "lit" read - albedo alone just looks like coloured plastic.
            // Pushed past 1 so it clears the deliberately high bloom threshold.
            block.SetColor(EmissionId, lit ? color * litEmission : Color.black);
            half.SetPropertyBlock(block);
        }
    }
}
