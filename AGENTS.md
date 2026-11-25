# PLATiNA LAB Modding Handover & Project Context

## 1. 대상 게임 버전 & 환경 정보
- **게임명:** PLATiNA LAB (Steam App ID: `3430470`)
- **Steam Build ID / 지문:** `25981392` / `GameAssembly.dll` `96,829,440`바이트 (`ModHealth`가 `appmanifest_3430470.acf`의 `buildid`와 실제 파일 크기를 이중 확인 — `.acf` 자동 업데이트 차단·DepotDownloader 복사본 환경 대비)
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
- **런타임 데이터 경로 (게임 폴더 기준):** `hwa\`(커스텀 곡), `SaveCustomKey\config.txt`(설정 핫리로드), `UserData\PLATiNALAB\song_ids.json`(곡 ID 영속화)·`song_ownership.json`(공식 곡 보유·해금 기록, 19차) — 둘 다 `JsonMapFile`로 임시 파일 교체 저장·`.bak` 직전본·손상 시 `.corrupt` 보관 — ·`ChartDump\`·`CustomCharts\`
- **오프라인 인터롭 하네스:** `H:\source\repos\PLATiNA LAB-unity-il2cpp\tools\interop-harness` (`dotnet run -c Release`, 종료 코드 0 = 통과)
- **후킹 명세서:** `H:\source\repos\PLATiNA LAB-unity-il2cpp\docs\hooks.md`
- **차트(PPP JSON) 형식 & 커스텀 차트 사용법:** `H:\source\repos\PLATiNA LAB-unity-il2cpp\docs\chart_format.md`
- **업데이트 내성 감사 & 감사 이력(1~20차):** `H:\source\repos\PLATiNA LAB-unity-il2cpp\docs\update_resilience_audit.md`

---

## 3. 아키텍처 & 코딩 규칙
- **Unity 6 (Metadata v39):** 독립 `Il2CppDumper`/`Cpp2IL`은 v39 미지원. MelonLoader 0.7.3 내장 `Cpp2IL`이 생성한 `Il2CppAssemblies` 및 `dummy_dll`을 기준으로 `ilspycmd`로 분석.
- **Nullable Context 해제:** `Il2Cppmscorlib` 참조 시 `CS0656` 방지를 위해 `.csproj`에 `<Nullable>disable</Nullable>` 설정.
- **커스텀 차트 (`mod/CustomCharts.cs`):** `PatternParser.Parse(PatternPlayer, Stream)` Prefix에서 `ref Stream`을 `Il2CppSystem.IO.MemoryStream`으로 교체. 키 = `{GroupId}__{FilePath 파일명}`.
- **커스텀 곡 (`mod/CustomSongs.cs`, `mod/CustomSongPatches.cs`):**
  - `hwa\<곡폴더>\`에서 스캔. 곡 번호는 `UserData\PLATiNALAB\song_ids.json`에 폴더명별로 영속화(`hwa_NNN`, `PatternIndex = 990000 + N*10 + i`).
  - `info.txt`에 없는 메타(`#TITLE`, `#ARTIST`, `#BPM`, `#PLAYLEVEL`)는 BMS 헤더에서 자동 보완.
  - `CategoryCacheFactory.CreateAll` Postfix에서 `custom` 카테고리 추가, `SongCategoryResourceCollection.GetDataSet`에서 UI 리소스 생성.
  - `AddressableKeyRedirect`가 Addressables `Locate` 구현체 3종에서 `hwa_NNN` 키를 기반 곡 ID(`s_baseSongId`)로 치환.
  - `PatternPlayer.DetermineRunning`에서 `DurationMicroSec - 3s`까지 클리어 연출 보류.
- **미디어 (`mod/CustomMedia.cs`, `mod/MediaDuration.cs`):** 커버(`CoverManager.GetCoverSprite`, LRU 24개), BGM/미리듣기(`SoundData.FromHEAudioClip`), BGA(`BGAPlayer.SetClip`). Unity 6 `ReadOnlySpan` 래퍼 이슈는 `MarshalledUnityObject.MarshalNotNull` + `ManagedSpanWrapper` + `*_Injected` icall로 우회 (`AllowUnsafeBlocks`).
- **BMS 변환 (`mod/BmsConverter.cs`):** `#WAV` 파일명(`ShortNote`, `LongNote 홀드 시작/끝`, `PitchNote`→key 255) + LNTYPE 1(5x/6x) + `#LNOBJ` 지원. `#RANDOM`/`#IF`는 1번 분기 고정(경고). 음원 길이는 `MediaDuration.Audio`(Ogg Vorbis/WAV)로 읽고 못 읽으면 경고. 다국어 인코딩 자동 감지(`EncodingHelper`).
- **플레이 보조 & 오버레이 (`mod/ModConfig.cs`, `mod/PlayAssist.cs`, `mod/PlayOverlay.cs`):** `SaveCustomKey\config.txt` 핫리로드. 오토 플레이(`Extra.AutoPlay`), 올 퍼펙트(커스텀 곡 전용), uGUI Canvas 판정바·키뷰어.
- **오프라인 세션 (`mod/OfflineSession.cs`):** 커스텀 곡의 `RequestGameStart`/`RenewGameSession`/`SendGameEnd` 서버 호출 차단 및 로컬 결과(`AccuracyV2Formula`/`MasteryV2Formula`) 생성. `OfflineMode=1`이면 공식 곡도 같은 로컬 경로. `SendGameEnd`는 훅이 꺼져도 `LastStarted`가 커스텀이면 서버로 보내지 않는다.
- **오프라인 독립 실행 모드 (`mod/OfflineMode.cs`, `SteamBypassPatches.cs`, `CurtainBypassPatches.cs`, 19차):** `config.txt` `OfflineMode=1`(기본 0)일 때만 스팀 SDK·로그인·서버 오류 팝업·스토리 우회와 커튼 정리가 동작한다. 값은 `Offline.Init`이 시작 시 한 번만 읽는다(스팀 초기화가 시작 직후 1회라 핫리로드 불가). `Application.Quit()`/`Quit(int)`은 로그인 전(시작 후 최대 180초 — 로그인 완료 신호 훅이 업데이트로 꺼져도 종료가 영구히 막히지 않게)·오류 차단 후 15초만 막는다(`Offline.ShouldBlockQuit`).
  - **오프라인 필수 패치 (20차):** `ModHealth.s_offlineRequiredPatches`(스팀 SDK 초기화·로그인·인증 토큰·미로그인 팝업·종료)가 빠지면 `OfflineMode=1`일 때만 `[Health] 오프라인 모드 필수 패치 누락` 오류 로그. 커스텀 곡 주입은 끄지 않는다(오프라인 기동 문제와 무관). 하네스 `[Required]`가 이름을 대조한다.
  - **로그 규칙 (20차):** 오프라인에서는 모든 API 호출이 실패해 우회 훅이 연달아 불린다. 더미 응답 성공 로그는 `Offline.LogOnce`, 오류 팝업 차단 경고는 `Offline.WarnThrottled`(키당 10초)로 남긴다 — 새 오프라인 훅도 이 둘을 쓴다.
  - **공식 곡 보유 판정 (Rationale):** 오프라인에서는 서버 보유 정보가 없어 원본이 KeyNotFound를 던진다. decf3b7은 모든 공식 곡을 무조건 해금해 **미보유 유료 DLC까지 열었다** → 소유자 결정(2026-11-25)으로 제거. 온라인(`OfflineMode=0`)에서 `IsAvailable/IsNotOwned/IsLock` 원본 결과를 Postfix로 `SongOwnership`에 기록하고, 오프라인에서는 기록만 재현(없으면 잠김). 미보유 곡을 여는 기능은 공개 릴리즈에 넣지 않는다.
  - 곡 ID는 `CustomSongs.ReadSongIdCached`(백킹 필드 IL2CPP 문자열 포인터 캐시 + 내용 재비교)로 읽는다 — 판정 게터는 곡마다 반복 호출되는 핫패스.
- **진단 패치 (`mod/DiagnosticPatches.cs`):** 로그 전용 패치는 `DiagnosticLogs=1`일 때만 수동 적용.
- **패치 적용 (`mod/SafePatcher.cs`, 17차):** `[assembly: HarmonyDontPatchAll]`로 MelonLoader 자동 `PatchAll`을 끄고, `Core.OnInitializeMelon`에서 `[HarmonyPatch]` 클래스마다 `CreateClassProcessor(t).Patch()`를 따로 try/catch로 적용한다. 새 `[HarmonyPatch]` 클래스는 별도 등록 없이 자동 수집된다. 하네스 `[SafePatch]`가 자동 PatchAll이 꺼져 있는지 검사한다.
- **오버레이 성능 규칙 (`mod/PlayOverlay.cs`, 17차):** 매 프레임 경로에서 `Mathf.*`, `new Color/Rect/Vector2`, `Rect.x` 등 Unity 값 형식 헬퍼와 Unity `== null`을 쓰지 않는다(전부 IL2CPP 호출). `Rgba`/`Vec2`/`Box`(필드 직접 대입)와 `MathF`·`Math`를 쓰고, 화면 크기·오버레이 생존은 30프레임마다 확인한다.
- **코드 작성 필수 규칙 (하네스 `[Guarded]`/`[HarmonyPatch]`/`[PatchArgs]`/`[Required]`/`[Layout]`/`[Body]`/`[Diag]`/`[NativeField]`/`[Reflect]`/`[GuardKey]`/`[Mark]`/`[Fingerprint]`/`[SafePatch]`/`[Offline]` 검증):**
  1. 게임 타입 직접 참조를 `try`로 감쌀 때는 `[MethodImpl(NoInlining), Guarded("...")]` 별도 메서드로 분리 (같은 메서드의 `try`는 JIT 실패를 못 잡음). 게임 멤버를 직접 참조하는 모든 `[NoInlining]` 격리 메서드에도 `[Guarded]` 필수.
  2. 모드 클래스 필드에 게임 `enum`을 그대로 저장하지 말고 `int`로 저장.
  3. 게임 `record` 객체(`SongInfo`, `PatternInfo`, `PlayResult` 등)에 `== null`/`!= null` 대신 `is null`/`is not null` 사용 (인터롭 `op_Equality`는 `il2cpp_runtime_invoke`).
  4. 새 코드를 작성하면 반드시 `dotnet run --project tools/interop-harness -c Release`로 검증 (종료 코드 0).
  5. 리플렉션 폴백(`UpdateResilience.SafeSetFieldOrProperty`/`SafeGetFieldOrProperty`)의 멤버 이름은 **호출부에 문자열 상수**로 두고, 대상 인자는 **인터롭 타입으로 정적 타입이 잡힌 변수**로 넘긴다 (`object`·배열 순회 금지). 이름을 넘겨받는 헬퍼는 자기 인자를 그대로 전달하면 하네스가 래퍼로 인식한다(`SetSongField`, `CopyMember`). `[Reflect]`가 모드 자신의 `UpdateResilience.CanResolve`로 인터롭에서 찾히는지 확인한다.
  6. `HookGuard.Fail(키)`로 끄는 키는 반드시 어딘가에서 `HookGuard.IsDisabled(키)`로 단락하고, `IsDisabled` 키는 `[Guarded]` 이름이거나 `Fail` 키여야 한다 (`[GuardKey]`). 키는 `const string`으로 한 곳에 둔다.
  7. `ModHealth.Mark(키)`를 추가·변경하면 `ReportPlay`의 `s_fired.Contains(키)`와 짝을 맞춘다 (`[Mark]`). 게임 업데이트 후에는 `ModHealth.TestedBuildId`·`TestedGameAssemblySize`·`TestedLocateCount`를 갱신해야 하네스 `[Fingerprint]`가 통과한다 (일부러 실패시켜 갱신을 잊지 않게 함).
  8. 오프라인 전용 패치 클래스에는 `[OfflineOnly]`를 붙이고 Prefix/Postfix 첫 줄에서 `Offline.Enabled`(종료는 `Offline.ShouldBlockQuit`)를 확인한다. 하네스 `[Offline]`이 IL에서 이 호출을 확인하고, Steamworks·`HighendGames.GamePlatform` 타입을 겨냥한 패치에 `[OfflineOnly]`가 없으면 실패한다.

---

## 4. 핵심 후킹 타깃 클래스 & 메서드 시그니처
- `PatternParser.Parse(PatternPlayer, Stream)` / `Parse(Stream)`: 차트 Stream 교체.
- `PatternPlayer.ProcessJudgeResult(JudgeResult)` / `ProcessTickResult(JudgmentType)`: 판정바 수집 & 커스텀 곡 올 퍼펙트.
- `PatternPlayer.ProcessInputEvent(InputEvent)`: 키뷰어 입력 수집.
- `PatternPlayer.DetermineRunning()`: 커스텀 곡 클리어 연출 타이밍 보류.
- `SongListBuilder.BuildNameBanners()` / `CategoryCacheFactory.CreateAll()`: 커스텀 곡·카테고리 주입.
- `SongInfo.IsHiddenFromDefaultList` / `IsAvailable` / `IsNotOwned` / `IsLock` / `IsNewSongMarkTarget`: 커스텀 곡 잠금/보유 우회, 공식 곡은 온라인 기록(Postfix) / 오프라인 재현(Prefix, `SongOwnership`).
- `InGameSurface.RequestGameStart` / `RenewGameSession` / `SendGameEnd`: 커스텀 곡(및 `OfflineMode=1`의 공식 곡) 로컬 세션 및 로컬 결과 생성 (`mod/OfflineSession.cs`).
- `StoryManager.IsPrologueFirstHalfRead` / `IsPrologueSecondHalfRead` / `IsStoryRead` & `TitleDirector.LoadPrologueScene` & `TutorialLauncher.Enter` & `StorySequenceDirector.AskTutorialAndProceed` / `LoadTutorialScene`: 프롤로그 스토리 및 튜토리얼 스킵 (`mod/SteamBypassPatches.cs`).
- `CoverManager.GetCoverSprite` / `SoundData.FromHEAudioClip` / `BGAPlayer.SetClip`: 커스텀 미디어.
- `SteamAPI.Init` / `InitEx` / `RestartAppIfNecessary` / `IsSteamRunning` / `RunCallbacks` & `SteamUser.GetSteamID` / `BLoggedOn` / `GetAuthTicketForWebApi` & `InteropHelp.TestIfAvailableClient` & `Platform.PlatformUserId` / `GetAuthToken` & `LoginProcedure.LoginAndLoadProfile` & `LoginInitialize.Init` & `LoginSurface.LoginSteam` & `UserSurface.GetUserProfile` / `GetUnlockedFeatures` & `ContentsSurface.GetContentsAvailability` & `RewardManager.Load` / `FetchAllUnlockStates` & `PersonalAgendaToastBoardFriendsPage.FetchFocusInfoViaAPI` & `SocialSurface.GetScoreRecordsOfFocusedUsers` / `GetFollowingList` & `Application.Quit()`/`Quit(int)` & `HttpRequestInterceptor.IsCriticalError` / `HandleCriticalError` & `ErrorDialogCollection.FatalAPIServerError` / `APIServerError` / `ShowErrorForCode` / `ShowNotLoggedInDialogAndRestart` / `ShowMaintenanceDialogAndQuit` (`mod/SteamBypassPatches.cs`, 전부 `[OfflineOnly]`): `OfflineMode=1`일 때만, 오프라인 독립 사본을 위한 스팀 SDK, 로그인 인증(`LoginResultType.Success` + `RuntimeDatabase` 오프라인 프로필 구성), 카탈로그/기능 해금 및 보상 로딩 더미 완료 반환, 소셜/친구 랭킹 보드 더미 빈 데이터 반환 및 오류성 종료(`Application.Quit`, 로그인 전·오류 차단 후 15초) 차단, 가짜 `LoginOutcome`/`UserProfile` 반환, HTTP 치명적 에러 및 미로그인/서버 다이얼로그 차단 우회.
- `AiringCurtainController.Leave` / `FadingCurtainController.Leave` & `SongSelectDirector.Awake` / `OnSceneVisible` & `DetailViewerManager.SetSong` & `SectionController.Initialize` (`mod/CurtainBypassPatches.cs`, `SectionController` 외 `[OfflineOnly]`): 씬 전환 시 닫힌 ACCESSING.. 아이캐치 커튼이 열리지 않고 멈추는 현상 해소, 선곡 화면 진입 및 곡 변경 시 잔류 커튼 즉시 비활성화(`SetActive(false)`, 곡 변경 경로는 1초 스로틀 + 직전 정리에서 잔류가 없으면 다음 씬 전환 신호까지 스캔 생략), 커스텀 곡의 섹션 캐시 미등록 에러 방어.

---

## 5. 시도했다가 실패한 것들 (Dead Ends / Failed Attempts)
- **독립 Il2CppDumper / 구버전 Cpp2IL:** Metadata v39 미지원 → MelonLoader 0.7.3 생성 Interop 사용.
- **게임 폴더에서 평문 차트 찾기:** Addressables 번들 내부에 있어 불가 → `PatternParser.Parse` Stream 후킹.
- **`SongListBuilder.Build` Prefix:** 호출 안 됨 → `BuildNameBanners` Prefix 사용.
- **클리어 연출 보류를 `PatternPlayer.Update`에 걸기:** `DetermineRunning`이 입력 스레드(HFTask)에서도 불림 → `DetermineRunning` 직접 패치.
- **라벨 UI 직접 조작:** 흰 배경 잔류 및 타 카테고리 오염 → `SongCategoryResourceCollection.GetDataSet` 패치로 대체.
- **`PlayDirector.LoadGame` Prefix로 SongInfo 교체:** IL2CPP 인라인으로 미발동 → Addressables `Locate` 키 치환.
- **음원 교체 지점:** `PreviewSoundController` / `SetMainTrackToPlay` 미발동, `SoundData..ctor` IL2CPP 백엔드 경고 → `SoundData.FromHEAudioClip` 패치.
- **IMGUI(`OnGUI`):** 게임 빌드에서 스트리핑됨 → uGUI Canvas 오버레이로 교체.
- **`InGameSettingHelper.IsAutoPlayMode` / `InputStateMachine.Interpret` 패치:** 호출부 인라인 → `ExtraScheme.SetExtra` / `ProcessInputEvent` 사용.
- **`UpdateResilience` 백킹 폴백을 `Type.GetField`로만 찾기 (1~10차):** Il2CppInterop은 IL2CPP 필드를 관리 필드가 아니라 같은 이름의 **프로퍼티**(`SongInfo._Id_k__BackingField` 등)로 노출해서, 인터롭 타입에서는 폴백 35개 호출부 중 고유 이름 17개가 한 번도 찾히지 않았다(11차 실측). → 후보 이름을 필드 → 프로퍼티 순으로 탐색.
- **`SendGameEnd` 폴백 판정에 `CustomSongs.Current` 사용 (1~11차):** `Current`는 `PlayDirector.Exit`·선곡 복귀에서 지워지는데 `SendGameEnd`와의 호출 순서를 실측하지 못했다 → 지우지 않는 `CustomSongs.LastStarted`(최근 `RequestGameStart`의 곡)로 판정.
- **`record`/`record struct` 및 일부 LINQ 람다:** `CS0656(NullableAttribute)` 발생 시 일반 클래스/`foreach`로 대체.
- **구버전 골드버그(0.2.5) steam_api64.dll 직접 교체:** 최신 Steamworks.NET이 호출하는 `SteamInternal_SteamAPI_Init` 함수 누락으로 `EntryPointNotFoundException` 크래시 발생 → 원본 `steam_api64.dll` 복원 후 모드 Harmony 패치(`SteamBypassPatches.cs`)로 C# 수준에서 `SteamAPI.Init`/`InitEx`를 가로채는 방식으로 해결.
- **게임 exe 직접 실행:** 과거 Steam SDK 초기화 실패 종료 → 모드 내 `SteamBypassPatches` 적용 후 스팀 없이 독립 사본 직접 실행 가능.
- **오프라인 우회를 게이트 없이 상시 적용 (decf3b7):** 스팀으로 정상 실행하는 사용자까지 로그인 생략·공식 곡 기록 미전송·메뉴 종료 불가·미보유 DLC 해금이 걸렸다 → 19차에서 `OfflineMode` 옵트인 + 하네스 `[Offline]` + 보유 기록 재현으로 교체.
- **`Application.Quit` 무조건 차단:** 게임 메뉴 종료까지 막힘 → 로그인 전·오류 차단 직후만 차단. 호출처는 덤프에 메서드 본문이 없어 정적으로 찾지 못했다(ISIL 검색 0건) — 휴리스틱이므로 실기 로그(`Application.Quit() 차단 (...)`)로 확인할 것.
- **`ConcurrentDictionary.GetOrAdd(key, 람다)` (20차):** `Il2Cppmscorlib` 참조 환경에서 `CS0656(NullableAttribute)` → `TryGetValue` + `GetOrAdd(key, 값)`으로 작성.
- **사용자 PC 사용 중 자동 입력:** `um win drive ... idle` 1분 미만이면 입력 보내지 않음.

---

## 6. 빌드 명령 및 Mods 배포 경로
- **빌드 명령:**
  ```powershell
  dotnet build "H:\source\repos\PLATiNA LAB-unity-il2cpp\mod\PLATiNALAB.Mod.csproj" -c Release
  ```
  (다른 경로에 게임이 있으면 `-p:GameRoot="C:\...\PLATiNA LAB"` 지정)
- **배포 경로:** `PostBuild`가 `$(GameRoot)\Mods\PLATiNALAB.Mod.dll`로 자동 복사 (게임 실행 중 잠김 시 경고 후 계속).
- **오프라인 하네스:** `dotnet run --project "H:\source\repos\PLATiNA LAB-unity-il2cpp\tools\interop-harness" -c Release`
- **릴리즈 배포 (v1.2.0부터):** `Core.cs`의 `MelonInfo` 버전과 `CHANGELOG.md`를 갱신·커밋한 뒤 `tools\release.ps1`(검증) → `tools\release.ps1 -Publish`(태그 `v<버전>` + `gh release create`로 `PLATiNALAB.Mod.dll` 직접 업로드). 하네스 종료 코드 0이 아니면 중단한다. 릴리즈 에셋에는 순수 모드 DLL만 올리고 게임 파일·디컴파일 코드는 넣지 않는다. 게임을 업데이트해 재검증하면 `TestedBuildId`·`TestedGameAssemblySize`와 README·CHANGELOG의 검증 기준 빌드를 함께 갱신한다.
- **Git 원격:** `origin` = https://github.com/magjangin/PLATiNA-LAB-custom-chart (public), `local` = `H:\local-repos\PLATiNA LAB-unity-il2cpp.git` (로컬 백업). `dummy_dll/`, `src_dummy/`, `slices/`, `logs/`, `.reatool/`, `mod/bin|obj`는 `.gitignore`로 제외.

---

## 7. 다음 단계 (미구현 / 인게임 확인 필요)
- 20차(v1.4.1) 인게임 확인: `OfflineMode=1` 시작 로그 `[Health] 오프라인 모드 필수 패치 정상 (7종)`, 선곡 스크롤 중 `[Curtain]` 스캔이 씬 전환 직후에만 도는지, `[SteamBypass]` 로그가 `(이후 같은 로그 생략)`·`(직전 10초간 … 생략)`으로 줄었는지. `File.Replace` 폴백은 일반 NTFS에서는 타지 않는다(경고 로그가 없어야 정상).
- 19차(v1.4.0) 인게임 확인: `OfflineMode=0` + 스팀에서 로그인·공식 곡 기록 전송·메뉴 종료가 원본대로인지, 선곡 화면 후 종료 시 `song_ownership.json` 생성, 선곡 스크롤 체감 속도. `OfflineMode=1`에서 보유 곡만 열림·미보유 잠김·결과 화면·커튼·메뉴 종료. **소유자의 config.txt에는 `OfflineMode` 키가 없으므로 기본 0(온라인)** — 오프라인 사본은 기록을 만든 뒤 `OfflineMode=1`을 추가해야 한다.
- 최신 빌드 인게임 실기 확인: `[HookGuard]`(54개 메서드), 11차 백킹 프로퍼티 폴백(직접 경로가 정상이면 호출되지 않음 — 회귀 없음만 확인), 12차 `song_ids.json` 교체 저장(`.bak` 생성) 및 곡 번호 유지, `[Native]` 자체 검증, 커스텀 곡 잠금/플레이/결과, 오토 플레이(`IsScorable` 안전장치 포함), 판정바·키뷰어·올 퍼펙트.
- 17차(v1.3.0) 인게임 확인: 시작 로그 `[SafePatcher] 패치 클래스 N개 모두 적용` + `[Health] 필수 패치 정상`, 판정바·키뷰어 표시가 이전과 같은지(색·위치·틱 페이드, 창 크기 변경 후 30프레임 안에 재배치), `music.wav`만 있는 곡의 BGM·길이 연장.
- 16차(v1.2.1) 인게임 확인: 오토 플레이 중 설정 저장 후에도 오토 플레이 유지(Save Finalizer), `#ELSE` 분기가 있는 BMS 변환 결과.
- 14차(v1.2.0) 인게임 확인: 일반 곡 잠금 표시 회귀 없음(`IsCustomId`가 등록 ID까지 확인), `OverlayScale` 배치, WAV 음원 곡 끝까지 재생, `#RANDOM` BMS 변환 경고. 오토 플레이를 공식 곡에도 켤 수 있는 범위는 소유자가 "애매하다"며 보류 — 기록 보호는 `IsScorable` 안전장치만 의존(인게임 미확인).
- 키뷰어 위치가 하단 섹션 패널과 겹치는 문제 및 일시정지 화면 위 오버레이 표시 개선.
- BMS 박자 변경(`#mmm02`)·STOP·PitchNote 인게임 판정 확인.
- 결과 화면 `SectionDetailPanelController.Initialize` NRE 원인 해소 (`OfflineSession.EnsureSectionRecords` 빈 배열 방어 적용됨, 실기 재확인 필요).
