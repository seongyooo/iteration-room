using UnityEngine;

namespace IterationRoom
{
    // Implemented by everything E does something to: the door button, the drawer, the pickups and
    // the key lock. ControlHintDisplay reads this to decide whether to put the "E" prompt on screen
    // and where to hang it, and deliberately knows nothing else about the four very different
    // scripts behind it - two GhostInteractables, a pickup and a plain lock.
    public interface IInteractHintTarget
    {
        // "Pressing E right now would do something here", not merely "the player is standing near
        // it". A shut drawer with the tool inside is one place with two answers, depending on which
        // of the two the press would land on, and prompting over an object that would ignore the
        // press teaches the wrong lesson on the one occasion the prompt is ever shown.
        bool WantsInteractHint { get; }

        // Where the prompt is drawn, in world space.
        Transform HintAnchor { get; }
    }
}
