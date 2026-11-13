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
- **오프라인 인터롭 하네스:** `H:\source\repos\PLATiNA LAB-unity-il2cpp\tools\interop-harness` (`dotnet run -c Release`, 람다 판별·Locate 후보를 게임 없이 검증)
- **후킹 후보 명세서:** `H:\source\repos\PLATiNA LAB-unity-il2cpp\docs\hooks.md`
- **차트(PPP JSON) 형식 & 커스텀 차트 사용법:** `H:\source\repos\PLATiNA LAB-unity-il2cpp\docs\chart_format.md`
- **업데이트 내성 감사 & 게임 업데이트 후 확인 순서:** `H:\source\repos\PLATiNA LAB-unity-il2cpp\docs\update_resilience_audit.md`

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
- **sxtg2 기능 포팅: 설정 파일 / 오토 플레이 / 올 퍼펙트 / 판정바 / 키뷰어 (2026-10-31)** — `mod/ModConfig.cs`, `mod/PlayAssist.cs`, `mod/PlayOverlay.cs`. 원본은 `H:\source\repos\sxtg2\sxtg2-mod` (Mono, IMGUI).
  - 설정: `<게임>\SaveCustomKey\config.txt` (sxtg2와 같은 위치·형식, 없으면 주석 포함 기본 파일 생성). `PlayDirector.Awake` Prefix마다 다시 읽음 → 다음 곡부터 반영.
  - 오토 플레이: 게임 내장 모드(`InGameSettingType.Extra.AutoPlay`). `InGameSettingHelper.IsAutoPlayMode()`(RVA 0x2CD2680)는 `Instance.Extra.Extra == 1` 비교인데 직접 호출이 없어(인라인) 패치 불가 → `PlayDirector.Awake`에서 `ExtraScheme.SetExtra(AutoPlay)`, `OnDestroy`에서 원래 값 복원. `InGameSettingManager.Save` 앞뒤로 원래 값을 잠시 되돌려 PlayerPrefs에 저장되지 않게 함. `IsScorable()`은 Extra ∈ {AutoPlay, NoDeath, PartyTime}이면 false → 게임이 스스로 기록 안 남김. **인게임 미확인.**
  - 올 퍼펙트: `PatternPlayer.ProcessJudgeResult(JudgeResult)` Prefix에서 `judgeResult.Judgment = PerfectHigh`(JudgeResult는 인터롭에서 박싱 래퍼라 세터가 원본 호출에 반영되는 것으로 예상), `ProcessTickResult(ref JudgmentType)`도 PerfectHigh. 서버 랭킹 게임이라 **커스텀 곡(`CustomSongs.Current != null`)에서만** 동작. 2026-11-08 실기 확인: 결과 화면 PERFECT HIGH 552/552, 점수 반영.
  - 판정바: 같은 Prefix에서 `JudgeResult.Error`(ms, double; `TimingOffsetMilliseconds`는 같은 값의 정수) 수집. 게임은 error < 0이 FAST(`UpdateFastSlow` → `ScoreRecorderUtility.AddRecord(isFast: 0 > error)`) → 화면은 부호 반전(+ = FAST). 범위는 `Constants.JudgmentTimingMap` 실측 PerfectHigh ±25 / Perfect ±50 / Great ±80 / Good ±120 ms. 2026-10-31 실기 확인(로그 error=-7.1/23.2/51.1 등, 화면 왼쪽 세로 바 표시).
  - 키뷰어: `PatternPlayer.ProcessInputEvent(InputEvent{double Timestamp, ushort KeyCode, bool IsPressed})` Prefix. 키코드 → 레인은 `InputStateMachine.GetKeyBindingArray(LineMode)` 순서 + `GetPitchKey(LineMode)`(피치 칸). 키코드 = RawKeycode(상위 바이트 0xE0 = 확장키, 하위 = 스캔코드) → `user32!GetKeyNameTextW`로 라벨. 실측 FourLines: A S ; ' + 피치 Enter (0x1E 0x1F 0x27 0x28, 피치 0x1C). 2026-10-31 실기 확인.
  - 그리기: 화면 오버레이 Canvas(sortingOrder 30000) + `UnityEngine.UI.Image` + `Il2CppTMPro.TextMeshProUGUI`, `Core.OnUpdate`에서 갱신. 판정·입력 훅은 게임 루프 스레드(HFTask)라 lock으로 넘김. 반복 경고는 `WarnOnce`.
- **Nullable Context 해제:**
  - `Il2Cppmscorlib` 참조 시 `NullableContextAttribute` 관련 충돌(CS0656)을 방지하기 위해 `.csproj`에서 `<Nullable>disable</Nullable>`로 설정함.
- **성능 최적화, 안정성 및 버그 수정 내역 (2026-10-31):**
  - **OGG 파싱 최적화 (`MediaDuration.Ogg`):** 대용량 음원 파일 전체를 읽지 않고 헤더(앞 8KB)와 마지막 OggS 페이지(끝 64KB)만 선별 탐색하여 파일 I/O 병목 완전 해소.
  - **BMS & 차트 핫리로드 캐시 (`CustomCharts`):** 파일 `LastWriteTimeUtc`를 비교하여 변경 시에만 재변환/재로드 수행, 불필요한 반복 디스크 I/O 제거로 로딩 딜레이 0ms 유지.
  - **오버레이 렌더링 GC Allocation 제거 (`PlayOverlay`):** 매 프레임 `bool[]` Clone 대신 재사용 버퍼(`s_pressedBuffer`) 적용. 폰트 검색 3초 쿨다운 적용. 모양 스프라이트 캐시 초과 시 GPU 메모리 누수 방지(`Destroy`).
  - **결과 화면 진입 시 NRE 방어 (`OfflineSession` / `CustomSongs` / `CustomMedia`):**
    - 로컬 세션 결과 생성 시 필수 필드(`session.Token`, `response.PlayResult` 등) 완벽 보장.
    - `request.SectionRecords`가 null일 경우 빈 배열 할당으로 `SectionDetailPanelController.Initialize` NRE 방지.
    - 최종 점수 계산 시 `request.Score`와 `request.FinalScore` 중 최댓값 선택.
    - `CustomSongs`의 폰트/오브젝트 및 `CustomMedia`의 icall 호출 전 `WasCollected`/null 방어 검사.
- **중국어 지원 및 부실 부분 강화 감사 (2026-11-07):**
  - **다국어 인코딩 자동 감지 (`EncodingHelper`):** UTF-8 BOM/Strict 검사 후 실패 시 중국어 GBK(CP936)/Big5, 일본어 Shift-JIS(CP932), 한국어 CP949를 자동 감지하여 BMS/메타 파일 글자 깨짐 및 파싱 실패 원천 차단.
  - **중국어 메타 및 파일명 지원 (`CustomSongs`, `BmsConverter`):** info.txt 중국어 키워드(歌名/曲师/谱师/难度/轨道 등) 파싱, 封面/曲绘/音乐 등 파일명 확장, 롱노트/피치노트 중국어 키워드(长条开始/结束, 滑音, 音高) 인식.
  - **설정 파일 중국어 지원 (`ModConfig`):** config.txt에서 开/关, 开启/关闭, 是/否 및 중국어 색상명(红, 蓝, 绿, 黄, 白, 黑, 橙, 紫 등) 파싱 지원.
  - **로케일 폰트 자동 연동 (`PlayOverlay.FindFont`):** 게임 내 LocalizeTMPFont 컴포넌트의 폰트를 우선 탐색하여 중국어/CJK 글리프 Tofu(사각형 깨짐) 방지.
  - **강제 나가기 상태 격리 (`OfflineSession.cs` `PlayDirector_Exit_Patch`, 예전 이름 `Diag_Exit`):** 곡 플레이 도중 나가기(Exit) 시 오토플레이 복원, 오버레이 비활성화, 커스텀 세션 플래그를 완벽 초기화하여 상태 잔류 방지.
- **메모리 누수(ogg 제외) 감사 및 영문 축약형 정규화 보강 (2026-11-07):**
  - **커버 텍스처 LRU 캐시 및 VRAM 해제 (`CustomMedia.GetCover`):** 최대 24개 초과 시 가장 오래된 Sprite 및 Texture2D를 `UnityEngine.Object.Destroy`로 명시적 파괴하여 VRAM 누수 원천 차단.
  - **IL2CPP MemoryStream 명시적 해제 (`CustomCharts.ReadAll`):** try-finally 블록에서 `ms.Dispose()` 호출로 네이티브 힙 누수 방지, 차트 핫리로드 캐시 엔트리 상한(32개) 제어.
  - **UI 오브젝트 상한 풀링 (`PlayOverlay.UpdateJudgmentBar`):** 판정 틱 Image 오브젝트를 최대 32개로 상한 제한하여 무한 생성 방지.
  - **영문 축약형/공백 자동 교정 (`TextSanitizer`):** 영문 텍스트 내 비정상적 아포스트로피 띄어쓰기(`you'  re` → `you're`, `don ' t` → `don't`), 특수 따옴표 및 연속 공백을 정규화하여 info.txt 및 BMS 타이틀에 자동 적용.
  - **공개 문서화 최신화 (`README.md`):** 판정바/키뷰어, 오토플레이/올퍼펙트, 중국어/다국어 인코딩, 설정 가이드 전면 반영.
- **업데이트 내성 및 이중 래퍼(Double-Wrapper) 강화 (2026-11-07, 2026-11-08 감사로 범위 정정):** 재빌드 없이 게임 업데이트를 넘기는 것까지 보장하지는 않는다. 같은 메서드의 `try`는 그 메서드가 직접 참조한 멤버의 누락을 잡지 못하므로(JIT 단계 실패, .NET 6 실측) 폴백의 직접 참조는 `[MethodImpl(NoInlining)]` 별도 메서드로 분리한다. 상세는 `docs/update_resilience_audit.md`.
  - **상태 점검 (`ModHealth`, 2026-11-08):** 시작 시 Steam buildid를 기준과 비교해 로그, 필수 패치(서버 3종, Parse, 곡 목록·SongInfo 가드, Locate) 누락 시 커스텀 곡 주입 중단, 커스텀 곡 플레이마다 핵심 훅 호출 여부를 씬 종료 시 로그. 2026-11-08 실기 확인: 필수 패치 10종 + Locate 3건, 커스텀 곡 플레이 후 `[Health] hwa_001 핵심 훅 호출 정상`.
  - **결과 화면 정확도/P.A.T.C.H. (`OfflineSession.ComputeGrade`, 2026-11-08 실기 확인):** 서버가 주던 값이 없어 0% / 랭크 C로 나왔음 → `AccuracyV2Formula.GetAccuracyV2(request.Judge, request.WasPaused).Total`과 `MasteryV2Formula.CalcFromPatternInfo(patternInfo, accuracy)`로 계산해 스냅샷과 `PlayResult.NormalizedAccuracy`/`MasteryNormalized`에 넣는다. 올 퍼펙트 552노트 레벨 8: accuracy=1009999(100.9999%, 게임의 `Accuracy.MaxAccuracyScaled`), mastery=25669(256.69), 랭크 SS+, score 1656. 플레이어 전체 P.A.T.C.H.는 0 → 0 유지.
  - **오토 플레이 안전장치 (`PlayAssist.ApplyAutoPlay`, 2026-11-08):** `Extra.AutoPlay`를 런타임에 이름으로 찾고, 적용 후 `IsScorable()`이 true거나 확인 불가면 원래 값으로 복원. 인게임 미확인.
  - **백킹 필드 & 프로퍼티 동적 래퍼 (`UpdateResilience.SafeSetFieldOrProperty`):** 프로퍼티 setter 및 다양한 백킹 필드 형식(_k__BackingField, m_, _ 등)을 순차 탐색해 주입. 후보 이름 밖으로 바뀌면 `false`를 반환하고, 호출부 `SetSongField`/`SetPatternField`는 별도 메서드의 직접 필드 대입으로 넘어간다.
  - **네이티브 icall 이중 래퍼 (`CustomMedia.LoadImage`, `SetUrl`):** 1차 공식 Unity API 호출 -> Unity 래퍼 본문의 ReadOnlySpan 이슈(MissingMethodException) 시 2차 네이티브 icall fallback. 두 경로는 별도 메서드로 분리되어 한쪽 심볼이 사라져도 다른 쪽을 시도한다.
  - **오디오 콜백(Action) 예외 래퍼 (`UpdateResilience.WrapAction`):** `SoundData.FromHEAudioClip`의 `afterDispose` 콜백을 람다로 감싸 try-catch로 실행하고, 인터롭을 통해 관리 코드로 올라온 예외를 경고 로그로 격리. 네이티브 크래시(AV 등)는 막지 못하며, GC 수명 문제의 대책으로 검증된 적 없음(해당 크래시 재현 기록 없음).
  - **결과 레코드 생성 폴백 (`OfflineSession.BuildLocalRecord`):** `CreateRecordDirect`(별도 메서드)가 실패하면 Reflection으로 `(IntPtr)`·복사 생성자를 뺀 생성자를 후보별로 시도. 모두 실패하면 `NetworkError` 반환.
  - **Addressables 탐색 및 오버레이 방어 강화 (`AddressableKeyRedirect`, `PlayOverlay`):** Locate 메서드 탐색 유연성 및 메모리 unbox 전 포인터 유효성 검사 강화.
- **람다(컴파일러 생성) 탐색 강화 및 2차 코드 리뷰 (2026-11-12, 상세 `docs/update_resilience_audit.md` "2차 리뷰"):**
  - **`InteropNames`:** 런타임 이름 탐색에서 람다 캐시(`__c`)·클로저(`__c__DisplayClass`)·상태 머신(`_X_d__N`, async 람다 `__X_b__N_d`, 합성 이름 `ValueTypeCompilerGenerated…`)·FixedBuffer 등을 거른다. 1차 기준은 인터롭이 붙이는 `[ObfuscatedName("원래 이름")]`의 `<`, 2차는 이름 패턴. 오프라인 하네스로 인터롭 155개 어셈블리 2553개 타입에서 패턴 단독 누락·오탐 0 확인.
  - **인터롭 인터페이스 프록시:** `IResourceLocator` 같은 IL2CPP 인터페이스는 `Il2CppObjectBase`를 직접 상속하는 **일반 클래스**로 나오고 `IsInterface/IsAbstract`가 false다 → `Il2CppSystem.Object` 비상속 또는 `NativeMethodInfoPtr_{메서드}_…_Abstract_…` 필드로 판별(`IsIl2CppInterfaceOrAbstract`). 예전엔 이름 문자열 `"IResourceLocator"`로만 걸렀다.
  - **Locate 탐색:** 엄격 시그니처(`bool Locate(Il2CppSystem.Object, Type, out IList)`) → 0건이면 완화 조건. Prefix는 `__0`(타입은 탐색 단계에서 확인). `ModHealth`가 3건 미만이면 경고. 2026-11-12 실행 확인: 3건 패치, `[Health] 필수 패치 정상`, 선곡 화면에서 커버·미리듣기 키 치환(`hwa_001` → `wits_biz`) 발생. 플레이(차트/BGM/BGA)는 이번 빌드로 미확인.
  - **인코딩 감지:** 예전 코드는 UTF-8이 아니면 무조건 GBK였다(문서와 불일치) → Shift-JIS/CP949/GBK/Big5 바이트 패턴 점수 비교로 교체.
  - **차트 캐시:** 음원·영상 수정 시간까지 비교(바꾸면 endPosition 재계산). 커버 LRU 중복 등록으로 새 스프라이트가 파괴되던 문제 수정.
- **훅 예외 격리·병목·람다 판별 3차 (2026-11-14, 상세 `docs/update_resilience_audit.md` "3차"):** 인게임은 메인 메뉴까지만 확인(사전 점검·필수 패치·주입·네이티브 문자열 자체 검증 32건 일치). **선곡·플레이·결과 화면은 이 빌드로 미확인.**
  - **규칙 — 일반 곡에도 도는 훅은 본문에서 게임 멤버를 직접 참조하지 않는다 (`mod/HookGuard.cs`):** Il2CppInterop 트램펄린은 훅 예외를 잡고 원본 대신 기본값을 돌려준다(`During invoking native->managed trampoline`). 그래서 Prefix 본문이 업데이트로 사라진 멤버를 참조하면 JIT 실패로 **모든 곡**의 그 게임 메서드가 망가진다(빠른 경로 `return`도 소용없음 — 메서드 전체가 컴파일 안 됨). → 게임 멤버 접근은 `[MethodImpl(NoInlining), Guarded("훅 이름", required)]` 메서드로 빼고 try 안에서 호출, 실패는 `HookGuard.Fail`. 바인딩 실패면 그 훅을 끄고(필수면 커스텀 곡 주입도 끔), 그 밖의 예외는 한 번만 경고.
  - **사전 점검:** `HookGuard.Precheck`가 시작 시 `[Guarded]` 메서드를 `RuntimeHelpers.PrepareMethod`로 미리 JIT (순수 .NET 6 실측: 없는 메서드·필드·프로퍼티 참조는 여기서 Missing*Exception). 로그 `[HookGuard] 훅 바인딩 사전 점검 정상 (20개 메서드, 3ms)`.
  - **병목:** Locate 키를 IL2CPP 문자열 버퍼에서 직접 검사(할당 0, 처음 32건은 관리 문자열 결과와 비교해 다르면 전환), 일반 곡 차트 스트림은 복사하지 않고 그대로, 곡 폴더 미디어 경로 5초 캐시(`RefreshMedia`), 곡/차트 조회 사전, SongInfo 훅은 주입 전 바로 원본, `DetermineRunning`은 `CustomSongs.Current == null`이면 바로 원본, 판정바를 끄면 판정을 읽지 않음, 오버레이는 화면 크기·설정이 바뀔 때만 레이아웃 재설정.
  - **람다 판별 4번째 신호 "멤버 모양":** 인터롭은 컴파일러 생성 필드를 같은 이름 프로퍼티로 내보낸다(`__1__state`, `__t__builder`, `__4__this`, `__9`, `__9__8_0`, `__u__1`). `tools/interop-harness`(오프라인, 게임 실행 불필요)로 155개 어셈블리 30,509개 타입: 이름 패턴 단독 누락·오탐 0, 멤버 모양 단독 오탐 0(상태 머신 96%·람다 캐시 83% 판별). 인터롭이 `<`를 그대로 둔 UnityEngine 타입 281개가 2차 정답 기준에서 빠져 있었음 → 정답·이름 패턴에 포함.
- **전체 파일 리팩토링 · 병목 제거 · 업데이트 내성 4차 강화 (2026-11-14, 상세 `docs/update_resilience_audit.md` "4차"):** 오프라인 검증(`dotnet build -c Release/Debug` 오류·경고 0, `tools/interop-harness`: `[Guarded]` 44개 JIT 실패 0, `[HarmonyPatch]` 31개 타깃 누락·중복 0). **인게임 실기 검증은 아직 미실시.**
  - **업데이트 내성 보강:** `PlayOverlay.FindFont`의 `LocalizeTMPFont` / `TextMeshProUGUI` 폴백 분리(기존엔 같은 메서드에 있어 JIT 실패 시 폴백 미도달), `OfflineSession.BuildLocalRecord` 내부 `EnsureSectionRecords`/`CreateEndGameResponse`/`ComputeAccuracy`/`ComputeMastery` 격리, `CustomSongs.Inject` 및 `GetDataSet`/`IsPreferredBaseSong`/`_difficultyTypeInfos`/`_songMetaData` 격리 + 리플렉션 폴백, `PlayAssist.ApplyAutoPlay`/`RestoreAutoPlay`의 `InGameSettingManager.Extra` 격리, `CustomCharts.ProcessPlayer`의 `TryGetCustomKey`/`ReadVanillaKey` 분리, `UpdateResilience`의 `BaseType` 상속 계층 탐색 및 `Convert.ChangeType` 변환 추가.
  - **병목 제거 & 캐싱:** `PatternPlayer_DetermineRunning_HoldEnd_Patch`의 `Postfix` 제거(`Finalizer`만 유지해 매 프레임·입력 틱마다 `ClearFlag` 2회 호출되던 중복 제거), `PlayOverlay.Update` 비플레이 화면(`!showAny && !s_rootActive`)에서 `s_root == null` IL2CPP 호출까지 생략, `CustomCharts` 일반 곡(`!DumpEnabled && !HasCustomChartFiles()`)·BMS 이중 JSON 직렬화/파싱 제거, `CustomMedia` Unity 6 `ReadOnlySpan` 예외 플래그 캐싱 및 음원(`HEAudioClip`) LRU 상한(8개)+타임스탬프 핫리로드, `MediaDuration` 길이 캐시+UTF-8 Span 리터럴(`u8`), `ModConfig` 수정 시각 기반 재로드 생략, `BmsConverter.Parse` 1회 정렬, `CustomChart.BmsPath` 5초 `MediaSlot` 캐싱, `EncodingHelper.ReadAllLinesSafe` `StreamReader` 라인 읽기, `TextSanitizer` 빠른 경로, `InteropNames.ReasonSelf` 평가 순서 및 `DerivesFromIl2CppObject` 캐시.
  - **리팩토링·오프라인 검증:** `CustomSong.NormalizeDifficulty`와 `BmsConverter.GetDifficultyAliases` 난이도 별칭 일원화, `tools/interop-harness`에 빌드된 `PLATiNALAB.Mod.dll`의 `[Guarded]` JIT 준비 및 `[HarmonyPatch]` 타깃 존재/모호성 검증 추가.

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
   - `Il2CppPlatinaLab.Main.Director.SongSelectDirector.IsLock()`: 잠금 해제 우회 후보 (현재 패치하지 않음).
   - `Il2CppPlatinaLab.Main.Director.SongSelectDirector.IsNotOwned()`: 미보유 곡 제한 우회 후보 (현재 패치하지 않음).

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
- **Locate 후보에서 `IResourceLocator`를 `IsInterface`/`IsAbstract`로 거르기:** 인터롭 인터페이스 프록시는 일반 클래스라 둘 다 false → 후보에 섞였다(2026-11-12 오프라인 테스트). 상속 체인(`Il2CppSystem.Object` 없음)과 `_Abstract_` 메서드 정보 필드로 판별.
- **컴파일러 생성 타입을 `<>` 이름이나 `__c`/`DisplayClass` 패턴만으로 거르기:** 인터롭에서 `<`는 사라지고, async 람다(`__X_b__N_d`)·합성 이름(`ValueTypeCompilerGenerated…`)·FixedBuffer 등 115개가 패턴에서 빠졌다 → `ObfuscatedName` 원래 이름 우선 + 패턴 보강(`InteropNames`).
- **인터롭 어셈블리에서 `GetInterfaces()`로 IResourceLocator 구현체 탐색:** Il2CppInterop은 IL2CPP 인터페이스를 C# 인터페이스로 남기지 않음 → 메서드 이름 `Locate`(인자 3개)로 탐색. `Unity.Addressables`는 지연 로드라 `Assembly.Load` 선행 필요.
- **음원 교체 지점 실패들:** `PreviewSoundController.Method_Private_UniTask_1_SoundData_String_CancellationToken_PDM_0` Prefix·`PatternPlayer.SetMainTrackToPlay` Prefix → 둘 다 발동 안 함. `SoundData` 생성자 Prefix → `Failed to init IL2CPP patch backend for SoundData::.ctor` 경고(관리 코드 쪽만 패치됨) → 정적 `FromHEAudioClip(HEAudioClip,bool,Action)`로 이동해 성공. `HEAudioClip.name` 세터는 문자열 Span 경로라 쓰지 않음.
- **LINQ 람다 일부에서 CS0656(NullableAttribute) 빌드 오류:** `All.SelectMany(...).FirstOrDefault(...)` 등 → 해당 호출만 foreach로 교체. 람다 전반을 금지하는 규칙은 아님(`<Nullable>disable</Nullable>`인 현재 코드에서 `CustomSongs.All.FirstOrDefault(c => ...)` 등 LINQ 람다를 사용 중).
- **IMGUI(OnGUI)로 오버레이 그리기:** 이 게임 빌드는 IMGUI가 스트리핑돼 `GUI.*` 호출마다 `Method unstripping failed`(매 프레임 경고로 로그 3700줄+) → uGUI Canvas로 교체. `GUIStyle(GUIStyle)` 복사 생성자도 없음.
- **`Constants.JudgmentTimingMap` 값을 `ValueTuple.Item1/Item2`로 읽기:** 1.15E-311 같은 쓰레기값 → 박스를 `il2cpp_object_unbox`해서 double 두 개를 직접 읽음.
- **`InGameSettingHelper.IsAutoPlayMode` / `InputStateMachine.Interpret` 패치:** 둘 다 호출부에 인라인돼 직접 호출 없음 → 설정값 자체를 바꾸거나(`SetExtra`) 직접 호출되는 `ProcessInputEvent`를 후킹.
- **이전 세션 `xrefs.py`(E8 호출 탐색)가 아무것도 못 찾음:** `.text` 섹션만 훑었는데 GameAssembly 코드는 다른 실행 섹션에 있음 → 실행 가능(0x20000000) 섹션 전부를 훑어야 함.
- **게임 exe 직접 실행:** `PlatformInitializationFailedException: Failed to initialize steam sdk` 후 종료 → `steam://run/3430470//-screen-fullscreen 0 -screen-width 1600 -screen-height 900/`로 Steam 경유 실행.
- **`record`/`record struct` 사용 (2026-11-14):** `<Nullable>disable</Nullable>`이어도 레코드 합성 멤버가 `NullableAttribute`를 요구해 CS0656 → 일반 클래스나 값 튜플 + 유효 플래그로 대체.
- **사용자가 PC를 쓰는 중에 게임 창으로 입력 보내기:** 서버 랭킹이 있는 온라인 게임이라 입력이 엉뚱한 화면에 들어갈 수 있다. `um win drive ... idle`이 1분 미만이면 입력을 보내지 말고 사용자에게 맡긴다(2026-11-14 idle 2.6초 → 중단).
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
- **2026-11-14 빌드(훅 격리·병목 수정) 인게임 확인:** 선곡 화면 커버·미리듣기 키 치환, 커스텀 곡 플레이(차트/BGM/BGA, 판정바·키뷰어·올 퍼펙트), 결과 화면, 일반 곡 1곡 플레이·결과 전송. 씬 종료 시 `[Health] ... 핵심 훅 호출 정상`과 `바인딩 실패로 꺼진 훅` 없음 확인.
- 오토 플레이 인게임 확인(`IsScorable` 안전장치 포함). 키뷰어가 플레이 화면 하단 섹션 패널과 겹침 → 위치 조정 검토. 일시정지 화면 위에도 오버레이가 그려짐.
- BMS 박자 변경(#mmm02)·STOP·PitchNote 인게임 판정 확인. 박자 변경은 PPP `bars`의 비 4/4 마디 position 소수부 의미 미확인 → 변박 원곡 덤프로 확인 후 구현.
- 커스텀 BGM: `bgms[].sound`(예 `lolikami_165bpm.fmod.ogg`)는 FMOD 사운드 이름 → `SetMainTrackToPlay(SoundData)` 후킹으로 외부 ogg 주입 필요.
- BPM 변속이 있는 차트의 길이 계산 (현재 initialBpm 기준 근사, 경고 로그).
- 결과 화면 `SectionDetailPanelController.Initialize` NRE 해소 (섹션 기록/캐시 쪽 추정).
- 결과 score 로그와 화면 일치 확인됨(2026-11-08, 올 퍼펙트 552노트 = 1656). 값이 작아 보여도 정상 범위로 추정(노트당 3점, `GetMaxScore`는 노트 수 곱셈).
- 업데이트 내성 처리 내역과 업데이트 후 확인 순서: `docs/update_resilience_audit.md`.
- 정적 분석 도구: `scratchpad/calls.py` 방식 — `dummy_dll`을 ilspycmd로 풀어 `[Address(RVA=..)]` 맵을 만들고 capstone으로 GameAssembly.dll 함수의 call 대상을 이름 붙여 출력.
