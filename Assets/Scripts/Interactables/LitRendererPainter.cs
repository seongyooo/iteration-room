using UnityEngine;

namespace IterationRoom
{
    // Paints one renderer's base and emission colour through a MaterialPropertyBlock, and skips the
    // repaint when neither has changed since the last call. SymbolSlot's ApplyPlate and FinalSlot's
    // ApplyRim were this method, twice over - same guard, same lazily-built block, same two
    // SetColor calls - because a recess in a wall is a recess in a wall whichever room it is in, and
    // the next room's recess would have made it a third copy.
    public sealed class LitRendererPainter
    {
        private MaterialPropertyBlock block;
        private Color appliedBase;
        private Color appliedEmission;
        private bool applied;

        // Lazily, not eagerly: a MaterialPropertyBlock is not serialized, so a script reload during
        // play mode nulls a block built in the owner's Awake without Awake running again, and every
        // SetPropertyBlock call after that throws. Editor-only, but exactly when it bites.
        private MaterialPropertyBlock Block => block ??= new MaterialPropertyBlock();

        // Forces the next Paint call to write even if the colours it is given match what was last
        // applied. FinalSlot calls this from Clear(): the room resetting is a reason to be sure the
        // fixture is actually repainted, not just a reason to believe it already agrees.
        public void ForceNextRepaint() => applied = false;

        public void Paint(Renderer renderer, Color baseColor, Color emissionColor)
        {
            if (renderer == null) return;
            if (applied && appliedBase == baseColor && appliedEmission == emissionColor) return;

            applied = true;
            appliedBase = baseColor;
            appliedEmission = emissionColor;

            MaterialPropertyBlock b = Block;
            renderer.GetPropertyBlock(b);
            b.SetColor(LitPropertyIds.BaseColor, baseColor);
            b.SetColor(LitPropertyIds.EmissionColor, emissionColor);
            renderer.SetPropertyBlock(b);
        }
    }
}
