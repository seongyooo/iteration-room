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
    //     calibration room, which was a wall display but one hundred percent instruction (that room
    //     is gone; the split it illustrated is not). All of it
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
            { "record.empty",       "NO CYCLE COMPLETED" },
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
            // **THE INTAKE NOTICE, AND IT IS LOCALISED WHERE THE FACILITY'S OTHER SIGNAGE IS NOT.**
            // The rule at the top of this file is that the facility talking to ITSELF stays English -
            // ROOM 2, ERROR, FIRE AXE. This is the facility talking to the SUBJECT: it is addressed
            // to the player in the second person, and it is the only place the game states its own
            // premise and its own time limit. A player who cannot read it has lost the premise, which
            // is exactly the failure the split exists to prevent.
            { "note.header",         "ITERATION PROGRAM" },
            { "note.sub",            "SUBJECT INTAKE" },
            { "note.objectiveLabel", "OBJECTIVE" },
            { "note.objective",      "LEAVE THE ROOM" },
            { "note.cycleLabel",     "CYCLE LENGTH" },
            { "note.cycle",          "60 SECONDS" },
            { "note.repeat1",        "The cycle repeats until" },
            { "note.repeat2",        "the objective is met." },
            { "note.forget1",        "You will not remember" },
            { "note.forget2",        "reading this." },
            { "pause.restart",      "RESTART CYCLE" },
            { "pause.mainMenu",     "MAIN MENU" },

            // HUD. The bracketed key is deliberately NOT a substitution - the on-screen key names are
            // still hard-coded (TODO.md), so only the words around them are translated here.
            { "hud.putDown",        "[E] — PUT DOWN" },
            { "hud.endCycle",       "HOLD [N] — END CYCLE" },

            // Ending card
            { "end.title",          "C Y C L E   B R O K E N" },
            { "end.clickContinue",  "CLICK TO CONTINUE" },


            // ---------------------------------------------------------------- THE PA, SUBTITLED
            //
            // **THE ONE PLACE THE FACILITY'S OWN VOICE APPEARS IN THIS TABLE, AND IT IS NOT AN
            // EXCEPTION TO THE RULE ABOVE.** The AUDIO stays English in every language, exactly like
            // the signage - the building is an English-language facility and it is talking to itself.
            // What is localised is the CAPTION, which is the game handing the player a translation of
            // something it is deliberately not translating. See `PaSubtitle`.
            //
            // Sentence case rather than the shouted caps the wall displays use: this is subtitling, and
            // a full-screen line in capitals reads as the game raising its voice.
            { "pa.iteration",        "Iteration {0}. Sixty seconds remaining." },
            { "pa.iterationGeneric", "New iteration. Sixty seconds remaining." },
            { "pa.tenSeconds",       "Ten seconds remaining." },
            { "pa.newCycle",         "New cycle initialized." },
            { "pa.cycleTerminated",  "Cycle terminated." },
            { "pa.cycleBroken",      "Containment failure. Cycle broken." },
            { "pa.manualTermination","Manual termination available. Hold N to end the cycle." },
            { "pa.allCyclesBroken",  "All cycles have been destroyed." },
            { "pa.transportCalled",  "Transport has been called. Please stand by." },
            { "pa.cycleResult",      "Cycle {0}. {1} iterations, {2} minutes {3} seconds." },
            { "pa.cycleResultSeconds", "Cycle {0}. {1} iterations, {2} seconds." },
            { "pa.totalResult",      "Total. {0} iterations, {1} minutes {2} seconds." },
            { "pa.totalResultSeconds", "Total. {0} iterations, {1} seconds." },
            { "pa.ride0",          "Your experiment is complete." },
            { "pa.ride1",          "Records for cycles one, two and three have been stored." },
            { "pa.ride2",          "Your data has been assimilated. Improved results have been obtained." },
            { "pa.ride3",          "This data will contribute to the cycles we build next." },
            { "pa.ride4",          "Thank you for your participation." },
            { "pa.ride5",          "You are being removed from the test environment." },
            { "pa.ride6",          "Please remain inside the cable car. Do not lean out." },
            { "pa.ride7",          "The structures below you are not decommissioned." },
            { "pa.ride8",          "Occupancy is being restored on all levels." },
            { "pa.ride9",          "You are not the first subject to reach this elevation." },
            { "pa.ride10",          "Their results are also on file." },
            { "pa.ride11",          "Surface access will be granted shortly." },
            { "pa.ride12",          "This concludes your assignment." },
            { "pa.ride13",          "Guidance will continue." },

        };

        private static readonly Dictionary<string, string> Korean = new Dictionary<string, string>
        {
            { "menu.play",          "시작" },
            { "menu.continue",      "이어하기" },
            { "menu.cycleSelect",   "사이클 선택" },
            { "menu.record",        "기록" },
            { "record.empty",       "완료한 사이클이 없습니다" },
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
            { "note.header",         "반복 실험" },
            { "note.sub",            "피험자 등록" },
            { "note.objectiveLabel", "목표" },
            { "note.objective",      "방을 나갈 것" },
            { "note.cycleLabel",     "주기" },
            { "note.cycle",          "60초" },
            { "note.repeat1",        "목표를 달성할 때까지" },
            { "note.repeat2",        "주기는 반복됩니다." },
            { "note.forget1",        "당신은 이 글을 읽은 것을" },
            { "note.forget2",        "기억하지 못합니다." },
            { "pause.restart",      "사이클 다시 시작" },
            { "pause.mainMenu",     "메인 메뉴" },

            { "hud.putDown",        "[E] — 내려놓기" },
            { "hud.endCycle",       "[N] 길게 — 사이클 종료" },

            { "end.title",          "사 이 클  파 괴" },
            { "end.clickContinue",  "클릭하면 계속" },


            // THE PA, SUBTITLED - see the note in the English table. The audio stays English; this is
            // what the player reads under it.
            //
            // "이터레이션" IS NOT TRANSLATED, for the reason it never has been: it is the game's own
            // word, the title, and the unit the whole loop is counted in.
            { "pa.iteration",        "이터레이션 {0}. 60초 남았습니다." },
            { "pa.iterationGeneric", "새 이터레이션. 60초 남았습니다." },
            { "pa.tenSeconds",       "10초 남았습니다." },
            { "pa.newCycle",         "새 사이클을 시작합니다." },
            { "pa.cycleTerminated",  "사이클을 종료했습니다." },
            { "pa.cycleBroken",      "격리 실패. 사이클이 파괴되었습니다." },
            { "pa.manualTermination","수동 종료 가능. N 키를 길게 누르십시오." },
            { "pa.allCyclesBroken",  "모든 사이클을 파괴하였습니다." },
            { "pa.transportCalled",  "이송 수단을 호출했습니다. 잠시만 기다려 주십시오." },
            { "pa.cycleResult",      "사이클 {0}. {1}회 반복, {2}분 {3}초." },
            { "pa.cycleResultSeconds", "사이클 {0}. {1}회 반복, {2}초." },
            { "pa.totalResult",      "합계. {0}회 반복, {1}분 {2}초." },
            { "pa.totalResultSeconds", "합계. {0}회 반복, {1}초." },
            { "pa.ride0",          "귀하의 실험이 종료되었습니다." },
            { "pa.ride1",          "사이클 1, 2, 3 기록이 저장되었습니다." },
            { "pa.ride2",          "귀하의 데이터를 학습하여 더 좋은 결과물을 얻었습니다." },
            { "pa.ride3",          "이 데이터로 저희는 앞으로의 사이클을 만드는 데 기여할 것입니다." },
            { "pa.ride4",          "실험에 참가해 주셔서 감사합니다." },
            { "pa.ride5",          "실험 환경에서 나오는 중입니다." },
            { "pa.ride6",          "케이블카 안에 머물러 주십시오. 몸을 내밀지 마십시오." },
            { "pa.ride7",          "아래의 구조물은 폐기되지 않았습니다." },
            { "pa.ride8",          "전 층의 수용이 복구되고 있습니다." },
            { "pa.ride9",          "이 고도에 도달한 피험자는 귀하가 처음이 아닙니다." },
            { "pa.ride10",          "그들의 결과 또한 보관되어 있습니다." },
            { "pa.ride11",          "곧 지상 접근이 허가됩니다." },
            { "pa.ride12",          "이것으로 귀하의 임무를 마칩니다." },
            { "pa.ride13",          "계속해서 안내하겠습니다." },

        };
    }
}
