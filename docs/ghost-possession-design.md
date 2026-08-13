# Ghost possession

**Status**: designed and implemented 2026-08-11. Widened the same day - the first version carried only the key, and **every carryable now participates** - and then tightened: **an action performed with a tool requires the tool in hand, for ghosts too** (section 6). Verified by runtime sweeps in play mode covering custody, scale, visibility, both re-evaluations, the completed-errand rule, drop-on-retire, a holder being destroyed, Tab, the total rewind, and the pop gate. **Not yet play-tested by a human** - see section 7.

Ghosts can hold things. **Anything a past self did, it does again, and picking something up is not an exception** — floor pads, the drawer and balloon pops already worked for ghosts, so an object they could touch but not hold was the odd one out. This reverses what `CLAUDE.md` used to call *"the most important rule in the project"*, so the reversal is argued here rather than asserted.

---

## 1. Why the old rule goes

The rule reads: **a ghost can replay an *action*, never a *possession*.** Three things are wrong with it.

**It is already false.** Ghosts pull the nightstand drawer open and burst balloons. Both are physical objects; one gets moved, the other destroyed. The real rule was never "ghosts cannot touch things" — it was the much narrower "ghosts have no inventory", which is an implementation limit wearing a principle's clothes.

**A play-tester expected the opposite.** In Room2 they unlocked the door with the key and expected it to still be unlocked next iteration. That is not a misunderstanding to be trained out. Players already model a ghost as *a person who was here*, and a person who unlocked a door leaves it unlocked. The code is the thing violating the expectation.

**The original failure it was written from was a different bug.** A version once re-evaluated a ghost's unlock against `BalloonField.KeyRevealed` — *the key's balloon has burst* — instead of *this ghost has the key*. That is an argument against **re-evaluating the wrong condition**, not against possession. Make possession recorded state and the condition re-evaluated becomes the one that actually enabled the action, which is the rule working, not the rule broken.

### What it buys

`CLAUDE.md` independently names all of these as live problems:

| Recorded problem | After |
| --- | --- |
| "Room2's exit costs the *player* a key retrieval in every iteration that goes past it, and that toll never accumulates the way Room1's pad does" | It accumulates |
| "Room3's pads set the run's floor… that is what makes the missing ghost reset press harder" | The floor stops growing |
| Backlog: **accumulation is the most valuable unexplored direction** | Opens |
| Backlog: *"accumulation works only if the tool belongs to the station, not the hand"* — an axe must be bolted to the tree | An axe can be an axe |
| Open question: chapters would fix the key toll, but **one unbroken loop is the premise** | Same fix, premise intact |

That last row is the strongest argument for doing this. It resolves two of the three problems chapters were being considered for, without cutting the loop up.

---

## 2. The invariant

**There is one of each object. It is never duplicated and never lost.**

Every carryable is in exactly one state:

1. At origin — in the drawer, or inside an unburst balloon
2. Held by the player
3. Held by exactly one ghost
4. Seated in a socket — the key in the lock
5. Loose in the world at a position

Ghost custody is **custody, not ownership**. Every path out of state 3 must land somewhere, and the set of paths must be total:

| Leaving a ghost's hands | Lands in |
| --- | --- |
| The ghost's recorded surrender | 4 — socket |
| **The ghost's timeline runs out** | 5 — dropped at its last position |
| The player takes it | 2 — player |
| Loop reset | 1 — origin |

The third row is what "stealing" is for. It is not a puzzle mechanic and should not be dressed as one: it is the path that stops a ghost's custody from being a black hole. Without it, a ghost that grabs the key at t=15 locks the player out of it forever.

The second row is the case that motivated writing this down. A ghost's timeline can be short — `EndCycleControl` truncates recordings, so a ghost made from an iteration ended at t=5 retires at t=5 of every future iteration. It must not take the key with it.

---

## 3. Recording

**Carry events are identity-based, never positional** — the same shape as `PopEvent`, and for the same reason already documented for balloons: recording "the player picked up here" and replaying it against a world that has moved makes a ghost's contribution luck.

```
CarryEvent { float time; string itemId; CarryKind kind }   // kind = Take | Surrender
```

A separate list on `RecordedTimeline`, not more bits in `RecordedFrame.signals`. A signal is a *level*; taking is an instant with an identity. Drained the way pops are — **before** the end-of-timeline check, so a single long frame cannot retire a ghost with its closing events unfired.

## 4. Replay, and the two re-evaluations

A ghost never simply replays a carry. Both ends are conditional:

- **Take** succeeds if the item is actually available — not player-held, not already in a socket — or, failing that, if a ghost is holding it (§5c: reopened 2026-08-13, and not player-held even then).
- **Surrender** succeeds only if *this ghost is holding the item*.

That is `record the attempt, re-evaluate the condition` applied honestly. If the player steals the key at t=25, the ghost's recorded t=40 insertion finds nothing in its hands and the door stays shut.

### ~~The completed-errand rule~~ — existed, and was removed 2026-08-13

This section is kept as the record of a rule that shipped, was load-bearing for a long time, and was then deliberately taken back out — not as a bug fix, as a design reversal, on explicit request. Read it as history.

Left unconditional, one failure was common and completely opaque to the player:

- Ghost **A** (from iteration 3): takes the key at t=20, puts it in the lock at t=40. The door opens.
- Ghost **B** (from iteration 5): takes the key at t=18, gets distracted, never delivers.

Replay both and **B** wins the key by two seconds, **A**'s whole chain dies, and the door that has been opening for five iterations stops — with nothing on screen to say why. Every iteration spent fetching the key minted another competitor.

> A ghost only replayed a take if that same recording also surrendered the item. Errands it finished, not errands it started.

Deterministic, decidable from the timeline alone, and explicable in one sentence. It also had a tidy consequence: the key was only ever in the hands of a ghost that was on its way to the lock. It applied only to items with a socket — the pin has none and is never surrendered, so unscoped the rule forbade ghosts to carry it at all, which was the first version's bug.

**Why it came back out**: it also blocked the opposite of a fumble — a take that was never MEANT to deliver anything, just to reclaim an item for the taking ghost's own later use (see §5c). Scoping around that case turned out to want the same knob as removing the rule entirely, and the decision was to just remove it: a recorded take is now a fact this ghost always tries to reproduce, whether or not it went anywhere. **The cost the rule existed to prevent is real and is now live**: whichever recording's take fires earliest at replay wins a contested item, and can silently break another ghost's own later delivery of the same one. Accepted on the argument that it is not unrecoverable within a run — see §5c for the full reasoning, which is the same reasoning that applies here.

This retires one emergent idea worth naming so it is not lost: a *courier*, where you carry the key halfway, press `N`, and a past self delivers it partway for you every iteration afterwards. It is a genuinely nice second meaning for `EndCycleControl`. It wanted exactly the half-finished errands the old rule discarded, and did not get built while the rule stood; it is unblocked now, but still not built.

### Two more conditions, re-evaluated for the same reason

- **`requiresOpenDrawer`.** The pin lives in the nightstand drawer, and the condition that enabled taking it was the drawer being *fully* open. A ghost's own replayed pull opens it, so in practice this always passes — the check is there so that stops being a coincidence.
- **A destroyed ghost releases what it holds** (`OnDestroy`). A carried item is *parented* to the carry anchor, so destroying the ghost destroys the item, and there is one of each object. Nothing destroys a ghost mid-run today — but **the ghost reset is precisely a feature that destroys ghosts**, and it would have shipped straight into a soft-locked run.

### Stranding

Whether or not the completed-errand rule exists, drop-on-retire is still necessary. A ghost can still be left holding the key: its recorded surrender can *fail* because another ghost already opened the door, and then it holds the key until its timeline ends. That is the case §2 covers.

---

## 5. Visibility, and what is in the hand

If a ghost's key is invisible, the player cannot tell who has it, cannot know who to take it from, and cannot understand why a door stopped opening. **Ghosts show what they carry.** The afterimage shader makes this work better than it would have a week ago: a solid gold key riding a translucent silhouette is the only opaque thing on that figure, readable across a room, needing no HUD at all.

**Tab cycles which item is in the hand** — carried items, then an empty-handed slot, then wrap. It replaced a fixed `showInHand` flag whose only job was stopping the key from knocking the pin out of view. And it changed what the lock asks for: `KeyLock` and `BalloonTool` now gate on `hand.Holding(id)`, not `hand.Has(id)`. **The key has to be out.**

That has to hold for ghosts too, or a ghost could unlock a door out of its pocket while the living player cannot. So equips are recorded (`CarryKind.Equip`, empty id = empty hands) and `TrySurrender` refuses unless the item is the one equipped. It also fixes a plainer problem: a ghost carrying both the pin and the key would otherwise put both at the same anchor, inside each other.

### 5b. A GHOST WEARS ITS WHOLE INVENTORY, and mirroring Tab was the mistake

The first version answered "both at the same anchor" by **hiding** everything but the equipped item — the ghost's copy of the player's Tab. That contradicts the rule at the top of this section, and it does so in a way that is worse than cosmetic: **`CarryableItem.IsAvailable` reads `visible`, so a hidden item is untakeable.** A past self carrying the pin and the key with the pin out held the key somewhere the living player could not see, could not learn about, and could not take. That is precisely the black hole §"taking it back off a ghost is just E" exists to prevent, reintroduced through the back door.

**The asymmetry that made mirroring Tab wrong: the player has a readout and a ghost has none.** `CarriedItemsDisplay` shows the player every item they carry, held at full strength and stowed at alpha 0.33 — so the player's stow costs them no information. A ghost has no such display, so hiding an object on a ghost destroys the only channel there is. **The objects on the body are the ghost's readout.**

That is also why a floating item list over the ghost's head is the wrong shape of fix. It restores the information and leaves the item untakeable, producing a label that names something the player is then unable to act on — the prompt-for-a-press-that-cannot-succeed failure that `AlreadyHaveOne` was added to stop. Showing the object restores both halves at once, and it does it in the game's own idiom: this project has repeatedly moved information off the HUD and onto walls and objects.

The real content of "both at the same anchor" was a complaint about **the anchor**, and the answer to it is a second anchor rather than invisibility. The equipped item is in the hand; everything else is laid out along a belt line across the hips (`GhostReplayer.LayOutCarried`). Hips, not the hand, because a stowed object should not swing with the arm; not the ghost root, because there it slides beside the figure and reads as dragged.

**Equipped and visible are now two questions instead of one.** `HoldingEquipped` is unchanged and still gates every tool-shaped action on the item in the hand, so a ghost wearing the key on its belt and the pin in its fist cannot unlock a door — the rule this whole section is about survives intact. What changed is only that you can see the key, and take it.

### 5c. ~~Ghost-to-ghost taking is closed, deliberately~~ IT IS OPEN NOW, on a different rule (2026-08-13)

`IsFreeForGhost` is still `!IsCarried && visible`, and an item in any ghost's hands still fails that test — `FindFreeForGhost` is unchanged. What changed is that `GhostReplayer.TryTake` no longer stops there: when the free lookup comes back empty, `ItemRegistry.FindHeldByGhost` finds one a ghost is holding, and the take proceeds against it.

The original close was reasoned from an **unrecorded** steal: nothing stops a ghost reaching for a free item whenever it likes, so if "free" also meant "or in another ghost's hands", the earliest-recorded taker would win every contested item regardless of which one actually finished the job — a recording that fetched the key and delivered it losing the key to one that fetched it two seconds earlier and fumbled, with nothing on screen to explain why a door that had been opening for five iterations stopped. That reasoning is still correct about *unrecorded* takes; it just turned out not to describe this one.

**The living player taking an item off a ghost is not unrecorded.** `PlayerHand.Take` writes an ordinary `CarryEvent(Take, ...)` regardless of whether the item came off the floor or out of a past self's hands — the recording does not distinguish the two, because the game does not: a take is a take. Reproducing that recorded event, at its own timestamp, against whichever ghost currently holds the item, is the same "record the attempt, re-evaluate the condition" rule every other replayed action follows.

**This was requested reopened WITHOUT the completed-errand gate** — deliberately, not an oversight, and it is what led to §4 being removed outright rather than merely not applying here: a version scoped to "the same feature, but only for a take that also delivered" was rejected, because a recorded take is state whether or not the errand it started ever finished. The cost that reopens is the one §4 existed to prevent: an iteration that grabs an item off a ghost and then does nothing with it will, from then on, intercept that item at the same instant every iteration — including out of the hands of a *different* ghost that is trying to deliver it on schedule, silently breaking a delivery chain that used to work.

**Accepted on a specific argument, not just knowingly**: a ghost whose delivery is broken this way does not lose anything else. §1.3/§1.5 already mean every recorded action re-evaluates its own condition independently — one failed `Surrender` does not touch that ghost's walk or any other room's errand it performs — so "the door doesn't open by itself any more" is the whole of the damage, and the living player can simply close it out themselves, the same way any room a ghost could not finish gets closed out. Nothing is unrecoverable within a run. If it surfaces as a worse problem than that in practice, `git log` has the completed-errand rule's old implementation to restore.

### 5d. Ghost-to-ghost taking needed the WRONG OBJECT bug fixed too (2026-08-13)

§5c reopened stealing, but play-testing it against the pins found a second, separate problem: **`itemId` alone is not enough identity for a supply.** A key or a chess piece has exactly one instance per id, so "took the Tool" and "took *this specific* pin" mean the same thing. A pin does not — three objects share `"Tool"` — and a Take recorded as "off ghost 1's pin" was replaying as "whichever pin happens to be free right now" via `FindFreeForGhost`, which finds a *different* pin if one is free and never touches ghost 1's at all. The visible result: ghost 1 keeps its own pin (nothing ever took it away, in THIS replay), a second ghost ALSO ends up holding a pin (a different physical one), and the drawer reads one short of what the player expected. Not a duplication — the three pin GameObjects never changed count — but not a reproduction of the recorded hand-over either, and indistinguishable from one at a glance since every pin looks identical.

**Fix: `CarryEvent` for a `Take` now also carries `instanceName`** — the taken object's own GameObject name (`"BalloonTool_1"`, fixed at build time, already unique within a supply; borrowed rather than inventing a new id scheme). `PlayerHand.Take` fills it from `item.name`; `ItemRegistry.FindInstance(itemId, instanceName)` resolves it back. `GhostReplayer.TryTake` tries that exact object FIRST — free or ghost-held alike, just never the living player's — and only falls through to `FindFreeForGhost` / `FindHeldByGhost` (§5c) when the named object cannot be found or is unavailable. Equip and Surrender did not need the same fix: a ghost holds at most one instance per id at a time, so `itemId` alone already picks the right one out of `held` for those two.

This does not reopen anything §5c did not already open. Stealing a *specific, named* pin off a *specific* ghost is a narrower claim than "steal whichever pin is free", not a wider one - the fallback path is exactly what shipped in §5c, unchanged, still scoped to sockets, still there for recordings (or objects) instanceName cannot resolve.

## 5a. The rewind has to be total

`PlayerHand.ReturnAll` walks what the **player** picked up. A key a **ghost** fetched and seated was in nobody's list — the ghost let go of it when the socket accepted it, and it was never in `taken` — so nothing rewound it. It stayed in the keyhole with `IsCarried` true, which makes `IsFreeForGhost` false, and **no ghost could ever take it again: Room2's door opened exactly once per run and then never.**

The symptom pointed at the replay; the cause was in the rewind. `ItemRegistry.ReturnAllToOrigin()` now sweeps every registered carryable at the top of an iteration, because the invariant is about the objects, not about who was tracking them.

A related sharp edge: `KeyLock.Inserting` is a **property, not a flag**. The insert coroutine only learns the world moved on its next frame, so a reset landing mid-turn leaves the flag set while the key is already gone, and every gate refuses. Treating "the key has left the socket" as the end of the insertion makes it deterministic instead of a race the wake-up happens to win.

---

## 6. Everything is carried, and a tool-shaped action requires the tool

This section has been through three positions; the current one is the third.

1. **Pin excluded from ghost custody.** Ghosts popped with empty hands, ungated.
2. **Pin carried, pops ungated.** Reasoning: a pop can only be *in* a recording because the pin was
   genuinely held, so replaying it manufactures nothing. That answers **legitimacy** but not
   **consistency** — it never explained why the key must be in hand and the pin need not be. It was
   also the worst cell: ghosts popped without the pin while `BalloonTool` still gated the *player*
   on holding it, so a ghost taking the pin penalised only the living player.
3. **Pin carried, pops gated on holding it** *(current)*. `GhostReplayer.requirePopTool`.

**The rule is general.** An interaction performed *with* an object is gated on that object being in
the hand, for a past self exactly as for the living player, and taking the object away stops the
interaction. Balloons are not a special case; they were the last exception.

Possession is checked through `HoldingEquipped()`, which verifies against the **item** rather than
trusting the cached `equippedId`. `PlayerHand.Take` pulls an item straight out of a ghost's hands
without asking, and trusting the cache let a ghost keep popping for the rest of the iteration with
the pin visibly in the player's hand.

### The cost, stated and accepted

There is one pin, so **at most one entity in the room can pop at a time.** The earliest-recorded
taker wins it and holds it to the end of its timeline — a tool has no socket to be surrendered to —
and every other ghost's recorded pops silently do nothing. **Room2's accumulation does not survive
this.** A player slower than their own past self finds an empty drawer and must take the pin off a
ghost first.

This was chosen with those consequences on the table, in favour of rules that hold uniformly.

### The mitigation

**A supply of pins, not a change to the rule.** `one object, never duplicated` is about *unique*
objects; the pin was never unique in spirit — it is a drawerful, and its job is **discovery** (find
the drawer), not **scarcity**. With several, every ghost that took one has one and the gate costs
nothing. Queued in `TODO.md`. Reverting instead is `requirePopTool = false`.

## 6a. Objects with no destination

The pin is the first item that is carried but never delivered, and it needed two rules relaxed: the completed-errand rule (§4) had to be scoped to socketed items, and `DropCarried` on retire had to be the normal end of its life rather than an edge case. A tool that is picked up, used, and dropped where the ghost stops is the general shape — the key, which goes somewhere and stays, is the special one.

**A dropped item lands on the FLOOR under where it stood, not at the height it was let go from.** `DropAt` takes a world position and uses only its XZ, putting the object at `CarryableItem.floorY`. A ghost's grip is a wrist bone about a metre up, so dropping at the hand's own position left the item hanging in mid-air — there is nothing to make it fall. **A carryable has no `Rigidbody`, and deliberately so:** the loop has to put every object back exactly at the top of an iteration, and a simulated fall settles somewhere slightly different each time. Placing it is also the answer `BalloonField` already gave for a key coming out of a burst balloon, which is why `floorY` now lives on the item and both paths read it rather than two constants having to agree.

---

## 7. What Room2 becomes

The door now opens **when the ghost gets there, not when the player arrives.** As the run gets faster the player will reach Room2 before the ghost does, and wait.

Room2's cost converts from *retrieving the key* (work) to *waiting for a past self* (clock). That is the same beat Room3 already builds toward — "standing at the door watching two past selves converge is the intended beat" — so it is at least consistent with the game's own language. Whether waiting is better than fetching is a play-test question, not a design one.

## 8. Consequences to watch

- **The ghost reset gets teeth.** Resetting ghosts now takes the key back and re-locks Room2. That may be good (resetting costs something real) or fatal (nobody can afford to reset). Either way, this change stops the ghost reset from being deferrable — the two have to be designed together.
- **Room1's teaching blurs slightly.** Room1's door deliberately does not latch, because "a door that latched open when the pad was first touched is solvable in one iteration". The player now learns that *some* things persist. Room2's key door already latches within an iteration, so this extends an existing difference rather than inventing one — but it should be a conscious call.
- **Only simple carryables are safe.** Both current items are binary: held, or not. A future carryable with real physical presence would diverge between the player's world and the ghost's, and this design does not cover that.
