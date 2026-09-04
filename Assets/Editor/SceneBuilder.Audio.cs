using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IterationRoom.EditorTools
{
    // EVERY SOUND THE BUILD AUTHORS: the PA and its tannoy chain, the generated clips, and
    // `MakeSource` - the ONLY place in the project that makes an `AudioSource`, which is why the
    // effects' -4 dB lives there as one number.
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {

        private static void UseSinglePillow(GameObject bed, string keepName, string hideName, float centreX)
        {
            Transform hide = FindDescendant(bed.transform, hideName);
            if (hide != null) hide.gameObject.SetActive(false);

            Transform keep = FindDescendant(bed.transform, keepName);
            if (keep == null) return;

            Renderer[] renderers = keep.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            // Shift in world space: the model root is rotated, so nudging localPosition.x would
            // move the pillow along the model's axis rather than the room's.
            keep.position += new Vector3(centreX - bounds.center.x, 0f, 0f);
        }

        // Swaps a model's material for a copy with baked ambient occlusion switched off. The glb's
        // own materials are sub-assets that get regenerated on every reimport, so the copy has to
        // live as its own asset rather than being edited in place. The scene's SSAO still supplies
        // real contact shadows, so nothing is lost.
        private static void DisableBakedOcclusion(GameObject model, string materialName, string assetName)
        {
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.sharedMaterial == null || renderer.sharedMaterial.name != materialName) continue;

                string path = $"{MaterialsDir}/{assetName}.mat";
                Material copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (copy == null)
                {
                    copy = new Material(renderer.sharedMaterial);
                    AssetDatabase.CreateAsset(copy, path);
                }

                // Re-copy every build so the glb staying the source of truth for the other maps.
                copy.shader = renderer.sharedMaterial.shader;
                copy.CopyPropertiesFromMaterial(renderer.sharedMaterial);
                if (copy.HasProperty("occlusionTexture_strength")) copy.SetFloat("occlusionTexture_strength", 0f);

                EditorUtility.SetDirty(copy);
                renderer.sharedMaterial = copy;
            }
            AssetDatabase.SaveAssets();
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        // A MOTOR AND A CLIP ON EVERY DOOR, factored out of `BuildAudio` so the per-cycle rebuild can
        // call it too.
        //
        // It had to be: `RebuildCycleTwo` builds that cycle without the rest of the game, and
        // `BuildAudio` lives in the full build - so a cycle rebuilt on its own came out with seven
        // doors that had no AudioSource and no clip and opened in silence. That is the standing hazard
        // of a partial build, and the answer is that anything a cycle needs has to be reachable from
        // the cycle's own path rather than only from `Build`.
        private static void WireDoorAudio(Door[] doors)
        {
            AudioClip doorOpen = LoadClip(SfxDir, "sfx_door_open");
            foreach (Door d in doors)
            {
                if (d == null) continue;
                d.audioSource = MakeSource(d.transform, "DoorAudio", 1f, 1f);
                d.openClip = doorOpen;
            }
        }

        private static (NarrationDirector, RoomAmbience) BuildAudio(GameObject player, Door[] doors, WakeUpSequence wakeUp)
        {
            GameObject root = new GameObject("Audio");

            // --- the PA announcer ---
            GameObject paGO = new GameObject("PA");
            paGO.transform.SetParent(root.transform, false);
            NarrationDirector narration = paGO.AddComponent<NarrationDirector>();
            // 2D on purpose: a room-wide tannoy has no position you could walk away from.
            narration.voiceSource = MakeSource(paGO.transform, "Voice", 0f, 1f, pa: true);
            narration.chimeSource = MakeSource(paGO.transform, "Chime", 0f, 0.7f, pa: true);
            // Both go through the same speaker, so both get the same treatment.
            (AudioEchoFilter voiceEcho, AudioReverbFilter voiceReverb) =
                AddTannoyFilters(narration.voiceSource);
            // The chime is not trimmed. It is two notes with nothing to smear, and it is the same
            // two notes in every language - see NarrationDirector.
            AddTannoyFilters(narration.chimeSource);

            narration.voiceEcho = voiceEcho;
            narration.voiceReverb = voiceReverb;
            // THE NUMBERS LIVE HERE, not in the component (CLAUDE.md §2). English is what
            // `AddTannoyFilters` just authored, restated so switching back is a real assignment
            // rather than "whatever the filter happens to hold".
            narration.tannoy = new NarrationDirector.TannoyTrim
            {
                echoWetMix = 0.26f,
                reverbDecayTime = 2.3f,
                reverbLevel = 250f,
            };

            // **ONE SET. THE KOREAN FOLDER IS GONE** (2026-09-02, by request) - the PA speaks English
            // in every language and the player reads a subtitle, which is the same rule the signage
            // has always followed. See `PaSubtitle` and `NarrationDirector.english`.
            narration.english = LoadVoiceSet(VoiceDir);
            narration.announcementChime = LoadClip(SfxDir, "sfx_chime");

            // THE CAPTIONS. Found rather than threaded down, because the HUD is built long before the
            // PA is and the two have no other reason to know about each other. Same scene, so this is
            // not one of the references `SplitCyclesIntoScenes` can null - see `cross-scene-report.txt`.
            PaSubtitle subtitle = Object.FindFirstObjectByType<PaSubtitle>();
            if (subtitle == null)
                Debug.LogWarning("[SceneBuilder] No PaSubtitle in the scene - the PA will speak with "
                               + "no captions, so a player who does not read English gets nothing.");
            narration.subtitle = subtitle;
            if (subtitle != null)
            {
                subtitle.narration = narration;
                // **AND THE LOOP, FOR ONE QUESTION ONLY: IS A MENU UP.** The subtitle key (M) is
                // polled by the component itself, and a press captured by a rebinding row must not
                // also toggle the thing that key currently does. Found the same way the subtitle
                // itself is, and in the same scene, so it is not a reference the cycle split can
                // null - `cross-scene-report.txt` is the check.
                subtitle.loop = Object.FindFirstObjectByType<LoopManager>();
                if (subtitle.loop == null)
                    Debug.LogWarning("[SceneBuilder] The PA subtitle has no LoopManager, so its "
                                   + "toggle key will fire while the pause menu is capturing a "
                                   + "rebind. Harmless, but it means M cannot be rebound cleanly.");
            }

            // Say so rather than shipping a silent PA. There is one folder now, so this is no longer
            // a translation warning - it is "the generator has never been run in this checkout", which
            // is the state a fresh clone is in.
            if (narration.english == null || narration.english.iterationGenericLine == null)
                Debug.LogWarning("[SceneBuilder] No voice lines in " + VoiceDir + " - the PA will be "
                               + "silent. Run Tools/generate_narration.py (see its header).");

            // --- room tone and machinery ---
            GameObject ambienceGO = new GameObject("Ambience");
            ambienceGO.transform.SetParent(root.transform, false);
            RoomAmbience ambience = ambienceGO.AddComponent<RoomAmbience>();
            ambience.musicSource = MakeSource(ambienceGO.transform, "Music", 0f, 0.5f, loop: true);
            ambience.machineSource = MakeSource(ambienceGO.transform, "Machines", 0f, 0.7f);
            ambience.ominousLoop = LoadClip(SfxDir, "sfx_ominous_loop");
            // The two halves of the loop boundary, split across the blackout by LoopManager.
            ambience.pullIn = LoadClip(SfxDir, "sfx_pull_in");
            ambience.powerDown = LoadClip(SfxDir, "sfx_power_down");

            // ~~THE FLOOR PADS~~ WIRED IN `BuildFloorButton` SINCE 2026-08-21. They were wired
            // here from a hand-written list, cycle 3's four were never added to it, and play found
            // them silent. See that function for why the list itself was the bug.

            // --- the doors ---
            WireDoorAudio(doors);

            // --- the body waking up ---
            // 2D and parented to the player: this is the player's own breath, not a sound in the
            // room. It hangs off the player rather than the WakeUpSequence, which lives on the UI
            // canvas where a positional source would be meaningless.
            wakeUp.bodySource = MakeSource(player.transform, "BodyAudio", 0f, 1f);
            wakeUp.gaspClip = LoadClip(SfxDir, "sfx_gasp");
            wakeUp.sheetRustleClip = LoadClip(SfxDir, "sfx_sheet_rustle");

            return (narration, ambience);
        }

        // Turns a clean AudioSource into a wall-mounted PA horn firing into a hard, empty room.
        //
        // Done as a runtime filter chain rather than baked into the WAVs on purpose: the clips stay
        // clean masters, the treatment is one Inspector tweak away from being retuned, and a
        // replacement take dropped into Assets/Audio/Voice/ inherits the whole thing for free.
        //
        // Component order IS the signal chain - Unity runs the filters top to bottom on the
        // GameObject, so these are added in the order they should process. Reverb last: putting it
        // ahead of the distortion would grit up the tail as well as the voice, which reads as a
        // broken speaker rather than a room.
        // Returns the two filters whose settings are language-dependent, so `NarrationDirector` can
        // dial them without owning the rest of the chain. Everything else here is the same in every
        // language: the band-limiting and the drive are the SPEAKER, and the speaker does not change.
        private static (AudioEchoFilter echo, AudioReverbFilter reverb) AddTannoyFilters(AudioSource source)
        {
            GameObject go = source.gameObject;

            // Band-limit first. A horn driver has no bottom and no top, and losing both is most of
            // what makes a voice read as "coming out of a speaker" rather than as narration - the
            // ear identifies the channel long before it identifies the reverb.
            // **340 -> 240** (2026-09-02, by request: the PA sounds sharp). Measured: the chain was
            // taking the 0-300 Hz band from 16.9% of the clip's energy down to 9.8%, which is 42% of
            // the voice's body removed - and a voice with no body is all edge whatever else is done
            // to it. At 240 it comes back to 16.6%, i.e. to where the recording had it.
            //
            // Still a high-pass, and still doing the job `docs/audio.md` records: what makes this
            // read as "coming out of a speaker" is the band being LIMITED, not where exactly the
            // bottom is. A telephone starts at 300; a horn on a wall is bigger than a telephone.
            AudioHighPassFilter hp = go.AddComponent<AudioHighPassFilter>();
            hp.cutoffFrequency = 240f;
            hp.highpassResonanceQ = 1f;

            AudioLowPassFilter lp = go.AddComponent<AudioLowPassFilter>();
            lp.cutoffFrequency = 3400f;
            // Slightly resonant rather than flat: a real horn has a peak up there, and that peak is
            // the nasal honk of every station announcement ever made.
            // **Q 1.6 -> 0.7, AND THIS IS THE WHOLE OF THE SHARPNESS** (2026-09-02).
            //
            // A resonant low-pass PEAKS at its own cutoff, and the cutoff sat at 3.6 kHz - which is
            // very close to where the ear is most sensitive. So the filter that was supposed to be
            // taking the top off was putting a bump exactly where "harsh" lives. Measured through the
            // whole chain, the 3-5 kHz band went from 8.1% of the clip's energy to 10.0%: the
            // treatment was making the voice brighter than the recording.
            //
            // At 0.7 it is flat into the corner and that band drops to 6.2%. It reads as the same
            // announcer through the same speaker, without the edge.
            //
            // The resonance was there to give the horn a formant - a cheap speaker does have one -
            // and that idea is not wrong, it was just an octave and a half too high and far too
            // strong. If it is ever wanted back, it belongs low and gentle, not on the cutoff.
            lp.lowpassResonanceQ = 0.7f;

            // Just enough drive to suggest an overdriven line. Past ~0.3 the words stop being
            // intelligible, and the announcer has actual information in them (the iteration number).
            // 0.17 -> 0.10. Measured, it barely moves the spectrum (3-5 kHz 10.0% -> 9.5%), so it is
            // NOT what was making this sharp - but it is grit on a voice that no longer has a
            // resonance to hide behind, and halving it costs nothing. Some is wanted: a horn that
            // does not break up at all is a hi-fi speaker.
            AudioDistortionFilter dist = go.AddComponent<AudioDistortionFilter>();
            dist.distortionLevel = 0.10f;

            // The slap off the far wall. 105ms is roughly the round trip across a room this size,
            // so it reads as this room rather than as an effect.
            AudioEchoFilter echo = go.AddComponent<AudioEchoFilter>();
            echo.delay = 105f;
            echo.decayRatio = 0.22f;
            echo.dryMix = 1f;
            // 0.33 -> 0.24 -> 0.15 -> 0.21 (2026-09-02, four passes by ear in one afternoon: too
            // much, less, too little, and this). The overshoot is the useful part of that record -
            // 0.15 was past the point where the horn stops being in a room at all. The slap DELAY is not touched - that is the round trip across a room this
            // size, and the room is the same room. What changes is how much of it comes back.
            echo.wetMix = 0.26f;

            // And the tail. Preset is set to User first: assigning any individual property switches
            // it there anyway, and setting it explicitly keeps the intent readable.
            AudioReverbFilter verb = go.AddComponent<AudioReverbFilter>();
            verb.reverbPreset = AudioReverbPreset.User;
            verb.dryLevel = 0f;
            verb.room = -350f;
            // Dark tail. Hard painted panels absorb almost nothing low and quite a lot high, and a
            // bright tail would fight the band-limiting the horn just did.
            verb.roomHF = -900f;
            // **2.1 -> 1.3 -> 1.8, AND THIS IS MOST OF WHAT "IT RINGS" MEANT.** The echo is one slap at
            // 105ms and easy to blame; the ringing is this, a two-second tail under every syllable.
            // At 1.3 the room is still a hall rather than a cupboard, and the words stop smearing
            // into each other - which matters more here than anywhere, because the report's three
            // phrases are already separate clips and a long tail bridges the gaps into mush.
            verb.decayTime = 2.3f;
            verb.decayHFRatio = 0.55f;
            verb.reflectionsLevel = -650f;
            // 250 -> 140 -> 210, with the decay above. Both halves of the tail, not just length.
            verb.reverbLevel = 250f;
            verb.diffusion = 100f;
            verb.density = 100f;

            return (echo, verb);
        }

        // **THE GAME'S OWN EFFECTS SIT UNDER THE PA** (2026-09-02, by request: the announcer is
        // quieter than everything else in the building).
        //
        // Measured, and the guess written into `Tools/generate_narration.py` was WRONG: the tannoy
        // chain is not where the voice loses level. HP240 + LP3400 costs 0.1 dB - speech energy is
        // already inside that band - and `echo.dryMix` and `verb.dryLevel` are both unity gain. The
        // gap is in the FILES. The voice set sits at -17.2 dBFS RMS and the effects at -14.8, so the
        // effects are 2.4 dB louder before a single source volume is applied.
        //
        // And there is nothing left to turn up. `voiceSource` is already at 1.0, `AudioSource.volume`
        // clamps there, and the voice files already peak at full scale with the limiter working - so
        // the effects come down instead. **-4 dB.** The player makes that back on the system volume;
        // what cannot be fixed anywhere else is the RATIO, which is why `AudioListener.volume` (the
        // settings slider) is no use here - it moves both sides together.
        //
        // Applied HERE because this is the only method in the project that makes an `AudioSource`:
        // 53 call sites, one number, and a new fixture is quiet by default rather than by memory.
        //
        // **THE PA IS NOT AN EFFECT.** The two sources carrying the announcer pass `pa: true` and are
        // left at what they ask for.
        private const float SfxLevel = 0.63f;

        // spatialBlend 0 is 2D (heard the same everywhere), 1 is fully positional.
        private static AudioSource MakeSource(Transform parent, string name, float spatialBlend, float volume,
                                              bool loop = false, bool pa = false)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = spatialBlend;
            source.volume = pa ? volume : volume * SfxLevel;
            // Linear rather than the logarithmic default: the room is only ~10m across, and the
            // default curve is still near full volume across the whole of it.
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 14f;
            return source;
        }

        // A DULL WOOD CHOP, generated rather than sourced - which is how every other clip in this
        // project was made (see docs/audio.md).
        //
        // The chop used to borrow `sfx_item_pickup`, a bright little tick that read as picking
        // something up because that is what it is. An axe into a standing trunk is the opposite:
        // almost no high end, a hard transient, and a short woody thump that dies fast because a
        // living tree does not ring.
        //
        // Three layers, and each is doing one job:
        //  - a low body around 90Hz that drops a fifth as it decays. That downward slide is most of
        //    what makes a thump sound like it hit WOOD rather than a drum head.
        //  - a brief burst of filtered noise for the bite of the blade, gone in 40ms.
        //  - a faint mid ring, low enough in level to be felt rather than heard, so the tail is not
        //    perfectly dead.
        private static AudioClip MakeChopClip(string assetName)
        {
            string path = $"{SfxDir}/{assetName}.wav";
            AudioClip existing = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (existing != null) return existing;

            const int rate = 44100;
            const float seconds = 0.42f;
            int count = (int)(rate * seconds);
            float[] data = new float[count];

            var rng = new System.Random(20260816);
            float noiseLow = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;

                // BODY. The pitch falls as it decays, which is the wood.
                float bodyEnv = Mathf.Exp(-t * 19f);
                float freq = Mathf.Lerp(96f, 62f, Mathf.Clamp01(t * 14f));
                float body = Mathf.Sin(2f * Mathf.PI * freq * t) * bodyEnv * 0.85f;

                // BITE. White noise through a one-pole low-pass, so it is a thock rather than a hiss.
                float raw = (float)(rng.NextDouble() * 2.0 - 1.0);
                noiseLow += (raw - noiseLow) * 0.22f;
                float bite = noiseLow * Mathf.Exp(-t * 78f) * 0.55f;

                // RING, well down and gone quickly.
                float ring = Mathf.Sin(2f * Mathf.PI * 310f * t) * Mathf.Exp(-t * 34f) * 0.12f;

                // A 2ms fade in so the very first sample is not a click of its own.
                float attack = Mathf.Clamp01(t / 0.002f);
                data[i] = Mathf.Clamp(body + bite + ring, -1f, 1f) * attack;
            }

            WriteWav(path, data, rate);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        // A STEP THROUGH WAIST-DEEP WATER, generated like every other clip in this project.
        //
        // IT IS NOT A SPLASH AND IT IS NOT A FOOTSTEP, which is the whole reason it exists rather than
        // reusing either. `sfx_water_splash_*` is water hitting something from above - a hard front and
        // a bright scatter - and a footstep is a transient off a hard floor. Neither is what a leg
        // pushed through a body of water sounds like:
        //
        //  - **There is no transient.** Water is heavy and slow, so the sound SWELLS over ~50ms rather
        //    than starting. A sharp attack is the single thing that makes a water sound read as a
        //    sample of a water sound.
        //  - **Most of it is low.** The body is the mass being shoved sideways, a couple of hundred
        //    hertz and below, and it is what makes the water sound DEEP rather than shallow.
        //  - **The top is bubbles, and it comes AFTER.** Air dragged under surfaces behind the push
        //    and breaks up, so the fizz is delayed and decays on its own, faster, clock.
        //
        // Three of them, seeded differently, for the reason the three footsteps exist: consecutive
        // identical steps read as a sample player rather than as a person walking.
        private static AudioClip MakeWadeClip(string assetName, int seed, float seconds = 0.62f,
                                              float decay = 5.2f, float fizzLevel = 0.42f,
                                              float lowWeight = 2.6f)
        {
            string path = $"{SfxDir}/{assetName}.wav";
            AudioClip existing = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (existing != null) return existing;

            const int rate = 44100;
            int count = (int)(rate * seconds);
            float[] data = new float[count];

            var rng = new System.Random(seed);
            float low = 0f, mid = 0f, high = 0f;
            // Where the bubbles start, which is after the push - and not the same place twice.
            float fizzDelay = 0.045f + (float)rng.NextDouble() * 0.05f;
            float peak = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                float raw = (float)(rng.NextDouble() * 2.0 - 1.0);

                // Three one-pole filters off one noise source, which is how one sound ends up with a
                // bottom, a middle and a top that move together rather than three layers that do not.
                low += (raw - low) * 0.035f;
                mid += (raw - mid) * 0.20f;
                high += (raw - high) * 0.65f;

                // THE PUSH. Soft in, long out - and the tail is longer than the body because water
                // keeps moving after the leg has stopped.
                float swell = (1f - Mathf.Exp(-t * 26f)) * Mathf.Exp(-t * decay);
                float body = (low * lowWeight + mid * 0.9f) * swell;

                // THE BUBBLES, delayed and on their own decay, and gated so they only exist while
                // there is air still coming up.
                float ft = t - fizzDelay;
                float fizz = 0f;
                if (ft > 0f)
                {
                    // Modulated, because bubbles are individual events rather than a hiss - two slow
                    // waves are enough to break it into a texture.
                    float grain = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 23f * ft)
                                              * Mathf.Sin(2f * Mathf.PI * 7.3f * ft + seed);
                    fizz = (raw - high) * Mathf.Exp(-ft * 11f) * fizzLevel * grain;
                }

                // A 3ms fade in, so sample zero is not a click of its own.
                data[i] = (body + fizz) * Mathf.Clamp01(t / 0.003f);
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }

            // NORMALISED rather than hand-balanced. The three layers are noise, so their sum's level
            // depends on the seed - without this the three clips come out at three volumes and the
            // walk has a limp.
            float gain = peak > 0.0001f ? 0.86f / peak : 1f;
            for (int i = 0; i < count; i++) data[i] = Mathf.Clamp(data[i] * gain, -1f, 1f);

            WriteWav(path, data, rate);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        // A HANDWHEEL BEING WOUND ROUND. Generated like every other clip here.
        //
        // Three things, and the second is the one that makes it a valve rather than a machine:
        //  - a low GRIND, filtered noise with a slow tremolo on it, which is the thread turning under
        //    load. It runs the whole length of the turn and is most of the volume;
        //  - a SQUEAK that RISES then FALLS, two detuned partials with vibrato. Metal complaining
        //    changes pitch as the load on it changes, and a steady tone reads as an electric motor;
        //  - CLICKS, one per twelfth of a turn, so the wheel has teeth and you can hear how far round
        //    it has gone with your eyes elsewhere. That last part is the load-bearing one: this room
        //    is solved by past selves turning wheels behind you.
        //
        // Its length is the valve's turn duration - `Valve.turnDuration` - because a sound that ends
        // before the animation does says the wheel stopped when it did not.
        private static AudioClip MakeValveTurnClip(string assetName)
        {
            string path = $"{SfxDir}/{assetName}.wav";
            AudioClip existing = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (existing != null) return existing;

            const int rate = 44100;
            const float seconds = 1.4f;
            int count = (int)(rate * seconds);
            float[] data = new float[count];

            var rng = new System.Random(20260824);
            float low = 0f, mid = 0f;
            float peak = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                float u = t / seconds;
                // In and out over the first and last tenth, so neither end is a cut.
                float env = Mathf.Min(1f, u / 0.1f) * Mathf.Min(1f, (1f - u) / 0.12f);

                float raw = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (raw - low) * 0.06f;
                mid += (raw - mid) * 0.30f;
                // The tremolo is what makes the grind mechanical rather than a hiss.
                float grind = (low * 2.2f + mid * 0.5f) * (0.65f + 0.35f * Mathf.Sin(2f * Mathf.PI * 11f * t));

                // The squeak: up over the first half, down over the second, with vibrato on top.
                float bend = Mathf.Sin(Mathf.PI * u);
                float freq = Mathf.Lerp(430f, 690f, bend) + Mathf.Sin(2f * Mathf.PI * 6.5f * t) * 18f;
                float squeal = (Mathf.Sin(2f * Mathf.PI * freq * t)
                              + Mathf.Sin(2f * Mathf.PI * freq * 1.51f * t) * 0.45f) * 0.13f * bend;

                // Teeth. A short decaying tick every twelfth of a turn.
                const int teeth = 12;
                float toothPhase = (t * teeth / seconds) % 1f;
                float click = Mathf.Exp(-toothPhase * 60f) * 0.22f * (float)(rng.NextDouble() * 0.6 + 0.7);

                data[i] = (grind + squeal + click) * env;
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }

            float gain = peak > 0.0001f ? 0.82f / peak : 1f;
            for (int i = 0; i < count; i++) data[i] = Mathf.Clamp(data[i] * gain, -1f, 1f);

            WriteWav(path, data, rate);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        // THE VALVE REACHING ITS STOP. A short, hard, metallic clank - this is the sound that says
        // "this one is finished", and it is the only feedback a player gets that a wheel is done other
        // than E going quiet on it.
        private static AudioClip MakeValveStopClip(string assetName)
        {
            string path = $"{SfxDir}/{assetName}.wav";
            AudioClip existing = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (existing != null) return existing;

            const int rate = 44100;
            const float seconds = 0.5f;
            int count = (int)(rate * seconds);
            float[] data = new float[count];

            var rng = new System.Random(20260825);
            float noise = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;

                // The impact: a fast noise burst, low-passed just enough not to be a hiss.
                float raw = (float)(rng.NextDouble() * 2.0 - 1.0);
                noise += (raw - noise) * 0.55f;
                float hit = noise * Mathf.Exp(-t * 95f) * 0.7f;

                // And the ring, three INHARMONIC partials - a struck metal object has no harmonic
                // series, and giving it one is what makes a clank read as a musical note.
                float ring = (Mathf.Sin(2f * Mathf.PI * 320f * t) * 0.5f
                            + Mathf.Sin(2f * Mathf.PI * 741f * t) * 0.32f
                            + Mathf.Sin(2f * Mathf.PI * 1483f * t) * 0.16f) * Mathf.Exp(-t * 13f) * 0.5f;

                data[i] = Mathf.Clamp(hit + ring, -1f, 1f) * Mathf.Clamp01(t / 0.001f);
            }

            WriteWav(path, data, rate);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        // 16-bit mono PCM. Unity can author an AudioClip in memory but cannot SAVE one, and a clip
        // that exists only in memory does not survive the scene being written - the same trap the
        // reflection probes fell into.
        private static void WriteWav(string path, float[] samples, int rate)
        {
            using (var stream = new System.IO.FileStream(path, System.IO.FileMode.Create))
            using (var w = new System.IO.BinaryWriter(stream))
            {
                int dataBytes = samples.Length * 2;
                w.Write(new char[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + dataBytes);
                w.Write(new char[] { 'W', 'A', 'V', 'E' });
                w.Write(new char[] { 'f', 'm', 't', ' ' });
                w.Write(16);                      // PCM header size
                w.Write((short)1);                // PCM
                w.Write((short)1);                // mono
                w.Write(rate);
                w.Write(rate * 2);                // byte rate
                w.Write((short)2);                // block align
                w.Write((short)16);               // bits
                w.Write(new char[] { 'd', 'a', 't', 'a' });
                w.Write(dataBytes);
                for (int i = 0; i < samples.Length; i++)
                    w.Write((short)(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue));
            }
        }

        // SHRINKS THE TEXTURES OF AN IMPORTED MODEL, in place, by rewriting them as real assets.
        //
        // WHY THIS EXISTS. `realistic_tree.glb` ships four 4096x4096 textures and glTFast imports
        // them UNCOMPRESSED: 628MB of VRAM for one tree, measured. (`tree_roots.glb` added 17MB more
        // while the hall had a separate stump model; it does not any longer - the tree keeps its own.)
        // That is the stutter play reported, and it is not the polygon count it looks like - the
        // roots are 1.3M triangles, which is a lot, but a modern GPU eats triangles and chokes on
        // two thirds of a gigabyte of texture.
        //
        // They cannot simply be re-imported with better settings: they are SUB-ASSETS of a custom
        // importer (`GltfImporter`), so there is no TextureImporter to configure. So each one is
        // blitted into a render target at the size we actually want, read back, written out as a
        // real PNG asset with proper settings, and the material is pointed at the copy. A blit is
        // used rather than `GetPixels` because a sub-asset texture is not readable.
        //
        // Runs once per build and is idempotent: the PNG is only regenerated if it is missing.
        private static void ShrinkModelTextures(string modelPath, int maxSize)
        {
            var seen = new System.Collections.Generic.Dictionary<Texture, Texture2D>();
            long before = 0, after = 0;
            int rewritten = 0;

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                Material mat = asset as Material;
                if (mat == null) continue;

                foreach (string prop in mat.GetTexturePropertyNames())
                {
                    Texture src = mat.GetTexture(prop);
                    Texture2D srcTex = src as Texture2D;
                    if (srcTex == null) continue;
                    if (srcTex.width <= maxSize && srcTex.height <= maxSize) continue;

                    if (!seen.TryGetValue(srcTex, out Texture2D copy))
                    {
                        before += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(srcTex);
                        copy = ShrunkCopy(srcTex, maxSize,
                            System.IO.Path.GetFileNameWithoutExtension(modelPath) + "_" + srcTex.name);
                        seen[srcTex] = copy;
                        if (copy != null)
                        {
                            after += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(copy);
                            rewritten++;
                        }
                    }
                    if (copy != null) mat.SetTexture(prop, copy);
                }
            }

            if (rewritten > 0)
                Debug.Log($"[SceneBuilder] {System.IO.Path.GetFileName(modelPath)}: {rewritten} texture(s) "
                        + $"shrunk to {maxSize}px - {before / 1048576}MB -> {after / 1048576}MB");
        }

        // One texture, downscaled and saved as a compressed asset. The blit is what makes this work
        // on a texture that is not readable, which every model sub-asset is.
        private static Texture2D ShrunkCopy(Texture2D src, int maxSize, string assetName)
        {
            string path = $"{TexturesDir}/Shrunk/{assetName}.png";
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogWarning("[SceneBuilder] No graphics device: textures left at full size.");
                return null;
            }

            float aspect = src.height / (float)src.width;
            int w = Mathf.Min(maxSize, src.width);
            int h = Mathf.Max(1, Mathf.RoundToInt(w * aspect));

            // sRGB matters: an albedo blitted through a linear target comes back washed out.
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32,
                                                          RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;

            Texture2D flat = new Texture2D(w, h, TextureFormat.RGBA32, false);
            flat.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
            flat.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            Directory.CreateDirectory($"{TexturesDir}/Shrunk");
            File.WriteAllBytes(path, flat.EncodeToPNG());
            Object.DestroyImmediate(flat);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                imp.textureType = TextureImporterType.Default;
                imp.mipmapEnabled = true;
                imp.maxTextureSize = maxSize;
                // COMPRESSED, unlike the originals. This is where the 628MB actually goes.
                imp.textureCompression = TextureImporterCompression.Compressed;
                imp.alphaIsTransparency = true;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Extension-agnostic, so a sourced .ogg or .mp3 drops in as readily as a .wav.
        // ONE LANGUAGE'S WORTH OF PA LINES. Both folders use identical filenames, so which language
        // this is comes entirely from `dir` - that is the whole reason the Korean set went into a
        // subfolder rather than getting a suffix on every name.
        private static NarrationDirector.VoiceSet LoadVoiceSet(string dir)
        {
            var set = new NarrationDirector.VoiceSet();

            set.iterationLines = new AudioClip[NarrationIterationLines];
            for (int i = 0; i < set.iterationLines.Length; i++)
                set.iterationLines[i] = LoadClip(dir, $"voice_iteration_{i + 1:00}");

            set.iterationGenericLine = LoadClip(dir, "voice_iteration_generic");
            set.tenSecondsLine = LoadClip(dir, "voice_ten_seconds");

            // THE REPORT'S PHRASES. **Indexed BY VALUE with element 0 unused**, so `cycleLines[3]`
            // is cycle three - no arithmetic between the number the game has and the clip it needs.
            //
            // It was word clips and a number-assembling code path until 2026-09-02; see
            // `NarrationDirector.AnnounceCycleResult` for why six words could not be made to sound
            // like a sentence, and `Tools/generate_narration.py` for what replaced them.
            set.cycleLines = new AudioClip[NarrationReportCycles + 1];
            for (int i = 1; i < set.cycleLines.Length; i++)
                set.cycleLines[i] = LoadClip(dir, $"voice_report_cycle_{i:00}");

            set.iterationCountLines = new AudioClip[NarrationReportMax + 1];
            set.minuteLines = new AudioClip[NarrationReportMax + 1];
            for (int i = 1; i <= NarrationReportMax; i++)
            {
                set.iterationCountLines[i] = LoadClip(dir, $"voice_report_iterations_{i:00}");
                set.minuteLines[i] = LoadClip(dir, $"voice_report_minutes_{i:00}");
            }

            // Sixty of them, indexed FROM ZERO - "0 seconds." is a real clip, said on an exact
            // minute. See `NarrationDirector.PickFromZero`.
            set.secondLines = new AudioClip[60];
            for (int i = 0; i < 60; i++)
            {
                set.secondLines[i] = LoadClip(dir, $"voice_report_seconds_{i:00}");
            }

            set.totalLine = LoadClip(dir, "voice_report_total");

            set.transportCalledLine = LoadClip(dir, "voice_transport_called");

            // **AND WHAT IT SAYS ON THE WAY OUT.** Five clips have sat on disk in both languages
            // since the ride lines were written and NOTHING LOADED THEM, so `rideLines` was null and
            // `AnnounceRideLine` returned at its own null guard every time - a minute of silence with
            // no error anywhere to say why. A `VoiceSet` field nothing here assigns is a line the game
            // cannot speak: every one of them is loaded in this method, and only here.
            set.rideLines = new AudioClip[NarrationRideLines];
            for (int i = 0; i < set.rideLines.Length; i++)
                set.rideLines[i] = LoadClip(dir, $"voice_ride_{i}");

            // Element 0 is "Nine.", counting down to "One." at the end.
            set.countdownLines = new AudioClip[9];
            for (int i = 0; i < set.countdownLines.Length; i++)
                set.countdownLines[i] = LoadClip(dir, $"voice_count_{9 - i}");

            set.newCycleLine = LoadClip(dir, "voice_new_cycle");
            set.cycleTerminatedLine = LoadClip(dir, "voice_cycle_terminated");
            set.cycleBrokenLine = LoadClip(dir, "voice_cycle_broken");
            set.allCyclesBrokenLine = LoadClip(dir, "voice_all_cycles_broken");
            set.manualTerminationLine = LoadClip(dir, "voice_manual_termination");
            return set;
        }

        private static AudioClip LoadClip(string dir, string baseName)
        {
            foreach (string ext in new[] { ".wav", ".ogg", ".mp3", ".aif", ".aiff" })
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{dir}/{baseName}{ext}");
                if (clip != null) return clip;
            }
            return null;
        }

        // The HUD face. Monospace on purpose, and for two reasons at once.
        //
        // Diegetically, everything on screen belongs to the facility rather than to the player -
        // the iteration number, the clock, the control that ends a cycle - and a terminal face says
        // that where a proportional one reads as a game's own UI. The room is built out of wall
        // *displays* for the same reason.
        //
        // Practically, the countdown is a number that changes every second: in a proportional font
        // its digits are different widths, so it reflows and twitches on the spot every tick. In a
        // monospace one it does not move at all.
        //
        // NOTE ON LICENSING: Consolas is Microsoft's, copied out of C:/Windows/Fonts. Fine for a
        // prototype that never leaves this machine, NOT fine to ship. Swapping it is one file and
        // this path - JetBrains Mono or IBM Plex Mono are both SIL OFL and drop straight in.
        // THE INK ON A BRIGHT SURFACE. Near-black rather than the red everything on this screen used
        // to be: red is this game's ALARM - the ERROR test card, the last ten seconds, the collapse -
        // and spending it on five button labels leaves nothing to say anything with. It is the same
        // charcoal the grooves in every wall are painted, so the type reads as printed ON the room.
        // **NEAR-WHITE SINCE 2026-09-04, AND IT WAS CHARCOAL FOR A REASON THAT NO LONGER HOLDS.**
        // Every page that uses this - the title screen, settings, credits, the key bindings - sits
        // over `MenuBackground.png`, and that photograph used to be a bright white room. It is a
        // black room with one lit doorway now (`CaptureMenuBackground`, to `docs/mainmenu_dark.png`),
        // so charcoal type on it is charcoal on black. One constant flips the whole family, which is
        // the reason it was ever a constant.
        //
        // Not pure white: 0.90 sits a step below the doorway in the picture, so the brightest thing
        // on the title screen stays the room rather than the interface.
        private static readonly Color MenuInk = new Color(0.90f, 0.90f, 0.92f, 1f);
        // The one accent, and there is exactly one on the screen at rest.
        private static readonly Color MenuAccent = new Color(0.80f, 0.10f, 0.10f, 1f);
    }
}
