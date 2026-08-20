using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IterationRoom
{
    // The title screen. Its whole job is to hold the player outside the loop until they choose to
    // enter it, and to cover the load while the room comes in.
    //
    // The background is a still of the room from the foot of the bed - the exact frame the game
    // opens on - captured by SceneBuilder at build time. It is a separate lightweight scene rather
    // than a state of the room, which is what makes the loading bar real: IterationRoom is a
    // 306,000-triangle bed, 182 wall panels, seventy balloons and a pile of shaders, and it takes
    // a genuine moment to come in.
    public class MainMenu : MonoBehaviour
    {
        public string gameScene = "IterationRoom";

        public CanvasGroup menuGroup;
        public CanvasGroup loadingGroup;
        public Button playButton;
        public Button quitButton;

        // ~~testBoundaryButton~~ REMOVED 2026-08-15: the title screen no longer carries a TEST entry.
        // `DebugStart.AtCycleBoundary` and the jump it drives are still in place for a developer who
        // sets the flag by hand; what is gone is the button and the handler that set it.

        // CONTINUE: back to the bed the run last woke in, with no page in between. It used to open
        // the cycle picker and PLAY used to be the only way to start - the two swapped on 2026-08-20,
        // by request, because that had them the wrong way round: the thing a returning player wants
        // is the run they were in, and the thing they rarely want is a list.
        //
        // Hidden outright when `GameSettings.SavedCycle` is 0, which is the honest state of a game
        // nobody has played - an offer to continue nothing is worse than no offer.
        public Button continueButton;

        // THE CYCLE PICKER, on a button of its own. It went through three shapes in one day and
        // this is the one that survived: CONTINUE resumes, PLAY starts from the beginning, and
        // choosing a cycle is a THIRD thing that gets its own entry rather than being hidden behind
        // one of the first two.
        //
        // **PLAY MUST NOT OPEN A MENU.** It is the most universally understood button in games and
        // the promise is that pressing it plays; a list where a first-time player expected a game is
        // a stumble at the one screen that cannot afford one. It is also a list in which only one
        // entry is right for them - and one of the wrong ones, CYCLE 2, is both a spoiler and a trap,
        // dropping somebody who has learned nothing into the cycle that assumes they have.
        //
        // EVERY CYCLE IS UNLOCKED, 2026-08-20, deliberately and temporarily. Gating it to
        // `GameSettings.SavedCycle` is one condition and is what a shipped build wants; it is off
        // while cycle 3 is being built, because the shortcut into a cycle under construction is the
        // whole value of this page during development. See TODO.md.
        public Button cycleSelectButton;
        public Button cycleBackButton;

        // ONE PER CYCLE THAT HAS A SUCCESSOR: "start cycle N with its last room already finished", so
        // the hatch into cycle N+1 opens within seconds of the run starting. **A development
        // shortcut**, and it goes with the picker rather than on the title screen because that whole
        // page is one - see `cycleSelectButton`, which is unlocked for the same reason and gated the
        // same day.
        public Button[] cycleEndButtons;

        // THE LAST RUN'S BILL, on a page of its own. The ending card shows it once and then the
        // player clicks past it; this is where it lives afterwards, so a run that took thirty-one
        // iterations is a thing they can go back and look at rather than a number they had to
        // memorise off a screen that was about to close.
        //
        // Hidden outright when nothing has been finished - `RunReport.Load` returns null for both
        // "never played" and "stored but unreadable", and an empty table is worse than no entry.
        public Button recordButton;
        public Button recordBackButton;
        public CanvasGroup recordGroup;
        public Text recordText;
        public CanvasGroup cycleGroup;
        public Button[] cycleButtons;

        // SETTINGS, on a page of its own for the same reason the cycle picker is: the title screen
        // stays a short column of choices, and anything that needs a slider needs room the column
        // does not have.
        public Button settingsButton;
        public Button settingsBackButton;
        public CanvasGroup settingsGroup;
        public Slider volumeSlider;
        public Text volumeValue;

        // SENSITIVITY, ON THE TITLE SCREEN AS WELL AS IN THE PAUSE MENU AND THE CALIBRATION ROOM.
        // Three places for one number is not duplication - they answer three different situations. The
        // calibration room is for a player who has never played and does not know what to ask for; the
        // pause menu is for one mid-run who has just found out; this is for one who already knows their
        // number and wants it set before anything starts. All three write `GameSettings.MouseSensitivity`,
        // which is the single value, so none of them can disagree with another.
        public Slider sensitivitySlider;
        public Text sensitivityValue;

        // The controls list. Owns which key each verb is on only in the sense of asking
        // `InputBindings`; see KeyBindingPanel.
        public KeyBindingPanel bindings;

        // LANGUAGE. Two buttons rather than a slider or a cycling toggle: with two options a toggle
        // costs the same space and tells you only what you would get NEXT, where two buttons show
        // both choices and which one is live. `SceneBuilder` leaves their labels untranslated on
        // purpose - see the note where it builds them.
        public Button englishButton;
        public Button koreanButton;
        public Text englishInk;
        public Text koreanInk;
        public Image loadingFill;
        public Text loadingLabel;

        // A floor on how long the bar is up. It does NOT inflate the reported progress - see
        // LoadGame, which shows the *lesser* of real progress and elapsed fraction - it only stops
        // a fast machine from flashing the whole load past in two frames, which reads as a glitch
        // rather than as a load.
        public float minimumLoadingTime = 0.9f;
        public float fadeSpeed = 3f;

        // THE ROOM'S OWN TONE, UNDER THE TITLE SCREEN. The same `sfx_ominous_loop` that runs beneath
        // every second of every iteration, and deliberately not a piece of menu music: this game has
        // no music anywhere, and giving the title screen some would make PLAY the moment a track
        // stops rather than the moment a door opens.
        //
        // The screen is a photograph of that room. It should sound like that room - so what the
        // player crosses when they press PLAY is not silence into sound, it is a place they were
        // already standing in.
        public AudioSource ambience;

        // WELL UNDER THE 0.5 THE ROOM ITSELF RUNS AT. A title screen is a place somebody sits with
        // the window open while they do something else, and a hum that has to be turned down is a hum
        // that gets turned off. Present, not announced.
        public float ambienceVolume = 0.22f;

        // Faded rather than cut at BOTH ends, for the reason `RoomAmbience.FadeOutTone` gives: a cut
        // reads as a sound failing, a fade reads as a room being switched on or off around you.
        public float ambienceFade = 1.6f;

        private bool starting;

        private void Awake()
        {
            // Wired here rather than as persistent listeners on the buttons: a listener added from
            // an editor script has to be serialized through UnityEventTools, and this is one line.
            if (playButton != null) playButton.onClick.AddListener(Play);
            if (quitButton != null) quitButton.onClick.AddListener(Quit);

            if (continueButton != null) continueButton.onClick.AddListener(Continue);
            if (cycleSelectButton != null)
                cycleSelectButton.onClick.AddListener(() => ShowCyclePicker(true));
            if (cycleBackButton != null) cycleBackButton.onClick.AddListener(() => ShowCyclePicker(false));

            if (recordButton != null) recordButton.onClick.AddListener(() => ShowRecord(true));
            if (recordBackButton != null) recordBackButton.onClick.AddListener(() => ShowRecord(false));

            if (settingsButton != null) settingsButton.onClick.AddListener(() => ShowSettings(true));
            if (settingsBackButton != null) settingsBackButton.onClick.AddListener(() =>
            {
                // Committed on the way OUT, not on every frame of a drag - the same rule the pause
                // menu's sensitivity slider follows, and for the same reason (PlayerPrefs.Save is a
                // storage flush on WebGL).
                GameSettings.Save();
                ShowSettings(false);
            });

            // The stored volume reaches the engine here rather than at the first slider drag, so a
            // player who never opens this page still gets the level they chose last time.
            GameSettings.ApplyAudio();

            if (volumeSlider != null)
            {
                volumeSlider.minValue = 0f;
                volumeSlider.maxValue = 1f;
                // Seeded BEFORE the listener is attached: a Slider raises onValueChanged when its
                // value is assigned, and a seed that reported itself as a change would write the
                // default over whatever was loaded.
                volumeSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
                volumeSlider.onValueChanged.AddListener(SetVolume);
            }
            ShowVolumeValue();

            if (sensitivitySlider != null)
            {
                sensitivitySlider.minValue = GameSettings.MinMouseSensitivity;
                sensitivitySlider.maxValue = GameSettings.MaxMouseSensitivity;
                // Seeded before the listener for the same reason the volume slider is, one block up.
                sensitivitySlider.SetValueWithoutNotify(GameSettings.MouseSensitivity);
                sensitivitySlider.onValueChanged.AddListener(SetSensitivity);
            }
            ShowSensitivityValue();

            if (englishButton != null)
                englishButton.onClick.AddListener(() => SetLanguage(GameLanguage.English));
            if (koreanButton != null)
                koreanButton.onClick.AddListener(() => SetLanguage(GameLanguage.Korean));
            ShowLanguage();

            if (cycleButtons != null)
                for (int i = 0; i < cycleButtons.Length; i++)
                {
                    // Captured per iteration, or every button would close over the loop variable and
                    // start the last cycle.
                    int cycle = i + 1;
                    if (cycleButtons[i] != null)
                        cycleButtons[i].onClick.AddListener(() => PlayFromCycle(cycle));
                }

            if (cycleEndButtons == null) return;
            for (int i = 0; i < cycleEndButtons.Length; i++)
            {
                int cycle = i + 1;
                if (cycleEndButtons[i] != null)
                    cycleEndButtons[i].onClick.AddListener(() => PlayFromCycleEnd(cycle));
            }
        }

        private void Start()
        {
            // The game locks and hides the cursor and Unity does not put it back across a scene
            // load, so returning to this scene without this leaves a menu you cannot click.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // NOTHING TO CONTINUE IS A REASON NOT TO OFFER IT. A first-time player sees PLAY,
            // SETTINGS and QUIT; the button appears the moment a run has woken in a bed.
            if (continueButton != null)
                continueButton.gameObject.SetActive(GameSettings.SavedCycle > 0);
            // Same rule, different fact: CONTINUE needs a run STARTED, RECORD needs one FINISHED.
            if (recordButton != null)
                recordButton.gameObject.SetActive(RunReport.HasRun);

            if (menuGroup != null) menuGroup.alpha = 1f;
            ShowRecord(false);
            ShowCyclePicker(false);
            // Both sub-pages down, and the menu back up after them: each of these restores
            // `menuGroup`, so whichever runs last is what the player sees.
            ShowSettings(false);
            if (menuGroup != null) { menuGroup.alpha = 1f; menuGroup.blocksRaycasts = true; }
            if (loadingGroup != null)
            {
                loadingGroup.alpha = 0f;
                loadingGroup.blocksRaycasts = false;
            }

            SetProgress(0f);

            // FROM SILENCE, over the same beat the menu itself arrives on. The scene loads with the
            // source already playing at zero, so this is a level ride rather than a Play() - which
            // is what stops the loop's first sample landing as a click.
            if (ambience != null)
            {
                ambience.volume = 0f;
                if (!ambience.isPlaying) ambience.Play();
                StartCoroutine(FadeAmbience(ambienceVolume, ambienceFade));
            }
        }

        // Shared by the arrival and the departure. Unscaled, because nothing here is allowed to
        // depend on a time scale the menu does not own.
        private IEnumerator FadeAmbience(float target, float seconds)
        {
            if (ambience == null) yield break;

            float from = ambience.volume;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                ambience.volume = Mathf.Lerp(from, target, seconds > 0f ? t / seconds : 1f);
                yield return null;
            }
            ambience.volume = target;
            if (target <= 0f) ambience.Stop();
        }

        private void Update()
        {
            if (starting) return;
            // RETURN DOES THE OBVIOUS THING, which is now CONTINUE where there is something to
            // continue and the picker where there is not. Bound to the meaning rather than to a
            // button: a player pressing Enter at a title screen is asking to play, not to be shown a
            // list of cycles they have already finished.
            // NOT WHILE ANOTHER PAGE HAS THE SCREEN. `menuGroup.blocksRaycasts` is the one flag every
            // page already flips, so this asks "is the title column the thing being looked at" rather
            // than naming the three pages that are not. Without it ENTER starts the game from the
            // settings screen - and, mid-rebind, assigns ENTER to a verb and starts the game with it.
            bool titleColumnUp = menuGroup == null || menuGroup.blocksRaycasts;
            if (!titleColumnUp) return;

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (GameSettings.SavedCycle > 0) Continue();
                else Play();
            }
        }

        // PLAY STARTS THE GAME. The whole of it, from the beginning: the sensitivity room and then
        // cycle 1, which is the one path a first-time player should ever be on.
        //
        // `DebugStart.Clear` leaves `StartCycle` at -1, and that is the whole of what runs the
        // calibration step - `LoopManager` enters it exactly when no cycle has been named (and never
        // on a touch device, where every input it needs is a mouse). So the sensitivity room needs no
        // entry of its own anywhere: this IS its entry, and the value it sets is adjustable
        // afterwards on SETTINGS.
        //
        // Cleared on the normal route as well as set on the others: these are statics, so a run
        // started from the picker followed by PLAY without leaving the editor would otherwise inherit
        // whichever cycle was chosen last.
        public void Play()
        {
            if (starting) return;
            DebugStart.Clear();
            starting = true;
            StartCoroutine(LoadGame());
        }

        // CONTINUE: straight into the cycle the run last reached, no page in between. What "the
        // point they left off" can mean here is a whole cycle rather than a moment inside one - see
        // GameSettings.SavedCycle, which is a paragraph about ghosts and not about save files.
        //
        // Falls back to cycle 1 rather than refusing, so a stored value from a build with more cycles
        // in it than this one has cannot strand the button. `PlayFromCycle` clamps nothing, but
        // `LoopManager` does (`Mathf.Clamp(DebugStart.StartCycle - 1, 0, cycles.Length - 1)`).
        public void Continue()
        {
            if (starting) return;
            PlayFromCycle(Mathf.Max(1, GameSettings.SavedCycle));
        }

        // `PlayFromBoundary` went with the TEST button it was the handler for. `LoopManager` still
        // honours `DebugStart.AtCycleBoundary`, so the jump is reachable by setting that static; there
        // is simply nothing on the title screen that sets it.

        // A CYCLE, FROM ITS OWN BED, WITHOUT THE SENSITIVITY STEP - including cycle 1, which is the
        // one thing that changed here on 2026-08-20. It used to fall through to the ordinary route
        // (`if (cycle > 1)`), so CYCLE 1 and PLAY were the same run; with the calibration room now an
        // entry of its own, that made two of the three buttons do the same thing.
        //
        // Naming the cycle is what skips the step, so this is one guard removed rather than a flag
        // added. `LoopManager` reads `StartCycle > 1` for which cycle to wake, so 1 selects cycle 1
        // by falling through exactly as -1 did.
        public void PlayFromCycle(int cycle)
        {
            if (starting) return;
            DebugStart.Clear();
            DebugStart.StartCycle = Mathf.Max(1, cycle);
            starting = true;
            StartCoroutine(LoadGame());
        }

        // A CYCLE WITH ITS LAST ROOM ALREADY FINISHED, so the boundary into the next one runs at
        // once. For testing the cycle BELOW: the break plays, the storey underneath wakes, the gas is
        // repointed and the hatch opens, all on the real path - see DebugStart.FinishOnJump for why
        // the hole is not simply forced open instead.
        public void PlayFromCycleEnd(int cycle)
        {
            if (starting) return;
            DebugStart.Clear();
            DebugStart.StartCycle = Mathf.Max(1, cycle);
            DebugStart.AtCycleBoundary = true;
            DebugStart.FinishOnJump = true;
            starting = true;
            StartCoroutine(LoadGame());
        }

        private void SetVolume(float value)
        {
            GameSettings.MasterVolume = value;
            ShowVolumeValue();
        }

        private void ShowVolumeValue()
        {
            // As a percentage rather than 0.00: this is a loudness, and nobody thinks about loudness
            // in hundredths. Sensitivity keeps its decimals because a mouse multiplier is a ratio.
            if (volumeValue != null)
                volumeValue.text = Mathf.RoundToInt(GameSettings.MasterVolume * 100f) + "%";
        }

        private void SetLanguage(GameLanguage value)
        {
            // The setter is what raises `Loc.Changed`, so every `LocalizedText` on this page has
            // already redrawn by the time this returns - including the BACK button under the pointer.
            GameSettings.Language = value;
            ShowLanguage();
        }

        // WHICH ONE IS LIVE, said with ink rather than with a plate. The buttons already carry a
        // hover and a press state; adding a third background would make "selected" and "hovered"
        // two shades of the same thing. Full-strength ink for the current language, faded for the
        // other, which reads at a glance and survives the pointer being anywhere.
        private void ShowLanguage()
        {
            bool korean = GameSettings.Language == GameLanguage.Korean;
            if (englishInk != null)
                englishInk.color = new Color(englishInk.color.r, englishInk.color.g,
                                             englishInk.color.b, korean ? 0.35f : 1f);
            if (koreanInk != null)
                koreanInk.color = new Color(koreanInk.color.r, koreanInk.color.g,
                                            koreanInk.color.b, korean ? 1f : 0.35f);
        }

        private void SetSensitivity(float value)
        {
            GameSettings.MouseSensitivity = value;
            ShowSensitivityValue();
        }

        private void ShowSensitivityValue()
        {
            if (sensitivityValue != null)
                sensitivityValue.text = GameSettings.MouseSensitivity.ToString("0.00");
        }

        private void ShowSettings(bool show)
        {
            if (settingsGroup != null)
            {
                settingsGroup.alpha = show ? 1f : 0f;
                settingsGroup.blocksRaycasts = show;
            }
            // RE-SEEDED ON EVERY OPEN, not once in Awake. The calibration room and the pause menu both
            // write this number behind the page's back, so a slider seeded at scene load shows a stale
            // value and dragging it snaps away from the real one - the exact bug PauseMenu.SyncSensitivity
            // documents, which this page would otherwise have its own copy of.
            if (show && sensitivitySlider != null)
                sensitivitySlider.SetValueWithoutNotify(GameSettings.MouseSensitivity);
            if (show) ShowSensitivityValue();
            if (!show && bindings != null) bindings.Cancel();
            if (menuGroup != null)
            {
                menuGroup.alpha = show ? 0f : 1f;
                menuGroup.blocksRaycasts = !show;
            }
        }

        // Filled on the way IN rather than at startup, so a run finished and returned from is on the
        // page without the menu having to be reloaded.
        private void ShowRecord(bool show)
        {
            if (show && recordText != null)
            {
                CycleRecord[] run = RunReport.Load();
                recordText.text = run != null ? RunReport.Table(run) : string.Empty;
            }

            if (recordGroup != null)
            {
                recordGroup.alpha = show ? 1f : 0f;
                recordGroup.blocksRaycasts = show;
            }
            if (menuGroup != null)
            {
                menuGroup.alpha = show ? 0f : 1f;
                menuGroup.blocksRaycasts = !show;
            }
        }

        private void ShowCyclePicker(bool show)
        {
            if (cycleGroup != null)
            {
                cycleGroup.alpha = show ? 1f : 0f;
                cycleGroup.blocksRaycasts = show;
            }
            if (menuGroup != null)
            {
                menuGroup.alpha = show ? 0f : 1f;
                menuGroup.blocksRaycasts = !show;
            }
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private IEnumerator LoadGame()
        {
            if (menuGroup != null) menuGroup.blocksRaycasts = false;

            // OUT ACROSS THE LOAD, and it has to go out rather than be left running: the room scene
            // starts its OWN copy of this same loop, so carrying the menu's into the switch would be
            // heard as the tone restarting from its first sample under a room that is already toning.
            // Gone by the time the bar fills, and the room brings it back.
            if (ambience != null) StartCoroutine(FadeAmbience(0f, ambienceFade));

            // The title and the buttons go, the background stays: the last thing on screen before
            // the room loads is the room, which is also the first thing after. What greets the
            // player on the other side is the calibration step, which lives in the room scene -
            // see SensitivityCalibration for why it is not here.
            yield return Fade(menuGroup, 0f);

            if (loadingGroup != null) loadingGroup.alpha = 1f;

            AsyncOperation op = SceneManager.LoadSceneAsync(gameScene);
            // Withheld so the bar owns the moment of arrival rather than the scene dropping in
            // under a half-drawn one.
            op.allowSceneActivation = false;

            float started = Time.unscaledTime;
            while (op.progress < 0.9f || Time.unscaledTime - started < minimumLoadingTime)
            {
                // Unity reports 0..0.9 while activation is withheld and never reaches 1, so it is
                // rescaled here; a bar that stops dead at 90% reads as a failed load.
                float loaded = Mathf.Clamp01(op.progress / 0.9f);
                float waited = minimumLoadingTime > 0f
                    ? Mathf.Clamp01((Time.unscaledTime - started) / minimumLoadingTime)
                    : 1f;

                // The LESSER of the two, so the bar never claims more than has actually happened.
                SetProgress(Mathf.Min(loaded, waited));
                yield return null;
            }

            SetProgress(1f);
            // One frame at 100% before the switch, or the bar's last state is never drawn.
            yield return null;

            op.allowSceneActivation = true;
        }

        private void SetProgress(float t)
        {
            if (loadingFill != null) loadingFill.fillAmount = t;
            // Formatted here rather than through a `LocalizedText`, because this is rewritten every
            // frame of the load - a component that also writes it would be a second author of the
            // same label, and the two would race.
            if (loadingLabel != null)
                loadingLabel.text = $"{Loc.Get("menu.loading")} {Mathf.RoundToInt(t * 100f)}%";
        }

        private IEnumerator Fade(CanvasGroup group, float target)
        {
            if (group == null) yield break;

            while (!Mathf.Approximately(group.alpha, target))
            {
                // Unscaled: nothing here touches Time.timeScale today, but a menu that stops
                // working because something else paused the game is a poor way to discover that.
                group.alpha = Mathf.MoveTowards(group.alpha, target, fadeSpeed * Time.unscaledDeltaTime);
                yield return null;
            }
        }
    }
}
