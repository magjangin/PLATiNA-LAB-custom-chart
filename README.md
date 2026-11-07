# PLATiNA LAB Custom Chart Mod

[PLATiNA LAB](https://store.steampowered.com/app/3430470/)에 커스텀 곡·차트를 넣는 MelonLoader 모드입니다.
게임 폴더의 `hwa\` 폴더에 곡 폴더를 넣으면 선곡 화면에 **CUSTOM** 카테고리가 생기고, 그 안에서 자작 차트를 자작 음원·BGA·커버와 함께 플레이할 수 있습니다.

> 비공식 팬 모드입니다. 게임 파일이나 디컴파일 코드는 이 저장소에 포함되어 있지 않습니다.
> 커스텀 곡은 서버에 기록을 보내지 않고 로컬에서만 처리되며, 원곡 기록·랭킹에는 영향을 주지 않습니다.

## 기능

- **CUSTOM 카테고리** — 선곡 화면에 파란 `CUSTOM` 탭을 추가합니다.
- **폴더 기반 커스텀 곡** — `hwa\<곡폴더>\`마다 곡 하나. 제목·아티스트·BPM·난이도·레벨을 `info.txt`로 지정합니다.
- **미디어 교체** — 커버 이미지, 미리듣기·인게임 BGM(`music.ogg`), BGA(`video.mp4`)를 교체합니다.
- **곡 길이 자동 맞춤** — 음원/영상 길이를 읽어 차트 끝 위치를 늘려서, 곡이 끝까지 재생된 뒤 결과 화면으로 넘어갑니다.
- **오프라인 세션** — 게임 시작·종료 서버 요청을 로컬 처리해서 결과 화면까지 정상 진행됩니다.
- **기존 곡 차트 덮어쓰기** — 원본 차트를 JSON으로 덤프하고, 수정한 JSON으로 교체할 수 있습니다(핫리로드).
- **실시간 판정바 & 키뷰어 오버레이** — 세로/가로 판정바, FAST/SLOW ms 오차 표시, 키 입력 반응 키뷰어를 인게임 오버레이로 제공합니다.
- **오토 플레이 & 올 퍼펙트 지원** — 게임 내장 오토플레이 모드 토글 및 커스텀 곡 전용 올 퍼펙트(PERFECT HIGH) 조작을 지원합니다.
- **다국어 및 중국어 완벽 지원** — UTF-8, 중국어 GBK(CP936)/Big5, 일본어 Shift-JIS, 한국어 CP949 자동 감지, 중국어 키워드(歌名/曲师/谱师/难度 등) 및 파일명(封面/曲绘/音乐 등)을 완벽 지원합니다.
- **영문 띄어쓰기 자동 정규화** — 영문 축약형의 비정상 공백(`you'  re` → `you're`, `don ' t` → `don't`)을 자동 교정합니다.
- **안정성 및 메모리 관리** — 커버 텍스처 LRU 캐시 및 IL2CPP 메모리 스트림 명시적 해제로 VRAM/RAM 누수를 원천 방지합니다.

## 환경

| 항목 | 버전 |
| --- | --- |
| 게임 | PLATiNA LAB (Steam App ID `3430470`, Build ID `25981392`) |
| 엔진 | Unity `6000.3.25f1`, IL2CPP |
| 모드 로더 | MelonLoader `0.7.3` (net6) |

## 설치

1. 게임에 [MelonLoader 0.7.3](https://github.com/LavaGang/MelonLoader/releases)을 설치하고 게임을 한 번 실행합니다(Il2Cpp 어셈블리 생성).
2. `PLATiNALAB.Mod.dll`을 `<게임 폴더>\Mods\`에 넣습니다.
3. 게임을 실행하면 `<게임 폴더>\hwa\` 폴더와 샘플 곡이 생성됩니다.

## 커스텀 곡 만들기

```
PLATiNA LAB\
└─ hwa\
   └─ my_song\
      ├─ info.txt          곡 정보 (필수)
      ├─ chart_easy.json   난이도별 차트 (없으면 템플릿 자동 생성)
      ├─ chart_hard.json
      ├─ music.ogg         BGM / 미리듣기 (audio.ogg, 音乐.ogg 도 가능)
      ├─ video.mp4         BGA (bga.mp4, 视频.mp4 도 가능)
      └─ cover.png         커버 (thumbnail.png, 封面.png, 曲绘.png 도 가능)
```

곡 폴더는 이름순으로 읽혀 `hwa_001`, `hwa_002` … 의 ID가 붙습니다.

### info.txt

`키 : 값` 또는 `키 = 값` 형식입니다. 한국어, 영어, 중국어를 모두 지원합니다.

```
title  : My Song
artist : someone
bpm    : 150
난이도 : easy, hard
easy   = 3
hard   = 9
patterner : me
```

| 지원 키워드 (다국어) | 설명 |
| --- | --- |
| `title` / `곡 제목` / `歌名` / `曲名` / `标题` | 곡 제목 |
| `artist` / `아티스트` / `曲师` / `作曲` / `艺术家` / `作者` | 아티스트 |
| `bpm` | BPM. 없으면 폴더 안 `.bms`의 `#BPM`을 사용 |
| `난이도` / `difficulty` / `难度` | 쉼표로 구분한 난이도 목록 (`easy`, `hard`, `简单`, `困难` 등) |
| `<난이도>` | 해당 난이도의 레벨 (예: `easy = 5`, `简单 = 8`) |
| `patterner` / `패턴제작자` / `채보` / `谱师` / `谱面` | 패턴 제작자 |
| `lanes` / `레인` / `轨道` / `键位` | BMS 레인 1~4에 대응하는 채널 (예: `16,11,12,13`) |

사용 가능한 게임 난이도 이름은 `easy`, `hard`, `over`, `plus`, `testpattern` 입니다.
그 밖의 이름(`normal`, `简单`, `困难`, `专家`, `进阶` 등)은 정규화 및 유효 난이도로 자동 매핑됩니다.

### 차트 (JSON / BMS)

- **JSON 차트**: 게임의 PPP 형식 JSON. `chart_<난이도>.json` → `chart.json` 순서로 찾고, 없으면 템플릿을 만들어 줍니다.
- **BMS 차트**: 곡 폴더에 `.bms/.bme/.bml/.pms`가 있으면 매 플레이마다 변환하여 사용합니다 (핫리로드).
  - `#WAV` 키음 이름으로 노트 역할을 지정합니다:
    - 단노트: `ShortNote.wav`, `단노트`, `单点`
    - 롱노트 시작: `LongNote 홀드 시작.wav`, `HoldStart`, `长条开始`, `长按开始`
    - 롱노트 끝: `LongNote 홀드 끝.wav`, `HoldEnd`, `长条结束`, `长按结束`
    - 피치 노트: `PitchNote.wav`, `피치`, `滑音`, `音高`

## 설정 파일 (SaveCustomKey/config.txt)

게임을 1회 실행하면 `<게임 폴더>\SaveCustomKey\config.txt`가 생성됩니다.
곡을 시작할 때마다 다시 읽으므로 게임을 재시작하지 않아도 즉시 반영됩니다.

```ini
# 오토 플레이 (1 = 켜짐, 0 = 꺼짐, 开/关)
AutoPlay=0

# 올 퍼펙트 판정 조작 (1 = 켜짐, 0 = 꺼짐)
# 서버 랭킹 보호를 위해 hwa 커스텀 곡에서만 동작합니다.
AllPerfect=0

# 실시간 판정바 표시
EnableJudgmentBar=1
JudgmentBarVertical=1
JudgmentBarSide=Center

# 실시간 키뷰어 표시
EnableKeyViewer=1
KeyViewerPressedColor=#26BFD9D9
```

## 기존 곡 차트 덮어쓰기

1. `UserData\MelonPreferences.cfg`의 `[PLATiNALAB]`에서 `DumpCharts = true`로 켭니다.
2. 곡을 플레이하면 `UserData\PLATiNALAB\ChartDump\{곡ID}__{패턴}.json`이 저장됩니다.
3. 수정한 파일을 `UserData\PLATiNALAB\CustomCharts\`에 같은 이름으로 넣으면 다음 플레이부터 교체됩니다.

## 빌드

.NET 6 SDK가 필요합니다:

```powershell
dotnet build mod\PLATiNALAB.Mod.csproj
```

빌드가 끝나면 DLL이 `<GameRoot>\Mods\`로 자동 복사됩니다.

## 저장소 구성

| 경로 | 내용 |
| --- | --- |
| `mod/` | 모드 소스코드 |
| `docs/chart_format.md` | 차트(PPP JSON) 형식 및 BMS 변환 규칙 상세 |
| `docs/hooks.md` | 후킹 대상 메서드 명세서 |
| `AGENTS.md` | 개발 인수인계 문서 (설계 의도, 감사 기록, 실패 기록) |
