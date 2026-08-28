using UnityEngine;

namespace IterationRoom
{
    // WHICH CEILING FIXTURES CAST - the ones near the player, and no more than `maxCasters` of them.
    //
    // **THIS IS ABOUT THE ROOMS, NOT ABOUT THE PLAYER.** It was built on 2026-08-24 to fix the
    // player's own shadow and outlived it by an afternoon: the player's shadow was removed the same
    // day, after play, because no arrangement of it looked good enough
    // (`SceneBuilder.BuildPlayerBody` records what each attempt ruled out). What remains is what the
    // fixtures do for everything ELSE in the building - a chess piece, a cube, a bucket, a tree.
    //
    // It is still worth having, because the state it replaced was worse for those too: until that day
    // exactly ONE light in the whole building cast anything - Room1's, and within Room1 a fixed CORNER
    // fixture of the 2x2 ceiling grid. Thirteen of fourteen rooms had no shadows at all, and the
    // fourteenth threw them sideways from one corner.
    //
    // **SO WHY A BUDGET AT ALL, IF EVERY FIXTURE IN A ROOM IS ALLOWED TO CAST?** Because "every
    // fixture in a room" and "every fixture" are not the same number. The building is one corridor of
    // six shells with doors between them, so a view down it puts twenty-plus fixtures in the frustum
    // and each one that casts is another whole render of the geometry near it. The cap is what makes
    // the bill a property of the ROOM rather than of how far you can see - four, wherever you stand.
    //
    // What to fix first if the shadows ever look muddy rather than merely plural: these are POINT
    // lights standing in for 1.4m emissive panels, and a real area source of that size gives a wide
    // penumbra where a point gives a hard edge. That is a lighting-model problem, not a count.
    //
    // Gated on distance and refreshed a few times a second rather than every frame, the same shape
    // nothing here changes fast enough to be worth doing every frame.
    public class ShadowBudget : MonoBehaviour
    {
        // Every ceiling fixture in this cycle. Named by `SceneBuilder` rather than gathered by type at
        // runtime (CLAUDE.md §2).
        public Light[] fixtures;

        // **FAR ENOUGH TO HOLD A WHOLE ROOM'S CEILING, and that is the point of the number.**
        //
        // Derived: the shell is 8.75 x 10.5, its fixtures sit at +-1.75 x and +-2.6 z, and the eye is
        // 3.8m below them. From the worst corner of the room the FURTHEST of the four is 9.61m away,
        // so 10 keeps all four of the room you are standing in lit wherever you stand in it.
        //
        // That stability is worth more than the saving a tighter number would give. At 7.5 - which was
        // right when only ONE fixture cast - the far corners of the room dropped in and out of range
        // as the player walked, which is a shadow appearing and vanishing rather than a shadow moving.
        // With the whole room in range the SET does not change until you leave the room.
        public float range = 10f;

        // **FOUR: one per ceiling fixture, which is what the room actually has** (2026-08-24, by
        // request). Every object gets four shadows, dark where all four overlap under it and faint
        // where only one reaches - which is what a ceiling of four panels does.
        //
        // It was ONE until the player's shadow was removed, and only because of it: four human
        // silhouettes radiating from one pair of feet read as eight limbs. Nothing else in the
        // building has limbs to multiply.
        //
        // **RAISING THIS COSTS ATLAS AS WELL AS FRAMES**, and the atlas half is silent. Four casters
        // is four extra shadow-map renders a frame, and four maps that have to FIT: at
        // `m_AdditionalLightsShadowResolutionTierHigh` 2048 that is 4 x 2048 into a 4096 atlas, an
        // exact fit. Raise this again without raising the atlas and URP quietly halves every map and
        // says so once - see `ConfigureUrpAsset`.
        public int maxCasters = 4;

        // HOW MUCH CLOSER A RIVAL HAS TO BE BEFORE THE SHADOW MOVES TO IT.
        //
        // With one caster this is what stops the shadow flicking back and forth while the player walks
        // the line halfway between two fixtures, where the nearest flips on every sample. It costs
        // nothing to be a little late: both candidates are about equally far, so the shadow either
        // way is about equally right, and the ONE moment that reads as a bug is the flicker.
        public float switchMargin = 0.8f;

        // Five times a second. The player walks at 2.5 m/s, so this is a fifth of a stride.
        public float refreshRate = 5f;

        private float nextCheck;
        // Which fixtures are casting right now. Kept because `switchMargin` needs to know what it is
        // defending: a light already on has to be BEATEN, not merely matched.
        private bool[] on;
        private float[] scores;
        private bool[] reachable;

        private void Update()
        {
            if (fixtures == null || fixtures.Length == 0) return;
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + 1f / Mathf.Max(0.01f, refreshRate);

            Camera eye = PlayerLookup.Eye;
            if (eye == null) return;

            Apply(eye.transform.position);
        }

        private void Apply(Vector3 from)
        {
            int count = fixtures.Length;
            if (on == null || on.Length != count)
            {
                on = new bool[count];
                scores = new float[count];
                reachable = new bool[count];
            }

            // **RANGE IS TESTED ON THE TRUE DISTANCE, RANKING ON A DISCOUNTED ONE.** A fixture that is
            // already casting gets its distance multiplied by `switchMargin`, which makes it look
            // nearer than it is and so takes a clear win to displace - that is the whole hysteresis,
            // and doing it as a discount rather than as a special case keeps it working for any
            // `maxCasters`. Being out of range is not a contest and is never damped.
            float limit = range * range;
            for (int i = 0; i < count; i++)
            {
                if (fixtures[i] == null) { reachable[i] = false; continue; }
                float d = (fixtures[i].transform.position - from).sqrMagnitude;
                reachable[i] = d <= limit;
                scores[i] = on[i] ? d * switchMargin : d;
            }

            // The `maxCasters` best scores, by selection rather than by sorting: the cap is small, the
            // fixture count is not worth allocating for, and this runs a few times a second.
            bool changed = false;
            int wanted = Mathf.Max(0, maxCasters);
            for (int i = 0; i < count; i++)
            {
                bool pick = false;
                if (reachable[i])
                {
                    int better = 0;
                    for (int j = 0; j < count; j++)
                        if (j != i && reachable[j] && scores[j] < scores[i]) better++;
                    pick = better < wanted;
                }

                if (pick != on[i]) { on[i] = pick; changed = true; }
            }

            if (!changed) return;

            for (int i = 0; i < count; i++)
            {
                if (fixtures[i] == null) continue;
                LightShadows want = on[i] ? LightShadows.Soft : LightShadows.None;
                // Assigned only on a change. Writing `shadows` is not free - it dirties the light -
                // and most of these are the same answer as last time.
                if (fixtures[i].shadows != want) fixtures[i].shadows = want;
            }
        }
    }
}
