# PLATiNA LAB Custom Chart Mod

[PLATiNA LAB](https://store.steampowered.com/app/3430470/)에 커스텀 곡·차트를 넣는 MelonLoader 모드입니다.
게임 폴더의 `hwa\` 폴더에 곡 폴더를 넣으면 선곡 화면에 **CUSTOM** 카테고리가 생기고, 그 안에서 자작 차트를 자작 음원·BGA·커버와 함께 플레이할 수 있습니다.

> 비공식 팬 모드입니다. 게임 파일이나 디컴파일 코드는 이 저장소에 포함되어 있지 않습니다.
> 커스텀 곡은 서버에 기록을 보내지 않고 로컬에서만 처리되며, 원곡 기록·랭킹에는 영향을 주지 않습니다.

## 기능

- **CUSTOM 카테고리** — 선곡 화면에 파란 `CUSTOM` 탭을 추가합니다.
- **폴더 기반 커스텀 곡** — `hwa\<곡폴더>\`마다 곡 하나. 제목·아티스트·BPM·난이도·레벨을 `info.txt`로 지정합니다.
- **미디어 교체** — 커버 이미지, 미리듣기·인게임 BGM(`music.ogg`/`music.wav`), BGA(`video.mp4`)를 교체합니다.
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
2. [Releases](https://github.com/magjangin/PLATiNA-LAB-custom-chart/releases)에서 최신 zip을 받아 안의 `Mods\PLATiNALAB.Mod.dll`을 `<게임 폴더>\Mods\`에 넣습니다. (직접 빌드하려면 아래 [빌드](#빌드) 참고)
3. 게임을 실행하면 `<게임 폴더>\hwa\` 폴더와 샘플 곡이 생성됩니다.

변경 내역은 [CHANGELOG.md](CHANGELOG.md)에 있습니다. 모드는 특정 게임 빌드에서 검증했으므로, 게임이 업데이트됐다면 [게임이 업데이트됐을 때](#게임이-업데이트됐을-때)를 먼저 보세요.

## 지원 형식과 제한

| 항목 | 지원 | 제한 |
| --- | --- | --- |
| 음원 | `music.ogg`(Ogg Vorbis) 또는 `music.wav` | 길이를 읽을 수 있어야 곡이 끝까지 재생됩니다. Opus·MP3는 길이를 못 읽어 곡이 중간에 끝날 수 있고, 경고 로그가 남습니다. |
| 영상(BGA) | `video.mp4` | `moov`가 읽히는 일반 MP4. 길이 미정(fragmented) MP4는 길이를 못 읽습니다. |
| 커버 | PNG, JPG | — |
| 차트 | PPP JSON, BMS(`.bms/.bme/.bml/.pms`) | BMS의 박자 변경(`#mmm02`)·STOP은 미지원(4/4로 변환). `#RANDOM`/`#IF`/`#ELSE`는 항상 1번 분기만 변환. |
| 판정 | 4레인 FourLines | 피치 노트 판정은 인게임 미확인. |

`info.txt`와 곡 폴더 구성을 바꾸면 **게임을 다시 시작**해야 반영됩니다(곡 목록은 시작할 때 한 번 읽습니다). 차트(JSON/BMS)·음원·영상·커버 파일 수정은 재시작 없이 다음 플레이에 반영됩니다.

## 문제 해결

| 증상 | 확인 |
| --- | --- |
| 선곡 화면에 CUSTOM 탭이 없다 | `MelonLoader\Latest.log`에서 `[Health]`와 `[Init]` 줄을 보세요. `필수 패치 누락`이면 게임 업데이트로 커스텀 곡 주입이 꺼진 것입니다. |
| 곡이 BGM/BGA 중간에 끝난다 | 로그의 `음원 길이를 읽지 못했습니다` 경고를 확인하세요. Ogg Vorbis 또는 WAV로 변환하면 됩니다. |
| 노트가 겹친다 / 일부만 나온다 | 로그의 `[BMS]` 줄(레인 매핑, `#RANDOM` 경고)을 확인하고 `info.txt`에 `lanes = 16,11,12,13`처럼 지정하세요. |
| 설정이 안 먹는다 | `[Config]` 경고(알 수 없는 키, 잘못된 값)를 확인하세요. 곡을 시작할 때마다 다시 읽습니다. |
| 곡 번호(`hwa_NNN`)가 바뀌었다 | `UserData\PLATiNALAB\song_ids.json`에 폴더 이름별로 저장됩니다. 폴더 이름을 바꾸면 새 곡으로 취급됩니다. |
| 원인을 더 자세히 보고 싶다 | `config.txt`에서 `DiagnosticLogs=1`로 켜고 다시 실행하세요. |

## 커스텀 곡 만들기

```
PLATiNA LAB\
└─ hwa\
   └─ my_song\
      ├─ info.txt          곡 정보 (필수)
      ├─ chart_easy.json   난이도별 차트 (없으면 템플릿 자동 생성)
      ├─ chart_hard.json
      ├─ music.ogg         BGM / 미리듣기 (audio.ogg, 音乐.ogg, music.wav 등도 가능. 같은 이름이면 .ogg 우선)
      ├─ video.mp4         BGA (bga.mp4, 视频.mp4 도 가능)
      └─ cover.png         커버 (thumbnail.png, 封面.png, 曲绘.png 도 가능)
```

곡 폴더는 `UserData\PLATiNALAB\song_ids.json`에 폴더명별로 영속 번호가 기록되어 `hwa_001`, `hwa_002` … 의 ID가 붙습니다(폴더를 추가·삭제해도 기존 곡 ID가 밀리지 않습니다).

### info.txt

`키 : 값` 또는 `키 = 값` 형식입니다. 한국어, 영어, 중국어를 모두 지원하며, 비워 둔 제목·아티스트·BPM·레벨은 같은 폴더 BMS 헤더(`#TITLE`, `#ARTIST`, `#BPM`, `#PLAYLEVEL`)에서 자동으로 채웁니다.

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
  - 난이도별 파일: 파일명에 난이도 이름(`song_hard.bms`, `song[HD].bms`, `曲_困难.bms` 등)이 든 BMS를 쓰고, 없으면 이름순 첫 BMS를 씁니다. 영문 이름·별칭(`ez`, `hd`, `ex`, `sp` 등)은 `_`, `[`, 공백, 숫자 등으로 나뉜 **낱말 전체**가 같아야 합니다(`next.bms`의 `ex`는 해당 없음). 정확한 난이도 이름이 별칭보다 우선합니다.
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

# 판정바·키뷰어 크기 배율 (기본 1, 0.5~4). 4K 등에서 작으면 2 정도로
OverlayScale=1

# 실시간 판정바 표시
EnableJudgmentBar=1
JudgmentBarVertical=1
JudgmentBarSide=Center

# 실시간 키뷰어 표시
EnableKeyViewer=1
KeyViewerPressedColor=#26BFD9D9

# 진단용 상세 로그 패치 (기본 0 = 꺼짐, 문제 조사 시에만 1)
DiagnosticLogs=0
```

## 기존 곡 차트 덮어쓰기

1. `UserData\MelonPreferences.cfg`의 `[PLATiNALAB]`에서 `DumpCharts = true`로 켭니다.
2. 곡을 플레이하면 `UserData\PLATiNALAB\ChartDump\{곡ID}__{패턴}.json`이 저장됩니다.
3. 수정한 파일을 `UserData\PLATiNALAB\CustomCharts\`에 같은 이름으로 넣으면 다음 플레이부터 교체됩니다.

## 빌드

.NET 6 SDK가 필요합니다:

```powershell
dotnet build mod\PLATiNALAB.Mod.csproj -c Release
```

게임이 다른 경로에 설치되어 있으면 `-p:GameRoot="C:\...\PLATiNA LAB"`로 덮어쓸 수 있습니다. 빌드가 끝나면 DLL이 `<GameRoot>\Mods\`로 자동 복사됩니다(게임이 실행 중이면 경고만 남기고 빌드는 계속됩니다).

### 배포 (유지보수자용)

`Core.cs`의 `MelonInfo` 버전과 `CHANGELOG.md`를 먼저 갱신한 뒤 커밋하고:

```powershell
.\tools\release.ps1            # 빌드 + 하네스 검증 + dist\ 에 zip 생성
.\tools\release.ps1 -Publish   # 위 + 태그(v<버전>)와 GitHub 릴리즈 게시 (gh 필요)
```

zip에는 모드 DLL, README, CHANGELOG만 들어가며 게임 파일이나 디컴파일 코드는 포함되지 않습니다.

## 게임이 업데이트됐을 때

모드는 특정 게임 빌드(Build ID `25981392`, `GameAssembly.dll` 96,829,440바이트)에서 검증했습니다. 업데이트 후에는 시작할 때 `MelonLoader\Latest.log`의 `[Health]` 줄이 상태를 알려 줍니다.

- `게임 빌드 ...와 다릅니다` / `GameAssembly.dll ...바이트와 다릅니다`: 게임이 바뀐 것입니다. 모드를 다시 빌드하세요(이름·시그니처 변경은 대부분 컴파일 오류로 드러납니다).
- `필수 패치 누락` 또는 `[HookGuard] ... 훅을 끕니다`: 해당 기능(또는 커스텀 곡 주입)이 자동으로 꺼지고 게임 원본 동작은 유지됩니다. 일반 곡 플레이는 영향이 없도록 설계했습니다.
- `[Init] ... 실패`: 해당 초기화 단계만 건너뛴 것입니다.

게임 없이 먼저 확인하려면 `tools/interop-harness`에서 `dotnet run -c Release`를 실행하세요 (종료 코드 0 = 통과. 훅 대상·JIT 바인딩·필드 레이아웃·리플렉션 폴백 이름·HookGuard 키까지 확인). 자세한 확인 순서는 [`docs/update_resilience_audit.md`](docs/update_resilience_audit.md)에 있습니다.

## 저장소 구성

| 경로 | 내용 |
| --- | --- |
| `mod/` | 모드 소스코드 |
| `tools/interop-harness/` | 게임 없이 인터롭 어셈블리·훅 대상을 검증하는 오프라인 도구 |
| `docs/update_resilience_audit.md` | 업데이트 내성 감사 기록과 게임 업데이트 후 확인 순서 |
| `docs/chart_format.md` | 차트(PPP JSON) 형식 및 BMS 변환 규칙 상세 |
| `docs/hooks.md` | 후킹 대상 메서드 명세서 |
| `AGENTS.md` | 개발 인수인계 문서 (설계 의도, 감사 기록, 실패 기록) |
