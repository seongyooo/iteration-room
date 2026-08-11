# Decisions, open questions and rejected ideas

Ideas weighed and not taken, and the questions still open. Kept because the grounds could change, not as history for its own sake.

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
