using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // Authored on the game/menu camera and root overlay canvas by SceneBuilder.
    // The canvas uses the same viewport as the camera so HUD edges and world hints agree.
    public class FixedAspectPresentation : MonoBehaviour
    {
        public const float Aspect = 16f / 9f;
        private Camera view;
        private RectTransform content;

        public static Rect Viewport(int width, int height)
        {
            float ratio = Mathf.Max(1, width) / (float)Mathf.Max(1, height);
            if (ratio > Aspect)
            {
                float w = Aspect / ratio;
                return new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
            float h = ratio / Aspect;
            return new Rect(0f, (1f - h) * 0.5f, 1f, h);
        }

        private void Awake()
        {
            view = GetComponent<Camera>();
            Canvas canvas = GetComponent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                var frame = new GameObject("AspectContent", typeof(RectTransform));
                content = frame.GetComponent<RectTransform>();
                content.SetParent(transform, false);
                // Move existing children only; iteration cannot include the new frame itself.
                for (int i = transform.childCount - 2; i >= 0; i--)
                {
                    Transform child = transform.GetChild(i);
                    child.SetParent(content, false);
                    child.SetAsFirstSibling();
                }
                CanvasScaler scaler = GetComponent<CanvasScaler>();
                if (scaler != null) scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                foreach (ControlHintDisplay hints in GetComponentsInChildren<ControlHintDisplay>(true))
                    if (hints.area == transform) hints.area = content;
            }
            LateUpdate();
        }

        private void LateUpdate()
        {
            Rect viewport = Viewport(Screen.width, Screen.height);
            if (view != null && view.targetTexture == null)
            {
                view.rect = viewport;
                view.aspect = Aspect;
            }
            if (content != null)
            {
                content.anchorMin = viewport.min;
                content.anchorMax = viewport.max;
                content.offsetMin = content.offsetMax = Vector2.zero;
            }
        }

        private void OnGUI()
        {
            if (view == null || !view.enabled || view.targetTexture != null || Event.current.type != EventType.Repaint) return;
            Rect r = Viewport(Screen.width, Screen.height);
            Color old = GUI.color;
            int depth = GUI.depth;
            GUI.depth = -10000;
            GUI.color = Color.black;
            float x = r.x * Screen.width, y = r.y * Screen.height;
            GUI.DrawTexture(new Rect(0f, 0f, x, Screen.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(Screen.width - x, 0f, x, Screen.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, y), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, Screen.height - y, Screen.width, y), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.depth = depth;
        }
    }
}
