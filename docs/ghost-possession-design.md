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

- **Take** succeeds only if the item is actually available — not player-held, not already in a socket, not held by another ghost.
- **Surrender** succeeds only if *this ghost is holding the item*.

That is `record the attempt, re-evaluate the condition` applied honestly. If the player steals the key at t=25, the ghost's recorded t=40 insertion finds nothing in its hands and the door stays shut.

### The completed-errand rule — and what it applies to

Left at that, one failure would be common and completely opaque to the player:

- Ghost **A** (from iteration 3): takes the key at t=20, puts it in the lock at t=40. The door opens.
- Ghost **B** (from iteration 5): takes the key at t=18, gets distracted, never delivers.

Replay both and **B** wins the key by two seconds, **A**'s whole chain dies, and the door that has been opening for five iterations stops — with nothing on screen to say why. Every iteration spent fetching the key mints another competitor.

> **A ghost only replays a take if that same recording also surrenders the item.** Errands it finished, not errands it started.

Deterministic, decidable from the timeline alone, and explicable in one sentence. It also has a tidy consequence: **the key is only ever in the hands of a ghost that is on its way to the lock.**

**It applies ONLY to items that have a socket, and getting that wrong was the first version's bug.** The rule's job is arbitration — several ghosts want the one key, and a fumbled fetch must not rob a successful one. Arbitration needs a notion of *finished*, which needs somewhere to finish. The pin has no socket and is never surrendered, so by that measure every pin errand is unfinished, and the rule silently forbade ghosts to carry it at all. A rule about scarcity was blocking an item nobody was fighting over. Scoped to socketed items, both work.

This forecloses one emergent idea worth naming so it is not lost: a *courier*, where you carry the key halfway, press `N`, and a past self delivers it partway for you every iteration afterwards. It is a genuinely nice second meaning for `EndCycleControl`. It is also speculative, and it requires exactly the half-finished errands this rule discards. Not built.

### Two more conditions, re-evaluated for the same reason

- **`requiresOpenDrawer`.** The pin lives in the nightstand drawer, and the condition that enabled taking it was the drawer being *fully* open. A ghost's own replayed pull opens it, so in practice this always passes — the check is there so that stops being a coincidence.
- **A destroyed ghost releases what it holds** (`OnDestroy`). A carried item is *parented* to the carry anchor, so destroying the ghost destroys the item, and there is one of each object. Nothing destroys a ghost mid-run today — but **the ghost reset is precisely a feature that destroys ghosts**, and it would have shipped straight into a soft-locked run.

### Stranding

The completed-errand rule does not make drop-on-retire unnecessary. A ghost can still be left holding the key: its recorded surrender can *fail* because another ghost already opened the door, and then it holds the key until its timeline ends. That is the case §2 covers.

---

## 5. Visibility, and what is in the hand

If a ghost's key is invisible, the player cannot tell who has it, cannot know who to take it from, and cannot understand why a door stopped opening. **Ghosts show what they carry.** The afterimage shader makes this work better than it would have a week ago: a solid gold key riding a translucent silhouette is the only opaque thing on that figure, readable across a room, needing no HUD at all.

**Tab cycles which item is in the hand** — carried items, then an empty-handed slot, then wrap. It replaced a fixed `showInHand` flag whose only job was stopping the key from knocking the pin out of view. And it changed what the lock asks for: `KeyLock` and `BalloonTool` now gate on `hand.Holding(id)`, not `hand.Has(id)`. **The key has to be out.**

That has to hold for ghosts too, or a ghost could unlock a door out of its pocket while the living player cannot. So equips are recorded (`CarryKind.Equip`, empty id = empty hands) and `TrySurrender` refuses unless the item is the one equipped. It also fixes a plainer problem: a ghost carrying both the pin and the key would otherwise put both at the same anchor, inside each other.

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
