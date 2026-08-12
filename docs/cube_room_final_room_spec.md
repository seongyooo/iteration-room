# Cube Room & Final Room Design Specification

> Scope: This document contains **only the new Cube Room and Final Room**.
> The existing Chess/Red Room is already implemented and should not be modified as part of this task unless required for integration.
>
> Core game concept: Each room contributes an object required for the final escape. The player's past iterations are replayed by Ghosts, allowing the final iteration to be completed by the accumulated Ghosts while the current player can simply walk through and collect the result.

---

## 1. Cube Room

### Purpose

The Cube Room is one of the three rooms branching from Room2.

The room contains multiple cubes scattered around the room. Each cube has a distinct **symbol/pattern**, rather than relying on color.

The player must place every cube into the matching slot.

When the puzzle is completed, a mechanism activates and produces the **Blue Sphere**, which is one of the three objects required to escape from the Final Room.

### Cube Symbols

Use symbols/patterns to distinguish the cubes and their corresponding slots.

Use the following three card-style symbols for the cubes and matching slots:

- Spade (♠)
- Heart (♥)
- Four-leaf Clover (♣ / four-leaf-clover-shaped symbol)

The symbols should be visually distinct and recognizable. Do not use color as the primary matching rule.

The exact visual implementation can use simple geometric symbols that are clearly recognizable.

### Cube Placement

- Scatter approximately **6 cubes** throughout the room.
- Each cube has one unique symbol.
- The corresponding placement slot also displays the same symbol.
- When the player is holding a cube, its matching slot should become visibly highlighted/glowing.
- When the player approaches the highlighted slot, show the left-click interaction overlay **only the first time**.
- Once the overlay has been shown for that slot, do not repeatedly show it every time the player approaches.
- Left-click places the cube into the matching slot.
- A cube cannot be placed into a slot with a different symbol.
- Once correctly placed, the cube becomes fixed in its slot.

Example:

```text
Cube: ♠
    ↓
Matching slot: ♠
```

### Puzzle Completion

When all cubes have been correctly placed:

1. Detect the puzzle as completed.
2. Trigger a room completion event.
3. Activate the room's mechanism/structure.
4. Reveal the **Blue Sphere**.
5. Allow the player to pick up the Blue Sphere.

The Blue Sphere is **not merely decoration**.

It is a required Clear Key Object for the Final Room.

### Iteration / Ghost Behavior

The Cube Room must work naturally with the existing iteration system.

Every cube placement performed by the player should be recorded so that the corresponding Ghost can repeat that action in later iterations.

Example:

```text
Iteration 1
Player → Square Cube → matching slot

Iteration 2
Ghost 1 → Square Cube → matching slot
Player  → Triangle Cube → matching slot

Iteration 3
Ghost 1 → Square Cube
Ghost 2 → Triangle Cube
Player  → Circle Cube

...

Final Iteration
Ghosts perform all previously recorded cube placements
                    ↓
             All slots completed
                    ↓
              Room mechanism
                    ↓
             Blue Sphere
```

The intended experience is:

> The accumulated Ghosts eventually complete the entire puzzle, and the current player does not need to repeat the work in the final iteration.

The player should be able to enter the completed room, observe the result, and collect the Blue Sphere.

### Important Design Principle

Do **not** make the Cube Room depend on cube colors.

The puzzle should be based on **symbols/shapes** so that the player understands the matching relationship visually.

---

# 2. Final Room

## Purpose

The Final Room is the ultimate escape room.

The player must bring the three Key Objects obtained from the three puzzle rooms:

- **Red Square** — obtained from the existing Chess/Red Room
- **Blue Sphere** — obtained from the Cube Room
- **Yellow Triangle** — obtained from the third puzzle room

All three objects are required.

The player cannot escape if even one object is missing.

### Required Objects

```text
Red Room
    ↓
Red Square

Cube Room
    ↓
Blue Sphere

Yellow Room
    ↓
Yellow Triangle
```

Final Room:

```text
Red Square
     +
Blue Sphere
     +
Yellow Triangle
     ↓
Escape Device
     ↓
ESCAPE
```

### Escape Device

Create a central escape mechanism/device with **three dedicated slots**.

Each slot corresponds to one of the three Key Objects.

Suggested arrangement:

```text
┌─────────────────────────┐
│                         │
│   [ Square ]            │
│   [Triangle]            │
│   [  Star  ]            │
│                         │
│      ESCAPE DEVICE      │
│                         │
└─────────────────────────┘
```

The exact visual layout can be adjusted to fit the room.

### Object Placement

- The player physically inserts each Key Object into its corresponding slot.
- The slots should visually indicate which object belongs there using the same symbol/shape.
- The objects should remain visible after being inserted.
- Each successful insertion should provide clear visual/audio feedback.
- The escape mechanism should only activate after **all three objects have been inserted**.

### Completion Sequence

Suggested sequence:

```text
Player inserts Red Square
        ↓
      CLACK

Player inserts Blue Sphere
        ↓
      CLACK

Player inserts Yellow Triangle
        ↓
      CLACK

All three slots filled
        ↓
Escape Device activates
        ↓
Room/system reacts
        ↓
Final exit opens
        ↓
Player escapes
```

The final activation should feel like a payoff for everything the player has done throughout the previous iterations.

### Iteration Considerations

The Final Room itself should **not require the player to redo the previous puzzles**.

The intended final-loop experience is:

```text
Ghosts complete the required work
        ↓
Three Key Objects become obtainable
        ↓
Current player collects the objects
        ↓
Current player reaches Final Room
        ↓
Insert 3 objects
        ↓
Escape
```

This preserves the core fantasy:

> **Ghosts do the work. I only need to finish the run.**

---

# 3. Integration Requirements

- Reuse the existing interaction/inventory system wherever possible.
- Reuse the existing Ghost replay/recording system.
- Do not redesign the existing Chess/Red Room.
- The existing Red Square from the Chess Room should be treated as an external input to the Final Room.
- The Cube Room should produce the Blue Sphere as its output.
- The third puzzle room should eventually provide the Yellow Triangle.
- The Final Room should accept all three objects.
- Do not hard-code the final escape to require only the currently available object; the intended requirement is **all three**.
- Avoid unnecessary changes to existing systems unless required to integrate these rooms cleanly.

# 4. Intended Overall Flow

```text
                         Room 2
                    /      |      \
                   /       |       \
                  ↓        ↓        ↓
             Red Door   Blue Door  Yellow Door
                ↓          ↓          ↓
           Chess Room   Cube Room   Third Room
                ↓          ↓          ↓
          Red Square  Blue Sphere  Yellow Triangle
                \          |          /
                 \         |         /
                  └──── Final Room ────┘
                           ↓
                    Insert all 3
                    Key Objects
                           ↓
                         EXIT
```

The three Key Objects are intentionally distinct in both color and shape:

- **Red Square**
- **Blue Sphere**
- **Yellow Triangle**

They should be visually distinct and immediately recognizable in the Final Room.
