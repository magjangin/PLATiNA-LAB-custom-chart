# PLATiNA LAB Modding Handover & Project Context

## 1. 대상 게임 버전 & 환경 정보
- **게임명:** PLATiNA LAB (Steam App ID: `3430470`)
- **Steam Build ID:** `25981392`
- **Unity 버전:** `6000.3.25f1`
- **스크립팅 백엔드:** IL2CPP (metadata v39)
- **모드 로더:** MelonLoader 0.7.3 Open-Beta (`net6` 런타임)

---

## 2. 디컴파일 & 덤프 산출물 경로
- **Dummy DLL 원본:** `H:\source\repos\PLATiNA LAB-unity-il2cpp\dummy_dll`
- **MelonLoader Interop 어셈블리:** `H:\steam\steamapps\common\PLATiNA LAB\MelonLoader\Il2CppAssemblies`
- **`ilspycmd` 디컴파일 소스 코드:**
  - `H:\source\repos\PLATiNA LAB-unity-il2cpp\src_dummy\Main` (`Il2CppPlatinaLab.Main.dll`)
  - `H:\source\repos\PLATiNA LAB-unity-il2cpp\src_dummy\DataModel` (`Il2CppPlatinaLab.DataModel.dll`)
- **실행 로그:** `H:\source\repos\PLATiNA LAB-unity-il2cpp\logs\`
- **후킹 후보 명세서:** `H:\source\repos\PLATiNA LAB-unity-il2cpp\docs\hooks.md`
- **차트(PPP JSON) 형식 & 커스텀 차트 사용법:** `H:\source\repos\PLATiNA LAB-unity-il2cpp\docs\chart_format.md`

---

## 3. 설계 의도 및 Rationale
- **Unity 6 (Metadata v39) 호환성 대응:**
  - 기존 구버전 `Il2CppDumper` 및 독립 실행형 `Cpp2IL`은 Metadata version 39를 지원하지 못해 에러 발생.
  - MelonLoader 0.7.3은 자체 플러그인(`Cpp2IL.Plugin.StrippedCodeRegSupport.dll`)을 포함한 Cpp2IL v2022.1.0-pre-release.21을 통해 v39 메타데이터 및 Unity 6000.3.25f1을 완벽히 디코딩하고 Interop 어셈블리를 빌드함.
  - 따라서 MelonLoader가 생성한 `Il2CppAssemblies` 및 `cpp2il_out` DummyDll을 일차 참조 기준으로 삼아 `ilspycmd`로 정적 소스를 복원함.
- **커스텀 차트는 Stream 교체 방식 (2026-10-30 실기 검증):**
  - 차트는 Addressables(`StreamingAssets/aa`) 번들 안에 있어 파일 직접 교체 대신, `PatternParser.Parse(PatternPlayer, Stream)` Prefix에서 `ref Stream`을 `Il2CppSystem.IO.MemoryStream`으로 갈아끼운다. 스트림 내용은 평문 PPP JSON(System.Text.Json, camelCase).
  - 원본 스트림은 전부 읽어 `UserData\PLATiNALAB\ChartDump\{키}.json`으로 덤프(최초 1회) → 소비한 스트림 대신 같은 바이트로 새 MemoryStream을 넘긴다.
  - `UserData\PLATiNALAB\CustomCharts\{키}.json`이 있으면 그 바이트로 교체. 매 플레이마다 다시 읽어 핫리로드됨.
  - 키 = `{PatternInfo.GroupId}__{FilePath 파일명}`, 예 `wits_biz__FourLines_Easy`. 패턴 파일명만으로는 곡 간 충돌함(서로 다른 곡이 모두 `FourLines_Easy.ppp`). 커스텀 곡은 키 `custom_test`.
  - `Parse(Stream)` 오버로드도 패치하되, Player 오버로드 처리 중엔 `[ThreadStatic] Handling` 플래그로 중복 처리 방지. 실측상 게임은 Player 오버로드를 호출함.
  - 검증: FourLines_Easy 덤프 성공 → 레인 좌우반전(1↔4, 2↔3) 차트로 교체 시 로그 `커스텀 차트 적용` 후 `SetPatternDataToPlay` 정상 호출(파서 예외 없음).
- **커스텀 카테고리 + 커스텀 곡 주입 (2026-10-30 실기 검증: "Custom Test" 1노트 플레이 → PERFECT DECODE):** `mod/CustomSongs.cs`, `mod/OfflineSession.cs`, `mod/AddressableKeyRedirect.cs`
  - 카테고리: `CategoryCacheFactory.CreateAll` Postfix에서 `new TagCategoryCache("custom","custom")` 추가 → `SonglistCategorizationTags`에 `custom`이 있는 곡만 표시. 라벨 로컬라이즈 키는 없음(빈 칸으로 보임).
  - 곡: `SongListBuilder.BuildNameBanners` Prefix(`Build`는 안 불림, 실측)에서 기반 곡의 `SongInfo`/`PatternInfo`를 복사 생성자로 복제 → `SongDataManager.SongInfoCollection`/`PatternRawData`에 추가. `_songMetaData`에 `SongMetaDataInfo`(Title="Custom Test") 추가.
    - 기반 곡 자동 선택: FilePath가 `FourLines_Easy`이고 `LockTypeString=="none"`, `ESDProduct=="default"`인 첫 곡 (현재 `wits_biz`). 튜토리얼(`tutorial_kr`, lock=`unlisted`)은 BGA/타임이벤트에 튜토리얼 안내가 섞여 제외.
    - 복제 곡 `LockTypeString="none"` 고정 (lockType 실측 분포: none=180, unlisted=5, anonymous=6, lock=4, hidden=2; unlisted면 카테고리에서 걸러짐).
    - `PatternIndex=990001`(서버에 없는 값) → 원곡 기록/랭킹 오염 방지. `PatternInfo.GroupId`=곡 ID로 `FindSongInfo` 연결됨.
    - `SongInfo.IsHiddenFromDefaultList/IsAvailable/IsNotOwned/IsLock/IsNewSongMarkTarget`은 곡 ID 사전 조회로 KeyNotFound → 커스텀 ID일 때 Prefix로 고정값 반환. (`EncryptedBool`은 `Il2CppPlatinaLab.Common.EncryptedPrimitives`, bool 암시 변환 있음)
  - 서버: `InGameSurface.RequestGameStart(990001)`은 "서버와의 통신 과정에서 오류" → Prefix에서 `new UniTask<SurfaceResult<GameSessionResult>>(SurfaceResult<..>.Success(토큰))` 반환. `RenewGameSession`(로컬 토큰), `SendGameEnd`(커스텀 패턴)도 서버 호출 생략. `SendGameEnd`는 현재 `NetworkError()` 반환 — 결과 화면 이후 동작은 미확인.
  - 리소스 주소: `PlayDirector.LoadGame`이 `Concat("Data/Patterns/", SongInfo.Id, "/", GetPatternFileName())`로 Addressables 로드 (GameAssembly 디스어셈블로 확인). 실측 키: `Data/Patterns/{id}/FourLines_Easy.ppp`, `Data/Patterns/{id}/{bgm}.fmod.ogg`, `Data/Patterns/{id}/bga.mp4`, `Music/Preview/{id}.fmod.ogg`. → Addressables `Locate(object key, Type, out IList)` 구현체 3종(`DynamicResourceLocator`, `ResourceLocationMap`, `ContentCatalogData+ResourceLocator`) Prefix에서 키 속 `custom_test`를 기반 곡 ID로 치환.
  - 첫 노트 차트: `CustomCharts/custom_test.json`이 없으면 기반 차트에서 startPosition 최소 노트 1개만 남기고 `timeTriggeredEvents`를 비워 자동 생성.
  - 클리어 연출 타이밍: `PatternPlayer.DetermineRunning()`(RVA 0x2CF5010)이 마지막 노트 처리 시 `_isLastNoteProcessingTriggered`(+0x60)를 세우고 `EventQueue`(+0xE0)에 3=PERFECT/4=FULL COMBO/5=CLEAR 이벤트를 넣음 → 1노트 차트는 첫 노트 직후 PERFECT DECODE. `DetermineRunning` Prefix/Postfix로 `DurationMicroSec - 3s`까지 플래그를 임시 true로 세워 보류 (2026-10-30 검증: 곡/BGA 끝에서 연출 → 결과 화면).
  - 결과 화면: `SendGameEnd` Prefix에서 로컬 `FreeModePlayRecordV2(request, snapshot, EndGameResponse{PlayResult, 빈 배열들})` 반환. 스냅샷은 `GameEndResultSnapShot.Record*`로 채움 (`RecordUpdateHelper.UpdatePatternRecordAndBuildSnapshot`은 세이브에 990001 기록을 남기므로 미사용). 결과 화면 정상 표시 확인. 단 `SectionDetailPanelController.Initialize` NRE(Player.log)가 남아 섹션 상세 패널은 비어 있을 수 있음.
  - 카테고리 라벨: 카테고리 UI(탭, 선곡 화면 상단 라벨 `SelelectedCategoryBannerBox/SmallLabelParant/SmallLabel` 등)는 `SongCategoryResourceCollection.GetDataSet(key)`의 `SongCategoryResourceDataSet`으로 그려진다. custom은 세트가 없어 흰 판이 나왔음. → `GetDataSet` Postfix에서 `ScriptableObject.CreateInstance`로 세트를 만들어 반환: 배경/색은 `all` 세트에서 차용, 이름 칸(`_smallLabel` 등)은 `CategoryResource.RenderType.Text` + `_displayedName="CUSTOM"`. 게임 자체 경로로 파란 탭 + "CUSTOM" 표시 확인 (2026-10-30).
  - 디버깅: 결과 씬 등 게임 쪽 예외는 MelonLoader 로그가 아니라 `%USERPROFILE%\AppData\LocalLow\HIGHEND GAMES\PLATiNA  LAB\Player.log`(공백 2칸)에 남는다. MelonLoader의 `Preferences Saved!`는 게임 종료 신호가 아님(플레이 중에도 찍힘).
- **hwa\ 폴더 기반 커스텀 곡 (2026-10-30 실기 검증: 커버/미리듣기/인게임 BGM/BGA 모두 교체 확인)** — `mod/CustomSongs.cs`, `mod/CustomMedia.cs`
  - 위치: `H:\steam\steamapps\common\PLATiNA LAB\hwa\<곡폴더>\` (모드 시작 시 생성, `CustomSongs.Scan()`이 폴더명 순으로 읽음). 곡 ID `hwa_001`…, PatternIndex `990000 + 곡번호*10 + 난이도순번`.
  - `info.txt`: `키 : 값` 또는 `키 = 값`. 키 `곡 제목/title`, `아티스트/artist`, `bpm`(없으면 폴더 `.bms`의 `#BPM`), `난이도 : easy, hard, ...`, `<난이도> = <레벨>`, `패턴제작자/patterner`.
  - 난이도는 게임 `_difficultyTypeInfos` 키여야 함: 실측 `easy, hard, over, plus, testpattern`. 없는 이름(예: `normal`)은 선곡 로딩 중 `DifficultyTypeInfo is not available` 예외로 ACCESSING 화면에서 멈춤 → 남는 유효 키로 자동 대체(경고 로그).
  - 차트: `chart_<난이도>.json` → `chart.json` 순. 없으면 기반 차트 첫 노트 템플릿을 `chart_<난이도>.json`으로 생성. 적용 시(`CustomSongs.PrepareChart`) `bgms[].sound`를 기반 차트 BGM 파일명으로 바꿔 Addressables 로딩을 통과시킴(실제 음원은 아래에서 교체).
  - 곡 길이: 게임은 `endPosition`(→ `DurationMicroSec`)에서 결과 화면으로 넘어감. 템플릿은 기반 곡 값(86.5마디)이라 BGM/BGA 중간에 끝났음 → `MediaDuration`이 music.ogg(마지막 Ogg 페이지 granule / vorbis 샘플레이트)와 video.mp4(`moov/mvhd` duration/timescale) 길이를 읽어 `endPosition = ceil(bgm위치 + (max길이+1초) × initialBpm / 240)`까지 늘림(짧아지지는 않음). 검증: どりーむもーど 145.3s → 97마디(149.2s), 클리어 연출 146.2s, 곡 끝까지 재생 후 결과 화면.
  - 커버(`썸네일.png`/`thumbnail.png`/`cover.png`/`jacket.png`): `CoverManager.GetCoverSprite` Postfix → Texture2D+Sprite.Create.
  - 음원(`music.ogg`): 게임 음원은 `HEAudioClip`(ScriptableObject, `_bytes` + `_audioFormat` Wav/Ogg)이고 `SoundData.FromHEAudioClip(clip, isLooping, afterDispose)`로 재생 객체가 됨. 이 메서드 Prefix에서 클립 이름에 기반 곡 ID가 포함되면(미리듣기 `wits_biz.fmod`, 인게임 `wits_biz_156bpm.fmod`) music.ogg 바이트로 만든 HEAudioClip으로 교체. 어떤 곡 소리인지는 `CustomSongs.Current`(RequestGameStart에서 설정, `PreviewSoundController.Play`에서 해제) / `CustomMedia.PreviewSong`(Play의 songId)으로 판단.
  - BGA(`video.mp4`): 게임은 Addressables `VideoClip`을 `BGAPlayer.SetClip`으로 넘김 → Postfix에서 `_videoPlayer.source = Url` + url = file URI + `Prepare()`.
  - **Unity 6 인터롭 주의:** `ImageConversion.LoadImage`(모든 오버로드), `VideoPlayer.url` 세터 등 문자열/바이트를 `Il2CppSystem.ReadOnlySpan`으로 넘기는 래퍼는 `MissingMethodException: ReadOnlySpan.GetPinnableReference` → 래퍼 본문을 재현해 `MarshalledUnityObject.MarshalNotNull` + `ManagedSpanWrapper(fixed ptr, len)` + `*_Injected` icall 직접 호출 (`CustomMedia.LoadImage`, `CustomMedia.SetUrl`). csproj에 `AllowUnsafeBlocks`.
- **BMS → PPP 변환 (2026-10-31 실기 검증: どりーむもーど hwa2.bms → 단노트 552, 4레인 플레이·판정 정상)** — `mod/BmsConverter.cs`
  - 곡 폴더에 `.bms/.bme/.bml/.pms`가 있으면 `CustomCharts.Process`가 JSON보다 우선해 매 플레이마다 변환(핫리로드). 난이도별: 파일명에 난이도 이름이 든 BMS → 없으면 이름순 첫 BMS.
  - 위치 = BMS 마디 + 1 + tick/마디 tick 수 (time = (position-1) × 240 / bpm). BGM 시작 = `music.ogg`를 가리키는 오브젝트 위치(이 곡은 채널 21, 000마디).
  - 오브젝트 ID 폭은 `#WAV` 키 길이로 판단 (이 곡은 3자리 `#WAV001`, 데이터도 3자리씩).
  - 노트 종류 = `#WAV` 파일 이름: `ShortNote.wav` 단노트, 같은 채널 `LongNote 홀드 시작.wav`~`LongNote 홀드 끝.wav` 롱노트(ticks 1/16마디), `PitchNote.wav` 피치. 표준 LNTYPE 1(5x/6x)·LNOBJ도 지원.
  - **WAV 규칙:** 곡 폴더 키음은 위 4개로 통일하고 원래 키음 이름을 종류별로 묶어 `#WAV`를 재연결(hihat/kick/snare → ShortNote, `X 홀드 시작/끝` → LongNote, drop → PitchNote). 오브젝트 번호 패턴으로 종류를 추측하지 말 것 — 002/003이 짝지어 나온다고 홀드로 오인했다가 정정함.
  - **PitchNote = PPP `key: 255` 단노트:** `PatternParser.GetNotes`(RVA 0x2D0C180)가 key를 그대로 `NoteEntity.HorizontalIndex`(+0x20)에 넣고, `PatternPlayer.PopulateNotes`(RVA 0x2CF72F0)가 `HorizontalIndex == 0xFF`면 `PitchNote`, 아니면 `ShortNote(time, HorizontalIndex)` 생성. Grouped(롱노트)는 피치 불가. 인게임 피치 판정은 아직 미확인(이 곡엔 피치 노트 없음).
  - 레인: info.txt `lanes = 16,11,12,13`, 없으면 BMSE 화면 순서(16,11,12,13,14,15,18,19) 앞 4개. 이 곡은 1=16/2=11/3=12/4=13으로 플레이 확인.
- **Nullable Context 해제:**
  - `Il2Cppmscorlib` 참조 시 `NullableContextAttribute` 관련 충돌(CS0656)을 방지하기 위해 `.csproj`에서 `<Nullable>disable</Nullable>`로 설정함.

---

## 4. 핵심 후킹 타깃 클래스 & 메서드 시그니처
1. **차트 파싱 및 패턴 주입:**
   - `Il2CppPlatinaLab.Main.InGame.Pattern.PatternParser.Parse(PatternPlayer, Stream)`
   - `Il2CppPlatinaLab.Main.InGame.Pattern.PatternParser.Parse(PPPScheme)`
   - `Il2CppPlatinaLab.Main.InGame.PatternPlayer.SetPatternDataToPlay(PatternData)`: 파싱 완료 후 호출 확인용 (현재 로그만).
   - **실제 주입 지점:** `PatternParser.Parse(PatternPlayer, Il2CppSystem.IO.Stream)` Prefix (`mod/CustomCharts.cs`).
   - 현재 패턴 정보: `PatternPlayer.PlayPattern` (`PatternInfo`: `FilePath`, `Difficulty`, `FindSongInfo().Id`).
   - `Il2CppPlatinaLab.Main.InGame.PatternPlayer.SetMainTrackToPlay(SoundData)`: 커스텀 BGM 주입 타깃.
   - `Il2CppPlatinaLab.Main.InGame.PatternPlayer.EvaluateVideo(int, int)`: 커스텀 MP4/BGA 주입 및 동기화 타깃.
2. **판정 및 All-Perfect 조작:**
   - `Il2CppPlatinaLab.Main.InGame.PatternPlayer.ProcessJudgeResult(JudgeResult)`: 판정 결과 인터셉트 (`JudgmentType.PerfectHigh` 주입 타깃).
   - `Il2CppPlatinaLab.Main.InGame.PatternPlayer.ProcessTickResult(JudgmentType)`
3. **선곡 및 곡 해금 우회:**
   - `Il2CppPlatinaLab.Main.Director.SongSelectDirector.IsLock()`: 잠금 해제 우회 (`false` 반환).
   - `Il2CppPlatinaLab.Main.Director.SongSelectDirector.IsNotOwned()`: 미보유 곡 제한 우회 (`false` 반환).

---

## 5. 시도했다가 실패한 것들 (Dead Ends / Failed Attempts)
- **독립 Il2CppDumper 실행:** `System.NotSupportedException: ERROR: Metadata file supplied is not a supported version[39]` 발생 (Unity 6 메타데이터 미지원).
- **독립 Cpp2IL 실행:** `System.FormatException: Unsupported metadata version found! We support 24-29, got 39` 발생 (Stripped CodeReg 플러그인이 없는 구버전 Cpp2IL이었음).
- **게임 폴더에서 차트 파일 직접 찾기:** `PLATiNA LAB_Data`에 `.ppp`/평문 차트 파일 없음 (Addressables 번들 내부) → 런타임 Stream 후킹으로 전환.
- **`SongListBuilder.Build` Prefix:** 호출 안 됨 → `BuildNameBanners` Prefix로 이동.
- **클리어 연출 보류를 `PatternPlayer.Update`에 걸기:** `DetermineRunning`이 입력 처리(HFTask) 스레드에서도 불려 Update 범위 플래그로는 안 막힘 → `DetermineRunning` 직접 패치.
- **`EffectManager.PlayPerfect/PlayFullCombo` 직접 호출부 탐색:** E8 call xref 없음(이벤트 큐 경유) → `DetermineRunning`의 플래그 쓰기(`mov byte [rdi+0x60],1`)를 역추적해 찾음.
- **라벨 UI 직접 조작 (폐기):** `SongCategoryView._categoryImage` 숨기기 → 하얀 사각형은 `SmallLabel`이었음. `SmallLabel` Image 끄기 + TMP 복제 + 매 프레임 강제 → 글자는 나왔지만 흰 판(배경)이 남음. 또 비-custom 카테고리에서 Image를 `enabled=true`로 되돌리자 PLATINA 등에 없던 흰 칸이 생김. 근본 원인은 리소스 세트 부재 → `GetDataSet` 패치로 대체.
- **`PlayDirector.LoadGame` Prefix로 SongInfo 교체:** IL2CPP가 `LoadGame` 스텁을 `LoadTargetPattern` MoveNext에 인라인(MoveNext 0x2D8A580 직접 호출) → 훅 미발동. Addressables `Locate` 키 치환으로 대체.
- **`PlayTarget`/`PlaySingleEnterContext` 수정:** 둘 다 값 타입(struct)이라 Prefix 수정이 원본에 반영 안 될 가능성 → 시도하지 않음.
- **다른 곡의 lock 속성 차용:** `BuildNameBanners` 시점엔 보유/잠금 판정 사전이 비어 모든 곡에서 KeyNotFound → `"none"` 하드코딩.
- **인터롭 어셈블리에서 `GetInterfaces()`로 IResourceLocator 구현체 탐색:** Il2CppInterop은 IL2CPP 인터페이스를 C# 인터페이스로 남기지 않음 → 메서드 이름 `Locate`(인자 3개)로 탐색. `Unity.Addressables`는 지연 로드라 `Assembly.Load` 선행 필요.
- **음원 교체 지점 실패들:** `PreviewSoundController.Method_Private_UniTask_1_SoundData_String_CancellationToken_PDM_0` Prefix·`PatternPlayer.SetMainTrackToPlay` Prefix → 둘 다 발동 안 함. `SoundData` 생성자 Prefix → `Failed to init IL2CPP patch backend for SoundData::.ctor` 경고(관리 코드 쪽만 패치됨) → 정적 `FromHEAudioClip(HEAudioClip,bool,Action)`로 이동해 성공. `HEAudioClip.name` 세터는 문자열 Span 경로라 쓰지 않음.
- **LINQ 람다 일부에서 CS0656(NullableAttribute) 빌드 오류:** `All.SelectMany(...).FirstOrDefault(...)` 등 → foreach로 교체.
- **게임 exe 직접 실행:** `PlatformInitializationFailedException: Failed to initialize steam sdk` 후 종료 → `steam://run/3430470//-screen-fullscreen 0 -screen-width 1600 -screen-height 900/`로 Steam 경유 실행.
- **모드 프로젝트 Nullable enable 빌드:** `CS0656: System.Runtime.CompilerServices.NullableContextAttribute..ctor` 컴파일 오류 발생 → `<Nullable>disable</Nullable>`로 전환하여 해결.

---

## 6. 빌드 명령 및 Mods 배포 경로
- **모드 프로젝트 경로:** `H:\source\repos\PLATiNA LAB-unity-il2cpp\mod`
- **빌드 명령:**
  ```powershell
  & "C:\Program Files\dotnet\dotnet.EXE" build "H:\source\repos\PLATiNA LAB-unity-il2cpp\mod\PLATiNALAB.Mod.csproj"
  ```
- **배포 경로:** 빌드 시 `PostBuild` 이벤트에 의해 `H:\steam\steamapps\common\PLATiNA LAB\Mods\PLATiNALAB.Mod.dll`로 자동 복사됨.
- **실기 검증 완료:** MelonLoader `Latest.log`를 통해 `PLATiNA LAB Custom Mod v1.0.0` 정상 로드 및 초기화 확인 완료.
- **커스텀 차트 폴더:** `H:\steam\steamapps\common\PLATiNA LAB\UserData\PLATiNALAB\CustomCharts` (기존 곡 차트 덮어쓰기, 있을 때만 사용).
- **원본 차트 덤프:** 형식 조사가 끝나 기본 꺼짐. `UserData\MelonPreferences.cfg`의 `[PLATiNALAB]` `DumpCharts = true`로 켜면 `UserData\PLATiNALAB\ChartDump`에 저장.
- **GitHub:** https://github.com/magjangin/PLATiNA-LAB-custom-chart (public). `dummy_dll/`, `src_dummy/`, `slices/`(게임 추출물), `logs/`, `.reatool/`, `mod/bin|obj`는 `.gitignore`로 제외 — 공개 저장소에 게임 바이너리/디컴파일 코드를 올리지 않는다.

## 7. 다음 단계 (미구현)
- BMS 박자 변경(#mmm02)·STOP·PitchNote 인게임 판정 확인. 박자 변경은 PPP `bars`의 비 4/4 마디 position 소수부 의미 미확인 → 변박 원곡 덤프로 확인 후 구현.
- 커스텀 BGM: `bgms[].sound`(예 `lolikami_165bpm.fmod.ogg`)는 FMOD 사운드 이름 → `SetMainTrackToPlay(SoundData)` 후킹으로 외부 ogg 주입 필요.
- BPM 변속이 있는 차트의 길이 계산 (현재 initialBpm 기준 근사, 경고 로그).
- 결과 화면 `SectionDetailPanelController.Initialize` NRE 해소 (섹션 기록/캐시 쪽 추정).
- 결과 score가 로그상 0 (`GameEndResult.FinalScore`) — 화면 표시와 일치 여부 확인.
- 정적 분석 도구: `scratchpad/calls.py` 방식 — `dummy_dll`을 ilspycmd로 풀어 `[Address(RVA=..)]` 맵을 만들고 capstone으로 GameAssembly.dll 함수의 call 대상을 이름 붙여 출력.
