using UnityEngine;

namespace IterationRoom
{
    // One-touch button: pressing attempts to open the door, but only succeeds
    // while the required FloorButton is active (held by the player or a ghost).
    [RequireComponent(typeof(Collider))]
    public class DoorButton : MonoBehaviour
    {
        public FloorButton requiredFloorButton;
        public Door door;
        public Renderer buttonRenderer;
        public Color idleColor = Color.white;
        public Color deniedColor = Color.red;
        public Color grantedColor = new Color(0.2f, 1f, 0.4f);

        private bool playerInRange;

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            playerInRange = true;
            TryPress();
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player")) playerInRange = false;
        }

        private void Update()
        {
            if (playerInRange && Input.GetKeyDown(KeyCode.E))
                TryPress();
        }

        private void TryPress()
        {
            bool active = requiredFloorButton != null && requiredFloorButton.IsActive;
            Flash(active ? grantedColor : deniedColor);
            if (active) door?.Open();
        }

        private void Flash(Color color)
        {
            if (buttonRenderer == null) return;
            buttonRenderer.material.color = color;
            CancelInvoke(nameof(ResetColor));
            Invoke(nameof(ResetColor), 0.3f);
        }

        private void ResetColor()
        {
            if (buttonRenderer != null) buttonRenderer.material.color = idleColor;
        }
    }
}
