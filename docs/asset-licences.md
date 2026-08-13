# Asset licences

Every file in this project that somebody else made, and what it is licensed under.

**Why this exists**: the project has shipped as a free WebGL build, where "it's Creative Commons" was
good enough. It is not good enough for a paid release. **CC is not one licence** — `CC-BY` needs an
attribution line, `CC-BY-SA` asks awkward questions of a commercial derivative, and **`CC-BY-NC`
cannot be sold at all.** Nobody can tell which of those applies to a `.glb` by looking at it, and the
file itself carries no record. So the record is here.

**The rule: nothing ships in a paid build with `UNKNOWN` in its row.** Either the source is found
again and written down, or the asset is replaced.

Status as of 2026-08-14. **Six rows are unresolved** and every one of them is a model.

---

## 3D models

| File | Author | Source | Licence | Notes |
|---|---|---|---|---|
| `ArtAssets/Smooth_Male_Casual@Walking.fbx` | Quaternius | *Smooth Male Casual* | **CC0** | The ghost. 7,932 tris, 31 bones, twelve clips. No attribution required. |
| `ArtAssets/Furniture/messy_bed.glb` | **thethieme** | [Sketchfab — *Messy Bed*](https://sketchfab.com/3d-models/messy-bed-b49dc1778b0b430cabdbad327d6e2e0d) | **CC-BY** | 306.9k tris. The author's own page calls it "old, poorly optimized" and links a newer one. |
| `ArtAssets/Furniture/chess.glb` | **YarikLegendary** | [Sketchfab — *Chess*](https://sketchfab.com/3d-models/chess-6471ad3881ad45dba7634f1442ed3efe) | **CC-BY** | 421.3k tris, board + pieces. Half the pieces are mirrored — see `gotchas.md`. |
| `ArtAssets/Furniture/rubiks_cube.glb` | **Shivansh Singh** | [Sketchfab — *rubik's cube*](https://sketchfab.com/3d-models/rubiks-cube-155420e09a124ec3a3bcca0852280672) | **CC-BY** | 1.7k tris. On the nightstand. |
| `ArtAssets/Furniture/gold_key.glb` | **JeremyW** | [Sketchfab — *Gold Key*](https://sketchfab.com/3d-models/gold-key-34998df98cac4fa4a3a1ac395df5f708) | **CC-BY** | 4.2k tris. |
| ~~`ArtAssets/Furniture/nightstand.glb`~~ | — | — | — | **DELETED 2026-08-14, unused.** Nothing loaded it — the nightstand is built from primitives. It was 8.5 MB of LFS and a CC-BY attribution obligation for a model not in the game. |

### The good news, and the obligation that comes with it

**Nothing is NC and nothing is SA.** Every model is plain **CC-BY**, which permits commercial use — so
Steam is not blocked by any of this, and no asset has to be replaced on licence grounds.

**But CC-BY makes attribution mandatory, and that applies to the free build already published.** This
is not a Steam-only problem. `SceneBuilder.CreditsLine` currently reads:

    FURNITURE MODELS: CREATIVE COMMONS

That is not an attribution. It names no author, no title, no licence version and no link — it is a
statement that a licence exists somewhere. Four separate creators are currently uncredited in a
build that is publicly downloadable.

### What the credits line has to say

CC-BY 4.0 asks for, in any reasonable manner: the **creator's name**, the **title**, the **licence**
(named and linked), and **a link to the material where practicable**. For a Sketchfab page a link is
trivially practicable, so it is required rather than optional — and it doubles as the only thing that
will let anyone re-verify these rows a year from now.

So each entry wants roughly:

    "Messy Bed" by thethieme — CC BY 4.0 — <sketchfab url>

Four of those will not fit on the one-line credits strip the menu currently has. The likely answer is
a credits **panel** — the game already builds wall displays out of `MakeMenuLine`, so a scrollable or
paged list is the same machinery, not new machinery.

## Fonts

| File | Source | Licence | Verified | Notes |
|---|---|---|---|---|
| `Fonts/JetBrains_Mono/` | JetBrains | **SIL OFL 1.1** | `OFL.txt` in tree | Embedding and commercial use both permitted. The OFL copy must travel with the font — it is in the repo, and the credits line names the typeface. |

## Audio

| What | Source | Licence | Notes |
|---|---|---|---|
| `Audio/SFX/*.wav` | `Tools/generate_sfx.py` | **Own work** | Synthesised from the Python standard library, seeded. No sample is sourced. |
| `Audio/Voice/*.wav` | `Tools/generate_narration.ps1` → Windows SAPI, **`Microsoft Zira Desktop`** | **⚠️ UNRESOLVED FOR A PAID RELEASE** | See below. |

### The announcer is the least obvious risk in this list

The 45 PA lines are rendered by a **voice that ships with Windows**. Microsoft licenses those voices
for use *with Windows*; whether that grants the right to **redistribute the rendered audio inside a
commercial product** is a separate question, and the answer is not obviously yes. It is easy to miss
because nothing was downloaded and no licence was ever clicked through — the audio simply came out of
the operating system.

This does not affect a free build. **Resolve it before money changes hands.** The cheapest fix is
already designed in: `generate_narration.ps1`'s own note says takes can be replaced by dropping files
in under the same names, because no C# refers to how they were made. A commercial TTS licence, or an
actual voice actor, is a drop-in.

## Generated, and therefore not a licence question

Everything below is produced by this repo's own code and owned outright. Listed so it is visible that
the third-party surface really is as small as it looks.

- **Both scenes** — `SceneBuilder`. Git-ignored build output.
- **Every texture, icon and symbol** — drawn by `IconCanvas` into a texture at build time.
- **Every material** — `SceneBuilder`.
- **The wall grain normal map** — `MakeNoiseNormalMap`.
- **The seven reflection probe cubemaps** — baked by `BakeReflectionProbes`.
- **`ArtAssets/Generated/*.mesh`** — the prism silhouettes for the escape objects.
- **The ghost afterimage shader** — hand-written for this project.

## Engine

Unity 6000.5.7f1. Unity's own licence terms decide the splash screen and any revenue thresholds; that
is a separate question from asset licensing and is not tracked here.

---

## Before a paid release

1. **Find the four furniture models again.** Sketchfab's CC0 filter, Poly Haven and ambientCG are
   where they most likely came from. Record the URL, the licence variant and the author in the table.
2. **Replace anything that turns out to be NC**, and anything that cannot be identified at all. An
   asset you cannot prove the provenance of is an asset you cannot defend.
3. **Add the attribution lines** any `CC-BY` row turns out to need. `SceneBuilder.CreditsLine`
   currently says `FURNITURE MODELS: CREATIVE COMMONS` — which is a placeholder, not an attribution:
   it names no author and no licence.
4. **Settle the announcer.**
5. **Settle the film** — the largest of the five, and not an asset question. See below.

---

## The film

**This is not an asset licence. It is the biggest unresolved question in this list**, and it is here
because it is the same kind of question: something in this project belongs to somebody else.

| | |
|---|---|
| Work | ***Iteration 1*** (2016 short film) |
| Written / directed by | **Jesse Lupini** — http://www.jesselupini.com/ |
| Distributed by | **DUST** (Gunpowder + Sky) — http://www.watchdust.com |
| BTS documentary | https://vimeo.com/257618651 |

The film's own synopsis: *"Anna wakes up in a strange white room. She has 60 seconds to escape, and
when the timer hits zero she drops dead, only to reawaken in the same room, alongside a shadowy
impression of her previous self. Stuck on repeat, Anna must think ahead and learn from her mistakes to
solve a series of puzzles and escape this dystopian maze with the help of her past lives."*

### What that means for this project, stated plainly

Read that synopsis next to this repo's README and the honest description is not "inspired by". **The
white room, the sixty seconds, the death-and-rewake, the shadowy past selves, and solving puzzles
*with the help of those past selves* are all the film's.** So is the number 60. Room1's and Room2's
contents and layout are close enough that a viewer of the film would recognise them.

What is genuinely this project's own is the part underneath: past selves as **recorded timelines that
re-evaluate their conditions**, custody rules for objects, the accumulation as a designed difficulty
curve, and the three puzzles themselves. That is a real body of invention — and it is invention in the
*mechanism*, which is the layer copyright protects least.

### Where the line actually is

- **Not protected**: a time loop; a sixty-second timer; the idea of past selves helping; game rules
  and mechanics generally.
- **Protected**: the film's footage, music, dialogue, characters (Anna), and its **specific
  audiovisual expression** — a particular room, dressed a particular way, as a creative composition.

The test is substantial similarity in *expression*. The more a room functions as a recreation of a
specific scene, the closer it sits to a derivative work rather than to an independent work sharing a
premise.

**The realistic risk is not a lawsuit. It is a takedown.** Valve and itch.io do not adjudicate
disputes; they act on complaints. A page pulled in release week is the shape this goes wrong in.

### The cheapest resolution, and it is available right now

**Write to Jesse Lupini.** He is findable, the film is nine years old, and short-film directors are
routinely pleased rather than threatened when someone builds on their work. Written permission —
even an informal blessing over email — converts this entire section into a non-problem, and becomes a
line on the store page worth more than it costs: *"made with the permission of the director."*

**Do it now, while the project is pre-commercial.** Asking permission of someone who has nothing to
gain is a conversation. Asking after a Steam page exists is a negotiation.

If permission does not come, the fallback is **divergence**: keep the mechanism, change the
expression. New puzzles are already planned for a Steam release — that is the natural moment for
Room1 and Room2 to stop being the film's rooms and become this game's own.

**Two things that are not fixes.** Crediting the film without permission is not a defence, and if
similarity is ever disputed it evidences access. And the title matters separately — titles are not
copyrightable but they are trademarkable, so the store name wants thought. `Iteration Room` is
adjacent to `Iteration 1`.
