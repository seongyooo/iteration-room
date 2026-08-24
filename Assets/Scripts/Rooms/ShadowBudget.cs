using UnityEngine;

namespace IterationRoom
{
    // WHICH CEILING FIXTURE CASTS - the one nearest the player, and only that one.
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
    // fourteenth threw them sideways from one corner. **The nearest fixture is very nearly the one
    // overhead**, so every room gets shadows that fall where the ceiling says they should, at the same
    // cost as the one caster the game already paid for.
    //
    // ONE CASTER, and the reason it is one is now historical rather than binding. It was forced by the
    // player: four ceiling fixtures gave one pair of feet four full-body silhouettes and play called
    // it "like a skeleton". With no player shadow left, **raising `maxCasters` is available again** -
    // ordinary objects multiply into a soft pool rather than into extra limbs. What to fix first, if
    // more of them ever look muddy, is that these are POINT lights standing in for 1.4m emissive
    // panels: a real area source gives a penumbra where a point gives a hard edge.
    //
    // Gated on distance and refreshed a few times a second rather than every frame, the same shape
    // `CctvFeed` uses for the same reason: nothing here changes fast enough to be worth a frame.
    public class ShadowBudget : MonoBehaviour
    {
        // Every ceiling fixture in this cycle. Named by `SceneBuilder` rather than gathered by type at
        // runtime (CLAUDE.md §2).
        public Light[] fixtures;

        // A LITTLE OVER HALF A ROOM'S DIAGONAL. The shell is 8.75 x 10.5 and the fixtures sit at
        // +-1.75 x, +-2.6 z from its centre, so the furthest a player can stand from the nearest one
        // inside their own room is about 5.5m. Past this nothing casts at all - which is correct
        // rather than a saving: a shadow thrown from further than this is the long raking one the
        // whole change exists to get rid of.
        public float range = 7.5f;

        // **ONE.** Forced by the player's shadow, which no longer exists - see the note above before
        // raising it, and expect to pay one extra shadow-map render per fixture if you do.
        public int maxCasters = 1;

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
        private int current = -1;

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
            int best = -1;
            float bestDistance = range * range;

            for (int i = 0; i < fixtures.Length; i++)
            {
                if (fixtures[i] == null) continue;
                float d = (fixtures[i].transform.position - from).sqrMagnitude;
                if (d <= bestDistance) { bestDistance = d; best = i; }
            }

            // HOLD THE CURRENT ONE unless the challenger is clearly closer - and unless the current one
            // has gone out of range entirely, which is not a contest and must not be damped.
            if (current >= 0 && current < fixtures.Length && fixtures[current] != null && best != current)
            {
                float held = (fixtures[current].transform.position - from).sqrMagnitude;
                if (held <= range * range && bestDistance > held * switchMargin) best = current;
            }

            if (best == current) return;
            current = best;

            for (int i = 0; i < fixtures.Length; i++)
            {
                if (fixtures[i] == null) continue;
                LightShadows want = i == best ? LightShadows.Soft : LightShadows.None;
                // Assigned only on a change. Writing `shadows` is not free - it dirties the light -
                // and all but one of these is the same answer as last time.
                if (fixtures[i].shadows != want) fixtures[i].shadows = want;
            }
        }
    }
}
