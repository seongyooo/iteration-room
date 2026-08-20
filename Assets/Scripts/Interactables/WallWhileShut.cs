using UnityEngine;

namespace IterationRoom
{
    // "THIS MOVES, BUT BAKE IT INTO THE REFLECTION PROBES ANYWAY."
    //
    // `SceneBuilder.MovesDuringPlay` keeps movers out of the probes, and it is right to: a door slab
    // baked in its closed position shows up as a closed door in the reflection of a room whose door
    // is standing open. Room3-1's gates are the one case where that rule gives the wrong answer, and
    // the reason is what the gates ARE.
    //
    // A gate leaf is not a slab in a doorway - it is a piece of the wall, built out of the same
    // panels at the same depth so that a shut gate cannot be told from the panelling. Leave it out of
    // the bake and there is a rectangle missing from the wall in every reflection: on surfaces at
    // 0.85 smoothness, which is almost entirely what they reflect, that rectangle is a permanent mark
    // saying "the door is here". The artifact the exclusion rule exists to prevent lasts only while a
    // door is open; this one lasts the whole game and defeats the concealment outright.
    //
    // So the trade is taken the other way round: baked SHUT, which is the state it is in for all but
    // a few seconds of any iteration. While a gate is open its reflection is a wall that is not there
    // any more - looked at through an opening the player is walking through.
    //
    // A marker with no behaviour. It is read once, at build time, by `MovesDuringPlay`.
    public class WallWhileShut : MonoBehaviour
    {
    }
}
