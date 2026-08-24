using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    public enum GameLanguage
    {
        English = 0,
        Korean = 1,
    }

    // EVERY WORD THE GAME SAYS TO THE PLAYER, in both languages.
    //
    // **WHAT IS IN HERE AND WHAT IS NOT IS A DESIGN LINE, not an oversight** (decided 2026-08-20).
    // Text in this game is two different things wearing the same font:
    //
    //   - THE GAME TALKING TO THE PLAYER - menus, the HUD, the pause screen, the ending card, and the
    //     calibration room, which is a wall display but is one hundred percent instruction. All of it
    //     is here, because a player who cannot read it cannot play.
    //   - THE FACILITY TALKING TO ITSELF - "ROOM 2" over a doorway, the `ERROR` on room2-0's console,
    //     "FIRE AXE" and "BOTTOMLESS" on its pictograms. **None of it is here.** Those are props. The
    //     building is an English-language facility and translating its signage changes where the game
    //     is set, not what language it is played in - the same reason the title stays "ITERATION".
    //
    // A key that is missing from the Korean column falls through to English rather than showing the
    // key, so a half-translated build is playable and looks unfinished instead of broken.
    public static class Loc
    {
        public static GameLanguage Current => GameSettings.Language;

        // Raised when the language changes, for anything already on screen. The title screen is the
        // one place where that happens - the settings page is ON it - and every other scene is loaded
        // after the choice. `LocalizedText` subscribes; nothing else should need to.
        public static event System.Action Changed;

        public static void RaiseChanged() => Changed?.Invoke();

        public static string Get(string key)
        {
            if (key == null) return string.Empty;

            if (Current == GameLanguage.Korean && Korean.TryGetValue(key, out string ko)) return ko;
            return English.TryGetValue(key, out string en) ? en : key;
        }

        // ENGLISH IS THE TABLE OF RECORD. It holds every key, and it is what `SceneBuilder` authors
        // into the scene, so a build with this file deleted still reads correctly in English.
        private static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            // Title screen
            { "menu.play",          "PLAY" },
            { "menu.continue",      "CONTINUE" },
            { "menu.cycleSelect",   "CYCLE SELECT" },
            { "menu.record",        "RECORD" },
            { "menu.settings",      "SETTINGS" },
            { "menu.credits",       "CREDITS" },
            { "menu.quit",          "QUIT" },
            { "menu.back",          "BACK" },
            { "menu.loading",       "LOADING" },

            // Settings
            { "set.volume",         "VOLUME" },
            { "set.sensitivity",    "MOUSE SENSITIVITY" },
            { "set.language",       "LANGUAGE" },
            { "set.controls",       "CONTROLS" },
            { "set.bindHintIdle",   "CLICK A KEY TO CHANGE IT" },
            { "set.bindHintArmed",  "PRESS A KEY   ·   ESC TO CANCEL" },
            { "set.resetBindings",  "RESET TO DEFAULTS" },

            // The verbs on the bindings page
            { "act.MoveForward",    "WALK FORWARD" },
            { "act.MoveBack",       "WALK BACK" },
            { "act.MoveLeft",       "STRAFE LEFT" },
            { "act.MoveRight",      "STRAFE RIGHT" },
            { "act.Jump",           "JUMP" },
            { "act.Sprint",         "SPRINT" },
            { "act.Crouch",         "CROUCH" },
            { "act.Interact",       "INTERACT" },
            { "act.Use",            "USE HELD ITEM" },
            { "act.EndIteration",   "END ITERATION" },
            { "act.Pause",          "PAUSE" },

            // Pause overlay
            { "pause.title",        "P A U S E D" },
            { "pause.resume",       "RESUME" },
            { "pause.restart",      "RESTART CYCLE" },
            { "pause.mainMenu",     "MAIN MENU" },

            // HUD. The bracketed key is deliberately NOT a substitution - the on-screen key names are
            // still hard-coded (TODO.md), so only the words around them are translated here.
            { "hud.putDown",        "[E] — PUT DOWN" },
            { "hud.endCycle",       "HOLD [N] — END CYCLE" },

            // Ending card
            { "end.title",          "C Y C L E   B R O K E N" },
            { "end.clickContinue",  "CLICK TO CONTINUE" },

            // The calibration room. A wall display, but pure instruction - see the note at the top.
            { "cal.title",          "M O U S E   S E N S I T I V I T Y" },
            { "cal.scrollAdjust",   "SCROLL TO ADJUST" },
            { "cal.begin",          "PRESS [E] AT THE PANEL BEHIND YOU" },
            { "cal.beginPlate",     "B E G I N" },
            { "cal.clickToLock",    "CLICK TO ENABLE MOUSE LOOK" },
        };

        private static readonly Dictionary<string, string> Korean = new Dictionary<string, string>
        {
            { "menu.play",          "시작" },
            { "menu.continue",      "이어하기" },
            { "menu.cycleSelect",   "사이클 선택" },
            { "menu.record",        "기록" },
            { "menu.settings",      "설정" },
            { "menu.credits",       "만든 사람들" },
            { "menu.quit",          "종료" },
            { "menu.back",          "뒤로" },
            { "menu.loading",       "불러오는 중" },

            { "set.volume",         "음량" },
            { "set.sensitivity",    "마우스 감도" },
            { "set.language",       "언어" },
            { "set.controls",       "조작" },
            { "set.bindHintIdle",   "키를 눌러 바꾸려면 클릭하세요" },
            { "set.bindHintArmed",  "바꿀 키를 누르세요   ·   ESC 취소" },
            { "set.resetBindings",  "기본값으로 되돌리기" },

            { "act.MoveForward",    "앞으로" },
            { "act.MoveBack",       "뒤로" },
            { "act.MoveLeft",       "왼쪽으로" },
            { "act.MoveRight",      "오른쪽으로" },
            { "act.Jump",           "점프" },
            { "act.Sprint",         "달리기" },
            { "act.Crouch",         "앉기" },
            { "act.Interact",       "상호작용" },
            { "act.Use",            "든 물건 사용" },
            { "act.EndIteration",   "반복 끝내기" },
            { "act.Pause",          "일시정지" },

            { "pause.title",        "일 시 정 지" },
            { "pause.resume",       "계속하기" },
            { "pause.restart",      "사이클 다시 시작" },
            { "pause.mainMenu",     "메인 메뉴" },

            { "hud.putDown",        "[E] — 내려놓기" },
            { "hud.endCycle",       "[N] 길게 — 사이클 종료" },

            { "end.title",          "사 이 클  파 괴" },
            { "end.clickContinue",  "클릭하면 계속" },

            { "cal.title",          "마 우 스  감 도" },
            { "cal.scrollAdjust",   "휠로 조절" },
            { "cal.begin",          "뒤쪽 패널에서 [E]" },
            { "cal.beginPlate",     "시  작" },
            { "cal.clickToLock",    "클릭하면 마우스 조작이 켜집니다" },
        };
    }
}
