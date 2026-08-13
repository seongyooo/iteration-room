using System.Collections;
using UnityEngine;

namespace IterationRoom
{
    // An object going INTO a socket, seen rather than teleported there.
    //
    // Every socket in the game used to call CarryableItem.InsertInto and be done: the object vanished
    // from the hand and appeared seated on the same frame. The only fixture that ever showed the move
    // was Room2's lock, which slides the key in and turns it - and that reads so much better than a
    // snap that the recesses wanted the same thing. This is that move, factored out so the cube room
    // and Room4's console share one, rather than becoming the second and third copy of KeyLock's.
    //
    // KeyLock is deliberately NOT rewritten onto this. Its version is a sequence - in, settle, turn,
    // and only then the door - where these are a single move, and folding a two-stage animation with
    // a door on the end into a shared helper would make the helper about the lock.
    //
    // THE OBJECT IS ALREADY IN THE SOCKET before this runs. InsertInto has been called, so the item
    // is parented, out of play and in state 4 of CLAUDE.md SS1.2; only where it is DRAWN is still
    // moving. That ordering is what makes the animation safe to interrupt - the object's ownership
    // never depended on the coroutine finishing.
    public static class SocketInsert
    {
        // `offered` is where the object starts, in the socket's own local space, and every caller
        // states its own because "out of the hole" is a different direction for a recess in a wall
        // and a well in the top of a console.
        public static IEnumerator Slide(CarryableItem item, Transform socket, Vector3 offered,
                                        Vector3 tiltEuler, float duration,
                                        AudioSource audioSource, AudioClip landClip)
        {
            if (item == null || socket == null) yield break;

            Transform t = item.transform;
            Quaternion tilted = Quaternion.Euler(tiltEuler);

            // Presented at the mouth rather than at the hand it came from. Starting from the hand
            // would read better still and cannot be done: InsertInto restores the object's FULL size
            // (a held one is scaled down to fit the view), so a slide that began at the hand would
            // begin with a metre of cube across the whole screen.
            t.localPosition = offered;
            t.localRotation = tilted;

            for (float e = 0f; e < duration; e += Time.deltaTime)
            {
                if (!StillIn(t, socket)) yield break;

                // Eased out: it goes in quickly and settles the last few centimetres slowly, which is
                // what a thing being placed by hand does. The same curve KeyLock's key arrives on.
                float u = Mathf.Clamp01(e / duration);
                float eased = 1f - (1f - u) * (1f - u);
                t.localPosition = Vector3.Lerp(offered, Vector3.zero, eased);
                t.localRotation = Quaternion.Slerp(tilted, Quaternion.identity, eased);
                yield return null;
            }

            if (!StillIn(t, socket)) yield break;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;

            // AT THE END, not at the press. The sound is the object touching the bottom of the
            // recess, and playing it on the press would put the clunk where the object is still in
            // the air.
            if (audioSource != null && landClip != null) audioSource.PlayOneShot(landClip);
        }

        // An iteration can end mid-slide. The reset reparents every item back to its origin, and a
        // coroutine still writing localPosition would drag it off across the room from there - the
        // exact failure KeyLock.StillInSocket exists to prevent. Losing the socket is the signal to
        // stop, and it needs no other bookkeeping: whoever moved the object already owns it.
        private static bool StillIn(Transform t, Transform socket) => t != null && t.parent == socket;
    }
}
