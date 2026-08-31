# Asset licences

Every file in this project that somebody else made, and what it is licensed under.

**Why this exists**: the project has shipped as a free WebGL build, where "it's Creative Commons" was
good enough. It is not good enough for a paid release. **CC is not one licence** — `CC-BY` needs an
attribution line, `CC-BY-SA` asks awkward questions of a commercial derivative, and **`CC-BY-NC`
cannot be sold at all.**

**~~The file itself carries no record~~ — IT DOES, AND THAT WAS THE WHOLE MISTAKE.** Every `.glb`
here came off Sketchfab, and Sketchfab's exporter writes the title, the author, the licence and the
source URL into the glTF's own `asset.extras`. This document spent months being a hand-typed
substitute for data that was inside the files the entire time, and it got two authors wrong, one
licence wrong, and missed thirteen models. **The record is the files now**; `SceneBuilder` reads them
at build time into `ATTRIBUTION.md` and the title screen's CREDITS page.

**What this document is for now**: the things a glTF header cannot tell you — the fonts, the audio,
the engine, and the judgement calls about which licences this project can actually live with.

Status as of 2026-08-23.

---

## 3D models

**THIS TABLE IS NO LONGER THE RECORD.** `SceneBuilder.ReadModelCredits` reads the title, author,
licence and source URL out of each `.glb`'s own glTF `asset.extras` at build time, writes
`ATTRIBUTION.md` from it, and puts the same data on the title screen's CREDITS page. **Read
`ATTRIBUTION.md` for the current list** — it cannot drift, because it is generated from the files.

### Why the hand-typed table was replaced (2026-08-23)

It was checked against the files for the first time and it was wrong in five ways:

| What the table said | What the file says |
|---|---|
| `chess.glb` by **YarikLegendary** | by **xnicrox** |
| `rubiks_cube.glb` by **Shivansh Singh** | by **DoobiDooba** |
| `billiard_balls.glb` by **Yanez Designs** | by **Anthony Yanez** (`paulyanez`) |
| `cctv_camera.glb` is **CC-BY** | **CC-BY-NC-4.0** — non-commercial. ~~The one blocker~~ **the file was deleted 2026-08-28 with the CCTV system it was in** |
| "Every model row is resolved" | **thirteen models were missing from the table entirely** |

Two of those are attributions to the wrong person, which is worse than no attribution. One is a
licence class that cannot be sold. The lesson is the one `FloorButton`'s audio already recorded: a
list of every instance of a thing, maintained by hand, is a list that will be wrong.

### The licence that is not plain CC-BY

**~~`cctv_camera.glb` — CC-BY-NC-4.0~~ GONE 2026-08-28, and how it went is the part worth keeping.**
Three of them stood in room3-2N, one per CCTV feed, and NonCommercial cannot be bought off with
attribution at any price. The fix was never sourcing a replacement camera: the feeds had been watching
a coloured staircase deleted on 2026-08-21, so the whole system was a week past its subject and was
being kept because it worked. Deleting it took the blocker with it. **A licence problem inside a
feature nobody needs is a feature problem** — ask what the asset is FOR before going shopping for
another one. `CheckModelLicences` stays, and fails the build the moment another NC file arrives.

**`wooden_bucket.glb` — CC-BY-SA-4.0.** Room2-2's buckets, a core cycle-2 puzzle. ShareAlike permits
commercial use, so this is not a blocker — but the model and any modification of it stay under
CC-BY-SA, and that is a decision to take deliberately rather than discover. Replacing it is cheap if
the answer is no; a bucket is not a hard model to source.

### The one model in the tree that is not the file that was downloaded

**`Play/bedlam_cube.glb` — CC-BY-4.0, by jamezac, and it is a DERIVATIVE.** The Sketchfab file holds
three colour-merged meshes and an already-assembled cube; room3-2N needs thirteen separate blocks and
a solved packing, and neither is recoverable at runtime. `Tools/split_bedlam_cube.py` produces the
shipped file from the downloaded one — splitting the meshes by connected component, solving the 4x4x4
exact cover, and writing each piece out in its solved pose. See `SceneBuilder.BuildBedlamCube`.

CC-BY permits derivatives; what it requires is that the attribution travels, and it does: the splitter
copies `asset.extras` through verbatim, so `ReadModelCredits` finds the same author, licence and
source URL in the derived file that it would have found in the original, and the CREDITS page names
it like any other model. **Nothing else in `ArtAssets` is modified**, which is why this one gets a
paragraph — a derived asset that silently lost its attribution block would be exactly the failure
this whole document exists about. The original is not in the tree; re-derive it from the Sketchfab URL
in the credits.

The rope round it (`Furniture/velvet_rope.glb`, CC-BY-4.0 by 5CNG5) is **unmodified** and needs no
note beyond this one: it is placed whole, at its own arrangement, and its credit is generated like
every other model's.

Everything else is plain **CC-BY 4.0**, which permits commercial use with attribution — and the
attribution is now generated, so it is met.

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
- **Room3-2N's Bedlam packing** — one of the 19,186 solutions, found by `Tools/split_bedlam_cube.py`.
  The MODEL it is solved for is not ours (above); the arrangement is arithmetic.

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
| Written by | **Jess Lupini** (she/her) **and Lucas Kavanagh** — both co-founders of Avo Media |
| Directed by | **Jess Lupini** — https://jesslupini.com/ |
| Produced by | Arshia Navabi, Mert Sari — made in 8 days for the 2016 **Crazy8s** festival (BC) |
| Best contact | **`hello@avomedia.ca`** — https://www.avomedia.ca, the company Lupini and Kavanagh co-founded. One address, both authors |
| Distributed by | **DUST** (Gunpowder + Sky) — https://www.watchdust.com (redirects to their YouTube channel), `contact@watchdust.com` |
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

### ASKED, AND ANSWERED 2026-08-30. THIS SECTION IS STILL OPEN.

Sent 2026-08-27 to `hello@avomedia.ca`, addressed to both writers. **Jess Lupini replied on behalf
of both on 2026-08-30, warmly, and did not grant permission**: *"we can't give blanket permission
for a commercial release that uses the film's IP... we'd need to come to an agreement first"*, with
that agreement deferred to *"when you're getting close to a store page"*. The full reply and what
follows from it are in `docs/lupini-permission-email.md`.

**So the position is now precise rather than unknown, and it is not better.** Before, nobody had been
asked and silence counted as no. Now the two people who could say yes have been asked, know exactly
what this game is, are pleased it exists — and have declined to permit a commercial release without a
negotiated deal that does not exist yet. **A paid release still has no permission behind it**, and
"made with the permission of the director" is not a line this project may write.

**The thread then closed cordially on 2026-08-31 — "Of course! No worries at all. Looking forward to
seeing how it progresses!" — AND THAT CHANGED NOTHING HERE.** It answers an acknowledgement, not the
original request; the refusal three messages above it still stands. It is the last thing in the
thread and therefore the first thing an eye lands on, which is exactly why it is flagged in both
files. **Never cite it as consent.**

What was gained is real and is not permission: a named contact who invited further contact, both
authors on one thread, no objection to the free prototype, and the takedown-in-release-week scenario
made very unlikely. What was NOT gained is anything that lets a store page open.

**THE NAME AND THE URL IN THIS FILE WERE BOTH WRONG UNTIL 2026-08-26, and the name is the one that
matters.** This document said "Jesse Lupini" and "he" throughout. Her own site
(https://jesslupini.com/) describes her as "a queer, Vancouver-based comedian, writer, director",
uses *she/her*, and claims the film as hers: "Her award-winning film work includes the viral short
*Iteration 1*." The old domain `jesselupini.com` 301-redirects to the new one — a redirect she set up
herself, which is what confirms this is one person rather than two. `www.jesselupini.com`, the
address this file used to give, has no DNS record at all and cannot be reached.

**Address her as Jess, with she/her.** Opening a letter that asks a favour with a name someone has
moved away from is a bad first line and an avoidable one. Credit the film under the same name for the
same reason: it is the name she presents the work under now.

**YOU WILL FIND "JESSE" IN PLENTY OF PLACES. IT IS STALE, NOT CONTRADICTORY.** DUST's own YouTube
description still reads *"Iteration 1" by Jesse Lupini* and still links `Jesse's Site:
http://www.jesselupini.com/` — which is where this file's dead URL came from in the first place.
IMDb, Letterboxd and the 2016 festival credits say the same. All of it is a third party's record of a
2016 credit, none of it has been updated since, and none of it outranks the person's own current
site. What settles it is that **she set up the redirect herself**: `jesselupini.com` 301s to
`jesslupini.com`. That is not two names in parallel, it is one name superseding another.
**Do not "correct" this back on finding an old credit.**

**ASK THE WRITERS, NOT THE DISTRIBUTOR.** `contact@watchdust.com` is easy to find and is the wrong
first door. DUST's channel says every film is "licensed directly from its creators" — a distribution
licence, with the underlying rights still the creators' — so **DUST cannot grant permission to build a
commercial game on the film's premise**, whatever they reply. It is a fallback for one purpose only:
asking how to reach them, if they do not answer directly.

**AND IT IS *WRITERS*, PLURAL — this was missed for as long as the file existed.** The credit is
"written by Jess Lupini and **Lucas Kavanagh**, directed by Jess Lupini". Everything this game takes
is PREMISE — the white room, the sixty-second loop, the past selves — and a premise belongs to the
screenplay, which has two authors. A yes from the director alone is therefore a partial yes.
**`hello@avomedia.ca` reaches both**, because the two of them co-founded that company: Kavanagh is
its Content Director and Lupini its Creative Director. One email, both signatures, and a written
record rather than a DM.

**Crazy8s is a loose end, not a blocker, and it now has a date on it.** The film was made in eight
days on Crazy8s funding, and festival-funded shorts sometimes leave rights partly with the
programme. Nothing found says so here. The writers have now replied and have named the moment
themselves — the store-page conversation — so that is when to ask them whether anyone else has a
claim, as part of working out an agreement rather than as a separate inquiry. **The informal
blessing over email that this paragraph used to hope for is the specific thing they declined**;
what would convert this section into a non-problem is the agreement they offered to work out, and
until it exists no version of *"made with the permission of the director"* may appear anywhere.

**"Ask now, while the project is pre-commercial" was tried and did not work — not because it was
wrong, but because it was never theirs to accept.** The argument was that asking someone with nothing
to gain is a conversation while asking after a Steam page exists is a negotiation. They answered by
scheduling the negotiation: they will not price a game they have not seen finished. Keep the
principle for the next rights-holder this project has to write to, and stop expecting it to have
produced an answer here.

**DIVERGENCE IS NO LONGER THE FALLBACK. IT IS THE LEVER.** Keep the mechanism, change the
expression — that was filed here as what to do if nobody replied. Somebody replied, and made the
terms depend on *"what the game actually becomes"*. Every part of the film's specific expression
still in the finished game is something a future agreement has to cover; every part replaced is
something it does not. Cycles 2 and 3 are already this project's own invention. **Room1 and Room2
are the two rooms a viewer of the film would recognise**, new puzzles for them are already in the
Steam plan, and that work now has a second and larger reason to happen.

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

Key Electricity Lever
3D Model
Avatar of ahmagh2e
Mehdi Shahsavan

Follow
1.7k
1706 Downloads
5.6k
5583 Views
198
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 2.9k
Vertices: 1.6k
More model information
Key Electricity lever

License:
CC AttributionCreative Commons Attribution

Learn more
Published 2 years ago
https://sketchfab.com/3d-models/key-electricity-lever-080e335bb1994eac8d6720297a569bdd

MIRROR - IKEA TRENSUM
3D Model
Avatar of sinnervoncrawsz
YouniqueĪdeaStudio
pro

Follow
5.6k
5562 Downloads
8.1k
8114 Views
211
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 30.6k
Vertices: 15.3k
More model information
This 2-sided mirror can be placed wherever you need it. It has a regular mirror on one side and a magnifying mirror on the other - ideal for shaving, plucking eyebrows or putting on makeup.

License:
CC AttributionCreative Commons Attribution

Learn more
Published 4 years ago
https://sketchfab.com/3d-models/mirror-ikea-trensum-258799ba04874be4a972b13eaf180fa0

Joe | Realistic Human 3D Model
3D Model
Avatar of arjunpkrishna
Arjun P Krishna

Follow
7.4k
7426 Downloads
65.7k
65666 Views
153
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 53.4k
Vertices: 28.7k
More model information
Joe is recent model I created. It’s completely game-ready, it comes with good topology. Thus it’s ready for rigging and animation, skinning will be much easier. There’s a rigged version of Joe which is done in Maya(Advanced Skeleton), if you need it feel free to comment or contact me.

Features:

Game-Ready
Good Topology
PBR Texture
Hope you love it!!

License:
CC AttributionCreative Commons Attribution

Learn more
Published 2 years ago
https://sketchfab.com/3d-models/joe-realistic-human-3d-model-2a108401fe5547409a3ad666b9b7d6b3

Realistic Female
3D Model
Avatar of Biviyt
Biviyt

Follow
19.6k
19574 Downloads
112k
111990 Views
641
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 82.1k
Vertices: 57.5k
More model information
Realistic Female realistic texture model everything rigged

License:
CC Attribution-ShareAlikeCreative Commons Attribution-ShareAlike

Learn more
Published 4 years ago
https://sketchfab.com/3d-models/realistic-female-b0cc2a6c26114da184252e433cf75d23

Hight Quality Realistic Girl Character
3D Model
Avatar of hammer.gamedev
hammer.gamedev

Follow
1.1k
1056 Downloads
3.4k
3380 Views
45
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 80.9k
Vertices: 58k
More model information
Realistic Female Character - High-Quality Humanoid Model

Introducing our meticulously crafted 3D model of a realistic female character, designed with an exceptional level of detail and precision. This high-quality humanoid model is perfect for a wide range of applications including games, animations, and virtual reality projects.

Features: Realistic Anatomy: Carefully modeled with accurate human proportions and anatomy to ensure a lifelike appearance. Detailed Texturing: High-resolution textures with intricate details, including skin pores, subtle facial features, and natural hair. Rigged Skeleton: Fully rigged with a comprehensive bone structure, allowing for smooth and realistic movements. Ideal for animators and game developers looking for fluid character motion.

Tags: #3DModel #FemaleCharacter #Humanoid #Realistic #HighQuality #Rigged #GameAsset #Animation #VirtualReality

License:
CC AttributionCreative Commons Attribution

Learn more
Published 2 years ago
https://sketchfab.com/3d-models/hight-quality-realistic-girl-character-e604d0b19f2f44c08f58bacad2ed03d8

Arm
3D Model
Avatar of Just8
Just8

Follow
9.3k
9256 Downloads
27.2k
27206 Views
267
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 87.8k
Vertices: 44k
More model information
A photoscan of my arm i created for a little render 😊 Idk what you could use this for but u can download it for free :)

License:
CC AttributionCreative Commons Attribution

Learn more
Published 3 years agoJun 10th 2023
Uploaded with Substance Painter
https://sketchfab.com/3d-models/arm-76c7f128c3fd427ca939c3050ae95e26

FREE GameReady [FPS Female Arms]
3D Model
Avatar of bamenwo05
BAMEN

Follow
304
304 Downloads
1.2k
1218 Views
13
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 38.8k
Vertices: 19.5k
More model information
GameReady Female First Peson Arms
Ditails
Topology: Perfect Quad-topology

Textures: 4k PBR

BaseColor
Roughness
Normal
RIG: RIGGED, skeleton included
Add: Example poses inclided, ready to animate

Usage:
Female Game Character. Perfect for FPS Games (Shooter, Survive and other games) I love ScetchFab, so I want to bring something usefull for all people here <3

Please enjoy this model <3

License:
CC AttributionCreative Commons Attribution

Learn more
Published 22 days ago


First Person hands rigged
3D Model
Avatar of davidfischer
David Fischer

Follow
37.9k
37910 Downloads
140k
139972 Views
830
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 8.2k
Vertices: 4.2k
More model information
Its a .blend file and a .fbx

Material applied and ready to animate.

First person hands for shooters or VR applications.

License:
CC AttributionCreative Commons Attribution

Learn more
Published 12 years agoNov 24th 2014
No category set.
https://sketchfab.com/3d-models/first-person-hands-rigged-547a45535f0c4fe787948f7a7a6a88db

Hight Quality Realistic Girl Character
3D Model
Avatar of hammer.gamedev
hammer.gamedev

Follow
668
668 Downloads
2.3k
2330 Views
28
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 78.3k
Vertices: 55.7k
More model information
Realistic Female Character - High-Quality Humanoid Model

Introducing our meticulously crafted 3D model of a realistic female character, designed with an exceptional level of detail and precision. This high-quality humanoid model is perfect for a wide range of applications including games, animations, and virtual reality projects.

Features: Realistic Anatomy: Carefully modeled with accurate human proportions and anatomy to ensure a lifelike appearance. Detailed Texturing: High-resolution textures with intricate details, including skin pores, subtle facial features, and natural hair. Rigged Skeleton: Fully rigged with a comprehensive bone structure, allowing for smooth and realistic movements. Ideal for animators and game developers looking for fluid character motion.

Tags: #3DModel #FemaleCharacter #Humanoid #Realistic #HighQuality #Rigged #GameAsset #Animation #VirtualReality

License:
CC AttributionCreative Commons Attribution

Learn more
Published 2 years ago
https://sketchfab.com/3d-models/hight-quality-realistic-girl-character-baba4b0b710745ceabfea9a9aede7f42

BEDLAM CUBE
3D Model
Avatar of jamezacristancho
jamezac

Follow
318
318 Downloads
4.2k
4189 Views
25
Unlike

Download 3D Model

Add to

Embed

Share
Report
Triangles: 13.1k
Vertices: 6.6k
More model information
The Bedlam Cube and his thirteen polycubic pieces.

License:
CC AttributionCreative Commons Attribution

Learn more
Published 6 years ago
https://sketchfab.com/3d-models/bedlam-cube-19ba7ec5181c46bcaa813f87a8dd35c9

Velvet Rope LowPoly
3D Model
Avatar of 5CNG5
5CNG5

Follow
490
490 Downloads
1.5k
1513 Views
16
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 21.2k
Vertices: 10.9k
More model information
https://www.instagram.com/cng.3ds/

License:
CC AttributionCreative Commons Attribution

Learn more
Published 4 years ago
https://sketchfab.com/3d-models/velvet-rope-lowpoly-83994d49179b41cebdf91d2c7fc78aab

Ladder
3D Model
Avatar of niver_mk
niver_mk

Follow
11.8k
11765 Downloads
20.1k
20074 Views
497
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 2.6k
Vertices: 1.5k
More model information
Textures resolution - 1024x1024.

License:
CC AttributionCreative Commons Attribution

Learn more
Published 8 years ago
https://sketchfab.com/3d-models/ladder-adf229c426194740ab0ec9cdb00262b4

### Three clipboards were downloaded; ONE ships

**In the game: "Clipboard" by Cookie (CC BY 4.0)** — `Assets/ArtAssets/Play/clipboard.glb`. It is
the intake notice on room1-1's floor. `ATTRIBUTION.md` and the CREDITS page carry it automatically,
read out of the file's own `asset.extras`.

**Not in the game, and not in `Assets/`** — the two below were evaluated and rejected. They are
recorded here because the raw licence blocks are pasted below and a reader would otherwise assume
all three ship, and credit two authors whose work is not in the build:

- *Document Clipboard with Pen* by Kami Rapacz — 62k triangles and 31 MB of textures for a prop read
  once, and its paper mesh is **curled rather than flat**, so text laid on it warps.
- *Clipboard_7MB* by Mehdi Shahsavan — the whole model is **one material**, so the page cannot be
  printed on separately at all. That was the disqualifier; nothing else about it mattered.

**What decided it was not the licence** (all three are CC BY 4.0) **but whether the page is its own
mesh.** Cookie's is a 24-vertex flat quad with its own `page` material and clean 0..1 UVs, so the
notice is a world-space canvas laid exactly on it. That is the question to ask of any model this
project has to write on.

Document Clipboard with Pen
3D Model
Avatar of kuroderuta
Kami Rapacz

Follow
7.9k
7887 Downloads
17.1k
17123 Views
424
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 62.1k
Vertices: 31.2k
More model information
ISO-216 A4 sized clipboard prop from an unreleased project. Enjoy and don’t forget to credit!

Features:
Segmented Mesh Parts (Tablet, Pen, Pages).

SubD Optimized Mesh.

2k PBR Textures (8bit PNG, 16bit Normal Maps).

Like my work and want to hire me for your project? Contact me at kamilbubela@gmail.com

License:
CC AttributionCreative Commons Attribution

Learn more
Published 3 years ago
https://sketchfab.com/3d-models/document-clipboard-with-pen-8650234a2e7949ca9fe9b4124e000d97

Clipboard
3D Model
Avatar of cookiepop
Cookie

Follow
10.5k
10493 Downloads
27.6k
27588 Views
146
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 2.3k
Vertices: 1.1k
More model information
A simple clipboard. Not sure what else to say about it!

License:
CC AttributionCreative Commons Attribution

Learn more
Published 8 years ago
https://sketchfab.com/3d-models/clipboard-a37158f20ccf436483029e8295629738

Clipboard_7MB
3D Model
Avatar of ahmagh2e
Mehdi Shahsavan

Follow
2.6k
2617 Downloads
6.2k
6158 Views
189
Like

Download 3D Model

Add to

Embed

Share
Report
Triangles: 656
Vertices: 354
More model information
Clipboard

License:
CC AttributionCreative Commons Attribution

Learn more
Published 2 years ago
https://sketchfab.com/3d-models/clipboard-7mb-a5f71d0bd08d4653880a92f19dbbf72a