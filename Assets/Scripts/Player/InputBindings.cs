using System;
using UnityEngine;

namespace IterationRoom
{
    // WHICH KEY EACH VERB IS ON, and the only place that answer lives.
    //
    // This sits directly behind `GameInput`, which already named every verb this game has and is the
    // one place any of them is read. That is what makes rebinding a table rather than a sweep: the
    // eight files that poll E ask `GameInput.InteractPressed`, `GameInput` asks here, and nothing
    // else in the building has an opinion about which key E is.
    //
    // **PERSISTED IMMEDIATELY, unlike the sliders.** `GameSettings` deliberately batches sensitivity
    // and volume into one `Save()` because a uGUI drag writes on every frame and each write is a
    // storage flush on WebGL. A rebind is the opposite kind of event - one deliberate act, minutes
    // apart - and the whole value of it is surviving a player who closes the tab. That is the same
    // reasoning `GameSettings.SavedCycle` is written through under.
    public enum GameAction
    {
        MoveForward,
        MoveBack,
        MoveLeft,
        MoveRight,
        Jump,
        Sprint,
        Crouch,
        Interact,
        Use,
        EndIteration,
        Pause,
        Subtitles,
    }

    public static class InputBindings
    {
        // The order the settings page lists them in, which is the order they are learned in: walk,
        // then the two speeds, then the two things a hand does, then the loop's own control.
        public static readonly GameAction[] All =
        {
            GameAction.MoveForward, GameAction.MoveBack,
            GameAction.MoveLeft,    GameAction.MoveRight,
            GameAction.Jump,        GameAction.Sprint,
            GameAction.Crouch,      GameAction.Interact,
            GameAction.Use,         GameAction.EndIteration,
            GameAction.Pause,       GameAction.Subtitles,
        };

        // **MOUSE BUTTONS ARE KeyCodes TOO** (`Mouse0`..`Mouse6`), which is why the whole table is one
        // type and USE can be rebound onto a keyboard key or a keyboard verb onto a mouse button. It
        // is also why `GameInput.UsePressed` stopped calling `GetMouseButtonDown`: `GetKeyDown`
        // answers for both, so there is one code path rather than two.
        private static KeyCode Default(GameAction action)
        {
            switch (action)
            {
                case GameAction.MoveForward:  return KeyCode.W;
                case GameAction.MoveBack:     return KeyCode.S;
                case GameAction.MoveLeft:     return KeyCode.A;
                case GameAction.MoveRight:    return KeyCode.D;
                case GameAction.Jump:         return KeyCode.Space;
                case GameAction.Sprint:       return KeyCode.LeftShift;
                case GameAction.Crouch:       return KeyCode.LeftControl;
                case GameAction.Interact:     return KeyCode.E;
                case GameAction.Use:          return KeyCode.Mouse0;
                case GameAction.EndIteration: return KeyCode.N;
                case GameAction.Pause:        return KeyCode.Escape;
                case GameAction.Subtitles:    return KeyCode.M;
                default:                      return KeyCode.None;
            }
        }

        // What the settings page calls each one. Deliberately the verb rather than the mechanism -
        // "END ITERATION", not "N" or "EndCycleControl" - because the page is the one place a player
        // who has not read anything finds out what the game can do.
        public static string Label(GameAction action)
        {
            switch (action)
            {
                case GameAction.MoveForward:  return "WALK FORWARD";
                case GameAction.MoveBack:     return "WALK BACK";
                case GameAction.MoveLeft:     return "STRAFE LEFT";
                case GameAction.MoveRight:    return "STRAFE RIGHT";
                case GameAction.Jump:         return "JUMP";
                case GameAction.Sprint:       return "SPRINT";
                case GameAction.Crouch:       return "CROUCH";
                case GameAction.Interact:     return "INTERACT";
                case GameAction.Use:          return "USE HELD ITEM";
                case GameAction.EndIteration: return "END ITERATION";
                case GameAction.Pause:        return "PAUSE";
                case GameAction.Subtitles:    return "SUBTITLES";
                default:                      return action.ToString().ToUpperInvariant();
            }
        }

        // `KeyCode.ToString()` is right for most of the table and wrong for exactly the keys a player
        // is most likely to bind - "LeftShift", "Alpha1" and "Mouse0" are the enum's names, not the
        // key's. Only the ones that differ are listed; everything else falls through to the enum,
        // upper-cased so it matches the rest of this menu's type.
        public static string KeyLabel(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.None:         return "-";
                case KeyCode.Mouse0:       return "LEFT MOUSE";
                case KeyCode.Mouse1:       return "RIGHT MOUSE";
                case KeyCode.Mouse2:       return "MIDDLE MOUSE";
                case KeyCode.LeftShift:    return "LEFT SHIFT";
                case KeyCode.RightShift:   return "RIGHT SHIFT";
                case KeyCode.LeftControl:  return "LEFT CTRL";
                case KeyCode.RightControl: return "RIGHT CTRL";
                case KeyCode.LeftAlt:      return "LEFT ALT";
                case KeyCode.RightAlt:     return "RIGHT ALT";
                case KeyCode.Space:        return "SPACE";
                case KeyCode.Return:       return "ENTER";
                case KeyCode.Escape:       return "ESC";
                case KeyCode.UpArrow:      return "UP";
                case KeyCode.DownArrow:    return "DOWN";
                case KeyCode.LeftArrow:    return "LEFT";
                case KeyCode.RightArrow:   return "RIGHT";
            }

            string name = key.ToString();
            // Alpha0..Alpha9 and Keypad0..Keypad9 are the two families whose enum name carries a
            // prefix the key does not have on it.
            if (name.StartsWith("Alpha")) return name.Substring(5);
            if (name.StartsWith("Keypad")) return "NUM " + name.Substring(6);
            if (name.StartsWith("Mouse")) return "MOUSE " + name.Substring(5);
            return name.ToUpperInvariant();
        }

        // **THE ON-SCREEN PROMPTS DO NOT FOLLOW THESE YET, and that is a known gap** (deferred
        // 2026-08-20, by request). The game names a key in six places - the interact disc, the
        // put-down hint, the end-cycle label, and three surfaces of the calibration room - and every
        // one is a string authored into `SceneBuilder`, correct only while the bindings cannot move.
        // Rebind INTERACT and the disc still reads "E". See TODO.md; the shape of the fix is a small
        // component per label plus a change event here, which is why no caller should start caching
        // the values below.

        private const string PrefixKey = "iteration.bind.";

        private static KeyCode[] bound;

        private static void Load()
        {
            if (bound != null) return;

            bound = new KeyCode[Enum.GetValues(typeof(GameAction)).Length];
            for (int i = 0; i < bound.Length; i++)
            {
                GameAction action = (GameAction)i;
                KeyCode fallback = Default(action);
                // Stored as the int the enum already is. A name would survive a KeyCode renumbering
                // that has never happened in Unity's history and would cost a parse on every entry.
                int stored = PlayerPrefs.GetInt(PrefixKey + action, (int)fallback);
                bound[i] = Enum.IsDefined(typeof(KeyCode), stored) ? (KeyCode)stored : fallback;
            }
        }

        public static KeyCode Get(GameAction action)
        {
            Load();
            return bound[(int)action];
        }

        public static KeyCode DefaultFor(GameAction action) => Default(action);

        // **ASSIGNING A KEY THAT IS TAKEN SWAPS, IT DOES NOT DUPLICATE.** The three obvious behaviours
        // are refuse, clear the other, and swap, and only swap leaves the table in a state the player
        // can still play from: refusing means the page silently does nothing at the moment it is being
        // asked to do something, and clearing leaves a verb with no key on it that nothing on screen
        // draws attention to. A swap is always total and always reversible by repeating it.
        public static void Set(GameAction action, KeyCode key)
        {
            Load();
            if (key == KeyCode.None) return;

            KeyCode previous = bound[(int)action];
            if (previous == key) return;

            for (int i = 0; i < bound.Length; i++)
                if (i != (int)action && bound[i] == key)
                {
                    bound[i] = previous;
                    Write((GameAction)i, previous);
                }

            bound[(int)action] = key;
            Write(action, key);
            PlayerPrefs.Save();
        }

        public static void ResetToDefaults()
        {
            Load();
            for (int i = 0; i < bound.Length; i++)
            {
                GameAction action = (GameAction)i;
                bound[i] = Default(action);
                Write(action, bound[i]);
            }
            PlayerPrefs.Save();
        }

        private static void Write(GameAction action, KeyCode key) =>
            PlayerPrefs.SetInt(PrefixKey + action, (int)key);

        // WHAT A REBIND IS ALLOWED TO LISTEN FOR, and it is a list rather than the whole enum for two
        // reasons. `KeyCode` has over four hundred entries, most of them joystick buttons this game
        // has no other support for - offering them would bind a verb to a device that cannot drive
        // any of the others. And a rebind poll walks this array every frame it is listening, so its
        // length is the cost of the feature.
        //
        // Note what is NOT here: `Escape`. It is bindable as a target only through being the PAUSE
        // default, and the listener treats a press of it as "cancel" instead - see `KeyBindingPanel`.
        // A player who binds a movement key onto Escape has locked themselves out of the only menu.
        public static readonly KeyCode[] Listenable = BuildListenable();

        private static KeyCode[] BuildListenable()
        {
            var keys = new System.Collections.Generic.List<KeyCode>();

            for (KeyCode k = KeyCode.A; k <= KeyCode.Z; k++) keys.Add(k);
            for (KeyCode k = KeyCode.Alpha0; k <= KeyCode.Alpha9; k++) keys.Add(k);
            for (KeyCode k = KeyCode.Keypad0; k <= KeyCode.Keypad9; k++) keys.Add(k);
            for (KeyCode k = KeyCode.F1; k <= KeyCode.F12; k++) keys.Add(k);

            keys.AddRange(new[]
            {
                KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Tab, KeyCode.Backspace,
                KeyCode.LeftShift, KeyCode.RightShift,
                KeyCode.LeftControl, KeyCode.RightControl,
                KeyCode.LeftAlt, KeyCode.RightAlt,
                KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
                KeyCode.Insert, KeyCode.Delete, KeyCode.Home, KeyCode.End,
                KeyCode.PageUp, KeyCode.PageDown,
                KeyCode.Comma, KeyCode.Period, KeyCode.Slash, KeyCode.Semicolon, KeyCode.Quote,
                KeyCode.LeftBracket, KeyCode.RightBracket, KeyCode.Backslash,
                KeyCode.Minus, KeyCode.Equals, KeyCode.BackQuote,
                KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.Mouse2, KeyCode.Mouse3, KeyCode.Mouse4,
            });

            return keys.ToArray();
        }
    }
}
