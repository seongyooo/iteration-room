using UnityEngine;

namespace IterationRoom
{
    // Scrolls the renderer's own material texture to read as moving water rather than a static
    // tinted shape. Visual only - independent of whether the tap is on; the interaction that
    // shows/hides this renderer is a separate concern.
    [RequireComponent(typeof(Renderer))]
    public class FlowingWaterVisual : MonoBehaviour
    {
        public Vector2 scrollSpeed = new Vector2(0f, -0.6f);

        private Renderer rend;
        private MaterialPropertyBlock block;
        
        private Vector2 tiling;
private Vector2 offset;

private void Awake()
        {
            rend = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
            tiling = rend.sharedMaterial != null ? rend.sharedMaterial.mainTextureScale : Vector2.one;
        }

private void Update()
        {
            offset += scrollSpeed * Time.deltaTime;
            rend.GetPropertyBlock(block);
            block.SetVector("_BaseMap_ST", new Vector4(tiling.x, tiling.y, offset.x, offset.y));
            rend.SetPropertyBlock(block);
        }
    }
}
