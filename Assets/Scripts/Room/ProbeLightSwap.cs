using UnityEngine;

namespace IterationRoom
{
    // **A ROOM WITH TWO LIGHTING STATES NEEDS TWO BAKED PROBES** (2026-09-04, by request).
    //
    // A reflection probe is a photograph taken at build time, and room2-1 is photographed with its
    // fixtures OFF - `SceneBuilder` douses them and swaps their glowing faces for an unlit material
    // while it builds, and the bake happens afterwards. Measured off the asset: that room's cubemap
    // has 0% of its pixels above 1.0 and peaks at 0.761, against 15.9% and 2.648 for an ordinary lit
    // room. There is no light in it at all.
    //
    // That is CORRECT while the room is dark, and it is why play could not find the square ceiling
    // panels in the walls after switching the lights on: the room lights up, and its reflection does
    // not, because a static cubemap cannot. So the room is baked twice and the pair is switched here
    // on the same condition that opens its door.
    //
    // **THE SWAP IS THE WHOLE COMPONENT.** It deliberately does not fade: a reflection is not a light
    // and there is nothing to cross-fade between two cubemaps with, so it changes on the frame the
    // condition changes - which is the frame the fixtures come on, and is hidden by that.
    public class ProbeLightSwap : MonoBehaviour
    {
        public ReflectionProbe probe;

        // The bake taken with the fixtures out - what `BakeReflectionProbes` produces normally,
        // since the room is dark for the whole build.
        public Cubemap dark;

        // The second bake, taken with the fixtures put back for the frame. Null is not a failure:
        // with nothing to swap to the room simply keeps the dark reflection it has always had.
        public Cubemap lit;

        // The room's own lit condition - the same object its door hangs on.
        public RoomCondition litWhen;

        // Nullable so the first Apply always writes, whatever state the probe was saved in.
        private bool? applied;

        private void OnEnable()
        {
            applied = null;
            Apply();
        }

        private void Update() => Apply();

        private void Apply()
        {
            if (probe == null) return;

            bool wantLit = litWhen == null || litWhen.Satisfied;
            if (applied == wantLit) return;

            Cubemap want = wantLit ? lit : dark;
            // A missing half leaves the other standing rather than blanking the probe - a room with
            // no reflection at all is a worse failure than one reflecting the wrong lighting state.
            if (want == null) return;

            applied = wantLit;
            probe.customBakedTexture = want;
        }
    }
}
