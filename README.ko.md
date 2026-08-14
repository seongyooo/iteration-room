# Iteration Room

[English](README.md) · **한국어**

Unity로 만든 1인칭 타임루프 퍼즐 게임. 2016년 단편 영화 *Iteration 1*에서 출발했습니다.

밀폐된 흰 시설의 침대에서 깨어납니다. 60초 뒤 루프가 당신을 데려가고, 같은 침대에서 다시 깨어납니다
— 하지만 **방은 기억합니다.** 이전의 모든 런이 **유령**으로 함께 재생됩니다. 당신이 걸었던 경로를
그대로 걷고, 눌렀던 것을 그대로 누르고, 들었던 것을 그대로 든 과거의 자신입니다. 발판을 밟고 있으면서
동시에 문 앞에 서 있을 수는 없지만, 과거의 자신이 발판 위에 서 있다면 그건 더 이상 문제가 아닙니다.
이 게임 전체가 그 아이디어의 확장입니다.

**매 회차마다 할 일이 줄어들어야 합니다.** 건물은 여섯 개의 방이 일렬로 이어진 하나의 복도이고,
클리어한다는 것은 퍼즐 방 세 개 분량의 심부름을 필요한 만큼의 과거 자신들에게 나눠준다는 뜻입니다.

최고 기록: **10회차, 총 4분 47초** (2026-08-14). 처음 하면 이보다 훨씬 많이 걸립니다.

---

## 실행하기

**Unity 6000.5.7f1**, URP 17.5.0. **Unity 버전을 바꾸지 마세요** — `Assets/Settings/`의 렌더 파이프라인
에셋들이 이 버전에 맞춰 배선되어 있습니다.

```bash
git lfs install          # 필수: 가구 모델이 LFS 객체입니다
git clone <이 저장소>
```

**새로 클론하면 씬이 없습니다.** `Assets/Scenes/*.unity`는 git-ignore이고 생성되는 파일입니다 —
[씬은 빌드 산출물](#씬은-빌드-산출물) 참고. Unity에서 프로젝트를 연 다음 둘 중 하나:

- **에디터에서**: 메뉴 **`Iteration Room ▸ Build Whitebox Scene`**
- **헤드리스** (에디터가 프로젝트를 열고 있지 **않을** 때만):

  ```bash
  Unity.exe -batchmode -nographics -projectPath . \
    -executeMethod IterationRoom.EditorTools.SceneBuilder.Build -quit -logFile build.log
  ```

빌드는 씬 두 개를 쓰고, 리플렉션 프로브 7개를 굽고, 메뉴 배경을 캡처합니다. 몇 분 걸립니다. 로그에
`[SceneBuilder] IterationRoom scene built`가 있고 `error CS`가 없으면 성공입니다.

그 다음 `Assets/Scenes/MainMenu.unity`(빌드 인덱스 0)를 열고 Play를 누르세요. `IterationRoom`이 인덱스 1입니다.

> **Player Settings ▸ Run In Background는 켜져 있어야 합니다.** `Build()`가 켜주지만, 꺼지면 루프가
> 오작동합니다.

## 조작

| | |
|---|---|
| **WASD** | 이동 |
| **마우스** | 시점 |
| **Space** | 점프 |
| **Left Shift** | 달리기 |
| **Left Ctrl** | 웅크리기 |
| **E** | 집기 · 내려놓기 · 작동(서랍, 자물쇠, 홈, 플레이트) |
| **좌클릭** | 핀으로 풍선 터뜨리기 · 체스 말 놓기 |
| **N** | 이번 회차 즉시 종료 |
| **Esc** | 일시정지 |

**E는 한 번에 한 가지만 합니다.** 빈손이면 화면에 보이는 가장 가까운 것을 집고, 뭔가 들고 있으면
내려놓습니다 — 단, 지금 들고 있는 것을 원하는 픽스처 앞에 서 있다면 거기에 들어갑니다. 물건은
**한 번에 하나만** 들 수 있습니다.

## 방 구성

| | |
|---|---|
| **캘리브레이션** | 마우스 감도, 조작 안내, `BEGIN` 플레이트. 시계가 돌기 전. |
| **Room 1** | 침대, 협탁, 바닥 발판, 문. 루프를 가르칩니다. |
| **Room 2** | 서랍, 핀, 풍선 70개, 색깔 열쇠 3개. |
| **Room 2 West** | 보드에서 흩어진 체스 말 12개. 되돌려 놓으면 불이 켜집니다. |
| **Room 2 East** | 유리 큐브 6개와 그것을 원하는 홈 6개. |
| **Room 3** | 바닥 발판, 문, 받침대. |
| **Room 4** | 모양이 다른 홈 세 개가 있는 콘솔. **유일한 탈출 조건.** |

퍼즐 방은 각각 탈출 오브젝트를 하나씩 내놓습니다 — 빨간 큐브, 파란 구, 노란 삼각형. 셋 다 **한 회차
안에** Room 4의 콘솔에 들어가야 하고, 그 어느 것도 리셋에서 예외가 아닙니다. 시계는 Room 4에서도
계속 돕니다. 거기 도착하는 것만으로는 아무것도 끝나지 않습니다.

---

## 프로젝트 구조

```
Assets/
  Editor/SceneBuilder.cs    ← 두 씬의 모든 오브젝트와 모든 조정값
  Scripts/
    Loop/                   루프, 시계, HUD, 일시정지, 메뉴, 엔딩
    Ghost/                  과거 자신의 기록과 재생
    Interactables/          들 수 있는 것, 문, 서랍, 자물쇠, 소켓
    Room/                   파일 하나당 퍼즐 규칙 하나
    Player/                 컨트롤러, 카메라, 든 물건 처리
    Audio/                  환경음과 안내 방송
  ArtAssets/                유일하게 외부에서 가져온 것 (CC 모델) — Git LFS
  Audio/                    Tools/가 생성. 외부 소싱 아님
  Settings/                 URP 파이프라인 에셋
Tools/
  generate_sfx.py           모든 효과음. 표준 라이브러리만 사용
  generate_narration.ps1    안내 방송 45줄, Windows TTS
docs/                       왜 그렇게 되어 있는지
CLAUDE.md                   코드를 고치기 전에 반드시 알아야 할 것
TODO.md                     남은 일
```

### 씬은 빌드 산출물

`Assets/Scenes/*.unity`는 `SceneBuilder`가 생성하며 git-ignore입니다. **씬을 손으로 편집하지 마세요**
— `SceneBuilder.cs`를 고치고 다시 빌드하세요. Unity는 리빌드할 때마다 모든 fileID를 새로 뽑기 때문에,
스크립트 한 줄만 고쳐도 의미 없는 11,000줄짜리 씬 diff가 나오고, 두 리빌드를 머지하면 노이즈 위에서
충돌이 반드시 납니다.

씬의 `.meta` 파일은 **추적됩니다** — `EditorBuildSettings.asset`이 값으로 저장하는 GUID를 고정하기
때문입니다.

### 거의 모든 것이 생성됩니다

씬만이 아닙니다. 효과음은 파이썬 스크립트가 합성하고, 안내 방송은 Windows TTS이고, 벽의 질감은 생성된
노멀맵이고, 모든 아이콘과 심볼은 코드가 텍스처에 직접 그립니다. 외부에서 가져온 것은
`Assets/ArtAssets/`의 크리에이티브 커먼즈 모델 몇 개뿐이고, 각각의 정확한 라이선스는
`docs/asset-licences.md`가 추적합니다. 이 거래의 조건은 **프로젝트 전체가 스크립트로부터
재생성된다**는 것입니다. 생성된 에셋을 진짜 물건으로 바꾸고 싶다면, 같은 이름으로 파일을 넣기만 하면
됩니다. 다른 건 아무것도 지우지 마세요.

---

## 어디에 무엇이 적혀 있는가

| 문서 | 내용 |
|---|---|
| `CLAUDE.md` | 코드를 고치기 전에 지켜야 할 규칙. 여기서 시작하세요. |
| `docs/architecture.md` | 스크립트별 색인 |
| `docs/puzzle-design.md` | 방별 설계 근거 |
| `docs/cycle-design.md` | 사이클 패러다임: 앞으로 퍼즐을 어떻게 늘릴 것인가 |
| `docs/ghost-possession-design.md` | 유령의 물건 소유: 전체 논증 |
| `docs/loop-and-ui.md` | 루프, 기상, 엔딩, HUD, 메뉴 |
| `docs/room-geometry.md` | 방 껍데기, 벽 그리드, 문 |
| `docs/rendering-notes.md` | 조명, 머티리얼, 리플렉션 프로브 |
| `docs/audio.md` | 방송 스케줄, 필터 체인, 클립 생성 |
| `docs/ghosts.md` | 유령의 외형, 잔상 셰이더, 애니메이션 |
| `docs/build-and-stack.md` | 엔진과 파이프라인 상세, 두 가지 리빌드 경로 |
| `docs/asset-licences.md` | 외부 파일 전부와 그 라이선스 |
| `docs/gotchas.md` | 비싸게 배운 것들 |
| `docs/decisions.md` | 기각된 아이디어와 미해결 질문 |
| `iteration-game-spec.md` | 최초 기획서 |

이 분리는 의도적입니다: **CLAUDE.md는 규칙, `docs/`는 이유, `git log`는 역사, `TODO.md`는 남은 일.**
