using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // Always-visible readout of time remaining before the current iteration resets.
    public class CountdownTimer : MonoBehaviour
    {
        public Text label;

        private void Update()
        {
            if (label == null || LoopManager.Instance == null) return;

            float remaining = Mathf.Max(0f, LoopManager.Instance.loopDuration - LoopManager.Instance.ElapsedTime);
            int minutes = Mathf.FloorToInt(remaining / 60f);
            int seconds = Mathf.FloorToInt(remaining % 60f);
            label.text = $"{minutes}:{seconds:00}";
        }
    }
}
