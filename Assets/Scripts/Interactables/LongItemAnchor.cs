using UnityEngine;

namespace IterationRoom
{
    // **E ANYWHERE ALONG A LONG OBJECT, not only at one point on it.**
    //
    // `CarryableItem` has ONE hint anchor, and every half of the press is asked about that one point:
    // `PlayerLookup.InView` asks whether it is on screen, `IsAimedAt` ranks it against everything
    // else by how far it sits from the middle of the view, and the prompt disc is drawn on it. For
    // anything that fits in a hand those three questions have one sensible answer and the anchor
    // never moves.
    //
    // The ladder is eight metres long. A fixed anchor means the press works where the anchor is and
    // nowhere else - stand at one end, look straight at the rung in front of you, and E does nothing
    // because the point being judged is four metres away down the ladder. Reported from play as
    // exactly that.
    //
    // So the anchor SLIDES. Every frame it moves to the point on the object's own length that the
    // player is looking nearest to, which makes "aim at it" and "aim at the anchor" the same act
    // again - and the disc lands on the rung under the crosshair rather than on the middle of the
    // thing.
    //
    // **NOT A SECOND ANCHOR, and not a list of them.** One moving point keeps `CarryableItem`,
    // `PlayerLookup` and `ControlHintDisplay` exactly as they are: the anchor is still one transform
    // and still the single answer to "where is this". Anything else would be a second description of
    // the same object for all three of them to keep in step.
    public class LongItemAnchor : MonoBehaviour
    {
        public CarryableItem item;
        public Transform anchor;


        private void LateUpdate()
        {
            if (item == null || anchor == null) return;

            // IN A HAND, IN A SOCKET, OR ON A GHOST: somebody else owns where this is, and there is
            // no prompt to place. Left where it was rather than reset, because the next frame it is
            // free the aim will move it anyway.
            if (item.IsCarried) return;

            Camera eye = PlayerLookup.Eye;
            if (eye == null) return;

            // READ OFF THE ITEM rather than kept here. It was a copy of `CarryableItem.heldSpan`,
            // and two statements of one measurement is one of them going stale.
            Vector3 ab = transform.TransformVector(item.heldSpan);
            float length = ab.magnitude;
            if (length < 0.05f) return;

            // Centred on the origin - see `CarryableItem.heldSpan`.
            Vector3 a = transform.position - ab * 0.5f;

            anchor.position = a + ab * ClosestOnSegment(a, ab / length, length,
                                                        eye.transform.position, eye.transform.forward);
        }

        // How far along the segment (0..1) the point nearest the player's LINE OF SIGHT is.
        //
        // The standard closest-approach of two lines, clamped to the segment. Where the two are
        // near-parallel the denominator vanishes and there is no meaningful answer - a player sighting
        // straight down the ladder is looking at all of it at once - so that case falls back to the
        // point nearest the EYE, which is the end they are standing at.
        private static float ClosestOnSegment(Vector3 a, Vector3 dir, float length,
                                              Vector3 from, Vector3 look)
        {
            Vector3 w = a - from;
            float b = Vector3.Dot(dir, look);
            float d = Vector3.Dot(dir, w);
            float e = Vector3.Dot(look, w);
            float denom = 1f - b * b;

            float along = Mathf.Abs(denom) > 1e-4f
                ? (b * e - d) / denom
                : -d;

            return Mathf.Clamp01(along / length);
        }
    }
}
