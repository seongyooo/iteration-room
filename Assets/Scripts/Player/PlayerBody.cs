using UnityEngine;

namespace IterationRoom
{
    // THE PLAYER'S OWN BODY - which everyone can see except the player.
    //
    // `BuildPlayer` made a `CharacterController` and a camera and no mesh at all, which is the usual
    // first-person shortcut and it cost three separate things. **Two of the three are still why this
    // exists**:
    //
    // - **The CCTV shows ghosts and not you.** A camera watching a room the player is standing in
    //   watched an empty room.
    // - **A mirror would reflect nowhere.** Cycle 3's mirrors are the puzzle, and a mirror pointed at
    //   somebody who has no body is a mirror pointed at a wall.
    //
    // The third was "look down and there is nothing there", and **that one lost, 2026-08-23, after
    // play** - see `SceneBuilder.BuildPlayerBody`. Looking down shows the floor again.
    //
    // **ONE INSTANCE NOW: `PlayerBody_World`**, a whole person that everything EXCEPT the player's
    // camera draws. This component drives it.
    //
    // ~~`PlayerBody_Shadow`~~ **GONE 2026-08-24, after play, by request.** There was a second copy
    // drawn ShadowsOnly for the player's own camera, because a culling mask culls shadow CASTERS too
    // and masking the world body out took its shadow with it. Four silhouettes, one capsule and a
    // single overhead caster were all tried in a day and none of them looked good enough to keep -
    // `SceneBuilder.BuildPlayerBody` records what each one ruled out, and what to fix FIRST if it
    // ever comes back.
    //
    // So the player casts nothing in their own view. The rooms still do.
    public class PlayerBody : MonoBehaviour
    {
        public Animator animator;

        // ~~`hideBones` and `onlyWhileDriving`~~ **GONE 2026-08-23, with the first-person body.**
        //
        // Both existed only for the instance the player's own camera drew: one blanked its head, arms
        // and finally its whole upper body, the other stopped it appearing during a scripted camera
        // move. That instance is not built any more (see `SceneBuilder.BuildPlayerBody` for the
        // argument), and the two that remain are whole people that nobody looks at from the inside.
        //
        // Which also retires the wake-up fault they were last used for: there is no longer a body in
        // the frame while the eye walks from lying to standing, because there is no longer a body in
        // that camera at all.

        // **THE SAME NUMBERS THE GHOSTS WALK ON**, because it is the same rig playing the same clip
        // and two sets of them would drift. See `GhostReplayer`.
        public float metresPerCycle = 1.55f;
        public float walkThreshold = 0.25f;
        public float idleCycleSeconds = 4.17f;

        private static readonly int WalkState = Animator.StringToHash("Walk");
        private static readonly int IdleState = Animator.StringToHash("Idle");

        private Vector3 lastFlat;
        private bool measured;
        private bool walking;
        private float walkPhase;
        private float smoothedSpeed;

        // **THE CLIP IS SCRUBBED, NOT PLAYED, and that is a bug fix as well as a house style.**
        //
        // The first version set the state with `animator.Play` and let it run at `animator.speed`.
        // Play found what that means: the walk looked right for about a second and then the legs
        // froze. `Man_Walk` is imported without Loop Time - nothing needed it, because the ghosts have
        // always scrubbed their own normalized time - so played straight it runs once, holds its last
        // frame and stays there until something changes state.
        //
        // Scrubbing removes the dependency rather than patching it: the phase is mine, so the clip's
        // own loop flag cannot matter. And it is advanced by **travel, not by time**, which is the
        // whole reason `GhostReplayer` does it this way - a stride tied to distance never skates.
        private void LateUpdate()
        {
            if (animator == null) return;

            Vector3 flat = transform.position;
            flat.y = 0f;

            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            float travel = measured ? (flat - lastFlat).magnitude : 0f;
            lastFlat = flat;
            measured = true;

            // A teleport is not a stride. The loop puts the player back at the bed inside one frame,
            // which is several metres of travel that no leg took.
            if (travel > 2f) travel = 0f;

            // Smoothed for the threshold only, exactly as the ghosts do it: raw per-frame speed
            // jitters across the line and the figure flickers between walking and standing.
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, travel / dt, 1f - Mathf.Exp(-8f * dt));
            walking = smoothedSpeed > (walking ? walkThreshold * 0.6f : walkThreshold);

            walkPhase += travel / Mathf.Max(0.01f, metresPerCycle);

            animator.speed = 0f;
            if (walking) animator.Play(WalkState, 0, Mathf.Repeat(walkPhase, 1f));
            // Idle is the one clip with nothing to sync to, so it runs on the clock - but still
            // scrubbed, so there is one rule for everything.
            else animator.Play(IdleState, 0,
                               Mathf.Repeat(Time.time / Mathf.Max(0.01f, idleCycleSeconds), 1f));
        }
    }
}
