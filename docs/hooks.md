# PLATiNA LAB 후킹 명세서 (Hooks)

Unity 6 (`6000.3.25f1`) IL2CPP 기반 어셈블리(`Il2CppPlatinaLab.Main.dll`, `Il2CppPlatinaLab.DataModel.dll`, `Il2CppHighendGames.HEAudio.dll`, `Unity.Addressables.dll`)에서 현재 모드가 실제로 후킹하는 메서드 목록입니다.

> **검증 기준:** Build `25981392` (`GameAssembly.dll` 96,829,440바이트), `[HarmonyPatch]` 26개 + 동적 `Locate` 3건 + 선택 진단 패치 5건.
> **필수 패치(`ModHealth.s_requiredPatches`)**는 굵게(**필수**) 표기했습니다. 필수 패치가 하나라도 누락되면 커스텀 곡 주입이 자동으로 꺼집니다.

---

## 1. 차트 파싱 & Addressables 리소스 주소 치환

| 클래스 | 메서드 | 패치 위치 / 파일 | 역할 |
| :--- | :--- | :--- | :--- |
| `PatternParser` | **`Parse(PatternPlayer, Stream)`** (**필수**) | Prefix + Finalizer (`CustomCharts.cs`) | 커스텀 곡(BMS/JSON) 및 기존 곡 차트 오버라이드 스트림 교체 |
| `PatternParser` | `Parse(Stream)` | Prefix (`CustomCharts.cs`) | `Parse(PatternPlayer, Stream)` 경유 외 단독 스트림 파싱 처리 |
| `IResourceLocator` 구현체 3종 (`DynamicResourceLocator`, `ResourceLocationMap`, `ContentCatalogData+ResourceLocator`) | **`Locate(Object, Type, out IList)`** (**필수**, 동적 패치) | Prefix (`AddressableKeyRedirect.cs`) | 문자열 키의 커스텀 곡 ID(`hwa_NNN`)를 기반 곡 ID로 치환 (UTF-16 네이티브 빠른 경로) |

---

## 2. 커스텀 곡 주입, 카테고리 & 잠금 우회

| 클래스 | 메서드 | 패치 위치 / 파일 | 역할 |
| :--- | :--- | :--- | :--- |
| `SongListBuilder` | **`BuildNameBanners()`** (**필수**) | Prefix (`CustomSongPatches.cs`) | `SongDataManager`에 `hwa\` 커스텀 곡·패턴 주입 트리거 |
| `CategoryCacheFactory` | `CreateAll()` | Postfix (`CustomSongPatches.cs`) | 선곡 화면에 `custom` 카테고리 탭 추가 |
| `SongCategoryResourceCollection` | `GetDataSet(string)` | Postfix (`CustomSongPatches.cs`) | `custom` 카테고리 UI 리소스 세트 생성 (`CreateDirect` + 리플렉션 폴백) |
| `SongInfo` | **`IsHiddenFromDefaultList`** (Getter, **필수**) | Prefix (`CustomSongPatches.cs`) | 커스텀 곡 숨김 해제 (`false`) |
| `SongInfo` | **`IsAvailable()`** (**필수**) | Prefix + Postfix (`CustomSongPatches.cs`) | 커스텀 곡 `true`. 공식 곡: 온라인이면 원본 결과를 `SongOwnership`에 기록, `OfflineMode=1`이면 기록값 재현(없으면 `false`) |
| `SongInfo` | **`IsNotOwned()`** (**필수**) | Prefix + Postfix (`CustomSongPatches.cs`) | 커스텀 곡 `false`. 공식 곡: 온라인 기록 / 오프라인 재현(없으면 `true` = 미보유) |
| `SongInfo` | **`IsLock()`** (**필수**) | Prefix + Postfix (`CustomSongPatches.cs`) | 커스텀 곡 `EncryptedBool false`. 공식 곡: 온라인 기록 / 오프라인 재현(없으면 잠김) |
| `SongInfo` | **`IsNewSongMarkTarget()`** (**필수**) | Prefix (`CustomSongPatches.cs`) | 커스텀 곡과 `OfflineMode=1`의 공식 곡은 `EncryptedBool false` (서버 NEW 정보 없음), 온라인 공식 곡은 원본 |
| `PatternInfo` | `GetPatternFileName()` | Postfix (`CustomSongPatches.cs`) | 커스텀 패턴의 리소스 파일명을 기반 패턴 파일명으로 반환 |
| `PatternPlayer` | `DetermineRunning()` | Prefix + Finalizer (`CustomSongPatches.cs`) | 커스텀 곡에서 차트 끝(`DurationMicroSec - 3s`)까지 클리어 연출 보류 |

---

## 3. 커스텀 미디어 (커버 · BGM/미리듣기 · BGA)

| 클래스 | 메서드 | 패치 위치 / 파일 | 역할 |
| :--- | :--- | :--- | :--- |
| `CoverManager` | `GetCoverSprite(string)` | Postfix (`CustomMedia.cs`) | 커스텀 곡 커버 이미지 교체 (LRU 24개 캐시, Unity 6 icall 이중 래퍼) |
| `PreviewSoundController` | `Play(string)` | Prefix (`CustomMedia.cs`) | 미리듣기 중인 커스텀 곡 추적 및 미디어 캐시 갱신 |
| `SoundData` | `FromHEAudioClip(HEAudioClip, bool, Action)` | Prefix (`CustomMedia.cs`) | 미리듣기·인게임 BGM을 `music.ogg`/`music.wav` 기반 `HEAudioClip`으로 교체 (LRU 8개 캐시) |
| `BGAPlayer` | `SetClip(...)` | Postfix (`CustomMedia.cs`) | 커스텀 곡 BGA를 `video.mp4` URL 모드로 전환 (Unity 6 `set_url_Injected` 이중 래퍼) |

---

## 4. 오프라인 세션 (서버 통신 차단 & 로컬 결과)

| 클래스 | 메서드 | 패치 위치 / 파일 | 역할 |
| :--- | :--- | :--- | :--- |
| `InGameSurface` | **`RequestGameStart(int)`** (**필수**) | Prefix (`OfflineSession.cs`) | 커스텀 패턴(및 `OfflineMode=1`의 모든 패턴) 시작 시 서버 호출 차단 및 로컬 세션 발급 |
| `InGameSurface` | **`RenewGameSession(string)`** (**필수**) | Prefix (`OfflineSession.cs`) | 로컬 세션 토큰(또는 `OfflineMode=1`)의 갱신 요청을 로컬 성공 응답으로 처리 |
| `InGameSurface` | **`SendGameEnd(GameEndResult, PatternInfo)`** (**필수**) | Prefix (`OfflineSession.cs`) | 커스텀 곡(및 `OfflineMode=1`의 모든 곡) 결과 서버 전송 차단, `AccuracyV2Formula`/`MasteryV2Formula`로 로컬 결과 생성. 훅이 꺼져도 `LastStarted`가 커스텀이면 서버로 보내지 않음 |
| `PlayDirector` | `Exit(bool)` | Prefix + Postfix (`OfflineSession.cs`) | 곡 나가기 시 오버레이·오토 플레이·현재 곡 상태 정리 |

---

## 5. 플레이 보조 & 오버레이 (오토 플레이 · 올 퍼펙트 · 판정바 · 키뷰어)

| 클래스 | 메서드 | 패치 위치 / 파일 | 역할 |
| :--- | :--- | :--- | :--- |
| `PlayDirector` | `Awake()` | Prefix (`PlayAssist.cs`) | `config.txt` 핫리로드, 오버레이 초기화, 오토 플레이 적용 (`IsScorable` 안전장치 포함) |
| `PlayDirector` | `OnDestroy()` | Postfix (`PlayAssist.cs`) | 오버레이 비활성화, 오토 플레이 복원, `ModHealth.ReportPlay()` 호출 |
| `InGameSettingManager` | `Save()` | Prefix + Postfix (`PlayAssist.cs`) | 오토 플레이 임시 적용 값이 게임 설정 파일에 저장되지 않도록 저장 전후 복원/재적용 |
| `PatternPlayer` | `ProcessJudgeResult(JudgeResult)` | Prefix (`PlayAssist.cs`) | 판정바 오차 수집 및 커스텀 곡 전용 올 퍼펙트(`PerfectHigh`) 적용 |
| `PatternPlayer` | `ProcessTickResult(JudgmentType)` | Prefix (`PlayAssist.cs`) | 커스텀 곡 전용 롱노트 틱 올 퍼펙트(`PerfectHigh`) 적용 |
| `PatternPlayer` | `ProcessInputEvent(InputEvent)` | Prefix (`PlayOverlay.cs`) | 키뷰어 원시 키코드 입력 상태 수집 |

*참고 (`JudgmentType` 열거형)*: `Undefined`(0), `PerfectHigh`(1), `Perfect`(2), `Great`(3), `Good`(4), `Miss`(5).

---

## 6. 선택 진단 패치 (`DiagnosticPatches.cs`, `DiagnosticLogs=1`일 때만 수동 적용)

평소에는 후킹하지 않으며, `SaveCustomKey\config.txt`에서 `DiagnosticLogs=1`을 켤 때만 `AccessTools.TypeByName`으로 개별 적용됩니다.

- `PlayDirector.TrySaveScoreAndGameOver` (Prefix)
- `ErrorDialogCollection.APIServerError` (Prefix)
- `InGameSettingHelper.IsScorable` (Postfix)
- `PatternPlayer.SetPatternDataToPlay` (Prefix)
- `AbstractPreFilteredCache.UpdatePostProcessData` (Postfix)

---

## 7. 오프라인 독립 실행 모드 (`OfflineMode=1`일 때만, `[OfflineOnly]`)

모든 Prefix/Postfix 첫 줄에서 `Offline.Enabled`(시작 시 한 번 읽은 값)를 확인하고, 꺼져 있으면 게임 원본을 그대로 실행한다. 하네스 `[Offline]`이 빌드된 DLL의 IL에서 이 확인이 있는지, 스팀·플랫폼 타입을 겨냥한 패치에 `[OfflineOnly]`가 붙었는지 검사한다.

| 대상 | 패치 / 파일 | 역할 |
| :--- | :--- | :--- |
| `SteamAPI.Init` / `InitEx` / `RestartAppIfNecessary` / `IsSteamRunning` / `RunCallbacks`, `SteamUser.GetSteamID` / `BLoggedOn` / `GetAuthTicketForWebApi`, `InteropHelp.TestIfAvailableClient`, `Platform.PlatformUserId` / `GetAuthToken` | Prefix (`SteamBypassPatches.cs`) | 스팀 클라이언트 없이 SDK 초기화·사용자 ID 성공 처리 (`OfflineUserId`) |
| `LoginProcedure.LoginAndLoadProfile`, `LoginInitialize.Init`, `LoginSurface.LoginSteam`, `UserSurface.GetUserProfile` / `GetUnlockedFeatures`, `ContentsSurface.GetContentsAvailability` | Prefix (`SteamBypassPatches.cs`) | 로그인 성공 + `RuntimeDatabase` 오프라인 프로필(`OfflineNickname`), 해금 기능·카탈로그 권한 빈 응답. 로그인 완료 시 `Offline.NoteBootCompleted` |
| `RewardManager.Load` / `FetchAllUnlockStates`, `PersonalAgendaToastBoardFriendsPage.FetchFocusInfoViaAPI`, `SocialSurface.GetScoreRecordsOfFocusedUsers` / `GetFollowingList` | Prefix (`SteamBypassPatches.cs`) | 보상·소셜 API를 완료된 빈 결과로 (무한 대기 방지) |
| `HttpRequestInterceptor.IsCriticalError` / `HandleCriticalError`, `ErrorDialogCollection.FatalAPIServerError` / `APIServerError` / `ShowErrorForCode`(int·SurfaceResult) / `ShowNotLoggedInDialogAndRestart` / `ShowMaintenanceDialogAndQuit` | Prefix (`SteamBypassPatches.cs`) | 서버 오류 팝업·재시작·종료 차단. 막을 때마다 `Offline.NoteBlockedError` |
| `Application.Quit()` | Prefix (`SteamBypassPatches.cs`) | `Offline.ShouldBlockQuit`: 로그인 완료 전이거나 오류 차단 후 15초 안일 때만 막는다 (사용자의 메뉴 종료는 허용) |
| `StoryManager.IsPrologueFirstHalfRead` / `IsPrologueSecondHalfRead` / `IsStoryRead`, `TitleDirector.LoadPrologueScene`, `TutorialLauncher.Enter`, `StorySequenceDirector.AskTutorialAndProceed` / `LoadTutorialScene` | Prefix (`SteamBypassPatches.cs`) | 프롤로그·튜토리얼 건너뛰기 |
| `AiringCurtainController.Leave`, `FadingCurtainController.Leave`, `SongSelectDirector.Awake` / `OnSceneVisible`, `DetailViewerManager.SetSong` | Postfix (`CurtainBypassPatches.cs`) | 닫힌 채 남은 `ACCESSING..` 커튼 비활성화. `SetSong` 경로는 1초에 한 번만 씬 전체를 찾는다 |
| `SectionController.Initialize` | Prefix (`CurtainBypassPatches.cs`, 모드 무관) | 커스텀 패턴(섹션 캐시 없음)은 초기화를 건너뛴다 |

---

## 8. 초기 분석 후 사용하지 않은 후보 (참고)

- `PatternPlayer.SetMainTrackToPlay` / `PreviewSoundController` 로더 / `SoundData..ctor`: 패치 미발동 또는 IL2CPP 백엔드 초기화 실패 → `SoundData.FromHEAudioClip`으로 대체.
- `PlayDirector.LoadGame`: `LoadTargetPattern`에 인라인되어 미발동 → `IResourceLocator.Locate` 키 치환으로 대체.
- `InGameSettingHelper.IsAutoPlayMode` / `InputStateMachine.Interpret`: 호출부 인라인 → `ExtraScheme.SetExtra` / `PatternPlayer.ProcessInputEvent`로 대체.
- `PatternPlayer.EvaluateVideo` / `UpdateFastSlow`: `BGAPlayer.SetClip` / `ProcessJudgeResult`로 충분해 미사용.
