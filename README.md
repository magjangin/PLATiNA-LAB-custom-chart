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

## 환경

| 항목 | 버전 |
| --- | --- |
| 게임 | PLATiNA LAB (Steam App ID `3430470`, Build ID `25981392`) |
| 엔진 | Unity `6000.3.25f1`, IL2CPP |
| 모드 로더 | MelonLoader `0.7.3` (net6) |

게임이 업데이트되면 동작하지 않을 수 있습니다.

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
      ├─ music.ogg         BGM / 미리듣기
      ├─ video.mp4         BGA
      └─ cover.png         커버 (thumbnail.png, jacket.png, 썸네일.png 도 가능)
```

곡 폴더는 이름순으로 읽혀 `hwa_001`, `hwa_002` … 의 ID가 붙습니다.

### info.txt

`키 : 값` 또는 `키 = 값` 형식입니다.

```
title  : My Song
artist : someone
bpm    : 150
난이도 : easy, hard
easy   = 3
hard   = 9
patterner : me
```

| 키 | 설명 |
| --- | --- |
| `title` / `곡 제목` | 곡 제목 |
| `artist` / `아티스트` | 아티스트 |
| `bpm` | BPM. 없으면 폴더 안 `.bms`의 `#BPM`을 사용 |
| `난이도` | 쉼표로 구분한 난이도 목록 |
| `<난이도>` | 해당 난이도의 레벨 |
| `patterner` / `패턴제작자` | 패턴 제작자 |

사용 가능한 난이도 이름은 `easy`, `hard`, `over`, `plus`, `testpattern` 입니다. 그 밖의 이름(예: `normal`)은 남는 유효 이름으로 자동 대체되고 로그에 경고가 남습니다.

### 차트

차트는 게임의 PPP 형식 JSON입니다. `chart_<난이도>.json` → `chart.json` 순서로 찾고, 둘 다 없으면 노트 1개짜리 템플릿을 `chart_<난이도>.json`으로 만들어 줍니다. 그 파일을 고쳐 쓰면 됩니다.

형식과 위치(마디) 단위는 [docs/chart_format.md](docs/chart_format.md)를 참고하세요.

## 기존 곡 차트 덮어쓰기

1. `UserData\MelonPreferences.cfg`의 `[PLATiNALAB]`에서 `DumpCharts = true`로 켭니다.
2. 곡을 플레이하면 `UserData\PLATiNALAB\ChartDump\{곡ID}__{패턴}.json`이 저장됩니다.
3. 수정한 파일을 `UserData\PLATiNALAB\CustomCharts\`에 같은 이름으로 넣으면 다음 플레이부터 교체됩니다. 파일을 지우면 원래 차트로 돌아갑니다.

## 빌드

.NET 6 SDK가 필요합니다. `mod\PLATiNALAB.Mod.csproj`의 `GameRoot`를 자기 게임 경로로 바꾼 뒤:

```powershell
dotnet build mod\PLATiNALAB.Mod.csproj
```

빌드가 끝나면 DLL이 `<GameRoot>\Mods\`로 자동 복사됩니다. 참조 어셈블리는 MelonLoader가 생성한 `MelonLoader\Il2CppAssemblies`를 사용합니다.

## 저장소 구성

| 경로 | 내용 |
| --- | --- |
| `mod/` | 모드 소스 (`CustomSongs`, `CustomCharts`, `CustomMedia`, `OfflineSession`, `AddressableKeyRedirect` 등) |
| `docs/chart_format.md` | 차트(PPP JSON) 형식과 커스텀 차트 사용법 |
| `docs/hooks.md` | 후킹 대상 메서드 정리 |
| `AGENTS.md` | 개발 인수인계 문서 (설계 의도, 실패한 시도, 검증 기록) |

## 알려진 한계 / 예정

- BMS → 차트 변환기는 뼈대만 있고 아직 연결되지 않았습니다 (`mod/BmsConverter.cs`).
- BPM 변속이 있는 차트는 곡 길이 계산이 근사치입니다.
- 결과 화면의 섹션 상세 패널이 비어 있을 수 있습니다.
- `CUSTOM` 카테고리 이름은 로컬라이즈되지 않습니다.
