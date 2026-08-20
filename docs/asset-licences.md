# Asset licences

Every file in this project that somebody else made, and what it is licensed under.

**Why this exists**: the project has shipped as a free WebGL build, where "it's Creative Commons" was
good enough. It is not good enough for a paid release. **CC is not one licence** — `CC-BY` needs an
attribution line, `CC-BY-SA` asks awkward questions of a commercial derivative, and **`CC-BY-NC`
cannot be sold at all.** Nobody can tell which of those applies to a `.glb` by looking at it, and the
file itself carries no record. So the record is here.

**The rule: nothing ships in a paid build with `UNKNOWN` in its row.** Either the source is found
again and written down, or the asset is replaced.

Status as of 2026-08-20. **Every model row is resolved.** The one that was not — `weigh_scale.glb` —
was closed on 2026-08-20 from the source pages pasted into the raw notes at the bottom of this file.

---

## 3D models

| File | Author | Source | Licence | Notes |
|---|---|---|---|---|
| `ArtAssets/Smooth_Male_Casual@Walking.fbx` | Quaternius | *Smooth Male Casual* | **CC0** | The ghost. 7,932 tris, 31 bones, twelve clips. No attribution required. |
| `ArtAssets/Furniture/messy_bed.glb` | **thethieme** | [Sketchfab — *Messy Bed*](https://sketchfab.com/3d-models/messy-bed-b49dc1778b0b430cabdbad327d6e2e0d) | **CC-BY** | 306.9k tris. The author's own page calls it "old, poorly optimized" and links a newer one. |
| `ArtAssets/Furniture/chess.glb` | **YarikLegendary** | [Sketchfab — *Chess*](https://sketchfab.com/3d-models/chess-6471ad3881ad45dba7634f1442ed3efe) | **CC-BY** | 421.3k tris, board + pieces. Half the pieces are mirrored — see `gotchas.md`. |
| `ArtAssets/Furniture/rubiks_cube.glb` | **Shivansh Singh** | [Sketchfab — *rubik's cube*](https://sketchfab.com/3d-models/rubiks-cube-155420e09a124ec3a3bcca0852280672) | **CC-BY** | 1.7k tris. On the nightstand. |
| `ArtAssets/Furniture/gold_key.glb` | **JeremyW** | [Sketchfab — *Gold Key*](https://sketchfab.com/3d-models/gold-key-34998df98cac4fa4a3a1ac395df5f708) | **CC-BY** | 4.2k tris. |
| `ArtAssets/Play/seesaw.glb` | **IronEqual** (ie-niels) | [Sketchfab — *Seesaw from Poly by Google*](https://sketchfab.com/3d-models/seesaw-from-poly-by-google-b518b55b56f249ee902205775d2d0cdd) | **CC-BY** | 3.2k tris. A Poly backup. Scenery on room2-5's ledge. |
| `ArtAssets/Play/rubber_duck.glb` | **Ikki_3d** | [Sketchfab — *Rubber duck*](https://sketchfab.com/3d-models/rubber-duck-f1de4fc390db4266a509b9739350512a) | **CC-BY** | 3.5k tris. Three of them float on room2-5's pool. |
| `ArtAssets/Play/weigh_scale.glb` | **Dimitri** (dimitri_blender) | [Sketchfab — *Digital Weight Scale*](https://sketchfab.com/3d-models/digital-weight-scale-1b1b2ad2f06640528e67c1c37e0359b0) | **CC-BY** | 2.4k tris. Room2-7's platform scale, scaled up ~6x. Its green display is embossed geometry with no texture — `SceneBuilder.FlatPanelMesh` replaces that submesh with a quad so `PanelDigits` can paint the readout onto the model itself. Carries a **NoAI** notice. |
| `ArtAssets/Play/industrial_valve.glb` | **Miguel Angel Jimenez** (Mangel Tekila) | [Sketchfab — *Industrial valve*](https://sketchfab.com/3d-models/industrial-valve-cea67369ae50485c9f06ea44cb608b92) | **CC-BY** | 3.2k tris. Three of them on room2-6's walls. Its textures are 175MB as imported — `ShrinkModelTextures` takes them to 5MB. |
| `ArtAssets/Play/beach_ball.glb` | **MaggaModels** | [Sketchfab — *Beach Ball*](https://sketchfab.com/3d-models/beach-ball-25e1816c0e22444bb62816d3999d1b0b) | **CC-BY** | 3.6k tris. Two of them, the biggest things on that pool. |
| `ArtAssets/Play/billiard_balls.glb` | **Yanez Designs** | [Sketchfab — *Billiard Balls*](https://sketchfab.com/3d-models/billiard-balls-523ac862d2154a7e8c96b964fb7cb11f) | **CC-BY** | 15.4k tris, sixteen balls as sixteen nodes each with its own 512x256 texture. Nine of them are room2-0's keys; the set is instantiated once and robbed, like `chess.glb`. Textures are small enough that `ShrinkModelTextures` is not called on it. |
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
| `Resources/Fonts/D2Coding.ttf` | NAVER Corporation | **SIL OFL 1.1** | `D2Coding-LICENSE.txt` beside it | The Korean UI face. Reserved Font Name "D2Coding", so a *modified* copy may not keep the name — we ship it unmodified. The licence sits inside `Resources/` deliberately: everything in that folder is included in the build, so the licence cannot be shipped without. **The credits line does not name it yet** — see "Before a paid release". |

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




A Water Tap
3D Model
Avatar of mellydeeis
3Dee

Follow
2.8k
2762 Downloads
9.6k
9552 Views
97
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 69.7k
Vertices: 35k
More model information
A Water Tap

License:
CC AttributionCreative Commons Attribution

Learn more
Published 7 years ago
https://sketchfab.com/3d-models/a-water-tap-e70f6136b2334081a13470118f3b8378

Boiling Water Tap
3D Model
Avatar of dm3V
dm3V

Follow
3.2k
3237 Downloads
7k
6950 Views
77
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 8.1k
Vertices: 4.1k
More model information
A kitchen tap with boiling water handle

License:
CC AttributionCreative Commons Attribution

Learn more
Published 7 years ago

https://sketchfab.com/3d-models/boiling-water-tap-b97e6b20be564f1e85c104c2e9e50766

Modern Faucet (high poly)
3D Model
Avatar of luca3d
Elasta Kristya

Follow
2.3k
2284 Downloads
4.5k
4527 Views
60
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 52.2k
Vertices: 26.2k
More model information
Wall Faucet for bathroom or sink

License:
CC AttributionCreative Commons Attribution

Learn more
Published 7 years ago
https://sketchfab.com/3d-models/modern-faucet-high-poly-0982ad18e2fd4ab7abcb5f5a79ee70a7

Valve II
3D Model
Avatar of victorhugohc
Víctor Hernández

Follow
977
977 Downloads
3.3k
3264 Views
55
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 201.4k
Vertices: 100.6k
More model information
Made in SolidWorks.

Buy Me a Coffee

License:
CC AttributionCreative Commons Attribution

Learn more
Published 8 years ago
https://sketchfab.com/3d-models/valve-ii-08a002755c784c889b8818eac15060ee

Factory pipe kit
3D Model
Avatar of Just8
Just8

Follow
3.4k
3358 Downloads
8.4k
8429 Views
289
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 74.4k
Vertices: 37.7k
More model information
A few modular mid-poly pipes from my unreal project 😎 In the future might add few more different pipe segments and connections but for now this already took more than 4h 🥲 Soo u dont have to be like me and u can just download this and not waste half of your day creating pipes for one room :)

License:
CC AttributionCreative Commons Attribution

Learn more
Published 4 years ago
https://sketchfab.com/3d-models/factory-pipe-kit-647a6eb8ef0049de892cfacd79221c3a

Gear Clock
3D Model
Avatar of Cipher95
Mrinal Sumitran

Follow
1.2k
1169 Downloads
3.5k
3477 Views
98
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 177.4k
Vertices: 90.3k
More model information
No description provided.

License:
CC AttributionCreative Commons Attribution

Learn more
Published 7 years ago
https://sketchfab.com/3d-models/gear-clock-bab1a7a488d94c6da9278517afc19d0f

Wooden Bucket
3D Model
Avatar of romullus
romullus

Follow
1.9k
1895 Downloads
5k
4963 Views
132
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 9k
Vertices: 4.5k
More model information
Small decorative wooden bucket.

License:
CC Attribution-ShareAlikeCreative Commons Attribution-ShareAlike

Learn more
Published 8 years ago
https://sketchfab.com/3d-models/wooden-bucket-217dd46026ac4cfd947097ad3c466bf2


Oak tree
3D Model
Avatar of massive-graphisme
massive-graphisme

Follow
28.3k
28317 Downloads
62.9k
62914 Views
711
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 7.1k
Vertices: 4.4k
More model information
A good old oak tree. Bark texture made using Substance Painter. High definition textures

License:
CC AttributionCreative Commons Attribution

Learn more
Published 6 years ago
https://sketchfab.com/3d-models/oak-tree-3dc59560f2d24345bdbe65c44636453b

Realistic Tree
3D Model
Avatar of danielpetrov
Daniel

Follow
19.9k
19948 Downloads
53.4k
53408 Views
497
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 19.9k
Vertices: 22.8k
More model information
An optimized, low poly realistic tree with OpenGL normal maps.

License:
CC AttributionCreative Commons Attribution

Learn more
Published 2 years ago
https://sketchfab.com/3d-models/realistic-tree-d989c0f801d847b9a74992ec4ddcfdfc

Fire Axe
3D Model
Avatar of denis_cliofas
denis_cliofas

Follow
8k
7993 Downloads
18k
18009 Views
271
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 912
Vertices: 458
More model information
A low poly fire axe model made blender and finished in substance painter. Tris: 912 Textures: 4K PBR

License:
CC AttributionCreative Commons Attribution

Learn more
Published 6 years ago
https://sketchfab.com/3d-models/fire-axe-b9d83055bb3b467c88496299c27ceae0

GA Free_151_Old Tree Stump
3D Model
Avatar of galaxyabundant
Galaxy Abundant
pro

Follow
202
202 Downloads
583
583 Views
16
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 1.3M
Vertices: 664k
More model information
Old Tree Stump – Fantasy Forest Nature Prop

A high-detail old tree stump prop designed for fantasy and realistic forest environments. This asset features a broad cut surface with visible growth rings, rugged bark, exposed roots, moss growth, and decayed hollow details that add age and environmental storytelling.

Built as a standalone nature prop, it works well in forest paths, woodland clearings, abandoned camps, fantasy villages, overgrown ruins, and open-world environments. Its strong silhouette and weathered organic detail make it suitable as both a supporting environment piece and a small storytelling prop.

Features

Large weathered tree stump with visible wood rings Exposed roots, bark texture, moss, and decay detail Hollowed and broken sections for natural age and realism Suitable for fantasy, realistic, survival, and RPG scenes Works as a forest prop, woodland dressing element, or environment storytelling asset

License:
CC AttributionCreative Commons Attribution

Learn more
Published a month agoJul 3rd 2026
Generated with AI
https://sketchfab.com/3d-models/ga-free-151-old-tree-stump-e79aa9aa3ef947cca37fff4e4ff2d6eb

Stylized tree stump
3D Model
Avatar of Aartee
Aartee

Follow
7.6k
7599 Downloads
19.1k
19107 Views
406
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 1.1k
Vertices: 564
More model information
Simple tree stump. Model was sculpted and then decimation was applied. It’s free. If you like it LIKE IT, please. Enjoy.

License:
CC AttributionCreative Commons Attribution

Learn more
Published 7 years ago
https://sketchfab.com/3d-models/stylized-tree-stump-04d51f5c2fb643aab3b93b451d1b77c9

Slide PlayGround
3D Model
Avatar of vaedskalw
vaedskalw

Follow
12.9k
12887 Downloads
27.1k
27062 Views
252
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 78.1k
Vertices: 39.4k
More model information
Slide from kids playground. Wooden stairs and color painted.

License:
CC AttributionCreative Commons Attribution

Learn more
Published 6 years ago
https://sketchfab.com/3d-models/slide-playground-e59c8559ba42463881732790dcbfbb3c

FFPS Discount Ball Pit
3D Model
Avatar of skylajade69
skylajade69

Follow
793
793 Downloads
2.4k
2358 Views
89
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 21.6k
Vertices: 12.7k
More model information
Who thought this was a good idea?

For legal reasons, the previous sentence was a joke.

License:
CC AttributionCreative Commons Attribution

Learn more
Published 2 years ago
https://sketchfab.com/3d-models/ffps-discount-ball-pit-760904b50a46430f886803665518da2f

Seesaw from Poly by Google
3D Model
Avatar of ie-niels
IronEqual

Follow
1.2k
1163 Downloads
2.8k
2801 Views
23
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 3.2k
Vertices: 1.7k
More model information
This is a backup of a Poly Asset named Seesaw. Saved from Poly by Google. Preview may be without textures, they are still in the Download ZIP with a preview thumbnail.

License:
CC AttributionCreative Commons Attribution

Learn more
Published 6 years ago
https://sketchfab.com/3d-models/seesaw-from-poly-by-google-b518b55b56f249ee902205775d2d0cdd

Rubber duck
3D Model
Avatar of ikki_3d
Ikki_3d

Follow
15.4k
15372 Downloads
43.5k
43479 Views
257
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 3.5k
Vertices: 1.9k
More model information
A cute little rubber duck

License:
CC AttributionCreative Commons Attribution

Learn more
Published 6 years agoJun 13th 2020
Uploaded with Substance Painter
https://sketchfab.com/3d-models/rubber-duck-f1de4fc390db4266a509b9739350512a

Beach Ball
3D Model
Avatar of MaggaModels
Maggatron

Follow
12.1k
12097 Downloads
22.5k
22482 Views
227
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 3.6k
Vertices: 2.1k
More model information
For Day 27 of Nodevember, I made a beach ball. But like a really big one. You know those giant beach balls they bounce on top of crowds at beach concerts? I modeled one of those big beach balls.

License:
CC AttributionCreative Commons Attribution

Learn more
Published 5 years ago
https://sketchfab.com/3d-models/beach-ball-25e1816c0e22444bb62816d3999d1b0b

Industrial Valve
3D Model
Avatar of mangel.jimenez
Miguel Ángel

Follow
3.2k
3169 Downloads
7.1k
7114 Views
137
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 1.9k
Vertices: 2k
More model information
Valve 3D model

Model made in 3ds Max 2015
Materials made in Photoshop
Miguel Angel Jimenez (Mangel Tekila) - 2017

License:
CC AttributionCreative Commons Attribution

Learn more
Published 9 years ago
https://sketchfab.com/3d-models/industrial-valve-cea67369ae50485c9f06ea44cb608b92

Digital Weight Scale
3D Model
Avatar of dimitri_blender
Dimitri

Follow
867
867 Downloads
2.3k
2282 Views
27
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 2.4k
Vertices: 1.4k
More model information
Digital Weight Scale created in Blender. Made in 15 minutes and pretty happy with the result. Free to download, edit and use it in any way that you like.

License:
CC AttributionCreative Commons Attribution

Learn more
Published 4 years agoJun 19th 2022
NoAI: This model may not be used in datasets for, in the development of, or as inputs to generative AI programs.
https://sketchfab.com/3d-models/digital-weight-scale-1b1b2ad2f06640528e67c1c37e0359b0

Billiard Balls
3D Model
Avatar of Yanez-Designs
Yanez Designs

Follow
2.8k
2842 Downloads
6.2k
6246 Views
58
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 15.4k
Vertices: 7.7k
More model information
A billiard ball is a small, hard ball used in cue sports, such as carom billiards, pool, and snooker. The number, type, diameter, color, and pattern of the balls differ depending upon the specific game being played. Various particular ball properties such as hardness, friction coefficient and resilience are important to accuracy.’

Info Link: https://en.wikipedia.org/wiki/Billiard_ball

I have a Patreon Join now! :https://www.patreon.com/user?u=14434838

License:
CC AttributionCreative Commons Attribution

Learn more
Published 8 years ago
https://sketchfab.com/3d-models/billiard-balls-523ac862d2154a7e8c96b964fb7cb11f

CCTV Camera
3D Model
Avatar of fairlight51
Jako

Follow
2.8k
2750 Downloads
7.8k
7754 Views
129
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 3.7k
Vertices: 2.1k
More model information
cctv camera lowpoly 3d model free to download

License:
CC Attribution-NonCommercialCreative Commons Attribution-NonCommercial

Learn more
Published 3 years ago
https://sketchfab.com/3d-models/cctv-camera-22ca80ef73034cb69597ef247816bbb3

TV CCTV Monitor
3D Model
Avatar of Simon_M2099
Simon_M2099

Follow
5.2k
5170 Downloads
11.9k
11894 Views
223
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 13.2k
Vertices: 7k
More model information
CCTV monitor from the monitoring system from the 80’s 90’s

PBR textures: one set for case 2K one set for screen 2K, you can also very easily make your texture on screen

Make for game Urbex Night Security.

You can easily reduce the number of triangles (to ~6k) by removing some of the plugs and sockets on the back.

License:
CC AttributionCreative Commons Attribution

Learn more
Published 4 years ago
https://sketchfab.com/3d-models/tv-cctv-monitor-2273be0837bc453b982dfb19d82c9cc0

Hanging Monitor
3D Model
Avatar of MaX3Dd
MaX3Dd

Follow
3.8k
3797 Downloads
6.2k
6238 Views
63
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 2.4k
Vertices: 1.3k
More model information
Hanging Monitor

Low-poly Ready to use in Games AR/VR (2370 tris)
Textures are in PNG format 2048x2048 PBR metalness 1 set
screen is a separate mesh
License:
CC AttributionCreative Commons Attribution

Learn more
Published 2 years ago
https://sketchfab.com/3d-models/hanging-monitor-1bcb147db4914410a02e4a134f9bf870