# Puzzle design

What each room asks of the player and why. Room-by-room reasoning, then the three shapes a loop puzzle can take.

---

## The puzzle

### Room1 — the pad and the door

Bed at (0, 0.7) (`messy_bed.glb`, head toward the door); nightstand at (-0.95, 1.35); floor pad at (2.8, -1.75); door centred on the north wall. Positions follow `room_layout_sample.png`.

- **Bed spawn is at (0, -0.7) facing 180°** — away from the bed, down the open room, as if you have just got up. It used to be at Z=-1.75, which left the bed nowhere near the player and failed to read as waking up *in* it. **The player cannot be spawned in the bed**: `PlaceModel` gives it a full-volume `BoxCollider` (X ±0.63, Y 0..0.89, Z -0.33..1.73) and a CharacterController placed inside gets shoved out on frame one. With the foot at Z=-0.33 and radius 0.3, **Z=-0.7 is as close as it can stand**.
  - Measured landmarks for re-tuning: mattress top Y=0.59, pillow centre (0, 0.59, 1.47), nightstand X -1.23..-0.67 / Z 1.16..1.54 — so the bed's **west** side is blocked, east is free.
  - The bed ships with **two pillows**, both off centre; `UseSinglePillow` hides one and slides the survivor onto X=0. They are separate glb nodes, so this is a transform tweak — a bed that baked its pillows into the bedding mesh could not be fixed this way.
- **The door has no control beside it and tracks its pad instead.** Step on, it opens across the room; step off, it shuts.
  - **Tracking rather than latching is load-bearing.** A door that latched open when the pad was first touched is solvable in one iteration — step on, stroll through — which deletes the premise the whole game rests on. Tracking also *teaches* the rule: the player watches the door open and shut with their own feet in the first seconds, and learns at that moment why one person cannot do both jobs at once.
  - **`DoorButton` is gone** (in git). A play-test found nobody could locate it: testers held the pad for an iteration, walked to the door in the next and expected it to open. They were right — the button was a second gesture the room never explained and it bought the puzzle nothing. Removing it also makes a ghost's contribution **visible from anywhere in the room**: the plate clunks, the lamp goes green and the door slides open in front of you. The wall beside the door is now bare, which is part of the point.
  - **The door refuses to shut on the player** (`doorwayClearance` 1.2m, tested against the *closed* slab's world position cached in `Awake`, since the door root sits at the room centre). A `CharacterController` is not pushed by a moving transform, so a slab closing through one leaves the player inside it. The **`openAmount > 0f` guard is not a shortcut**: without it, walking up to a shut door would hold it open forever. With it the reprieve only extends an already-open door, and cannot be used to reach one — pad to doorway is **7.7m against a 1.0s close** — 1.7s at `sprintSpeed` 4.5, 3.1s at `walkSpeed` 2.5, so it cannot be reached in time at either. **The margin points the way people first read it does not**: the number has to stay ABOVE the close, because what it prevents is stepping off the pad and beating your own door. Slowing the player makes it safer, never tighter; a sprint fast enough to cover 7.7m in under a second (7.7 m/s) is what would break it. Re-check if the pad moves, `openDuration` drops, or `sprintSpeed` is raised sharply. It doubles as anti-frustration: the intended solve no longer fails by half a second when a ghost's timing is short.
  - The open sound fires on the transition out of fully-shut, not from `Open()`. Closing is silent — that is the pad being released, which `FloorButton` already announces.

### Room2 — drawer, pin, balloons, key

**Deliberately not another pad-and-button.** The chain: the nightstand **drawer** in Room1 (E) holds a **pin tool** (E, held in hand) → walking into Room2 drops **70 translucent balloons** → swinging the tool (left mouse) bursts them → each one held holds a **key**, one of three colours → each key opens its own **key door** (E at the lock). All three keys are found in Room2; the doors they open now sit further up the corridor, one per join, in the order red → blue → yellow (Room2→Room2West, Room2West→Room2East, Room2East→Room3) — see `docs/room-geometry.md`.

- **A pop is recorded by *identity*, never by position** (`RecordedTimeline.PopEvent` carries a balloon id). This is the decision the whole thing rests on. Recording "the player swung here" and replaying it against physics that has moved everything makes a ghost's contribution luck; recording "burst balloon 17" means the ghost bursts balloon 17 wherever it has ended up, so progress accumulates exactly.
  - Accepted cost: a ghost can swing at air while a balloon bursts across the room. Tolerable because **ghosts have no colliders**, so only the living player scatters the field differently, and nobody eye-tracks one balloon among seventy.
  - Pops are an **event list, not more bits in `RecordedFrame.signals`** — there are more balloons than the mask is wide, and a signal is a *level* where a pop is an instant with an identity. `GhostReplayer.DrainPops` fires every event whose time has passed, so it survives a ghost skipping frames. **It drains before the end-of-timeline check** — after it, one long frame would retire a ghost with its closing pops unfired and those balloons would stay up for good.
- **The balloons are translucent and the key is visible inside the one that holds it.** This is what makes the search a matter of looking rather than of bursting seventy and hoping — luck in the first version, skill now. `MakeTranslucentMaterial` is the URP transparent set-up on **Lit**, not Unlit, because unlike a ghost these take the ceiling lights; the blend modes **and** the `_SURFACE_TYPE_TRANSPARENT` keyword are both required or the alpha does nothing. The in-balloon key is a **child of the balloon**, so it vanishes the instant the balloon bursts, and is a **darker** material than the loose key — at brass it washes out through the pink.
- **Balloon fall is hand-rolled, not gravity.** `useGravity` off, `fallAcceleration` 2.2 m/s² applied in `FixedUpdate`, damping 1.1, bouncy `PhysicsMaterial` (0.55, `bounceCombine` **Maximum** since floor and walls have none). Unity has no per-body gravity scale, and damping 9.81 down to a drift takes so much damping the balloons stop bouncing and read as underwater. **Low damping plus a small pull is light *and* lively; heavy damping is only slow.** Terminal velocity 2.0 m/s.
- **The player never collides with a balloon.** They are on their own layer (`EnsureLayer("Balloon")`, index 8) which the `CharacterController` excludes via `excludeLayers`; they stay solid to each other, the floor and the walls. Pushing is an explicit `OverlapCapsule` in `FirstPersonController.PushOverlapping`.
  - **This is the fix for "standing on a balloon lifts you into the air", chosen over the one-line version on purpose.** The old push took its direction from `hit.moveDirection`; standing still the controller's only motion is gravity `(0,-1,0)`, so both horizontal components were zero and the token lift normalised into a **pure upward shove**, re-applied every frame — the balloon rose, `stepOffset` walked the player onto it, and you rode it to the ceiling. Guarding the lift fixes that instance; removing the contact removes the class.
  - **Pushed by speed, not force, and horizontally only.** A capped `VelocityChange` at the centre of mass (off-centre mostly spins a sphere), along the horizontal offset from the controller's axis — geometry, not direction of travel, so it is well defined however the player moves. An early 0.55 impulse on a 0.04kg balloon was an 11 m/s kick off a brushed shin. The cap also means walking through a crowd nudges each balloon once rather than accelerating it every frame.
  - The sweep is skipped with no horizontal input, and `horizontalMove` is **cleared when `ControlEnabled` goes false** — the wake-up takes control mid-stride and a stale vector would blow balloons apart with the player's eyes shut.
- **The balloons are a pool, never instantiated at runtime**, which is what makes an id mean the same thing sixty seconds later. Spawn points and the key balloon (**index 52**) come from one fixed seed (`fieldSeed` 20260810) — reseed per iteration and the room is a different room each time, with nothing to learn.
  - `BalloonField` **parks the pool during `Build()`** (`ComputeSpawnPoints` is public for this). Built objects sit at their parent's origin, so left to `Awake` the saved scene stores seventy balloons heaped beside the bed.
  - `Balloon` resolves components through **`EnsureRefs()` rather than `Awake`**: Awake order is undefined and `BalloonField` resets the pool from its own `Awake`, so references would still be null and the reset would silently do nothing.
- **The drop triggers on anyone walking in — player *or* ghost** (`TriggerIfInside`). Ghosts are deterministic, so once one has been through, the balloons come down at the same instant every iteration, which keeps later recordings aligned with the field they were made against.
- **Carrying is world state and the loop rewinds it** (`PlayerHand.ReturnAll`, called *before* `BalloonField.ResetField` so the key is handed back and then hidden, not the other way round). Leave the tool in hand across a reset and the trip to the drawer stops costing anything.
- **The whole nightstand is generated geometry, drawer and all** (`BuildNightstand`). It began as `nightstand.glb` with a drawer bolted to its front, because the model bakes its body into one mesh and has no drawer node to pull — and that read as exactly what it was, a pale slab stuck on a dark cabinet sliding out over the two drawer fronts already painted on the model. There is no recess to import, so the carcass is built to have one, and the same numbers cut the opening and fill it. The drawer faces **-Z**, toward the foot of the bed where the player wakes up looking. The model was deleted 2026-08-14; the lamp and the pot it was chosen for are rebuilt from primitives.
- The tool gates on `Drawer.**IsFullyOpen**`, not `IsOpen` — otherwise one E press both opens the drawer and empties it, with script execution order deciding whether it worked, which reads as the item being unobtainable half the time.
- **The key lock is not a `GhostInteractable`, but a ghost CAN now unlock it** — by carrying the key there, through `IItemSocket.AcceptFromGhost`. This reverses what this file used to call its most important rule ("a ghost can replay an *action*, never a *possession*"), and the reversal is argued in full in **`ghost-possession-design.md`** — read that before touching any of it. The short version: the rule was written from the `BalloonField.KeyRevealed` bug, where a ghost's unlock was re-evaluated against *the key's balloon has burst* instead of *this ghost has the key*. That is a case of re-evaluating **the wrong condition**, not proof that possession cannot be re-evaluated at all. Possession is now recorded state, so the condition checked is the one that actually enabled the action, and *record the attempt, re-evaluate the condition* holds exactly as before.
  - **The rule it replaces was also already false.** Ghosts pull the drawer open and burst balloons — one physical object moved, another destroyed. What was really true was the much narrower "ghosts have no inventory", which was an implementation limit wearing a principle's clothes.
  - The trigger was a play-test: someone unlocked Room2 and **expected the door to still be unlocked next iteration.** Players already model a ghost as a person who was here, and a person who unlocks a door leaves it unlocked.
- **~~TAB CYCLES WHAT IS IN THE HAND~~ TAB IS GONE, 2026-08-13 by request: THE HAND HOLDS ONE OBJECT OR NONE.** `PlayerHand.Held` is the whole of what the player carries. There is no pocket, no `hand.Has`, and no `CarryKind.Equip`.
  - **What it cost to keep, and what removing it bought.** Tab existed for one reason — picking the key up must not knock the pin out of the hand you needed to get it with — and it paid for that with a second fact about every object (carried, *and* in hand) that every fixture and every ghost action had to keep in agreement. One object collapses the two: `hand.Holding(id)` is the only test there is, and a ghost cannot put two things in one fist because it can never have two.
  - **E is the whole of handling an object**: press to take, press again to put down. `CarryKind` is `Take | Surrender | Drop`, and a drop is recorded like the other two — a ghost that dropped something has to drop it again, or the world a recording leaves behind is not the world it was made in.
    - Which E does is decided by whether anything else claims the press. Every fixture calls `PlayerHand.MarkInteract()`; `PlayerHand.LateUpdate` runs after every fixture's `Update` and drops what is in hand only if the frame went unclaimed. So E at a recess with the right cube inserts it, and E anywhere else puts it down, with nobody having to know about anybody else.
  - **Hands full REFUSES a take rather than swapping.** E has one meaning at a time; a press that silently exchanged one object for another would be a third meaning with no prompt to announce it.
  - **Taking auto-equips**, trivially — there is nowhere else for it to go.
  - **The consequence is the endgame.** Three escape objects can no longer reach Room4 in one trip, so a clear now means past selves delivering what they delivered while the living player carries the last one. That is the game's own thesis — *ghosts do the work, I only finish* — stated as a rule instead of as a convenience. It also voids every pacing figure measured before it.
    - **The known sharp edge**: `GhostReplayer.TrySurrender` fires once at its recorded timestamp and does not retry, and `FinalSlot.Live` requires the living player to have reached Room4 already. A delivery recorded earlier than this run's arrival is refused and the ghost carries the object to the end of its timeline. **A clear on 2026-08-14 was not stopped by it** — whether it was ever hit is unknown, since a refused delivery is indistinguishable from a ghost that never had the object. Left as-is.
  - `CarriedItemsDisplay` draws the one held item, and carries the `[E] — PUT DOWN` prompt that used to teach Tab. See `loop-and-ui.md`.
- **Held objects are their TRUE SIZE**, 2026-08-13 by request. `handLocalScale` is the object's own world scale rather than a shrink-to-fit, and `SceneBuilder.HandPoseFor(size)` holds bigger things further out and lower — sub-linearly, because scaling the distance in step with the object would put everything back at the same angular size and undo the point. A metre of glass genuinely gets in the way; that is the accepted trade and `HandPoseFor` is the one place to tune it.
  - `handLocalScale` is still **required** for anything under a scaled parent. Every chess piece is a child of a set scaled 0.3039, so without it a piece is handed over at 1/0.3039 of its size — a 3.5m king. What went is the extra factor that shrank it *below* board size on top of that.
- **Inserting the key is literal.** E at the lock calls `PlayerHand.Surrender`, which drops it from the carried set (so the HUD loses the icon) but **keeps it in `taken`** so `ReturnAll` still rewinds it; `CarryableItem.InsertInto` parents it to a `KeySocket` and makes it visible. "A ghost cannot do this" becomes something you watch happen rather than a rule in a file.

#### The key model and the insert-and-turn

`Assets/ArtAssets/Furniture/gold_key.glb` — 4,194 triangles, one flat-colour material, no textures. It replaced five primitives (ring bow, shaft, two teeth), and swapping the model is the smaller half of the change: the key now goes **into** the lock and is **turned**, where before it was laid flat against the plate like a sticker.

- **`KeyLock.InsertAndTurn` is a coroutine: slide in (0.35s, eased out — the last few millimetres are the pins taking it), settle (0.08s), turn 90° (0.30s, smoothstep), and only then `door.Open()`.** The settle beat is what stops the slide and the turn reading as one continuous swipe. Total ~0.73s, added to a toll Room2 already charges every iteration — small, but it is the reason not to make it more elaborate.
  - **The door's own sound lands at the end of the turn and there is no separate lock click.** That is the right read anyway — the turn is what releases the door — and it is why this needed no addition to `generate_sfx.py`.
  - **`inserting` gates `Update` as well as `WantsInteractHint`.** `IsSpent` is `door.IsOpen`, which is still false for the whole animation, so without it a second E would surrender a key the player no longer has and start the sequence on top of itself.
  - **`CanOpen` is true while inserting**, so the lamp does not drop to red for the three quarters of a second between the key leaving the pocket and the door moving — announcing a failure in the middle of a success.
  - **An iteration can end mid-turn.** `PlayerHand.ReturnAll` reparents the key, and a coroutine still writing `localPosition` would drag it across the room and then open a door the loop had just shut. `StillInSocket` checks the parent every frame and bails.
- **`BuildKeyModel` establishes the frame everything else depends on: +Z is into the lock, so inserting is a slide along local Z and turning is a roll about it.** Two rotations composed in order, **not** one Euler triple — Unity evaluates Euler as `Y*X*Z`, so a triple would roll before the lay-down and put the bit back where it started. The roll exists because the keyhole's slot is vertical and a key entering it sideways is the wrong key.
  - The model is offset so the **shaft's own axis** sits on the parent's Z axis. Left alone it is 0.75mm off, which is the difference between a key turning and a key being waggled.
  - **The origin is the BOW, deliberately** — it is the point that seats on the plate face, and `InsertInto` drops the root exactly there. That is wrong for the copy inside a balloon, which ends up shouldered against one side of a 0.24-radius sphere instead of suspended in it; `centreOnParent` measures the bounds and subtracts them for that one case.
- **The socket is a seated position, not a parking spot.** The plate face is at z −0.045; putting the bow junction exactly there leaves the bow proud and the shaft buried. Behind the plate is 0.015 of air and then 0.125 of wall, so the 0.13 of shaft and bit has somewhere to be and is hidden the whole way — the tip stops **0.10 short** of Room3.
- **Length 0.20m, matching the primitives it replaced.** Not a free choice: Room2's puzzle is spotting the key *through* a balloon from across the room, and that was play-tested at this size.
- **`metallicFactor` is dropped 1.0 → 0.3, and this is the one glTF value overridden.** A fully metallic surface has **no diffuse term** — everything it shows is a reflection — and whatever this room's baked probes hand a metal, it is not the white box they were baked in: the key rendered **black with a thin gold rim** (the rim being the only direct specular) floating in the middle of a bright white room. Verified it was the metalness and not the lighting — forcing `occlusionTexture_strength` to 0 changed nothing, and 0.3 brought the gold back.
  - This departs from **keep the glTF materials these ship with**. That rule exists to stop a hand-rolled stand-in dropping a model's metallic/roughness **maps**, and this model has no textures at all, only factors. It is also the more robust setting: a key with a diffuse term reads as gold wherever it is put.
  - **Worth knowing beyond the key: this project cannot currently light a pure metal.** Anything imported at `metallicFactor` 1 will come in black. Whether that is the probes or the fallback is unresolved.
  - Both keys use **clones** in `Assets/Materials/` (`KeyGold`, `KeyInBalloon`), because glb materials are sub-assets regenerated on every reimport — the same trap `DisableBakedOcclusion` works around. The in-balloon one is the same colour scaled to 0.36, scaled rather than set to a chosen triple so it stays correct whichever colour space the glTF shader reads `baseColorFactor` in.
- **`KeyRestRotation` (bow up) lives on the key ROOT, not on the model**, and that is load-bearing: `InsertInto` zeroes the root's rotation, which is exactly the frame the animation works in, and `ReturnToOrigin` puts it back at the top of the loop.
- The HUD icon is still **drawn**, not rendered from the model — at 58px the model's bit resolves to a smudge.
- **The key does not stay in the lock across the loop** — but a ghost now re-fetches and re-inserts it every iteration, so the *effect* accumulates while the state still rewinds. That retires the constraint this file used to record here ("Room2's exit costs the player a key retrieval in every iteration that goes past it, and that toll never accumulates the way Room1's pad does"), and it retires it **without** cutting the run into chapters — which the open question below was weighing precisely because chapters would have cost the premise. **Room2's cost converts from fetching to waiting**: the door opens when the ghost gets there, so a fast run now arrives and waits, the way Room3 already asks you to.

#### Ghost custody — the short version (`ghost-possession-design.md` is the long one)

- **One object, never duplicated, never lost.** A carryable is at origin, on the player, in exactly one ghost's hand, in a socket, or loose on the floor. Every path out of a ghost's hands has to land somewhere or an object that exists once in the game is gone.
- **Carries are recorded by identity** (`CarryEvent`, a third list on `RecordedTimeline` beside `pops`) — never by position, for the reason already established for balloons. Drained before the end-of-timeline check, like pops.
- **Both ends are re-evaluated.** A take only lands if the item is genuinely free (`IsFreeForGhost`); a surrender only lands if that ghost is genuinely holding it. Steal the key off a ghost and its recorded unlock finds an empty hand and the door stays shut.
- **The completed-errand rule: a ghost only replays a take if that same recording also surrendered the item — and it applies ONLY to items with a socket.** Without it, an iteration where you fetched the key and fumbled mints a ghost that grabs the key two seconds earlier than the ghost that delivers, kills its chain, and stops a door that had been opening for five iterations, with nothing on screen to explain it. Every fetch mints another competitor, so this is not a rare collision. **Scoping it to socketed items is not a detail** — the rule arbitrates scarcity, arbitration needs a notion of *finished*, and finished needs somewhere to finish. Unscoped it silently forbade ghosts to carry the pin at all, which is exactly the bug it shipped with. It also forecloses a *courier* (carry the key halfway, press N, let a past self deliver it) — a nice idea that needs exactly the half-finished errands this discards.
- **`requiresOpenDrawer` is re-evaluated too.** A ghost may no more take the pin through a shut drawer than the player can. Its own replayed pull opens it, so this always passes in practice; the check is there so that stops being a coincidence.
- **`OnDestroy` releases what a ghost is holding.** A carried item is *parented* to the carry anchor, so destroying the ghost destroys the item — and there is one of each object. Nothing destroys a ghost mid-run today, but **the ghost reset is precisely a feature that destroys ghosts** and would have shipped into a soft-locked run.
- **A retiring ghost DROPS what it holds** (`DropCarried`), it does not vanish with it, and it drops it **on the floor** — `DropAt` uses only the XZ of where the item was and puts it at `CarryableItem.floorY`, because a ghost's grip is a wrist bone a metre up and nothing makes a carryable fall (no `Rigidbody`, deliberately — the loop has to rewind every object exactly). Reachable because a surrender can be *refused* — Room2's lock says no once its door is already open — and then that ghost carries the key to the end of its timeline. `EndCycleControl` makes short timelines ordinary, so this is a common path, not an exotic one.
- **Taking it back off a ghost is just E.** The item's trigger stays enabled and rides the ghost, so proximity and the existing prompt work unchanged — no new verb. This is not a puzzle mechanic; it is what stops a ghost's custody being a black hole (grab the key at t=15 and the player is locked out of it forever).
- **Ghosts show what they carry**, which is what makes "who has the key" and "why did the door stop opening" answerable at all. The afterimage shader is what sells it — a solid gold key is the only opaque thing on a translucent figure.
  - **ALL of it, not just the equipped one, and that took a second pass.** A ghost mirrored the player's Tab: equipped item in the hand, everything else hidden. That quietly broke the rule above the moment a past self carried two things — and it broke more than the *seeing*, because `CarryableItem.IsAvailable` reads `visible`, so a stowed item was **untakeable**. A ghost holding pin and key with the pin out put the key somewhere the player could neither find nor reach, with no way to learn it was there.
  - The old justification was that the living player cannot reach into a past self's pocket any more than into their own. **The asymmetry it missed is the readout**: the player has `CarriedItemsDisplay` telling them what is in their own pockets, and a ghost has nothing of the kind. The objects on the body ARE the ghost's readout, which is why showing them is the fix rather than hanging a label over its head — a label would have said "this ghost has the key" over an E press that still could not take it, which is exactly the prompt-for-an-impossible-press failure `AlreadyHaveOne` exists to prevent.
  - **Equipped and visible became two questions.** `GhostReplayer.HoldingEquipped` still gates every tool-shaped action on the one in the hand, so a ghost with the key on its belt and the pin in its fist still cannot unlock a door. Nothing about the tool rule moved.
  - The belt is on the **hips**: not the hand, because a stowed object should not swing with the arm; not the ghost root, because there it slides alongside the figure and reads as being dragged. Laid out centred and rebuilt from scratch whenever custody changes, including when the player takes one — otherwise a gap is left where an object the player was just shown used to be.
- **A ghost still cannot take from another ghost**, and that stays shut deliberately. `IsFreeForGhost` is `!IsCarried && visible`, and an item in any ghost's hands fails the first test. Opening it would let past selves rob each other mid-errand — a recording that fetched the key and delivered it losing to one that fetched it two seconds earlier and fumbled — which is the exact failure the completed-errand rule was written to prevent. The traffic is one-way: only the living player takes from a ghost.
- **THE RESET SWEEPS THE REGISTRY, not just the player's `taken` list** (`ItemRegistry.ReturnAllToOrigin`, called from the loop right after `PlayerHand.ReturnAll`). This was a **shipped bug and the symptom pointed at the wrong place**: a ghost would open Room2 once and never again. `ReturnAll` walks what the *player* picked up, so a key a *ghost* fetched and seated was rewound by nobody — it stayed in the socket with `IsCarried` true, `IsFreeForGhost` false, and no ghost could ever take it again. The fault was in the rewind, not the replay. Anything holding an object now has its own release (`ghost.ReleaseCarried`, `ReturnAll`), and the sweep catches whatever those two miss.
- **`KeyLock.Inserting` is a self-healing property, not a raw flag.** The insert coroutine only notices the world moved on its *next* frame, so a reset landing mid-turn leaves `inserting` true while the key is already back in its balloon — and every gate refuses. In the running game the wake-up hands over dozens of frames and the coroutine always wins; the property makes it not a race at all, by treating "the key has left the socket" as the end of the insertion whatever the flag says.
- **`ItemRegistry` resolves an itemId to a SUPPLY of objects and to its one socket**, self-registered in `OnEnable`. Unlike `ghostInteractables` this is a lookup by name with no bit positions, so order does not matter. It used to log an error at a second claimant; an id can now name several objects (three pins), so what remains is a log line whenever a pool grows past one — an id reporting a size nobody intended is the collision that error existed to catch.
- **MEASURED 2026-08-12, and it corrects what is written below: Room2's POPPING IS NOT ACCUMULATIVE.** That run was the four-iteration one, whose exit was crossing Room3's threshold — the 15-iteration figure elsewhere in this file is the later game, and nothing about the pins changed between them. It was cleared with three pins in the drawer and the change was not felt, for a reason the design already contains — the key is visible *through* its balloon from across the room, so the task is spot-it-and-pop-one, not search. There is nothing for extra poppers to divide.
  - **Room2's real accumulation is the DELIVERY**, and that does work: a ghost re-fetches and re-inserts the key every iteration, so the door opens without the player (see the key-door notes above).
  - So the pin supply is correct infrastructure with **no consumer yet**. It stops being idle the moment Room2 holds more than one key: several keys behind several locks is more errands than one iteration has time for, which is what makes iterations divide the work and what makes simultaneous poppers matter. That is the argument for the Room2 expansion in `TODO.md`.
- **THE PIN IS CARRIED TOO** — `ghostCarryable` defaults **true** and nothing sets it false. A first pass excluded it, reasoning that one pin means one popper and Room2's accumulation would die. That conflated *holding the pin* with *being able to pop*.
  - **`DrainPops` calls `PopById` with no possession check, and that must never change.** Gate it on the one pin and exactly one ghost can pop — which mattered less than this line assumed, per the measurement above, but will matter again the moment there is more than one key.
  - It costs nothing in honesty: `BalloonTool` gates the player on `hand.Has("Tool")`, so **a pop can only be in a recording because the pin was genuinely held when it was made.** Replaying it cannot manufacture a pop that never happened — which is exactly what separates it from `KeyRevealed`, where a ghost could unlock a door in an iteration where nobody ever held the key.
  - What one pin actually means: **the player races their past selves for it.** Whoever gets there first holds it; the loser finds an empty drawer and takes it off the ghost with E. Self-balancing, since a ghost's take fires at a timestamp from a slower earlier iteration. A ghost that loses still pops, empty-handed — which is what every ghost did before this change.
  - **The pin is the general shape and the key is the special one**: picked up, used, and dropped where the ghost stops. The key going somewhere and staying is the exception, and it is the only reason sockets exist.
- **The ghost's `CarryAnchor` is TWO objects and both are load-bearing.** The FBX has a `HumanArmature` node at scale 100 inside a body scaled to 0.377, so a bone's `lossyScale` is **37.7**: the outer node cancels that, and it must sit at localPosition zero because an offset written there would be in the 37.7× space too (3cm arriving as 1.13m — this was the bug). The grip offset goes on the child, where the scale is already 1 and centimetres mean centimetres. `AttachToGhost` therefore uses **zero**, not `handLocalPosition` — those numbers are a first-person framing slung under the player's camera and mean nothing on a wrist bone.
- **Reset ordering gained a step**: `ghost.ReleaseCarried()` runs *before* `PlayerHand.ReturnAll()`, which runs before `BalloonField.ResetField()`. Same reason as the existing pair — an item has to be back at its parked position before the thing that hides it runs. It is separate from `ResetPlayback` because that runs *after* `ResetField`, by which point handing the key back would un-hide it.
- **A ghost cannot take from another ghost**, only the player can. Untested territory with no evidence it is wanted; `IsFreeForGhost` is the line that draws it.
- **The lock's face is a keyhole, not a key** (`BuildKeyholeSlot` — a round seat and a slot, flat decals rather than a bored hole, which would show the wall behind). An etched key was right while nothing could be put in it; with a real key in the socket, an inserted key over an engraved one reads as two keys.
- `DoorIndicator` takes an optional `keyLock` so this lamp reports "you are carrying the key" the way Room1's reports "the pad is held".

### Room2West — the chess set, and the first accumulation puzzle

**Through the red door out of Room2: a chess set with twelve of its thirty-two pieces scattered across the room floor, and a board waiting for them. Put every piece back and the room's lights come up.** Left mouse places a held piece; it only goes on **its own square**, which lights up while it is in your hand. (Room2West used to be a side room reached by a door on Room2's own wall; it is a stop on the main corridor now, between Room2 and Room2East — see `docs/room-geometry.md`.)

This is the first room built to the **accumulation** shape (see the three shapes below), and it is the shape the loop was always for: twelve errands do not fit in sixty seconds, so the work is split across iterations, and **every delivery a past self made replays**. An iteration spent badly still counts for something here, which is the thing no other room in the game can say.

- **Ghosts tidy it for free, and that is a consequence of the existing design rather than a feature written for it.** `ChessBoard` registers **one `IItemSocket` per piece**, each of which is that piece's own square. A ghost's recorded hand-over carries an `itemId` and nothing else; `ItemRegistry` turns that id into a socket; `GhostReplayer.TrySurrender` puts the piece in it. **`GhostReplayer` learns nothing about chess.** The completed-errand rule scopes itself correctly too — a piece has a socket, so a ghost only repeats a pickup it also delivered.
- **The home square is where the piece started.** `SceneBuilder` measures every piece's pose off the model's own opening position and *only then* scatters twelve of them. The answer is the board as chess sets in the world are already arranged: nothing invented, nothing to look up.
- **It therefore is not a knowledge puzzle, which is what this file previously assumed chess had to be.** A puzzle whose difficulty is *remembering* where a piece goes costs zero iterations once learned. This one **tells you** where the piece in your hand goes and charges you the **walk** — the cost is in metres and iterations, which is the currency the loop is denominated in.
- **The lit square is the whole of the teaching**, and it is what makes "only its own square" fair rather than fussy. Snapping to whatever square was clicked would leave a piece somewhere plausible and wrong with nothing to say so; refusing means the lit slab is not decoration, it is the one place the click works.
- **The left-button prompt shows ONCE, and only from close to the square it points at.** Both halves matter. *Once*, because unlike E — a control used constantly at a different fixture every time — this one has the lit square standing next to it doing the pointing, so the disc only has to say which button, and nobody forgets a button they have just pressed. *Close* (4m, against the 6m the placement itself reaches), because hung on the marker the moment a piece entered the hand it would appear the instant the player picked something up on the far side of the room, describing a click that cannot reach — which makes it a HUD element about a control rather than a label on what the control acts on. The lit square itself never retires: which square is a new answer every time.
- **Twelve scattered, twenty left standing.** The remainder are not padding — a board that is nearly right **shows the player which squares are empty**, so there is no layout to memorise and nothing to work out. Twelve is about three iterations of work, not twelve, because past selves compound. One number in `SceneBuilder` (`ScatteredPieceCount`).
- **The room is DARK until it is solved, and that is the reward's first half.** Its four ceiling fixtures start at intensity **0** — lights and emissive panels both, because dimming the lights alone leaves four bright tiles lighting nothing, which reads as broken rather than as off. The room is still legible at zero: the building's ambient is Trilight with an equator of 0.644 and a ground of 0.719, so what goes out is the bright pools and the falloff, not the room. What is left is flat and grey and obviously waiting for something.
  - It also makes the lit home square the brightest thing in the room, which is exactly where a player carrying a piece needs to be looking.
- **The second half is the board coming apart.** Lights up, the board opens down the middle, and a plinth carrying a **red cube** rises out of the gap. Three beats over about 3.4s, deliberately overlapped rather than played in sequence — six seconds of a sixty-second iteration is a long time to stand still.
  - **The board really splits.** It is not one mesh: the export is 72 tiles, four frame rails and a base plate, 77 children in all, so sorting them by which side of the centre line they sit on is a genuine split rather than a trick with two copies. Seven parts run the whole width (the base plate, the two cross rails, four inlay strips); each is duplicated and each copy squashed to half width about its own outer edge. That is exact for an axis-aligned slab and costs nothing here because **every material on this board is a flat colour** — there is no texture to stretch.
  - **The seated pieces ride their half out**, because a piece's home anchor is parented to the half its square is on. Nothing tracks the pieces; the parenting is the mechanism.
  - Driven by a **clock, not a coroutine**. An iteration can end at any point in those 3.4 seconds and the loop's reset has to be total and instant: a coroutine would have to be found and stopped, and a half-finished one leaves the board open. A float is set to zero.
- **The red cube is not carryable yet, and that is deliberate.** It has no consumer — no lock it opens, no clear condition that asks for it — and a carryable with nowhere to go sits in the HUD row claiming to matter. Making it one is an `itemId` and an icon; see `TODO.md`, which also names the harder half of that question.
- **Left click, not E.** E means "take the thing in front of me" everywhere in this game and is how the piece got into the hand. Putting it down is the opposite motion and needs its own button, or a player standing over the board with a piece has one key meaning both things depending on state they cannot see. It shares the button with `BalloonTool` and cannot clash with it: that one is silent unless the pin is held, this one unless a piece is, and `PlayerHand` has exactly one item out at a time.

#### What this room cost to build, and would cost again

- **The board's grid is MEASURED off the opening position, not divided out of the slab.** The model has a frame: 5.40m of board around 4.19m of playing area, so `bounds/8` gives 0.675m cells where the real ones are **0.598m** — by the far file that error is more than a whole square, and in play it reads as "the piece does not land in the middle of a square". Measuring from the extremes is safe because an opening position fills the outer files *and* the outer ranks, so the lowest and highest coordinate on each axis are seven pitches apart. The build **checks its own answer**: 32 pieces must resolve to 32 distinct squares or it logs an error.
- **A piece's home socket is an empty object carrying the pose the piece had before it was scattered.** `CarryableItem.InsertInto` parents an item to its socket at local identity, so an anchor built as a *sibling* of the piece with its original local position and rotation reproduces the model's arrangement exactly — and nothing in `ChessBoard` has to know that the set is scaled 0.3039, modelled Z-up, or mirrored. Its `localScale` is **one, not the piece's**: `InsertInto` restores the item's own local scale on top, and a dark piece carries −1 there.
- **HALF THIS SET IS MIRRORED, and it broke the hand pose.** The dark pieces have a local scale of **(−1,−1,−1)**; Unity decomposes that basis as a pitch of **+90** with a negative scale where the light ones are **270** with a positive one. Both stand up on the board — the two cancel — but `handLocalScale` copies the sign of the scale, so handing both colours the same −90 stood the light pieces up and the dark ones **on their heads**. The fix is to take the pitch off **each piece's own world rotation** so the pair cancels in the hand exactly as it does on the board. Verified numerically: all 32 pieces' model-up lands on world up to within a dot product of 1.0000.
  - The same mirror also inverts anything taken through `InverseTransformVector`, which is how the piece triggers and the board's collider are sized — they come back **negative** and the box is built inside out. Both take an absolute value now.
  - And a `BoxCollider` on a negatively scaled transform warns on every load, sixteen times. The resolution for the whole family of problems is one move: **the −1 goes on a wrapper INSIDE the piece.** The composite is untouched — `P·R·S(−1)·child` either way, because the wrapper sits after the piece's own rotation and scale — but the piece's own transform is positively scaled again, and that transform is what the trigger, `handLocalScale`'s sign and the home anchor are all read off. The pieces stay mirrored where it matters: a light piece's renderer matrix has determinant +0.028 and a dark one's −0.028.
- **One press had been taking two and three pieces at once.** Every `CarryableItem` polls E for itself, and `PlayerHand.Take` only refuses a *second item of the same id* — fine while nothing overlapped and no two takeable things shared a place, and wrong the moment 32 uniquely-named pieces stood a square apart with square-wide triggers. `ItemRegistry.NearestTakeable` arbitrates: **nearest to the camera**, not whoever ran first (script execution order is arbitrary, so first-wins hands the player a piece they were not looking at) and the same measure the prompt disc uses, so the press and the disc always agree.
- **`floorY` is 0, i.e. the floor.** It is where a piece rests when it is *dropped*, and the only thing that drops one is a ghost's timeline running out under it, mid-room. Putting a piece back on the board is not a drop — it goes into a socket that carries its own height. This read `boardTop` while every piece still started on the board, and a piece let go of by a past self floated a centimetre above the ground.
- **Placement is refused *before* the item leaves the hand, never after.** `PlayerHand.Surrender` writes the event into the recording, and a recorded surrender that could not be completed is a past self doing something the player did not.

### Room2East — the cube room, and the second accumulation puzzle

**Through the blue door out of Room2West: six cubes on the floor, each carrying a symbol, and six recesses in the walls carrying the same six. Put every cube where it matches and a plinth rises in the middle of the room with the blue sphere on it.** E while the cube is in hand; a recess lights up while the cube that fits it is on the player. (Room2East is the next stop on the corridor after Room2West, and the yellow door onward to Room3 is in its own north wall — see `docs/room-geometry.md`.)

- **Symbols, not colours, and that is the room's design rule rather than a preference.** A puzzle matched by colour is one a colour-blind player cannot see and one a dim room cannot show. The six are a card family extended past the four suits — spade, heart, clover, diamond, star, crescent — and the two extras are picked from the same visual world (flat, solid, symmetrical, no interior detail) so they read as one set rather than as four suits and two strays.
- **Three recesses per wall, on the two walls that have no doorway.** Room2East sits between Room2West and Room3, so unlike a dead-end side room it has a doorway on both its south and north wall — only the east and west walls are free, three recesses apiece instead of two on three. Still readable from the door: a player who walks in sees six plates spread across both side walls and knows at once both what the room wants and how much of it there is.
- **The recess is a real hollow, and the cube slides into it.** It was a flat dark frame with the glyph panel laid on top, on the reasoning that without CSG a cavity has to be near-black inside a lighter border — true, and only convincing head-on. A raised **rim** of four bars with the glyph panel at the bottom of the well between them makes the same hole out of geometry, so its depth is something the light and the viewing angle agree about. Built **outwards**, because the wall cannot be cut into and everything behind it is simply invisible; shallow (0.16m), because a deep box at head height on a corridor wall stops being a recess and becomes a shelf. The seat is deep enough that the cube's back is inside the wall — the wall is the occluder, so a seated cube looks buried rather than clipped, which is the one thing this can do that a real hollow cannot.
  - The placement itself is a **slide**, not a snap (`SocketInsert`): presented clear of the mouth, eased in, straightening from a small tilt so it reads as put there by a hand rather than fed by a machine. The clunk moved from the press to the *landing*, where the object actually touches the bottom.
- **E, not left click**, and the difference is what the fixture *is*. The chess board is a surface AIMED at from across a room, so it takes the button meaning "act on what I am pointing at". A recess in a wall is a fixture you STAND at, which in this game has always been E — the drawer, the locks, the plates. Left click here would mean aiming at a hole 26cm across from inside arm's reach.
- **The plate lights on `hand.Has` and the prompt gates on `hand.Holding`, deliberately two different tests.** The light is a beacon: it has to be readable from across the room, and a cube stowed by Tab is still a cube that has to be delivered. The prompt is a promise the press will work, and the press needs the cube out — like every other fixture operated *with* an object.
- **Ghosts place them for free**, by the route chess proved: one `IItemSocket` per **cube**, each of which is that cube's own recess. `GhostReplayer` learns nothing about symbols, and the completed-errand rule scopes itself — a cube has a socket, so a ghost only repeats a pickup it also delivered.
- **The first textured material in the building.** Every other surface here is a flat colour. The glyphs are drawn once by `IconCanvas` and used twice: as an **opaque** picture (ink over paper — an alpha glyph on a lit material is a white card, because nothing here reads alpha off an opaque material) and as a HUD sprite. One drawing, so the thing on the wall and the thing in the readout cannot drift apart. Mipmaps on, unlike the HUD's: these are read at four metres and an unmipped glyph at that distance shimmers.
- Three of the six were unrecognisable on the first pass while measuring perfectly plausibly — see `gotchas.md`. **Render a generated glyph and open the PNG.**
- **The cubes are glass in a silver steel frame, and the frame is not decoration.** Glass in a white room has no silhouette of its own — it is only what it reflects, and this project has no refraction (see `rendering-notes.md`), so from the wrong angle a clear cube is a smudge on a white floor with a symbol apparently floating over it. The twelve edges in polished steel are what draw the *shape*; the glass is what makes it worth looking at. The bars are mitred rather than cut to one length — twelve equal bars overlap inside each corner with coplanar faces and flicker.
- **The symbol is a solid core suspended INSIDE the glass, not printed on its faces.** Two things follow from making the core *opaque* rather than the glyph alone floating in the middle: the near face hides the far one, so there is one mark to read from any angle instead of a symbol and its own mirrored twin seen through it; and it draws in the **opaque** queue with depth, so the glass simply blends over it and there is no transparent-vs-transparent sort order to lose. A cube rather than a wafer because the room's cubes lie on the floor and stack — **from above** is a real viewing angle here, and a wafer answers it with an edge.
  - **The core's glyph is inverted against the wall plate's**: pale mark on a near-black body, where the recess is ink on paper. The plate is a printed sign; the core is read *through* a tinted body in a white room, where a pale face is a white card inside a white cube against a white wall. Same drawing, two textures — `SaveSymbolTexture` takes the paper and ink colours.
- **They are SOLID: you walk around a cube, not through one.** At better than a metre across, a thing you walk through is not an object, it is a hologram — and the room is asking you to treat these as heavy. The scatter's own margins are what make that safe rather than a trap: at `wallMargin` 1.5 a cube leaves 0.825m between itself and the wall, against the 0.76m a 0.3-radius controller with 0.08 skin needs to pass. **That margin is a function of the cube's size**, so growing the cube back without growing the margins walls off the edge of the room.
  - The solid collider is on a **child**, and that is load-bearing: `CarryableItem` takes `GetComponent<Collider>()` as its reach trigger, so a solid box on the same object is a coin toss over which of the two becomes the reach — the trap `BuildKeyPlinth` records as *scenery gets a solid box, a takeable gets a trigger, and it is never both*. `CarryableItem.blocker` points at it and switches it off on every path that puts the object in a hand: in the player's hand it is a block their own controller shoves itself against, and in a ghost's it hands a past self the collider ghosts are forbidden (CLAUDE.md §1.7). Off while socketed too — a floor recess is seated from directly on top of it, and a solid box appearing under the player's feet is a shove.
- **Size is one constant, `cubeSize` in `BuildCubeRoom`, and everything else is expressed against it.** The recess frame is a fixed margin over it, the reach trigger is a fixed number of world metres past each face divided back out by it, the frame's edge thickness likewise. Written as ratios they would each silently change meaning on every resize — and the cube has now been resized three times (2.16 → 1.8 → 1.35 → 1.0).
- **The floor layout is WRITTEN OUT, not sampled — four fixed places with a jitter, two of them carrying a tower of two.** This room used the chess room's rejection sampler until play called the result a huddle. The sampler suits a room whose only obstacle is a board in the middle; this one has a plinth at its centre, three of its six recesses on the floor and a doorway at each end, so what is left to sample is a thin ring — and 400 attempts at "far from everything" inside a thin ring either fall through to the fallback, which returns a point satisfying *nothing*, or land in whichever lobe the seed liked. Written out, the room cannot huddle; the rng still owns the jitter and every yaw, so it does not read as a grid, and the build stays reproducible. `SceneBuilder` logs the closest pair so a regression is visible in the build output rather than only in play.
  - **Stacks are how the room was FOUND, not a property of a cube.** `floorY` stays half a cube whatever level it was built at: take the top one off, put it down, and it belongs on the floor. Only the loop's own `ReturnToOrigin` rebuilds the tower — which is precisely the rewind it exists to do.
  - **Take the BOTTOM cube of a tower and the top one falls** — `StackedItem`, one axis, real gravity, to one known height. Not a Rigidbody, and for the reason carryables have never had one: the loop must be able to put every object back exactly, and a simulated fall settles somewhere slightly different every time. Nothing about the fall is recorded either, because it is *derived* — a ghost repeating the pickup makes it happen again by itself, and takes are by identity rather than position, so a cube that ended up somewhere else this iteration is still the same cube to whoever comes for it.
  - A tower of two, not three: a metre cube stacked three high stands taller than the player and reads as scenery to walk around rather than something to take apart.

### The three escape objects

Each puzzle room pays out one, on a plinth: **red cube** (Room2West), **blue sphere** (Room2East), **yellow triangle** (Room3). Room4's console has a recess cut to each silhouette.

- **They are valid within ONE iteration only, and that is the design rather than a limitation.** Nothing exempts them from `ItemRegistry.ReturnAllToOrigin`. The last run is therefore a lap: the ghosts have already finished all three rooms, the objects are waiting, and the player collects them and walks to Room4. That is the run's whole fantasy stated as a rule — *ghosts do the work, I only finish*.
  - The cost is a real timing dependency. A room completes at the timestamp of the LAST recorded placement in it, so how much of the final run is left depends on when past selves happened to do their work.
  - **MEASURED 2026-08-12: it fits, with 8 seconds to spare.** The game was cleared on **iteration 15**, and the last lap — three collections plus the walk to the console — finished with 8s left on the clock. That closes the question this section was written to raise, and it closes it in favour of the design as built: no object survives the reset, no room's completion is latched, every invariant stands.
    - **Eight seconds is the entire margin, and it is an observation of one run rather than a guarantee.** It moves with when past selves finished their rooms, so a bad draw is tighter than this. Anything that lengthens the lap — a room past Room4, a fourth escape object, a slower walk, a plinth further off the route — spends this first, and should be measured rather than reasoned about.
    - The two alternatives this decision was weighed against are therefore dead and worth naming so they are not re-proposed: **objects surviving the reset** (§1.2's five states become six, and "never lost" has to be re-argued for an item nothing rewinds) and **gating the escape on rooms SOLVED rather than objects CARRIED** (cheap, but it deletes the lap, and the lap is the run's whole fantasy stated as a rule).
- **The same fixture in three colours and three shapes** (`BuildKeyPlinth`), so a player who has seen one knows what the next one is the moment it comes up.
- **They are polished metal on chamfered silhouettes**, and they were flat emissive colour on raw Unity primitives. The two faults compounded into "three coloured blocks": a matte surface has no highlight to catch, and a primitive has no geometry at its edges for a highlight to run along even if it did — which is the wrong look for the three objects the whole run is spent collecting. A 6% chamfer at every rim gives a band facing halfway between two faces, and on a near-mirror that band catches a bright line that moves as the player turns.
  - **The emission survives at a fifth of its old strength, and the original reason is why.** Each of these arrives in the same second as the biggest lighting change its room has — Room2West is genuinely dark until that moment — so a matte object would turn up in the brightest beat of the game and be the least interesting thing in it. At 2.4 it blew the middle out to near-white and left the colour readable only at the silhouette; at 0.40 the metal is what is seen and the glow is a floor under it.
  - **It only became possible when the reflection probes were found to be empty** — a metal is entirely reflection. See `docs/gotchas.md` and `docs/rendering-notes.md`; that fix changes every glossy surface in the building, not just these three.
  - The blue one is a **chamfered cylinder, not a sphere**, and is called SPHERE everywhere in the code and in this file. The recess in Room4 is cut round, so either shape fits it; the name and the mesh have simply never agreed.
- **Putting all three into Room4's console is the game's only exit condition**, and there is no button any more. `FinalSlot.AllFilled` is the rule; it refuses an empty array, following `FloorButton.AllActive`.
  - **The clock runs through Room4**, which is what makes that safe. Reaching the last room used to stop the loop, so arriving with two objects would have left a player sealed in a room with no way forward and no way back. Now it is an errand like any other: get there with all three inside sixty seconds, or get pulled to the bed and go again.
  - It also says something the plate could not. A press is a formality; three named objects from three rooms is the run being asked to show what it has done.

### Room3 — two pads, and the way out (into Room4)

**Two floor pads at (±3.2, 4·RoomPitch=43.4) — one against each side wall, facing each other across the room's mid-depth; `Door3` needs BOTH held at once.** (Room3 moved from `2·RoomPitch` to `4·RoomPitch` when Room2West and Room2East joined the main corridor — see `docs/room-geometry.md`.) One person cannot stand in two places, so it takes **two past selves overlapping in time** — the thing Room1 does not ask for. Room1 proves a past self can do a job; Room3 asks you to make two of them do it *simultaneously*.

- **It is the plainest room of the three — no items, nothing to search or carry — and that follows from Room2, not from laziness.** Room2's key toll is paid every iteration, so a second expensive room behind it would be unreachable rather than hard. What Room3 costs is **iterations**, not seconds: one per pad, plus one to walk through. Each visit is "walk in, stand on a pad".
- The pads are a **facing pair, not a scatter** — one on each side wall, both at the room's mid-depth. Two is the smallest count that still says *not something one person can do*, and a facing pair is the clearest arrangement for it: a player who finds one is looking straight at the other, so there is nothing to hunt for and nothing to suggest a subset might do.
  - **It is mirror-symmetric about the room's north–south axis, which the earlier three-pad triangle could not be.** A triangle symmetric about that axis has to put a vertex *on* it, and that axis is the straight walk from the south door to the north one — a pad there fires every time the player crosses the room. With a pair the symmetry and the clear walk come free: each pad sits **3.2m** off the line against a 0.4m reach. Standing on one is the only way to learn what the other is for, so the room has to show them together and never trigger by accident.
  - **3.2 from the centre line** leaves 1.175m to the wall face — against the wall as read from the middle of the room, with room to stand on the pad rather than in the wall. It is the same figure the earlier layouts used for their half-width, so clearance is unchanged.
- **`FloorButton.AllActive(pads)` is the rule and lives in one place**, called by both `Door` and `DoorIndicator`, so the lamp can never say "go" on a rule the door does not use. A null or **empty** array is deliberately *not* active — a door wired to no pads should stay shut, where "all zero are held" would vacuously mean open.
- The lamp does more work here than anywhere: at Room1's door red means nobody is on the pad; here it is the only way to tell **one** past self from **two**.
- Expect the player to reach the door **before** the ghosts reach their pads — each iteration gets faster as the rooms ahead get solved, so the recordings are slower than the run replaying them. Standing at the door watching two past selves converge is the intended beat. It is also why ghosts' `EndCycleControl` timing matters: a ghost that quit at t=40 releases its pad at t=40.
- **Two pads used to set the run's floor at about five iterations** (Room1's pad, Room2's key balloon, two Room3 pads, the escape) — down from about seven under the original four-pad rectangle. **That estimate is superseded: the measured run is 15 iterations**, and the pads are a small part of it now that Room2West's twelve pieces and Room2East's six cubes are on the critical path. The estimate is kept only as the way to weigh a change to the *pad count* — the pads still cost one iteration each — not as the run's length.

#### Room3 pays out the yellow triangle

**Both pads held raises a plinth in the middle of the room carrying the yellow triangle, on the same rule that opens the door** — `FloorButton.AllActive(pads)`, which `Door`, `DoorIndicator` and `RewardPlinth` all ask and none of them restates.

- **It rises and falls with the pads rather than latching once.** A hold-door that shuts when a past self steps off, standing beside a reward that stays out once earned, would be two different promises about the same two pads. What actually protects the player is that taking the triangle is a **pickup**: once it is in hand the plinth can sink under them and they keep it.
- **Dead centre, on the straight walk from the south door to the north one.** That axis was ruled out for a *pad*, and the reason does not carry over: a pad there fires by accident, where this is the one thing in the room the player is meant to walk into. It also keeps the mirror symmetry the pair of pads is built on.
- **The triangle is HIDDEN while the plinth is down, not merely out of reach.** `CarryableItem.IsFreeForGhost` reads `visible`, so hiding is what stops a past self reaching through the floor for it at a timestamp when the pads happened to be held — the same mechanism that keeps a key inside an unburst balloon. Out of reach alone would hold the living player and not the ghosts.
- **It is the first fixture in the game whose prompt appears because somebody ELSE is holding a condition open.** Two past selves on the pads, the door open, the triangle up, and the living player walking through to take it — which is the whole shape the run is supposed to end on.
- Unlike `Door`, the plinth keeps moving while the loop is stopped. The door freezes because a slab sliding under the closed eyelids is the machinery showing through; a plinth that froze would stay **up** through the wake-up and then sink in full view on the first frame the player has control. Left running it settles under the black screen, which is what the door's rule was buying.

#### The wall message — moved to Room1, iteration 2

`Room/PanelMessage.cs` puts `H O L D   [ N ]` / `TO SKIP TO THE NEXT ITERATION` on **all four walls at once**, with the PA saying "Manual termination available." **It hangs in Room1 now and does not appear until iteration 2.**

- **On the wall rather than the HUD, because the walls are displays** — established long before there was anything to display. A fifth grey disc on the HUD would have said it in the game's voice instead of the facility's.
- **Room1 rather than Room3, and the reason the old address was chosen is the reason it moved.** Room3 was picked to buy a delay — "by the time they have set up two pads they will have felt the dead time" — which is the right instinct expressed in the wrong currency. Distance is a proxy for iterations, and `showFromIteration` states the real thing directly. Having stated it, Room1 is the better room: **it is where every iteration begins**, so the player is standing still, facing a wall, with nothing yet to do. Room3 could only ever catch them mid-errand.
- **Iteration 2 is the earliest it means anything.** It is an offer to skip dead time, and a player who has never watched a clock run out has no dead time to skip. In iteration 1 it is noise on a wall.
- **All four walls, and that is not decoration.** The player wakes facing 180° — down the room at the south wall, away from the bed — and then turns for the door in the north one. The two walls that matter are opposite each other, so covering all four removes the question for nothing.
- **It retires on the ACTION — the player actually holding N — not on leaving the room**, and in Room1 that is load-bearing rather than a refinement. The old rule ("shown for the whole of the first visit, retired on the way out") works in a room the player walks into. Room1 is the room they wake up in and can be **out of in under three seconds**, so the leave-once rule could retire a sign nobody read. `EndCycleControl.UseCount` is the test, and it is deliberately not reset at the loop boundary: learning a control is not state an iteration rewinds.
  - This is the same rule Room2's pictogram uses (`retireOnPop`) and the same one the left-click prompt and the Tab hint settled on. **A sign that has been seen has taught nothing; one whose action has been performed has nothing left to say.**
  - It is still the **opposite call** to `ControlHintDisplay`'s E prompts, which never retire. E is a control used constantly at a different fixture every time, so a prompt that retires leaves the player hunting; N is one act, learned once.
- **The PA line is delayed 5 seconds; the sign is not.** `NarrationDirector.Speak` is `Stop()` + `Play()`, so announcements replace each other — and Room1 at the top of an iteration is exactly when the loop says "Iteration N, 60 seconds remaining." Without the delay the sign lighting up would cut the loop's own line off mid-word. No room had to care about this while the message lived three rooms away.
- **Height 3.95, set so the plate's bottom edge clears the door lamp** — not merely the doorway. The plate is 1.98m tall, so it spans **2.96..4.94**: above the lamp at 2.725..2.835 by 0.125m and under the 5.408 ceiling by 0.468m. At the 3.40 it was first built at, the north face **covered the lamp completely**, which in this room is the only way to tell one past self from two. All four faces share the height even though only the north one has a lamp under it — two walls are in view at once from most of this room, and a sign that changes height between them reads as a mistake.
- **Not the panels themselves spelling it out** — that was the first idea and the grid kills it. A wall is 5–6 cells across by 4 tall, so panel-as-pixel is a **6×4 display** and nothing legible fits. What still makes it read as the wall is the **dark plate behind the type**: white panelling switching to near-black with red text is what a display doing something looks like.


### Puzzle mechanics: the three shapes

Worth naming, because the game currently only uses one of them and the other two are where the unexplored ideas live.

1. **Simultaneity** — several conditions held at the same moment. Room1's pad, Room3's two pads. *Implemented, and arguably finished*: more pads is more of the same.
2. **Accumulation** — a quota filled across iterations, where each visit adds to a running total. **The most valuable direction, and Room2West's chess board is the first one built.** This is the reference film's tree-and-axe (several people chopping before it starts to give), and the same shape as filling an aquarium, cranking a winch, or charging something. It is valuable because it is the only shape where **an iteration spent badly still counts for something** — where today a wasted iteration is wasted entirely.
3. **Knowledge** — a combination, an arrangement, a sequence. **Unexplored, and the cheapest per iteration**: knowledge is the one piece of state the loop **cannot** rewind, because the player carries it in their head. Once learned it costs zero iterations forever. A numeric lock lives here. **Chess-piece placement used to be filed here and is not** — see Room2West: telling the player where the piece goes moves the whole cost into the walk, which is category 2.

**~~The constraint that governs all of them: a ghost can replay an *action*, never a *possession*.~~ LIFTED 2026-08-11** — ghosts carry things now (see **Ghost custody** above and `ghost-possession-design.md`). Everything this section concluded from that constraint is void, and the three consequences it drew are worth keeping only as a record of what changed:

- ~~"Accumulation works only if the tool belongs to the station, not the hand" — an axe must be bolted to the tree.~~ **An axe can be an axe.** This was the single biggest thing the old rule cost, and it is the reason lifting it opens the accumulation category rather than merely tidying Room2.
- ~~"A water/aquarium quota must be a valve or a pump, not carried buckets".~~ Buckets are fine.
- ~~"A chessboard cannot be ghost-assisted at all, since placing a piece is carrying one."~~ It can be, and it now is — Room2West. What did **not** survive is the advice that came with it: "make the hard part the *arrangement* and the execution cheap once known" would have built a memory test. The board tells you the arrangement and charges you the errand.

The constraint that replaces it is narrower and mechanical rather than conceptual: **there is one of each object, so two ghosts cannot both use one.** That is what keeps the pin out of ghost custody, and it is the thing to check before making any new puzzle depend on a carried tool — one axe means one chopper per iteration.


---

## The tree hall (cycle 2) - built 2026-08-16

`room2-3`, `room2-4` and `room2-5` are **one room**: 10.5m across, 30.45m along, 17.57m to the
ceiling, with a 10.5m pit cut wall to wall across the middle. A 13.4m tree stands on the near ledge;
felling it drops it across the pit as the only bridge, and the door out hangs on it having fallen.

**The verb is accumulated work, and it is the first room in the game that one pair of hands cannot
finish.** Forty swings fell the tree and a lone player cannot land forty inside sixty seconds. Five
axes lie on the near ledge, so iteration N has up to N past selves swinging at once and the tree goes
over on whichever iteration first musters enough of them - and on every iteration after, because the
ghosts always redo it. This is `docs/decisions.md`'s *"accumulated work is RE-PERFORMED, never
stored"* in its first actual room: **nothing about the notch survives an iteration.**

**Left click, not E.** Swinging is "do the thing this object is for", which is what `UsePressed`
means for the balloon tool and the chess placer. It also frees E to keep meaning *put the axe down*
while standing at the tree, which E-to-chop made impossible without walking away.

**Why the ring turned round.** Cycle 2 used to switchback back along -Z, directly under cycle 1's
corridor, which capped how tall any of its rooms could be - a 17.5m ceiling drives up through the
floor of the room above. Rotating the whole cycle 180 degrees **about the bed** leaves the bed under
the exit shaft where it must be and swings everything else past the end of cycle 1. Nothing is above
cycle 2 now except its own first room, so this is a constraint lifted for every cycle-2 room still to
be designed, not just for this one.

**The pit is fatal.** Falling in ends the iteration and the player wakes in bed - death and an
iteration ending are the same event, and the loop already had the machinery. A past self that fell in
simply stops there: its recording ends, and `GhostReplayer` releases what it was carrying, so an axe
taken into the pit comes back to the room.

**Twenty-five chops** (2026-08-19, down from forty and then thirty). The number only has to be well
past what one pair of hands fits into a minute; past that, more of them is more waiting rather than
more accumulation. `TreeNotchStages` stays at 8, so the notch simply deepens faster per swing.

**Things dropped into the pit fall down it**, and that took a fix: `FallingItem` looked 9m for a floor
and settled at the cycle's own floor height when it found none, so an axe dropped into a 26m hole hung
at the lip. See `docs/gotchas.md`. An object that goes in is gone for that iteration and back at the
top of the next one, which is what CLAUDE.md §1.2 already said happened to an axe a past self carried
in there.

**What the notch is.** Eight cut trunk meshes, built by `SceneBuilder` and switched by chop count, so
wood is genuinely missing rather than a dark shape being laid over the bark. The first version drew
the wedge on and play read it as a vertical groove stuck to the tree. See `docs/gotchas.md` for the
two things that had to fail first.

**Open**: whether forty is right, whether five is right, and whether five ghosts at once is
affordable. None of it has been played - see `TODO.md`.


### Crossing the felled tree - what "hard to cross" turned out to be (2026-08-16)

Play reported that getting across the felled tree was awkward. The measurement behind it: **the clear
trunk between the cut and the first fork is 2.4m, and the pit is 10.5m.** The other 8m of the span is
canopy. The tree can therefore never be a clean log bridge across a hole this wide - no scaling fixes
it, because the tree is already sized to the hall's width, and growing it grows the crown faster than
the trunk.

So the crossing is over the canopy, and it was built as a scramble: every mesh solid, leaves included,
over a drop that is now fatal. Three changes, in the order they matter:

- **Leaves are not solid.** You push through foliage; you do not stand on it. Trunk and branches still
  take weight, so nothing you can lean on passes through you. It also takes an 18,500-vertex mesh
  collider out of the scene.
- **The deck is wide** - 3.3m rather than 1.5m. The alternative to a wide walkway over a fatal pit is
  dying to a sidestep, and its top is level with the log's so it reads as the log.
- **A ramp at each lip.** The deck's top is 0.6m up and the player's step is not; without them the
  crossing opened with a jump onto a narrow surface over a drop, which was the least forgiving moment
  in the room and the least deliberate.

**Still unmeasured**: whether that is enough. If it is not, the honest next lever is the pit's width -
it is 10.5m because room2-4 was 10.5m, not because anything about the tree wanted it.


### The slide's landing room is flooded (2026-08-19)

The room at the bottom of room2-5's slide - the shell hung under the tree hall's south-west corner,
reached by riding the chute through what is now that room's **ceiling** - is full of water, and
plastic balls float on it.

**It is presentation, not a mechanic.** 1.2m of water, no collider on any of it, no `KillVolume`
under it, no signal bit, nothing for `Cycle.ResetRooms` to put back. The player drops through the
surface, splashes, and wades. That was the decision taken over the two alternatives:

- **Drowning** - the pit's `KillVolume` verbatim, ending the iteration. Rejected: the game already
  has exactly one fatal hole and a second one turns "somewhere the slide goes" into a punishment for
  taking the slide.
- **Swimming** - buoyancy and a swim stroke in `FirstPersonController`. Rejected as a new movement
  mode for one room, and this room has no exit yet for a swimmer to reach.

**1.2m is the deepest it can be without becoming one of those.** The eye is at 1.6m, so at 1.2m the
player looks *down* at the surface from above it. Any deeper puts the waterline across the camera,
which needs an underwater view and a way back out that this room does not have.

**Walking in it is slowed to 0.45 of the dry speed, and the footsteps change with it.** Both are on
the player rather than on the water (`FirstPersonController.SpeedScale` / `wadeClips`, switched by
`WaterPool`), and the sound is a swapped CLIP rather than a second sound system: a step is fired from
the head bob's own phase, so anything playing wade sounds from outside would have to guess when a
foot lands and would drift out of time with the walk. The clips are generated (`MakeWadeClip`) and
are neither a splash nor a footstep - no transient, most of the energy low, and the bubbles arriving
after the push rather than with it.

### Where the slide actually meets the room (2026-08-19, second pass)

The room's depth below the hall is **derived from the mouth, not chosen**: its ceiling is exactly the
top of the 1.7m opening (`SlideRoomFloorY = SlideMouthHeight - RoomHeight`). Both walls are then cut
over the same band, so the hall's opening and the room's are **one hole** with a door pocket's depth
of tunnel between them - lined on four sides, or the gap is a slot with the sky at the end of it.

That replaced a full `StoreyDrop`, which failed in a way worth recording: the chute left the hall at
the hall's own floor and the room's opening was five metres further down, so the rider passed through
solid slab and **arrived falling out of the ceiling**, and the hole in the hall's wall looked into a
void. The point of the new depth is that you can stand at the top of the slide, look through the
hole, and see the water you are about to land in before you commit to the ride.

**The drop is now the last stretch of the slide rather than a fall.** The chute's tip hangs inside
the room at the top of its wall and the ride's final leg runs 4.3m out for 4.0m down - about 43
degrees, into the water. `SlideRideLanding` is what sets that, and it is the one number to change if
it plays as too much of a launch.

**Why nothing about it is solid.** Beyond the wading: the slide's ride path is **raycast at build
time** off whatever is under the line (`SceneBuilder.SampleRidePath`). A water surface with a
collider would be what those rays hit, and the rider would be set down on the waterline a metre and a
bit in the air instead of on the floor.

**The balls are built the opposite way to room2-7's ball pit**, on purpose. The pit is 5,000 balls
merged into six meshes because a solid mass of them never moves; these are separate renderers because
things floating on water have to drift independently, and a merged mesh can only move as a lump. One
`FloatingBalls` component writes every transform from the clock and a per-object phase - no
simulation, nothing accumulating, so nothing can wander out of the pool however long the run lasts
and there is no state at the loop boundary to reset.

**Three things float, and the small one's count is set by the other two.** 210 plastic balls (down
from 300), three rubber ducks and two beach balls. Three hundred small balls is a surface the bigger
things have to be picked OUT of, and the only reason to have them is to be seen.

**What "cheap-looking" turned out to be**, when the first pass of the balls was called too bright and
poor: display colours at full value (a flat bright patch with no shading left in it - the fix is to
put the colour in the midtones at 0.55/0.72 and let the highlight carry the brightness), one shade
and one size for all of them (now two tones per hue and a tenth either way on scale), and a
96-triangle sphere seen at knee height rather than as a distant mass (now 384). The pit is untouched:
it is seen as a mass from above, which is what 96 triangles is right for.

**The ducks and the beach balls rock and turn; the balls do not.** Rotation is off by default in
`FloatingBalls` and that is not an oversight - a single-coloured sphere cannot show one, so writing it
would cost 210 transforms a frame for nothing. For a duck it is the opposite: turning is most of what
says a thing is floating rather than placed.

### What the first play of it found (2026-08-19, third pass)

Four things, and two of them were faults the build had been reporting as fine:

- **The mouth flickered.** The tunnel lining the gap between the two walls spanned the whole pocket,
  and two things already reach into that pocket - `BuildSlab` runs to the room PITCH rather than its
  depth, so the room's ceiling overruns its own north wall by half a pocket, and the hall's south wall
  has a body hanging a `WallDepth` south of its face. Two pairs of coplanar faces, one flicker. Every
  piece is now sized to what is genuinely open, derived from those constants rather than measured.
- **Riding the slide carried the player over the roof of the level.** `Physics.Raycast` returns the
  CLOSEST hit, which from above is the HIGHEST surface - and once the landing room came up to meet the
  mouth, the highest thing under the last two samples of the chute was that room's ceiling slab. The
  measured path climbed from the chute at 0.53m onto the roof at 1.80m, and the ride played exactly as
  it was built. Leg 0 now uses `RaycastAll` and **prefers the slide**, holding its last height rather
  than stepping onto anything else. The build's own "path climbs" assert did not catch it because
  1.80m is below the 2.73m the ride starts at - the assert only ever knew about climbing past the top.
- **The plunge was a vertical drop.** Every sample of leg 1 raycast the same floor, so the whole four
  metres of descent happened between two waypoints. It is interpolated on a **squared curve** from the
  chute's exit height down to the floor now - shallow where it leaves the chute and steepening, which
  is the shape of something thrown - with the raycast still deciding where it ends and still clamping
  it out of the floor.
- **The balls were still too bright**, at 0.55/0.72 of their display colour and 0.88 smoothness. The
  room is the reason: six fixtures over white walls and a white floor is bounced light arriving from
  every direction, so a midtone renders nearly a stop up and a near-mirror wears the whole ceiling.
  0.30/0.42 and 0.72.

**Still unplayed**: whether the arc off the end of the chute reads as the slide finishing, whether
0.45 speed in the water is heavy or merely annoying, and whether the hole shows enough of the room
from the hall to be worth the geometry it cost.

### The floats collide, and are real rigidbodies for it (2026-08-19)

Play asked for the things on the water to bump into each other; the first version wrote each one's
position from a sine of the clock, which cannot collide with anything by construction - two balls
crossing passed through one another, and so did the player.

**This is the same exception the tap's spray takes, not a hole in the no-physics rule.** CLAUDE.md §4
forbids rigidbodies on CARRYABLES, because the loop must put every carryable back exactly and PhysX
solves in islands - where a dropped object settles depends on everything near it, including a living
player who moves differently every iteration. Nothing floating on this pool is carried, recorded,
socketed or reset.

- **Gravity is off; buoyancy is a damped spring** to the height each was placed at. Nothing can sink
  and nothing can be knocked out of the pool, and there is no settle for a hard shove to get wrong.
- **A weak mooring** pulls each back toward where it was placed. Without it, a room walked through for
  fifteen minutes ends with everything heaped in one corner.
- **The balls have no bob and no drift, and that is what makes them free**: with nothing writing to
  them they settle, sleep, and cost nothing until something disturbs them. Only the five big floats
  are awake all the time.
- **They are on the BALLOON layer**, which is not a borrow of convenience: that layer already means
  "a light thing the player wades through and shoves aside". The controller excludes it, so there is
  no invisible wall in the pool, and `pushLayers` already contains it, so the player's push works with
  nothing rewired.
- **A ghost leaves the water flat.** Ghosts have no colliders (CLAUDE.md §1.7) and are not getting
  one; a colliding ghost would change the recording being made against it, which is a far worse thing
  to be wrong about than still water.

### The sound is passing through water, and the rings are drawn (2026-08-19)

The entry used the tap's three `sfx_water_splash_*` clips, and in play that is what they sounded like
- water landing on tiles from above, laid over the room. It is the generated wade sound at twice the
length now, slower to die and with more of it low: the same event, person-sized.

**Ripples are quads, not shader.** The surface shader's noise is the same everywhere and answers to
nothing; a ripple has to start where the player is. The alternative is feeding the shader a list of
disturbance centres and their ages - this is a texture and eight pooled quads, expanded and faded by
`WaterRipples`, and it composes with a shader nobody has to touch. They are fired from the same points
and moments as the spray, so what spreads is what the player just did.


## Room2-6: three valves, a drain, and the room you are standing in emptying (2026-08-19)

The pool room IS room2-6 now. The old room2-6, room2-7 and room2-0 - the whole of the ring's third leg
- are deleted, and with them cycle 2's console. The walk is **room2-1, room2-2, the tree hall, and
then down the slide into room2-6**, which is a way on that no door provides.

**The puzzle**: three wheel valves, one centred on each wall that has no door, two full turns each.
Six turns opens a drain in the middle of the floor; the room empties; the empty room is what the door
hangs on.

- **`Satisfied` is "the water is gone", not "the valves are open".** The door does not open when the
  puzzle is solved, it opens when the CONSEQUENCE has finished - the drain takes six seconds in full
  view and the player watches the room they are standing in change before they are let out of it.
- **One press is one turn, and the turn is an animation.** A valve that snapped would be a switch
  drawn as a wheel. It is also why this is two presses rather than a hold: a hold is a LEVEL, and a
  level has to survive a ghost that skips several frames in one tick, where an instant does not.
- **Six turns is more than one pair of hands fits in a minute**, and that is the room. The three
  wheels are as far apart as this room allows, so a past self at the far wheel is the room being
  solved - the same accumulated-HANDS shape as room2-2's tank, and nothing about it survives the
  boundary. Three bits of `RecordedFrame.signals`, one per valve, because they are three separate
  things a past self can be doing.
- **The grate is solid, and that is a safety rule.** The floor has a real hole in it; a player who
  could fall into the sump would be stuck in it with a sixty-second clock running.
- **The way out faces the way in.** The door is in the south wall, straight ahead of the chute, so a
  rider lands looking at it. It opens onto a capped pocket - there is nothing beyond it yet.

**Cycle 2 has no final room, and `finalRoom` null is a supported state** rather than a hole this left:
`Cycle.Complete`, `LoopManager` and `CycleBinding` all guard it, and the cycle could not be finished
before this either - its console declared three shard slots that nothing in the cycle wore, which the
build shouted about three times per run. One honest null replaces three errors about a console nothing
could fill.


## Room2-7: the scale, and six weights nobody is told (2026-08-19)

Through room2-6's door. One room, one platform scale, one number - and **the player is told none of
the weights.** There is no label on an axe and no note on a wall; the only way to learn what anything
weighs is to carry it here and put it down. That makes the room a MEASURING instrument first and a
lock second, and the first thing most players will weigh is themselves.

| | kg |
|---|---|
| rubber duck | 0.4 |
| beach ball | 1.3 |
| bucket, empty | 2.1 |
| fire axe | 4.7 |
| cube | 8.3 |
| bucket, full | 12.0 |
| the player | 71.0 |
| **target** | **26.7** |

**The player is weighed with whatever they are holding.** Standing on the pan with an axe in your
hands reads 74.6 - a real scale cannot tell the two apart and neither does this one - which also
gives the player a way to read two things at once without putting either down. A GHOST's hands are
empty as far as the scale is concerned, and that asymmetry is the room's rule rather than an
oversight: a past self has no collider (CLAUDE.md §1.7) so it can never be in the pan at all, and
what it CAN do is put an object down in it, which is how the room is solved.

**The rule is BRING ALL FIVE**, and the weights were solved for rather than picked (2026-08-19).
The target is the sum of one of each, and the numbers are chosen so that **no other combination in the
whole cycle reaches it**:

    cube 8.3 + full bucket 12.0 + axe 4.7 + beach ball 1.3 + duck 0.4 = 26.7

Checked exhaustively against everything cycle 2 contains (5 axes, 4 buckets each of which may be empty
or full, 1 cube, 2 beach balls, 3 ducks): **exactly one combination reaches 26.7**, and the nearest
miss is 0.1 away - a whole display digit, against a tolerance of 0.05.

This replaces a first pass where 23.0 had twelve solutions and the cheapest was three objects. That
version was defended on the grounds that a unique answer punishes a player who worked out a different
one; the rule changed to "all five", and with it the argument - there is now one intended set, the
sign names all of it, and the only thing left to discover is which of the five add up, which is
everything.

**Five objects is the largest headcount any room in this game has asked for.** One object in the hand,
a 60-second clock and a sweep home at every boundary means five on the pan at once is five past selves
each carrying one.

**The player's own weight is not part of any solution**, and that is what it is for: it is far heavier
than the target, so standing on the scale teaches the room in one step and cannot be mistaken for
progress.

**The real cost is TRIPS.** One object in the hand (CLAUDE.md §4), a 60-second clock, and
`ItemRegistry.ReturnAllToOrigin` sweeping everything home at the boundary - so three objects on the
pan at once is three past selves each carrying one. The bucket is the connection worth having: filling
it is room2-2's puzzle, and the knowledge that a full one weighs 12.0 is what makes that room pay off
two doors away.

**The scale latches.** `Door` re-reads its condition every frame and shuts when it lapses, which is
right for pads and wrong here for one specific reason: the player's own weight is part of what this
can read, so any target they are standing in would be one they could not walk away from. Latched, the
scale means "this reading HAPPENED", and the reset at the top of each iteration is what stops that
being permanent.

### The two signs (2026-08-19)

**Over the door onto room2-8**, `26.7 KG` - the door this scale opens, so the number is written on the
thing it is the price of. **Painted on the wall rather than on a plate**: `withPlate` draws the
near-black backing every other sign in the building sits on, which is what makes those read as
DISPLAYS, and this one is meant to read as stencilled over a doorway.

**On both side walls**, the equation: `cube + full bucket + axe + beach ball + duck = ?` in icons.
That is what the scale ACCEPTS, which is also the answer now that the rule is "bring all five" - the
question mark says the total is the unknown, and the number over the door says what it has to be. So
the two signs together name the ingredients and the answer, and what is left for the player is every
weight, which is the whole puzzle.

It was cut back to the number alone for one pass and put back by request. The reason it belongs: the
objects it names are scattered across four rooms the player has already walked through, and without it
the room says what total it wants and nothing about what to bring.

Both walls, because the player is turning on the spot at a scale that is 5.7m across in the middle of
the room - whichever way they face, one of the two is in front of them.

The target is written to ONE DECIMAL like the scale's own readout, so the two are visibly the same
kind of value and nobody has to wonder whether `23` and `23.0` mean the same thing. It never
retires: `MakeWallFace` authors every sign at alpha 0 because the ones it was built for are faded in
and retired by `PanelMessage`, and this is set to 1 and stays - the target is true for as long as the
room is unsolved, and a player who has forgotten it must be able to look up again.

**The ducks and the beach balls are both physics props and carryables**, which nothing else in this
game is. The handover is in `FloatingBalls`: while a hand has it, or once it is further from home than
the pool is wide, its rigidbody goes kinematic and the ordinary carryable machinery owns it -
including `FallingItem`, so its drop is scripted and lands in the same place every iteration. That is
what the scale needs of it, and it is the no-physics rule (CLAUDE.md §4) being kept rather than bent.

---

## Room2-0: four pedestals, and counting what you already walked past (2026-08-20)

The last room of cycle 2, and the one that breaks it. It is the `room2-8` slot in the walk - the
naming rule is that a cycle always ends in its `-0` (`cycle-design.md` §4a), so the code name is
`Room2_0` and the room2-8 shell it replaced is gone.

**Four pedestals rise as the player comes through the last door.** Each has a round recess in the top
and one object drawn on the side facing them: an axe, a rubber duck, a bucket, a beach ball. What goes
in the recess is the billiard ball whose NUMBER is how many of that object cycle 2 contains -

| pedestal | count | ball |
|---|---|---|
| beach ball | 2 on the pool | 2 |
| rubber duck | 3 on the pool | 3 |
| bucket | 4 in room2-2 | 4 |
| axe | 5 in the tree hall | 5 |

All four right and the cycle breaks: the way back seals, the PA says so, every panel in cycle 2 fails
to a test card, and the floor opens in the middle of the room.

### Why the answer is a count and not a clue

**Nothing in the building says any of these numbers, and nothing can.** It is the player's own memory
of four rooms they have already crossed - which is the one kind of key a time loop can ask for that a
past self cannot fetch for them. Every other lock in this game can be opened by a ghost repeating an
errand; this one needs somebody to have been paying attention.

The row reads **5, 3, 4, 2 left to right**, deliberately. A row that read 2, 3, 4, 5 would be solvable
by noticing that it counts.

### Nine balls, in the chest of drawers

The chest in room2-1 was built on 2026-08-15 with two opening bays and **nothing in them**, on the
argument that "what goes in a drawer belongs to a puzzle that has not been designed, and the point of
building the container now is to have somewhere for that to go". This is that puzzle. Both bays were
wired as `GhostInteractable`s from that first build for exactly this day, because appending a signal
bit later is the one thing `RecordedFrame.signals` makes awkward (CLAUDE.md §1.6).

**Nine balls: 1-8 and the cue.** The four the puzzle wants are 2, 3, 4 and 5, so the decoys have to
reach past 5 or the answer is "the ones that are in the drawer". 1, 6, 7, 8 and the cue do that.
Sixteen would also be sixteen more E fixtures a square apart inside a volume the two drawer fronts
already contest.

**One id each**, which is the opposite of every other multiple object in this building. Pins, buckets,
ducks and beach balls are SUPPLIES - an id naming several interchangeable things - because a recorded
"took a Tool" only ever meant "a free one". Here *which one* is the entire question, so there are nine
ids with one member each, and a pedestal's socket can never be handed a different ball than the one it
names.

**They weigh nothing on room2-7's scale, and that is deliberate rather than forgotten.** The 26.7kg
target has exactly one solution across everything cycle 2 can carry - one duck, one beach ball, one
full bucket, one axe, one cube - with the nearest miss 0.1kg away. A ball worth anything at all
destroys that: at a real 0.17kg an exhaustive search finds **49** ways to make the number, and 0.16 or
0.20 are no better, because nine small addends fill every gap the coarse weights leave. The scale's own
sign already names the five things it accepts, so a ball reading zero is what that sign says rather
than a lie it tells.

### A wrong ball is refused, not kept - and that is a ghost decision

`FinalSlot` gained a **family** (`offerItemIds`) for this room. Before it, a recess prompted only when
the object that fits it was in hand - so the rim lighting up WAS the answer, readable by walking the
row holding each ball in turn, without ever pressing anything or reading a pictogram. Now the prompt
appears for any billiard ball, the press either lands or flashes red, and a wrong guess costs the walk
back to the drawer. Counting the axes is cheaper than guessing, which is the whole design.

**The pedestal refuses a wrong ball rather than keeping it**, and the reason is ghosts. A wrong ball
that STAYED would be re-delivered by that iteration's past self for the rest of the run, blocking a
pedestal the living player then has to clear by hand every sixty seconds - the exact "the puzzle is
blocked by an old ghost" failure. Refused, the wrong delivery simply fails again each iteration and
costs nothing; and a refusal is not a `CarryEvent`, so no ghost ever replays a guess in the first
place.

### How the room is actually finished

The player carries **one object at a time**, so four balls is four trips down the whole length of the
building - and room2-7's scale re-locks every iteration, so each trip needs the five weights on the
scale first. That is the loop working as designed: past selves load the scale and deliver the balls
they delivered, and the living player adds one more thing.

**Past selves can raise the pedestals themselves.** `EscapeTrigger.TryArm` is called by
`GhostReplayer.Tick` as well as by the living player, which is what stops a ghost's recorded delivery
from needing the living player to independently reach this room first in the same iteration. Cycle 1's
console needed that fix for the same reason.

### Cleared: iteration 22, 10:04 (2026-08-20)

The first end-to-end clear of cycle 2, on the build with the hemispherical dishes and the neutral
rims. Everything above worked: the chest, the nine balls, the four pedestals, the refusal on a wrong
ball, the ERROR and the floor opening.

**27.5 seconds an iteration**, against cycle 1's practised 28.7. The tempo is the same and the COUNT is
what differs, which says the extra length is errands rather than walking - the loop working as
designed rather than a room being slow.

**Why 22 and not 9.** The obvious floor is the deliveries: five objects onto the scale and four balls
into the pedestals. But room2-7's scale **re-locks every iteration** - `ItemRegistry.ReturnAllToOrigin`
sweeps all five objects home at the top of each one - so every single trip to room2-0 carries a
standing cost of five past selves re-loading the scale before the door will open. Those five are not
paid once; they are paid for the rest of the run. That, plus four light switches, two taps on one
tank, twenty chops and six valve turns, is where the other thirteen go.

**It is a knows-everything run.** Played by the person who designed the room, with the four counts,
the 26.7kg target, the six turns and the twenty chops all known going in - so none of the time is
puzzle-solving, and 22 is close to a floor rather than a first-play figure. Cycle 1's comparable
number is its third clear (10), not its first (14).

**Still unanswered**: whether 22 iterations is enjoyable or a slog, whether the pictograms read as
*how many of these are there* rather than *bring one of these* to somebody who was not told, and how
many of the 22 were spent on iterations where the chest happened to be toggled shut.

### All four rims are the same colour (2026-08-20, after the first play)

The first build gave each pedestal an accent of its own and play reported the obvious thing: the rim
colour and the ball that goes in it disagree. Billiard balls are a standard set - **2 blue, 3 red, 4
purple, 5 orange** - so there are only two ways to stop them disagreeing, and matching them is the one
that costs the puzzle. A purple rim over a purple ball turns *how many buckets are there* into *find
the purple one*, and the pictogram on the side becomes decoration.

So the rims are **identical and neutral**. Every one of them lights the same way for every ball,
because `offerItemIds` is the whole family - the row answers a press and never a question, and the
only thing in the room that tells one pedestal from another is the object drawn on it.

### The recess is a hemisphere cut to the ball (2026-08-20, after the first play)

It was a flat disc - the same faked well cycle 1's console uses, where "what reads as depth is the
pair: a rim standing 12mm proud and a near-black floor 4mm above it". That is enough when the recess's
job is to say *a square/round/triangular thing goes here* and the three differ from each other. It is
not enough here, where all four recesses take the same kind of object and the room has to say **a
billiard ball goes in this** to a player who has never seen a ball go into anything.

So it is a real cavity: a hemisphere cut to the ball's own radius plus 5mm, with the ball seated at
the bowl's centre so it sits exactly half in. A bowl a ball drops half into is a shape that can be for
nothing else.

That needs geometry rather than a trick, because a cavity means material taken OUT of the thing above
it and there is no CSG here. `SlotShape.Dish` generates one mesh - the pedestal's top plate, the
circular hole in it, and the bowl hanging under the hole - and the pedestal's body is built short by
the cap's thickness so the cap IS the top of it. The lit ring round the mouth is a separate annulus,
because `FinalSlot` paints one renderer to say idle/ready/filled/refused and painting the whole cap
would light a dark inset the size of the pedestal top; the mark has to be at the hole, which is what
the player is aiming at.

**Every face is double-sided.** A hand-derived triangle winding is the one kind of mistake in
`SceneBuilder` that cannot be checked without rendering it, and here it costs nothing: of any
coincident pair exactly one is front-facing from any camera, so the other is culled rather than
fighting, and every back face is sealed inside the cap and the body under it.

### The floor opens onto a shaft that is capped

Cycle 1's way out drops through the floor of one storey into the ceiling of the next. There is nothing
under room2-0, so cycle 2's is **one lid, and the shaft under it has a bottom**. That is the honest
shape of "cycle 3 does not exist yet": the way down is built and not yet connected, and uncapped it
would be a hole into nothing that a player in a broken room can walk out of the world through.

It is also opened by the BREAK rather than by the boundary
(`FinalRoomSequence.opensWayOutOnBreak`). Cycle 1's is opened by `LoopManager.CrossToNextCycle`, well
after the break, because the storey below has to be woken and its gas repointed first. Nothing has to
wake here. When cycle 3 is built, three things change back: the flag goes to false, the cap comes out,
and the shaft lengthens to `ServiceVoid`.

**What cycle 2 completing currently does**: `LoopManager` has no cycle after it, so it runs
`RunEnding` - the break, ten seconds of the room failing with the hatch open, and then the game's
ending scrim. That is the truthful state of the build rather than a placeholder: cycle 2 is the last
cycle there is.

