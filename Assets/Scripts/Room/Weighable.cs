using UnityEngine;

namespace IterationRoom
{
    // WHAT SOMETHING WEIGHS, on the one object that has any business knowing.
    //
    // A whole component for one float is deliberate. The alternative - a table of weights inside
    // `WeighScale`, keyed by `itemId` - puts a fact about an AXE inside the room that happens to have
    // a scale in it, and every new carryable then needs an edit in a room it has never been to. This
    // way the scale asks whatever is on it what it weighs, and an object with no `Weighable` weighs
    // nothing at all, which is the correct answer for a chess piece that has wandered in.
    //
    // A GHOST WEIGHS NOTHING and neither does anything a ghost is holding: `WeighScale` skips carried
    // items, and a past self has no collider to stand on the pan with (CLAUDE.md §1.7). That is not a
    // limitation being worked around, it is the room's rule - what a past self CAN do is put an object
    // down on the pan, and the object is what the scale weighs.
    //
    // NOTHING HERE IS RECORDED OR RESET. A weight is a constant of the object; what changes is where
    // the object is, and the loop already puts that back.
    public class Weighable : MonoBehaviour
    {
        // Kilograms, and the display shows one decimal - so a tenth is the smallest difference the
        // player can ever read, and two objects that differ by less than that are the same object as
        // far as this room is concerned.
        public float kilograms = 1f;

        // WHAT IT IS CARRYING, for the one object whose weight is not a constant: a bucket weighs its
        // own mass plus the water in it. Left null for everything else.
        public Bucket bucket;
        public float contentsKilograms = 0f;

        public float Kilograms =>
            kilograms + (bucket != null ? Mathf.Clamp01(bucket.Level) * contentsKilograms : 0f);
    }
}
