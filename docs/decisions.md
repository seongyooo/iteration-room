# Decisions, open questions and rejected ideas

Ideas weighed and not taken, and the questions still open. Kept because the grounds could change, not as history for its own sake.

---

### Decided 2026-08-12: accumulated work is RE-PERFORMED, never stored

The tree that has to be felled, the aquarium that has to be filled, and anything else shaped like
"more work than one iteration holds" all read two ways, and the two are not close:

- **A — the damage persists.** The tree remembers being 40% cut, across iterations.
- **B — enough simultaneous ghosts inside ONE 60-second loop finish it.** Nothing is stored. Iteration
  N has N past selves, so the number of simultaneous axes rises on its own; once it is enough, the
  tree falls that iteration *and every iteration after*, because the ghosts always redo it.

**B.** A is the first piece of un-rewound state the game would have, and ghost possession was built
specifically so that would never be needed — *"a solved room stays solved without the loop keeping
un-rewound state"*. Room2's key already works exactly the way B does: a ghost re-fetches and
re-inserts it every sixty seconds, and the door is open every time without anything being remembered.

B also gives more of what the tree was for, not less. The wanted image is several past selves swinging
at once and the player walking through it — that is a picture of *simultaneous* work, which is what B
produces and what A does not: under A one ghost chipping away for twenty iterations would do.

The water works the same way. Valves, buckets and pumps are roles that have to be filled at the same
time, not deposits into a total.

**What this costs:** the threshold is now a headcount, not a budget. A puzzle needing five simultaneous
workers cannot be finished before iteration five whatever the player does, and every one of those
iterations must be *worth* playing on its own. The ~5-iteration floor stops being a floor and starts
being set by whichever accumulation puzzle wants the most hands at once.

---

### Decided 2026-08-12: the nightstand is built, not imported

`TODO.md` had this as a choice between building the carcass and sourcing a model with a real drawer
node, and named sourcing the smaller job. Built won, on one argument that is not about effort:
**there is no recess to import.** The imported `nightstand.glb` bakes its whole body into a single
mesh, so the drawer had been a generated slab bolted to a solid face — it slid forward to reveal the
two drawer fronts the model already had painted on it, in a colour that did not match. Sourcing a
model with a drawer node would have fixed that, and made every number about the drawer a measurement
off someone else's topology, re-measured on every re-export. Building it means the numbers that cut
the opening are the numbers that fill it, which is why the carcass and the drawer are one method.

What it cost: the model's lamp and pot had to be rebuilt from primitives, and are cruder for it — a
drum shade on a stem, and a green dome in a pot. Both were the reason that model was chosen, so
dropping them was not an option. **The lamp casts no light**, deliberately: the additional-light
shadow atlas is sized at 2048 for exactly the four fixtures that cast, and a fifth would take every
map down a tier without saying so.

One thing found only by rendering it: the tray had inherited the room's white prop material, and in
wood the pin vanished — a dark handle on dark wood. The tray is lined in pale grey for that reason
alone. `nightstand.glb` is now unreferenced; it stays on disk until someone decides to delete it.

---

### Decided 2026-08-11: a tool-shaped action requires the tool, for ghosts too

**The rule is general, not a balloon special case:** an interaction performed *with* an object is
gated on that object being in the hand, for a past self exactly as for the living player. Take the
object away and the interaction stops. `requirePopTool` on `GhostReplayer` implements it for pops;
`KeyLock` already did for the key.

**Chosen with the cost stated and accepted.** The costs below are real and were not disputed —
they were judged worth paying for a room where the rules hold uniformly.

#### What it costs, in one line

There is one pin, so **at most one entity in the room can pop at a time.** The earliest-recorded
taker wins it and holds it to the end of its timeline (a tool has no socket to be surrendered to);
every other ghost's recorded pops silently do nothing. Room2's accumulation — every past iteration's
pops replaying so the field shrinks — does not survive that. A player who is slower than their own
past self finds an empty drawer and must take the pin off a ghost before they can pop anything.

#### The mitigation, when it is wanted

**A supply of pins, not a change to the rule.** The `one object, never duplicated` invariant is about
*unique* objects; the pin was never unique in spirit — it is a drawerful, and its job is **discovery**
(find the drawer), not **scarcity**. With several, every ghost that took one has one, the gate stays
honest, accumulation survives, and nobody fights for a tool that gates nothing. Queued in `TODO.md`.

#### Reverting

`requirePopTool = false` on the ghost prefab. Nothing else changes.

---

### The argument that was weighed (kept — the grounds could change)

**The principle is right and is not disputed.** Every other tool-shaped interaction in the game
requires the tool in hand for the player *and* the ghost — `KeyLock` gates on `hand.Holding`, and
`AcceptFromGhost` checks the ghost has the key equipped. Balloon pops are the one exception:
`GhostReplayer.DrainPops` calls `PopById` with no possession check at all.

The usual defence — *a pop can only be in a recording because the pin was genuinely held, so
replaying it cannot manufacture anything* — answers **legitimacy**, not **consistency**. It shows a
ghost is not cheating. It does not explain why the key must be in hand and the pin need not be.

**It is blocked on there being exactly one pin.** Gate pops on possession with one pin and at most
one entity in the room can pop at any moment: the earliest-recorded taker wins the pin, holds it to
the end of its timeline (there is no socket to surrender it to), and every other ghost's recorded
pops die. Player or one ghost — never both. That is stuck, not merely slower, and it runs against
the loop's premise that each iteration should leave less to do.

**The unblocking change is a supply of pins, not a change to the rule.** The `one object, never
duplicated` invariant is about *unique* objects. The pin was never unique in spirit — it is a
drawerful of pins, and its job is **discovery** (find the drawer), not **scarcity**. With several,
every ghost that took one has one, pops can be gated honestly, accumulation survives, and the player
never fights for a tool that gates nothing.

Outcome: **the gate shipped with one pin**, the costs above accepted. The pin supply remains the
mitigation to reach for if the room turns out to be too slow to clear.

### Open question: one loop, or chapters?

Today every puzzle sits inside one 60-second loop, from one bed, with ghosts accumulating forever. The alternative is **chapters**: clear a section, wake in a *new* bed, start a fresh set of iterations.

Chapters would answer three live problems at once, which is why this needs deciding before more rooms go in rather than after:

- **The ghost reset** (next steps §2) gets its trigger for free — the chapter boundary *is* the reset, with a reason the player understands.
- ~~**Room2's key toll** stops compounding.~~ **Answered 2026-08-11 by ghost possession instead**, and answered without cutting the loop up. A ghost re-fetches and re-inserts the key every iteration, so a solved Room2 stays solved.
- ~~**The ~7-iteration floor stops growing.**~~ Also largely answered by the same change — a room whose work a ghost can carry out stops adding to the floor once it is solved.

**Two of the three reasons for chapters are now gone**, which weakens the case considerably: what is left is the ghost reset, and that has its own answer to find. The cost was always the thing the prototype is actually about — **one unbroken loop is the premise**, and cutting it into chapters makes each one a small puzzle box rather than a place you are trapped in. It also throws away accumulated ghosts, which are the visible record of the work; the ending's last image is two past selves holding pads, and that only lands because they were both earned in one run.

### Rejected, on grounds that could change

- **Etching an `E` glyph on every fixture** to teach interaction — fully diegetic, always true, free at runtime. Lost to the prompt disc because a permanent label is *permanently* on screen in a game whose whole texture is repetition, and because the prompt generalises to controls with no fixture to etch (the mouse button belongs to the thing in your hand). If the discs ever read as too game-like, this is the swap.
- **Letting the key stay in the lock** as world state the loop does not rewind, which would make Room2 cheap to re-cross. Rejected — but see the toll it leaves, which constrains every room behind it.
- **Spec deviations still open**: the floor pad sits on the bed's left where spec §3 says right (it follows `room_layout_sample.png` instead), and the ghost reset of §4.6 is not implemented.

---
