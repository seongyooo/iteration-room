using UnityEngine;

namespace IterationRoom
{
    // WHAT HOLDS A DOOR OPEN, when it is a room's own rule rather than a set of pads.
    //
    // `Door` used to name each kind by hand - pads, then a number lock, then this would have been a
    // third `if` and cycle 2's six puzzles a ninth. Every one of them answers the same question, so
    // they answer it through one type and `Door` asks once.
    //
    // Deliberately NOT an interface. Unity serialises a component reference and does not serialise a
    // bare interface field, and the alternative - `[SerializeReference]` or storing a MonoBehaviour
    // and casting - is a worse trade than a two-member abstract class for something every room in the
    // game will inherit.
    //
    // A condition is TRACKED, not latched by the door: `Door` reads `Satisfied` every frame and
    // shuts again when it lapses. Whether the condition itself latches is the room's own business -
    // `Chorus` does, because three levers aligned for a moment must not demand the player sprint a
    // corridor inside that moment; `NumberLock` does not, because its pads simply hold their counts.
    public abstract class RoomCondition : MonoBehaviour
    {
        // "This room's rule is met right now."
        public abstract bool Satisfied { get; }

        // The loop rewinding. Called from `Cycle.ResetRooms` with the other room resets, AFTER the
        // item sweep - anything a condition latched is world state exactly like a door's position,
        // and left standing it would survive into the next iteration and be re-satisfied on top of
        // itself by the ghosts.
        public abstract void ResetCondition();
    }
}
