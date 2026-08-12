using UnityEngine;

namespace IterationRoom
{
    // The two MaterialPropertyBlock properties every lit fixture in the project paints - _BaseColor
    // and _EmissionColor on the URP Lit shader - cached once. Shader.PropertyToID hashes the string;
    // several fixtures painted through a bare string literal instead and paid that hash every frame
    // they repainted, and one paid it while also never guarding against repainting when nothing had
    // changed. One cache shared by every painter closes both gaps at once.
    public static class LitPropertyIds
    {
        public static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        public static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    }
}
