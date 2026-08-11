using UnityEngine;

namespace IterationRoom
{
    // The plate under the calibration room's wall display. E at it ends the calibration step and
    // starts iteration 1 - it is the only way out of that step, and the only thing in the room E
    // can be pressed at.
    //
    // Everything about walking up to it, prompting over it and lighting it lives in PressPlate; all
    // this adds is when it is live and what pressing it does. It gates on
    // SensitivityCalibration.Active rather than LoopManager.AcceptsInput, which is the deliberate
    // exception recorded in CLAUDE.md 1.8: AcceptsInput is false for the whole of calibration, which
    // is exactly when this has to work.
    //
    // The player's very first E press in the run happens here, against a real fixture, which is what
    // the control list above it has just finished describing.
    public class CalibrationStartButton : PressPlate
    {
        public SensitivityCalibration calibration;

        // Only while the step is actually open. Once it has been pressed the room is behind the
        // player forever, but the prompt must not linger over it on the way out.
        protected override bool IsLive => calibration != null && calibration.Active;

        protected override void OnPressed() => calibration.Confirm();
    }
}
