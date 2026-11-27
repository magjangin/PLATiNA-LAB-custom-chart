# 업데이트 내성 감사 (2026-11-08)

기준: 게임 Build `25981392`, MelonLoader 0.7.3.

근거 표기: **실측**(실행·디컴파일로 확인) / **판독**(코드를 읽어 판단) / **일반 동작**(라이브러리의 알려진 동작, 이번에 재현은 안 함).
실제 게임 업데이트를 겪어 본 것은 아니다. 수정 후 게임에서 커스텀 곡을 결과 화면까지 플레이해 정상 경로를 확인했다(`[Health]` 로그, 필수 패치 10종 + Locate 3건, 핵심 훅 호출 정상). 폴백 경로 자체와 오토 플레이 안전장치는 실행되지 않았으므로 **인게임 미확인**이다.

## 핵심 원칙: 같은 메서드 안의 `try`는 누락 멤버를 잡지 못한다

모드 DLL은 컴파일 시점의 인터롭을 참조한다. 업데이트로 멤버가 사라지면 그 멤버를 **직접 참조하는 메서드는 JIT 단계에서 실패**하고, 메서드 본문은 시작되지 않는다.

**실측:** 순수 .NET 6.0.36에서 생성자·메서드·필드를 제거한 라이브러리로 바꿔치기하고 실행했다. 세 경우 모두 메서드 첫 줄도 실행되지 않았고, 호출자의 `try`에서만 잡혔다.

```
[ctor]   OUTER caught: MissingMethodException
[method] OUTER caught: MissingMethodException
[field]  OUTER caught: MissingFieldException
```

**규칙:** 폴백을 넣을 때는 위험한 직접 참조를 `[MethodImpl(NoInlining)]` 별도 메서드로 빼고, 호출하는 쪽에서 `try`로 감싼다.

## 처리 결과

| # | 문제 | 상태 |
|---|---|---|
| H1 | `BuildLocalRecord`의 Reflection 폴백에 도달 불가 | **수정.** `CreateRecordDirect`, `FillSnapshot` 분리 |
| H2 | `SetSongField`, `SetPatternField` 직접 필드 참조 | **수정.** `*Direct` 분리 |
| H1 동일 구조 | `CustomMedia.LoadImage`, `SetUrl` | **수정.** 표준 경로와 icall 경로를 각각 분리 |
| H3 | 오토 플레이 enum 상수 고정, 기록 보호를 게임에만 의존 | **수정.** 이름으로 값을 찾고, 적용 후 `IsScorable()`이 true거나 확인 불가면 복원 |
| M3 | 필수 패치가 빠져도 커스텀 곡 주입 | **수정.** `ModHealth`가 시작 시 점검하고 누락이면 주입 중단 |
| M4 | 호출이 안 되는 훅을 감지 못함 | **수정.** 커스텀 곡마다 Locate/Parse/Music/Video 호출을 기록해 플레이 씬 종료 시 경고 |
| M5 | 게임 빌드 확인 없음 | **수정.** 시작 시 `appmanifest_3430470.acf`의 buildid를 기준과 비교해 로그 |
| L1 | `SafeSetFieldOrProperty` 반환값 무시 | **수정.** 실패 시 경고 |
| L2 | 생성자 순회가 첫 예외에서 중단, `(IntPtr)` 생성자 시도 | **수정.** 후보별 `try`, `(IntPtr)`·복사 생성자 제외 |
| L3 | 탐색 실패를 캐시 안 함, 캐시가 스레드 안전하지 않음 | **수정.** `ConcurrentDictionary` + null 결과 캐시 |
| L4 | 동작하지 않는 `<>` 타입 필터 | **제거** |
| L6 | 빈 `IsLock`/`IsNotOwned` Postfix, 호출되지 않는 `SongListBuilder.Build` 패치 | **제거.** `Diag_*`는 디버깅용으로 유지 |

## 수정하지 않은 것

- **M1 `[HarmonyPatch]` 인자 타입 미지정:** 게임에 오버로드가 추가되면 `AmbiguousMatchException`으로 그 패치만 실패한다(실측: AccessTools). 하지만 인자 타입을 명시하면 더 흔한 시그니처 변경에서도 실패하게 되므로 유지한다.
- **M2 Prefix 인자 이름 바인딩:** 이름이 바뀌면 그 패치 클래스가 패치 시점에 실패한다(일반 동작). `__0` 위치 인자로 바꾸면 깔끔한 실패가 잘못된 인자를 조용히 받는 문제로 바뀌므로 유지한다.
- **L5 `WrapAction`:** 예외 격리 용도일 뿐 업데이트 내성과 무관하다. 동작은 그대로 둔다.
- **판정 enum:** `JudgmentType` 등은 컴파일 상수로 남아 있다. 올 퍼펙트는 커스텀 곡에서만 동작하고 나머지는 표시용이라 영향이 작다. 재빌드하면 새 값으로 맞춰진다.

## 잘 되어 있는 것

- **패치 클래스별 격리:** `MelonBase.HarmonyInit`이 클래스마다 `try/catch`를 건다. 패치 하나가 실패해도 나머지는 적용된다(실측).
- **자동 생성 이름·주소 비의존:** 런타임 코드에 `Method_Private_...` 이름과 RVA가 없다. RVA는 주석에만 있고 업데이트 후에는 무효가 된다.
- **데이터 자동 탐색:** 기반 곡 선택, 유효 난이도 키, `Locate` 구현체 모두 런타임에 찾는다.
- **값 검증:** `JudgmentTimingMap`은 범위와 순서를 검사하고 이상하면 기본값을 쓴다.
- **안전한 실패 방향:** 올 퍼펙트는 `CustomSongs.Current != null`일 때만 켜진다.

## 2차 리뷰 (2026-11-12): 람다 탐색 강화 및 코드 리뷰 수정

인게임: 수정 빌드로 게임을 실행해 `[KeyRedirect]` Locate 3건 패치, `[Health] 필수 패치 정상 (10종 + Locate 3건)`을 확인했다. 선곡 화면 로딩에서 `__0` 바인딩 Prefix로 키 치환이 실제로 일어남을 확인(`Cover/hwa_001.png` → `Cover/wits_biz.png`, `Music/Preview/hwa_001.fmod.ogg` → `Music/Preview/wits_biz.fmod.ogg`). 인게임 플레이(차트/BGM/BGA 로드)까지는 이번 빌드로 다시 돌려 보지 않았다.

### 컴파일러 생성 타입/메서드 판별 (`InteropNames`)

Il2CppInterop이 바꾼 이름을 실측했다. 바뀐 타입에는 `[ObfuscatedName("원래 전체 이름")]`이 붙는다.

| 원래 | 인터롭 |
|---|---|
| `<>c`, `<>c__114`1` | `__c`, `__c__114`1` |
| `<>c__DisplayClass102_0` | `__c__DisplayClass102_0` |
| `<Start>d__15` | `_Start_d__15` |
| `<<BuildItems>b__17_0>d` (async 람다) | `__BuildItems_b__17_0_d` |
| `<<Awake>g__PreloadLazyObjects\|14_0>d` (async 로컬 함수) | `ValueTypeCompilerGeneratedNPrivateSealed…Unique` (이름을 새로 합성) |
| `<>f__AnonymousType0`2`, `<>O`, `<>z__…`, `<x>e__FixedBuffer` | `__f__AnonymousType0`2`, `__O`, `__z__…`, `_x_e__FixedBuffer` |
| 람다 본문 `<get_AllLocations>b__8_0` | `_get_AllLocations_b__8_0` (메서드에는 속성 없음) |

판별 순서: 1) 원래 이름(`ObfuscatedName`/`OriginalName`의 마지막 중첩 이름)에 `<`, 2) `[CompilerGenerated]`, 3) 위 표의 이름 패턴. 감싸는 타입까지 거슬러 올라간다. 속성 타입을 직접 참조하지 않고 이름으로 비교하므로 인터롭이 바뀌어도 JIT 단계에서 실패하지 않는다.

**실측 검증 (오프라인 하네스, 실제 인터롭 어셈블리):**
- 인터롭 어셈블리 155개, 원래 이름에 `<`가 있는 타입 2553개 → **이름 패턴만으로** 누락 0, 오탐 0 (속성이 없어져도 같은 결과).
- Unity.Addressables 람다 본문(`_b__`) 메서드 47개 전부 판별.
- 처음 패턴은 async 람다·합성 이름·FixedBuffer 등 115개를 놓쳤다 → 패턴 추가 후 0.

### Locate 탐색 (`AddressableKeyRedirect`)

| # | 문제 | 상태 |
|---|---|---|
| R1 | 람다/클로저 타입을 거르지 않음 (이전 감사 L4에서 `<>` 필터 제거 후 대체 없음) | **수정.** `InteropNames.IsCompilerGenerated(method)` |
| R2 | `IResourceLocator`를 이름 문자열로만 제외 → 재작성 중 빠지자 인터페이스 프록시가 후보로 잡힘(오프라인 테스트로 발견) | **수정.** 인터롭은 IL2CPP 인터페이스를 `Il2CppObjectBase` 직접 상속 클래스로 만든다 → `Il2CppSystem.Object` 비상속 또는 `NativeMethodInfoPtr_Locate_…_Abstract_…` 필드로 판별 (`IsIl2CppInterfaceOrAbstract`) |
| R3 | 인자 수(1~4)만 확인 | **수정.** 엄격 조건 `bool Locate(Il2CppSystem.Object, Type, out …)`, 0건이면 완화 조건(첫 인자 `Il2CppSystem.Object`)으로 재탐색 |
| R4 | Prefix가 인자 이름 `key`에 묶임 | **수정.** 첫 인자 타입을 탐색 단계에서 확인하므로 `__0`으로 받는다 (정적 패치의 M2 판단과 달리, 여기서는 잘못된 인자를 받을 위험이 없음) |
| R5 | 어셈블리 이름 `Unity.Addressables` 고정 | **수정.** 이름에 `Addressables`가 든 어셈블리 전부, `ReflectionTypeLoadException`이 나도 읽힌 타입으로 진행 |
| R6 | Locate가 일부만 패치돼도 알 수 없음 | **수정.** `ModHealth`가 검증 빌드 기준 3건보다 적으면 경고 |
| R7 | 곡 ID를 `Contains`로 처음 맞는 것 선택 → `hwa_1000`이 `hwa_100`으로 치환될 수 있음 | **수정.** 가장 긴 ID (`CustomSongs.FindIdIn`) |

### 그 밖의 코드 리뷰 수정

| # | 문제 | 상태 |
|---|---|---|
| C1 | `EncodingHelper`가 UTF-8이 아니면 무조건 GBK — 문서(README/AGENTS)의 Shift-JIS/CP949 감지와 불일치, 일본어 BMS 제목 깨짐 | **수정.** 932/949/936/950 바이트 패턴 점수 비교. 일·한·간체·번체 샘플 7종 감지 확인 |
| C2 | 차트 캐시가 차트 파일 수정 시간만 비교 → music.ogg/video.mp4를 바꿔도 이전 길이(endPosition)의 차트 사용 | **수정.** 차트+음원+영상 수정 시간 스탬프, lock 추가 |
| C3 | 커버 스프라이트가 수집되면 LRU에 같은 경로가 중복 등록 → 내보낼 때 새 스프라이트까지 파괴 | **수정** |
| C4 | 필수 패치 누락으로 주입을 꺼도 빈 `custom` 카테고리는 추가됨 | **수정** |
| C5 | BPM 폴백이 `*.bms`만 검색 (`.bme/.bml/.pms` 무시) | **수정.** `BmsConverter.FindFor` 사용 |
| C6 | `UpdateResilience` 탐색 코드 중복, 빈 이름에서 예외, 같은 이름 프로퍼티 여럿이면 `AmbiguousMatchException` | **수정.** `FindProperty`/`FindField`로 통합 |
| C7 | 판정바 폴백 GREAT ±85 (실측 ±80), 의미 없는 항등 함수 `customPathWrite`, CustomMedia 주석의 옛 훅 설명 | **정리** |

## 3차 (2026-11-14): 훅 예외 격리, 병목, 람다 판별 신호 추가

인게임: 수정 빌드로 게임을 두 번 실행해 메인 메뉴까지 확인했다. `[HookGuard] 훅 바인딩 사전 점검 정상 (20개 메서드, 3ms)`, `[Health] 필수 패치 정상 (10종 + Locate 3건)`, 커스텀 곡 주입, `[KeyRedirect] 네이티브 문자열 검사 자체 검증 32건 일치`. 사용자가 PC를 쓰는 중이라 게임 입력을 보내지 않았고, **선곡 화면·플레이·결과 화면은 이번 빌드로 미확인**이다.

### 업데이트 내성: 훅 본문이 깨지면 일반 곡도 망가진다 (`HookGuard`)

**실측(문자열):** Il2CppInterop의 native→managed 트램펄린은 훅에서 올라온 예외를 잡아 `During invoking native->managed trampoline`으로 로그하고 원본 대신 기본값을 돌려준다(`Il2CppInterop.HarmonySupport.dll`에 `ReportException`, `BeginCatchBlock`).
**판독:** 1차 감사의 "같은 메서드의 `try`는 누락 멤버를 못 잡는다"가 Prefix 본문에도 그대로 적용된다. 본문이 사라진 멤버를 직접 참조하면 그 Prefix는 JIT 단계에서 실패하고, 빠른 경로(`if (커스텀 곡 아님) return`)가 있어도 소용없다(메서드 전체가 컴파일되지 않음). 그러면 `RequestGameStart`·`SendGameEnd`·`SoundData.FromHEAudioClip`·`CategoryCacheFactory.CreateAll` 등 **모든 곡**이 지나가는 게임 메서드가 기본값을 돌려주게 된다.

| # | 훅 | 직접 참조하던 것 | 수정 |
|---|---|---|---|
| U1 | `RequestGameStart` | `GameSessionResult`, `UniTask`/`SurfaceResult` 생성 | `StartLocal`로 분리. 실패하면 커스텀 곡만 원본(서버 거부)으로 넘김 |
| U2 | `RenewGameSession`, `SendGameEnd` | `UniTask`/`SurfaceResult` 생성, `request.Score` | `RenewLocal`/`Success`/`NetworkError`/`FinalScore` 분리. 커스텀 곡은 어떤 실패에서도 서버로 보내지 않음 |
| U3 | `SoundData.FromHEAudioClip` | `audioClip.name` | `TryReplace` 분리 (효과음 포함 전 음원 생성 경로) |
| U4 | `BGAPlayer.SetClip` | `_videoPlayer` | `Apply` 분리 |
| U5 | `PatternPlayer.DetermineRunning` | `_isLastNoteProcessingTriggered`, `_patternData`, `CurrentTimeMicroseconds` | `TryHold`/`ClearFlag` 분리 |
| U6 | `ProcessJudgeResult`, `ProcessInputEvent` | `JudgeResult.*`, `LineMode` | `Handle`/`HandleInput` 분리 |
| U7 | `CategoryCacheFactory.CreateAll`, `UpdatePostProcessData` | `TagCategoryCache` 생성, `Key` | `AddCategory`/`LogCustom` 분리 |
| U8 | `SongInfo.IsLock` 등 5종 | `__instance.Id`, `EncryptedBool` 암시 변환 | `CustomSongs.IsCustomSong`/`EncryptedFalse`로 통합 |
| U9 | `InGameSettingManager.Save` | `Extra.SetExtra` | `SetExtraDirect` 분리 |
| U10 | `GetPatternFileName` | try 없음 (`IsCustom` 예외가 그대로 올라감) | try + `Of`를 `NoInlining` |

`HookGuard.Fail`: 바인딩 실패(`MissingMember`/`TypeLoad`/`BadImageFormat`)면 그 훅을 꺼서 게임 원본만 남기고(매 호출 예외 반복 방지), 필수 경로면 `ModHealth.DisableCustomSongs`로 커스텀 곡 주입도 끈다. 그 밖의 예외는 훅을 유지하고 같은 예외는 한 번만 경고한다. 꺼진 훅은 플레이 종료 시 `[Health]`가 다시 알린다.

**사전 점검 (`HookGuard.Precheck`, `[Guarded]`):** 분리한 메서드에 `[Guarded("훅 이름", required)]`를 붙이고 시작 시 `RuntimeHelpers.PrepareMethod`로 미리 JIT한다.
**실측(순수 .NET 6.0, 멤버를 지운 라이브러리로 바꿔치기):** 없는 메서드·필드·프로퍼티를 참조하는 메서드는 `PrepareMethod`에서 `MissingMethodException`/`MissingFieldException`, 정상 메서드는 통과.
→ 업데이트로 깨진 훅은 첫 호출 전에 꺼지고, 필수 경로면 커스텀 곡이 처음부터 주입되지 않는다. 인게임 정상 빌드에서 20개 메서드 3ms.

### 병목

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| B1 | `AddressableKeyRedirect.Prefix` | 게임의 모든 Addressables 로드마다 키를 관리 문자열로 변환(할당) | IL2CPP 문자열 UTF-16 버퍼에서 `hwa_`를 직접 검색(`il2cpp_string_chars`). 처음 32건은 기존 방식과 비교해 다르면 기존 방식으로 전환 — 인게임 32건 일치 |
| B2 | `CustomCharts.Process` | **일반 곡도** 차트 JSON 전체를 관리 배열로 복사하고 새 MemoryStream으로 다시 감쌈 | 덤프 꺼짐 + 커스텀 차트 없음이면 원본 스트림 그대로 |
| B3 | `CustomSong.MusicPath/VideoPath/ThumbnailPath` | 호출마다 후보 파일 최대 12개 `File.Exists` (커버·음원 훅에서 반복) | 5초 캐시 + 곡 시작·미리듣기 때 `RefreshMedia` |
| B4 | `ById`/`ByPatternIndex`/`ByKey` | 모든 곡의 훅에서 목록 순회 | 사전 (Scan에서 통째로 교체) |
| B5 | `SongInfo` 판정 훅 5종 | 주입 전에도 모든 곡의 Id를 IL2CPP 문자열에서 변환 | `Injected` 전에는 바로 원본 |
| B6 | `DetermineRunning` | 매 프레임 + 입력 스레드에서 `PlayPattern` 래퍼 생성 | `CustomSongs.Current == null`이면 바로 원본 |
| B7 | `ProcessJudgeResult` | 판정바를 꺼도 판정을 모으기만 하고 비우지 않음, 올 퍼펙트가 같은 세터를 직접+리플렉션으로 두 번 | 둘 다 꺼지면 아무것도 안 읽음, 대기열 상한 256, 세터 실패 시에만 리플렉션 |
| B8 | `PlayOverlay` | 매 프레임 판정바 틀·범위·중앙선·키 칸 위치·색·글자 재설정, `RemoveAll` 람다 할당, 판정 문구 문자열 매 프레임 생성 | 화면 크기·설정이 바뀔 때만 재설정, 판정 문구는 새 판정일 때만, 키 칸은 누름 상태가 바뀐 칸만 |
| B9 | `CustomCharts.Sanitize` | 글자마다 `Path.GetInvalidFileNameChars()` 새 배열 | 정적 집합 |
| B10 | `IsCustomId` | 문화권 의존 `StartsWith` | `Ordinal` |

### 람다(컴파일러 생성) 판별: 멤버 모양 신호 (`InteropNames`)

인터롭은 컴파일러가 만든 필드를 같은 이름의 프로퍼티로 내보낸다(실측 빈도: `__N__N_N` 1886, `__u__N` 1174, `__1__state` 1097, `__t__builder` 931, `__4__this` 884, `__9` 735 …). 타입 이름 규칙(`ValueTypeCompilerGenerated…` 같은 합성 이름)이나 `ObfuscatedName` 속성이 업데이트로 바뀌어도 필드 모양은 C# 컴파일러가 정하므로 4번째 신호로 추가했다.

**실측 (`tools/interop-harness`, 인터롭 155개 어셈블리 30,509개 타입, 정답 2,834개):**

| 신호 | 누락 | 오탐 |
|---|---|---|
| 이름 패턴 단독 | 0 | 0 |
| 멤버 모양 단독 | 861 (상태 머신 1096/1141, 람다 캐시 735/889, 클로저 141/658 판별) | 0 |
| 이름 패턴 + 멤버 모양 (속성 없이) | 0 | 0 |
| 전체 | 0 | 0 |

- 2차 감사의 정답 기준(`ObfuscatedName`의 `<`)은 인터롭이 이름을 바꾸지 않고 `<`를 그대로 둔 UnityEngine 모듈 타입 281개를 빠뜨렸다. 정답에 넣고, `<` 검사를 이름 패턴 신호로 옮겼다.
- 메서드 이름 패턴의 숫자 꼬리를 여러 개 허용(`_b__8_0_0` 같은 인터롭 오버로드 접미사 대비). 람다 메서드 Unity.Addressables 47/47, Il2CppPlatinaLab.Main 653/653.
- 메서드 판별 결과도 캐시.
- Locate 엄격 조건 3건 그대로.

### 리팩토링 감사

| 항목 | 상태 |
|---|---|
| SongInfo 판정 훅 5종의 같은 조건 복붙 | **수정.** `IsCustomSong` |
| 곡/차트 조회 선형 탐색 3종 | **수정.** 사전 |
| 일반 곡 훅의 격리 방식이 훅마다 달랐음 | **수정.** `HookGuard` 한 곳 |
| `Diag_Exit`가 진단이 아니라 나가기 상태 정리 담당 | **수정.** `PlayDirector_Exit_Patch`로 이름 변경 |
| `SetPatternDataToPlay` 로그 "커스텀 차트 주입 대기 지점"(옛 계획) | **수정.** 커스텀/일반 곡만 표시 |
| 클리어 연출 보류 로그가 게임 실행 후 첫 곡에만 | **수정.** 곡(PatternPlayer)마다 |
| `PlayOverlay.cs`(판정바+키뷰어 약 600줄) 분리 | 보류. 동작 변화 없이 diff만 커지고, 다음에 둘 중 하나를 크게 고칠 때 나누는 편이 낫다 |
| `Diag_TrySave`/`Diag_APIServerError`/`Diag_IsScorable` | 유지. 결과 화면·오토 플레이 디버깅용, 호출 빈도 낮음 |

## 4차 (2026-11-14): 전체 파일 리팩토링, 병목 제거, 업데이트 내성 보강

검증: Release/Debug 빌드 경고·오류 0건, 오프라인 하네스(`tools/interop-harness`)에서 컴파일러 생성 판별 누락 0·오탐 0, Locate 엄격 조건 3건, `[Guarded]` 44개 메서드 사전 JIT 준비 실패 0건, `[HarmonyPatch]` 31개 타깃 누락·중복 0건. **인게임 실기 실행은 이 빌드로 미확인.**

### 업데이트 내성 보강

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| R4-1 | `PlayOverlay.FindFont` | `LocalizeTMPFont`와 폴백 `TextMeshProUGUI` 탐색이 한 메서드에 있어 전자 변경 시 폴백 미도달 | `TryFindLocalizedFont`/`TryFindAnyTmpFont`를 `[Guarded]` 별도 `NoInlining` 메서드로 분리 |
| R4-2 | `OfflineSession.BuildLocalRecord` | `SectionRecords`, `EndGameResponse` 배열, `AccuracyV2Formula`/`MasteryV2Formula`가 혼재 | `EnsureSectionRecords`/`CreateEndGameResponse`/`SetEmptyResponseArrays`/`ComputeAccuracy`/`ComputeMastery`로 분리 + `Mastery` 리플렉션 폴백 |
| R4-3 | `CustomSongs.Inject`, `GetDataSet` | `Inject` 미표기, `ESDProduct`/`_difficultyTypeInfos`/`_songMetaData`/`_eyeCatchV1` 직접 참조 | `Inject`에 `[Guarded(..., required: true)]`, 선택 필드 헬퍼 분리, `GetDataSet` 리플렉션 `CreateFallback` 추가 |
| R4-4 | `PlayAssist.ApplyAutoPlay`/`RestoreAutoPlay` | `InGameSettingManager.Instance.Extra` 직접 참조 | `TryGetCurrentExtra`/`TrySetExtra`를 `[Guarded("PlayAssist.AutoPlay")]`로 분리 |
| R4-5 | `CustomCharts.ProcessPlayer` | `KeyOf`가 `PlayPattern`/`FilePath`/`GroupId`를 한 번에 접근 | `TryGetCustomKey`(required)와 `ReadVanillaKey` 분리, `Parse` 패치 `HookGuard` 적용 |
| R4-6 | `CustomMedia` | `HEAudioClip` 직접 필드 설정·icall 경로 `Precheck` 누락 | `CreateAudioClip`/`SetClipDataDirect` 분리 + `SafeSetFieldOrProperty` 폴백, `*Icall`에 `[Guarded]` 추가 |
| R4-7 | `UpdateResilience` | `Type.GetField`/`GetProperty`가 상위 클래스 `private` 백킹 필드를 찾지 못함, 타입 불일치 | `BaseType` 계층 순회 탐색 및 `Convert.ChangeType`/enum 변환 추가 |
| R4-8 | `tools/interop-harness` | 컴파일러 생성·Locate 외에 모드 DLL의 `[Guarded]`/`[HarmonyPatch]` 검증 없음 | 빌드된 `PLATiNALAB.Mod.dll` 대상 `PrepareMethod` 44개 및 `[HarmonyPatch]` 31개 오프라인 검증 추가 |

### 병목 제거 · 리팩토링

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| P4-1 | `DetermineRunning` 패치 | 정상 실행 시 `Postfix`와 `Finalizer`가 둘 다 `ClearFlag`를 호출해 IL2CPP 쓰기 2배 | `Postfix` 제거, `Finalizer`로만 복원 |
| P4-2 | `PlayOverlay.Update` | 비플레이 화면에서도 매 프레임 `s_root == null` 및 `SetActive` IL2CPP 호출 | `s_rootActive`/`s_barActive`/`s_keyActive` 캐시로 비플레이 프레임 IL2CPP 호출 0건 |
| P4-3 | `CustomCharts.Process` | 일반 곡에서 매번 `PlayPattern`/`FilePath`/`GroupId` 조회, BMS 변환 시 JSON 2회 파싱·직렬화 | `!DumpEnabled && !HasCustomChartFiles()` 빠른 경로, `PrepareChart(JsonObject, ...)` 오버로드로 1회 직렬화 |
| P4-4 | `CustomMedia` | Unity 6 `ReadOnlySpan` 누락으로 `ImageConversion.LoadImage`/`VideoPlayer.url`마다 예외 발생, `s_clips` 상한/핫리로드 없음 | 첫 `MissingMethodException` 후 표준 API 건너뛰기, `HEAudioClip` LRU 상한(8개) 및 파일 수정 시각 핫리로드 |
| P4-5 | `MediaDuration`, `ModConfig`, `TextSanitizer`, `EncodingHelper` | 매 호출 버퍼 할당, 변경 없는 `config.txt` 재로드, 정상 문자열 Regex 호출, 대용량 문자열 `Split` | 수정 시각 캐시, UTF-8 리터럴(`u8`), `config.txt` 타임스탬프 비교, 빠른 경로, `StreamReader` 라인 읽기 |
| P4-6 | `BmsConverter`, `CustomSongs`, `InteropNames` | `Convert` 내 `OrderBy` 3회, `BmsPath` 매번 디렉터리 스캔, 난이도 별칭 테이블 중복, `OriginalName` 우선 평가 | `Parse` 끝 1회 정렬, `MediaSlot` 5초 캐시 공용화, 난이도 테이블 일원화, `NamePattern` 우선 및 `DerivesFromIl2CppObject` 캐시 |

## 5차 (2026-11-15): 전체 파일 재감사, 핫패스 병목, 정규화 버그

검증: Release/Debug 빌드 경고·오류 0건, 오프라인 하네스에서 `[Guarded]` 45개 사전 JIT 실패 0건, `[HarmonyPatch]` 31개 타깃 누락·중복 0건, `TextSanitizer` 회귀 10건 통과. **인게임 실기 실행은 이 빌드로 미확인** (네이티브 필드 직접 읽기는 게임 안에서만 검증된다 — 아래 확인 항목 참고).

### 병목

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| P5-1 | `CustomSongs.IsCustomSong` (SongInfo `IsLock`/`IsAvailable`/`IsNotOwned`/`IsHiddenFromDefaultList`/`IsNewSongMarkTarget`) | 주입 후 모든 곡의 판정마다 `SongInfo.Id` 게터 = `il2cpp_runtime_invoke` + 관리 문자열 생성. 선곡 화면은 곡 수 × 판정 수만큼 반복 | `NativeField`가 `<Id>k__BackingField` 오프셋을 IL2CPP 원래 이름으로 찾아 문자열 포인터를 직접 읽고, `NativeStrings.StartsWith`로 접두사(`hwa_`) 확인 (할당 0) |
| P5-2 | `CustomSongs.Of(PatternInfo)` (`GetPatternFileName` Postfix·`SendGameEnd`) | 모든 패턴에서 `PatternIndex` 게터 runtime invoke | `<PatternIndex>k__BackingField` 직접 읽기 |
| P5-3 | `DetermineRunning` 훅 | 커스텀 곡 플레이 중 매 프레임 + 입력 스레드에서 `PlayPattern`(runtime invoke)·`PatternIndex`·`_patternData`·`DurationMicroSec` 재조회 | PatternPlayer 포인터별 `HoldState`(커스텀 여부·보류 해제 시각) 캐시. `RequestGameStart`마다 비움. 패턴/차트가 아직 없으면 캐시하지 않음 |
| P5-4 | `CustomSongs.Inject` | `BuildNameBanners`·`CreateAll`마다 패턴 전체를 `PatternIndex` 게터로 훑어 주입 여부 확인 | 같은 목록 포인터·개수면 바로 반환 (목록이 바뀌면 기존 확인으로) |
| P5-5 | `PlayOverlay` | 판정 틱마다 `Place`가 `Screen.height`(IL2CPP) 호출, 위치가 안 바뀌어도 매 프레임 재배치 | 화면 크기는 프레임당 1회, 틱 위치는 기록 추가·만료·배치 변경 때만, 매 프레임은 색(투명도)만 |
| P5-6 | `CustomCharts.ReadAll` | `Il2CppStructArray<byte>`를 LINQ `ToArray`로 복사 → 요소마다 가상 인덱서 | `AsSpan().ToArray()` (네이티브 버퍼 한 번에 복사) |

`NativeField` 안전장치: 필드를 못 찾거나 오프셋이 객체 헤더 안·4096 초과면 꺼지고 게터 경로로 돌아간다. 처음 32건은 게터 결과와 비교한다 — `Id`는 접두사 판정이 아니라 **문자열 전체**를 비교한다(일반 곡은 모두 false라 접두사만 비교하면 잘못된 오프셋을 못 잡음). 다르면 `[Native] ... 관리 경로로 전환` 경고 후 게터만 쓴다. Locate 훅의 네이티브 문자열 검사도 같은 `NativeStrings`를 쓴다.

### 정확성

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| B5-1 | `TextSanitizer` 일반 아포스트로피 정규식 | `\s*'\s*`라 따옴표를 축약형으로 오인: `Rock 'n' Roll` → `Rock'n' Roll`, `said 'hello'` → `said'hello'` | 양쪽이 모두 띄어진 경우(`o ' clock`)만 붙임 |
| B5-2 | `TextSanitizer` 축약형 정규식 | `the 'S' rank` → `the'S' rank` | 접미사 뒤에 따옴표가 오면 제외 (`(?!')`) |
| B5-3 | `ModConfig.Load` | 파일에서 지운 줄은 이전에 읽은 값이 게임을 끌 때까지 유지 (예: `AutoPlay=1` 줄을 지워도 계속 켜짐) | 다시 읽을 때 기본값으로 초기화한 뒤 적용 |

### 확인 항목 (인게임)

- `Latest.log`에 `[Native] SongInfo.Id 직접 읽기 자체 검증 32건 일치`, `[Native] PatternInfo.PatternIndex 직접 읽기 자체 검증 32건 일치`. `관리 경로로 전환` 경고가 있으면 기능은 유지되지만 최적화가 꺼진 것이다.
- 선곡 화면에서 custom 카테고리 곡이 보이고 잠김/미보유로 나오지 않는지, 일반 곡 잠금 표시가 그대로인지.
- 커스텀 곡 클리어 연출이 곡 끝에서 나오는지(`클리어 연출 보류` 로그 1회), 재도전 시에도 같은지.

## 6차 (2026-11-16): 파일 지문, 초기화 격리, 필수 패치 이름 오프라인 대조

검증: Release 빌드 경고·오류 0건, 하네스 `[Guarded]` 45개·`[HarmonyPatch]` 31개 실패 0건, 신규 `[Required]` 10개 실패 0건. **인게임 실기 실행은 이 빌드로 미확인** (`Latest.log`는 2026-11-14 빌드 기록).

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| U6-1 | `ModHealth.LogGameAssemblyFingerprint` | 업데이트 감지가 Steam 매니페스트의 `buildid` 하나뿐. 이 프로젝트는 `.acf` 패치 차단·DepotDownloader 복사본을 쓰므로 매니페스트가 실제 파일과 어긋나거나 아예 없을 수 있어 업데이트를 놓칠 수 있음 | `GameAssembly.dll` 실제 크기를 검증 기준(`TestedGameAssemblySize` = 96,829,440바이트)과 비교해 로그. 다르면 매니페스트와 무관하게 경고 (`[Health] GameAssembly.dll ...`) |
| U6-2 | `Core.OnInitializeMelon` | 초기화 4단계(설정·차트·곡 스캔·Locate 패치)가 한 메서드에 연달아 있어 앞 단계 예외가 뒤 단계를 막음 (예: 곡 폴더 스캔 실패 → Locate 패치 미적용) | `Step()`으로 단계별 격리, 실패한 단계만 `[Init] ... 실패` 로그. `OnLateInitializeMelon`의 `ModHealth.CheckOnStart`도 예외가 모드 로드를 막지 않게 감쌈 |
| U6-3 | `tools/interop-harness` | `ModHealth.s_requiredPatches` 이름은 문자열이라 오타·리팩토링으로 `[HarmonyPatch]` 대상과 어긋나도 컴파일 오류가 안 남. 어긋나면 게임에서 항상 "필수 패치 누락"으로 커스텀 곡이 꺼짐 | 하네스가 `[HarmonyPatch]`에서 `선언타입.메서드`(getter는 `get_`) 키를 모아 필수 패치 10종과 대조 (`[Required]`). 어긋나면 하네스가 실패 코드를 반환 |

### 확인 항목 (인게임)

- `Latest.log`에 `[Health] GameAssembly.dll 96,829,440바이트 (검증된 파일)` 한 줄. 게임 업데이트 후엔 `검증 기준 ...와 다릅니다` 경고가 `buildid` 경고와 별개로 나온다.
- `[Init] ... 실패` 줄이 없는지 (있으면 해당 단계만 꺼진 것).

## 7차 (2026-11-18): 클래스 로드 단위 의존성, 게임 record 연산자, 규칙의 기계 검사

검증: Release/Debug 빌드 경고·오류 0건, 하네스 종료 코드 0 — `[Guarded]` 51개·`[HarmonyPatch]` 31개·`[Required]` 10개 실패 0, 신규 `[Layout]` 258개 필드·`[Body]` 386개 메서드·`[BmsFind]` 8건 실패 0. 새 검사 세 가지는 **수정 전 코드에서 각각 8·12·2건을 잡은 뒤** 수정해서 0으로 만들었다. **인게임 실기 실행은 이 빌드로 미확인** (게임 파일은 그대로: buildid 25981392, `GameAssembly.dll` 96,829,440바이트).

### 새 원칙: 메서드가 아니라 클래스 단위로 깨지는 경우

지금까지의 규칙(`[Guarded]` 격리)은 **메서드** JIT 실패만 다뤘다. 순수 .NET 6.0.36에서 라이브러리 타입을 지운 뒤 바꿔치기해 실측했다 (`scratchpad` 실험, 결과만 기록):

| 모드 클래스의 정적 필드 | 타입이 사라진 뒤 그 클래스의 다른 정적 필드(bool)를 읽으면 |
|---|---|
| 게임 enum (`static L.Mode s_mode;`, 초기값 유무 무관) | `TypeLoadException` — 매번 |
| 게임 enum을 담은 모드 struct의 `Nullable` (`static Tick? s_last;`) | `TypeLoadException` |
| 게임 enum 배열 초기화 (`static readonly L.Mode[] a = {…}`) | `TypeInitializationException` → 이후 영구히 사용 불가 |
| 게임 참조 타입을 제네릭 인자로 초기화 (`static readonly List<L.Ref> s = new()`) | `TypeInitializationException` |
| 게임 참조 타입 필드 (`static L.Ref s_ref;`), 초기화 안 한 `List<L.Ref>` | 정상 |
| 게임 참조 타입 필드를 가진 모드 클래스를 제네릭 인자로 (`Dictionary<string, Entry>`), `new Entry()` | 정상 |

즉 **게임 값 타입을 필드로 들고 있는 모드 클래스는 그 타입이 바뀌면 통째로 못 쓴다.** 그 클래스의 무해한 플래그(`PlayOverlay.InPlay`, `PlayAssist.AllPerfectActive`)를 일반 곡 훅이 읽고 있으면, 훅 본문은 멀쩡해도 예외가 나서 트램펄린이 원본 대신 기본값을 돌려준다(3차 `HookGuard` 설명) — `ProcessJudgeResult`(모든 곡·모든 노트)가 그 경우였다.

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| U7-1 | `PlayAssist.s_savedExtra`, `s_autoPlayValue` | `InGameSettingType.Extra` 정적 필드. 판정 훅 2개(`ProcessJudgeResult`·`ProcessTickResult`)가 `AllPerfectActive`로 이 클래스를 건드림 | `int` 저장. 변환은 `[Guarded]` `TrySetExtraRaw`·`SetExtraDirect` 안에서만. `Save` 훅 본문·`RestoreAutoPlay`에서 게임 enum 참조 제거 |
| U7-2 | `PlayOverlay.HitTick.Judgment`, `s_last`, `s_pending`, `s_history`, `Judgments`, `s_mode` | `JudgmentType`·`LineModeType`. `InPlay`를 판정 훅·`Core.OnUpdate`(매 프레임)가 읽음 | `int` 저장. `JudgeColor`/`JudgeName`은 `(int)JudgmentType.X` 상수 비교(컴파일 시 숫자, 타입 로드 없음). 맵 조회는 `[Guarded] LoadRanges` 안에서 변환 |

### 게임 record의 `==` / `!=`

`SongInfo`·`PatternInfo`·`PlayResult`·`FreeModePlayRecordV2` 등은 게임 쪽 C# record라 인터롭이 `op_Equality`를 만들고, 그 본문은 `il2cpp_runtime_invoke`다(`src_dummy` `SongInfo.cs` 확인). 그래서 `song == null`이 **IL2CPP 왕복**이 되고, 연산자가 바뀌면 그 메서드가 JIT에서 실패한다.

| # | 위치 | 영향 | 수정 |
|---|---|---|---|
| P7-1 | `CustomSongs.IsCustomSong` (`song == null`) | 주입 후 모든 곡의 `IsLock`/`IsAvailable`/`IsNotOwned`/`IsHiddenFromDefaultList`/`IsNewSongMarkTarget`마다 1회 — 5차에서 게터 호출을 없앤 바로 그 경로에 남아 있던 runtime invoke | `is null` |
| P7-2 | `CustomSongs.Of` (`info == null`), `GetPatternFileName` Postfix (`__instance == null`) | 모든 패턴의 파일명 조회마다 1~2회 | `is null` |
| P7-3 | `ReadVanillaKey`, `Inject`, `IsPreferredBaseSongDirect`, `CreateState`, `SendGameEnd` Prefix·`BuildLocalRecord` | 곡/플레이당 몇 회 (성능보다 의존성 문제) | `is null` / `is not null` |

`?.`·`??`는 참조 검사로 컴파일되므로 해당 없다. Unity `Object`의 `==`(파괴된 객체 판정)는 의도된 동작이고 엔진 타입이라 검사 대상이 아니다.

### "같은 메서드 안의 try"가 남아 있던 곳

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| U7-3 | `CustomSongs.ReadValidDifficulties` | `_difficultyTypeInfos` 직접 참조와 `try`가 같은 메서드 → 필드가 사라지면 기본 난이도 폴백에 도달하지 못하고 `Inject` 전체(= 커스텀 곡 주입) 실패. 4차 문서의 "격리 + 폴백"은 실제로 동작하지 않았음 | `ReadDifficultyKeysDirect`로 분리 |
| U7-4 | `CustomSongs.RegisterSongMeta` | 같은 구조. `[Guarded]`라 사전 점검은 했지만 `Inject`가 감싸지 않아 실행 시엔 주입 전체 실패 | `RegisterSongMetaDirect`로 분리, 실패 시 경고 후 계속 |
| U7-5 | `CustomSongs.IsPreferredBaseSong` | 같은 구조 (`ESDProduct` 등이 사라지면 주입 전체 실패) | `IsPreferredBaseSongDirect`로 분리, 실패 시 선호 조건만 포기 |
| U7-6 | `GetDataSet` 폴백 `CreateFallback`·`TextResource`, `ForcePerfect` | catch가 있는데 게임 타입을 직접 씀 | 보고용 이름으로 `[Guarded]` (시작 시 사전 JIT 점검) |

### 규칙의 기계 검사 (`tools/interop-harness`)

사람이 읽어서 지키던 규칙을 하네스가 모드 DLL을 읽어 확인한다. 게임 어셈블리 = 인터롭 폴더의 어셈블리 중 `UnityEngine*`·`Unity.*`·`Il2Cppmscorlib`·`Il2CppSystem*`·`Il2CppMono*`를 뺀 것 (엔진·BCL은 Unity 버전이 같으면 바뀌지 않음).

- **`[Layout]`**: 모든 모드 타입의 필드(정적·인스턴스)가 게임 값 타입(Nullable·값 튜플·모드 struct 안 포함)이거나, 게임 타입을 제네릭 인자·배열 요소로 쓰면 실패.
- **`[Body]`** (IL 읽기, `Module.ResolveMember`): ① 모든 메서드에서 게임 타입의 `op_Equality`/`op_Inequality` 호출 ② 훅 진입점(Prefix/Postfix/Finalizer)이 게임 타입·멤버를 직접 참조(자기 시그니처에 이미 있는 타입의 box/stobj 같은 타입 토큰은 제외 — 그 타입이 없으면 패치가 먼저 실패) ③ catch가 있는 비`[Guarded]` 메서드가 게임 멤버를 직접 참조. enum 상수는 IL에 숫자로 들어가므로 잡히지 않는다(= 실제로 타입 로드도 없음).
- **`[BmsFind]`**: 아래 B7-1 회귀.

### 정확성

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| B7-1 | `BmsConverter.FindFor` | 난이도 별칭을 파일명 부분 문자열로 찾아 `over`의 `ex`가 `next_hard.bms`에, `plus`의 `sp`가 `space.bms`에 걸림. 파일 순서가 별칭 순서보다 앞서 `a_ez.bms`가 `b_easy.bms`보다 먼저 선택됨 | 영문 별칭은 낱말(영문 글자 덩어리) 단위, 한글·중국어는 부분 문자열. 정확한 이름 → 정규화 이름 → 별칭 순 |
| B7-2 | `ModConfig.Load` | 기본값으로 초기화한 **뒤에** 파일을 읽어서, 편집기가 저장하는 순간 읽기가 실패하면 로그는 "이전 값 유지"인데 실제로는 기본값(예: `AutoPlay` 꺼짐) | 끝까지 읽은 뒤 초기화·적용 |
| B7-3 | `CustomSongs.Scan` | 한 곡 폴더의 읽기 예외가 스캔 전체를 실패시켜 커스텀 곡이 하나도 안 나옴 | 폴더별 `try`. 번호(곡 ID·패턴 번호)는 폴더 순서 그대로 두고 실패한 번호만 비움 (당기면 다른 곡의 ID가 바뀜) |

### 확인 항목 (인게임)

- `[HookGuard] 훅 바인딩 사전 점검 정상 (51개 메서드, …)`.
- 선곡 화면 custom 카테고리 곡 표시·잠금 없음, 일반 곡 잠금 표시 그대로 (P7-1·P7-2 경로).
- 커스텀 곡 플레이: 판정바·키뷰어 색과 글자(`PERFECT HIGH` 등), 올 퍼펙트, 오토 플레이 켬/끔 로그(`[AutoPlay] 끔 → Extra=Off`처럼 이름으로 찍히는지), 결과 화면.

## 8차 (2026-11-19): 전수 리팩토링, 병목 제거, 업데이트 내성 보강

검증: Release 빌드 경고·오류 0건, 하네스 종료 코드 0 — `[Guarded]` 50개·`[HarmonyPatch]` 26개·`[Required]` 10개·`[Layout]` 268개·`[Body]` 382개 메서드·`[BmsFind]` 8건·`[PureLogic]` 실패 0건. **인게임 실기 실행은 이 빌드로 미확인.**

### 업데이트 내성 · 정확성

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| U8-1 | `PlayAssist.ApplyAutoPlay` | 본문에서 `InGameSettingType.Extra`를 직접 참조해 업데이트 시 바인딩 실패가 `HookGuard`에 보고되지 않고 매 곡마다 반복될 위험 | `TryEnableAutoPlayDirect`(`[Guarded]`)로 통합 격리하고 `HookGuard.Fail`로 보고 |
| U8-2 | `OfflineSession.EnsureSectionRecords` | `GameEndResult` null 비교에 `!=` / `==` 사용 | 향후 `record` 전환 대비 `is not null` / `is null`로 통일 |
| B8-1 | `CustomMedia.GetClip` / `GetCover` | `s_clips`/`s_covers`는 `OrdinalIgnoreCase`인데 `LinkedList<string>.Remove(path)`는 `Ordinal`이라 대소문자 불일치 시 LRU 고아 노드 잔류, `s_failedCovers` 상한 없음 | `LinkedListNode<string>`을 엔트리에 보관해 O(1) 제거/갱신, `MaxFailedCovers = 128` 상한 추가 |
| B8-2 | `CustomSongs.Scan` / `Load` | `song_ids.json` 수동 편집 등으로 중복 번호가 있을 때 `PatternIndex` 충돌 가능, `lanes` 중복 줄 누적 | `usedNumbers` 집합으로 중복 방어, `lanes` 재설정 시 `Clear()` |
| B8-3 | `BmsConverter.Convert` | `#BPMxx` 또는 채널 `03`의 0 이하 비정상 BPM이 `bpms`에 들어가 0으로 나누기 위험, `Ticks` 과다 루프 방어 없음 | `bpm > 0`만 반영, `end <= start` 및 `MaxTicksPerNote = 65536` 방어 (`PureLogicTests` 회귀 추가) |

### 병목 · 할당 제거

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| P8-1 | `CustomMedia.GetClip` / `GetCover` | LRU `TouchLru`/`Remove`가 매번 `LinkedList` 선형 탐색 | 엔트리 `Node` 보관으로 O(1) |
| P8-2 | `CustomSongs.FindIdIn` | `hwa_` 키마다 `All` 전체 순회 | `Scan()` 시 ID 길이 내림차순 배열(`s_songsByIdLengthDesc`) 생성 후 첫 매치 즉시 반환 |
| P8-3 | `CustomCharts.HasCustomChartFiles` | 일반 곡 플레이마다 `Directory.EnumerateFiles(CustomDir, "*.json").Any()` 디스크 열거 | 5초 TTL 캐시(`Environment.TickCount64`) |
| P8-4 | `BmsConverter.Convert` | `OrderBy().ThenBy()` LINQ 할당 및 `Objects.Any`/`notes.Count` 3회 추가 순회 | in-place `notes.Sort` 및 변환 루프 내 직접 집계 |
| P8-5 | `PlayOverlay.LoadRanges` / `ModConfig.ColorOf` | 일회성/색상 검사에 LINQ(`All`/`Zip`/`Skip`/`Take`/`Any`) 할당 | 단순 `for` 루프로 교체 |

## 9차 (2026-11-19): 폴백 도달성·격리 누락 해소, 스레드 안전성 보강, 하네스 `[Body]` 규칙 확장

검증: Release 빌드 경고·오류 0건, 하네스 종료 코드 0 — `[Guarded]` 54개·`[HarmonyPatch]` 26개·`[Required]` 10개·`[Layout]` 271개·`[Body]` 382개 메서드(비`[Guarded]` NoInlining 2개)·`[BmsFind]` 8건·`[TextSanitizer]` 10건·`[PureLogic]` 실패 0건, `NETSDK1138` 경고 0건. **인게임 실기 실행은 이 빌드로 미확인.**

### 업데이트 내성 · 격리

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| U9-1 | `SongCategoryResourceCollection_GetDataSet_Patch.Create` | `[Guarded]` 이름이 `Postfix`의 `Hook`과 같아, `Create` 사전 JIT 실패 시 `GetDataSet` 훅 전체가 꺼져 `CreateFallback`에 도달 불가 | `Create`의 `[Guarded]` 이름을 `SongCategoryResourceCollection.CreateDirect`로 분리, `CreateOrFallback`에서 해당 키만 검사해 폴백 유지. `SetTextRenderTypeDirect`에도 `[Guarded]` 추가 |
| U9-2 | `OfflineSession` (`FinalScore`, `ReadMasteryRawDirect`, `RenewGameSession`) | `[NoInlining]` 게임 멤버 헬퍼 2건에 `[Guarded]` 누락, `RenewGameSession`에서 `HookGuard.IsDisabled` 미확인 | `[Guarded]` 2건 추가 및 `RenewGameSession`에 `IsDisabled` 가드 추가 |
| U9-3 | `SongInfo_IsLock_Patch`, `SongInfo_IsNewSongMarkTarget_Patch`, `AddressableKeyRedirect.Prefix` | `HookGuard.Fail`로 비활성화 기록은 하지만 진입부에서 `HookGuard.IsDisabled`를 확인하지 않아 바인딩 실패 시 매 호출 예외 반복 가능 | 세 훅 모두 진입부에 `HookGuard.IsDisabled` 가드 추가 |
| U9-4 | `PlayAssist.ForcePerfect` / `SetPerfectDirect` | `SetPerfectDirect`에 `[Guarded]` 누락, 바인딩 실패 시 노트마다 예외 후 리플렉션 폴백 반복 | `SetPerfectDirect`에 보고용 `[Guarded("PlayAssist.SetPerfectDirect")]` 추가, 실패 시 해당 경로만 꺼서 이후 노트는 예외 없이 즉시 리플렉션 폴백 |
| U9-5 | `DiagnosticPatches.Apply` | 게임 타입 5종을 `typeof(...)`로 한 메서드에 직접 참조해 한 타입만 바뀌어도 나머지 진단 패치까지 JIT 실패 | `AccessTools.TypeByName` 문자열 조회로 개별 격리, `LogUpdatePostProcess` null 비교를 `is null`로 통일 |
| U9-6 | `UpdateResilience.TryCast<T>` | `SafeSetFieldOrProperty`는 `Nullable`/`Enum` 변환을 지원하지만 `SafeGetFieldOrProperty`는 미지원 | `Nullable.GetUnderlyingType` 및 `Enum` 변환 대칭 지원, 미사용 `using` 제거 |
| U9-7 | `tools/interop-harness` | `[NoInlining]` 격리 헬퍼에 `[Guarded]`를 빠뜨려도 기계 검사가 잡지 못함, `InteropHarness.csproj`에서 `NETSDK1138` 경고 출력 | `[Body]` 규칙 ④(비`[Guarded]` `NoInlining` 메서드의 게임 멤버 직접 참조 금지) 추가, `NormalizeDifficulty`·손상 미디어 파일 회귀 테스트 추가, `CheckEolTargetFramework=false` 설정 |

### 스레드 안전성 · 방어

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| Q9-1 | `CustomSongs` (`Current`, `s_baseFileName`, `s_baseSongId`), `CustomMedia.PreviewSong` | 메인·입력(HFTask)·오디오·Addressables 스레드가 공유하는 정적 필드에 `volatile` 누락 | `volatile` 지정 |
| Q9-2 | `CustomSongs.ExtendToMediaLength` | 커스텀 JSON에 `initialBpm <= 0`이 들어오면 잘못된 `endPosition` 계산 가능 | `bpm <= 0`이면 기본 `120` 사용 |
| Q9-3 | `CustomMedia.GetCover` | 2차 lock 구간에 `MaxCoverCache` 상한 재검사 없어 동시 로드 시 상한 초과 가능 | 2차 lock 삽입 직전 `MaxCoverCache` 퇴거 루프 추가 |
| Q9-4 | `CustomSongs.cs`, `CustomSongPatches.cs` | 리팩토링 후 남은 미사용 `using` | 정리 |

### 확인 항목 (인게임)

- `[HookGuard] 훅 바인딩 사전 점검 정상 (54개 메서드, …)`.
- 선곡 화면 custom 카테고리 표시·잠금 없음, 일반 곡 잠금 표시 그대로.
- 커스텀 곡 플레이: 판정바·키뷰어, 올 퍼펙트, 오토 플레이 켬/끔, 결과 화면.

## 10차 (2026-11-19): 수정 기록 감사, 폴백 단락·리팩토링 및 하네스 `[Diag]`·`[NativeField]` 검증 추가

검증: Release 빌드 경고·오류 0건, 하네스 종료 코드 0 — `[Guarded]` 54개·`[HarmonyPatch]` 26개·`[Required]` 10개·`[Layout]` 277개·`[Body]` 384개 메서드(비`[Guarded]` NoInlining 2개)·신규 `[Diag]` 5개·신규 `[NativeField]` 2개·`[BmsFind]` 8건·`[TextSanitizer]` 10건·`[PureLogic]` 실패 0건. **인게임 실기 실행은 이 빌드로 미확인.**

### 수정 기록 감사 (1~9차 이력·HookGuard 키 전수 대조)

- `mod/*.cs` 전체의 `[Guarded]`·`HookGuard.IsDisabled`·`HookGuard.Fail` 키를 전수 대조하고, 1~9차 감사 기록과 현재 코드·`AGENTS.md`·`docs/hooks.md`의 정합성을 확인했다.
- 결과: 아래 U10-1~U10-3의 "사전 점검에서 꺼진 `[Guarded]` 키를 호출부가 단락하지 않던 지점" 5곳과, 문자열/네이티브 오프셋 타깃의 오프라인 검증 공백(U10-4)을 발견해 수정했다.

### 업데이트 내성 · 리팩토링

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| U10-1 | `PlayAssist.ApplyAutoPlay` / `ReadIsScorable` | `InGameSettingHelper.IsScorable`가 사전 점검에서 실패해도 `ApplyAutoPlay`가 `AutoPlayHook`만 확인해 `Extra.AutoPlay`를 먼저 켠 뒤 예외로 되돌리는 흐름 | `ApplyAutoPlay` 진입부에서 `HookGuard.IsDisabled(IsScorableHook)`도 함께 확인해 기록 보호 안전장치가 깨졌으면 아예 켜지 않음 |
| U10-2 | `CustomMedia.SetClipData` | `SetClipDataDirect`가 `[Guarded]`이지만 실패 시 `HookGuard.IsDisabled`/`Fail`로 단락하지 않아 업데이트 후 매 클립마다 예외 후 리플렉션 폴백 반복 | `SetClipDataDirectHook` 상수로 단락·보고 |
| U10-3 | `CustomSongs` (`PopulateSongMeta`, `SetSongField`, `SetPatternField`) | `*Direct` 헬퍼 3종이 `[Guarded]`이지만 호출부가 `HookGuard.IsDisabled`/`Fail`로 단락하지 않아 곡/패턴 수만큼 예외 반복 가능 | 3종 모두 `HookGuard.IsDisabled`/`Fail`로 단락해 첫 실패(또는 사전 점검) 이후 바로 리플렉션/건너뛰기 처리 |
| U10-4 | `DiagnosticPatches.cs`, `tools/interop-harness` | `DiagnosticPatches`가 문자열(`TypeByName`)로 전환되면서 오프라인 하네스 검증 대상에서 빠졌고, `NativeField` 백킹 필드 2종도 오프라인 검증이 없었음 | `DiagnosticPatches.s_targets` 테이블화(리팩토링) + 하네스에 `[Diag]`(5종)·`[NativeField]`(2종) 검증 추가 |
| R10-1 | `CustomMedia.GetCover`, `CustomSongs.MakeFirstNoteChart` | `GetCover`의 LRU 퇴거 루프가 1차/2차 lock에 중복, `MakeFirstNoteChart`가 `notes` 누락/비정상 JSON에서 NRE 가능 | `EvictCoversIfFull()` 추출, `MakeFirstNoteChart` null/타입 방어 보강 |

## 11차 (2026-11-19): 리플렉션 폴백 부활, 하네스 `[Reflect]`·`[GuardKey]` 추가

검증: Release 빌드 경고·오류 0건, 하네스 종료 코드 0 — `[Guarded]` 54개·`[HarmonyPatch]` 26개·`[Required]` 10개·`[Layout]` 277개·`[Body]` 388개 메서드·`[Diag]` 5개·`[NativeField]` 2개·`[BmsFind]` 8건·`[TextSanitizer]` 10건·`[PureLogic]` 실패 0건, 신규 `[Reflect]` 호출부 35개(이름 전달 래퍼 3개)·신규 `[GuardKey]` `IsDisabled` 호출 42개·`Fail` 키 39종·`[Guarded]` 키 49종 실패 0. **인게임 실기 실행은 이 빌드로 미확인.**

### 발견: 리플렉션 폴백 층이 인터롭 타입에서 한 번도 동작하지 않았다

`UpdateResilience.SafeSetFieldOrProperty`/`SafeGetFieldOrProperty`는 "프로퍼티 → 백킹 필드(`_X_k__BackingField` 등)" 순으로 찾았는데, 백킹 단계가 `Type.GetField`만 썼다. Il2CppInterop은 IL2CPP 필드를 관리 필드가 아니라 **같은 이름의 프로퍼티**로 노출한다(관리 필드는 `NativeFieldInfoPtr_*` 정적 포인터뿐). 그래서 getter만 있는 `SongInfo.Id`나 소문자 필드(`HEAudioClip._bytes`) 같은 경우 폴백이 항상 `false`였다.

새 `[Reflect]` 검사를 수정 전 로직으로 돌린 실측: 35개 호출부 중 고유 이름 17개(읽기/쓰기 20건) 실패 — `HEAudioClip.bytes/audioFormat`, `SongCategoryResourceDataSet.categoryKey/displayedName/carouselBackground/detailFocusBackground/categoryColor`, `CategoryResource.resourceRenderType`, `SongInfo.Id/SonglistCategorizationTags/LockTypeString`, `PatternInfo.GroupId/PatternIndex/Level/PreciseLevel/Patterner/Difficulty`. 직접 경로(`*Direct`)가 정상인 현재 빌드에서는 드러나지 않았지만, 업데이트로 직접 경로가 깨지는 순간 "폴백이 받쳐 준다"는 1~10차의 전제가 거짓이었다.

### 업데이트 내성 · 정확성

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| U11-1 | `UpdateResilience` | 백킹 폴백이 필드만 탐색 → 인터롭 타입에서 무효 (위 실측) | 후보 이름을 상속 계층까지 **필드 → 프로퍼티** 순으로 탐색(`FindBacking`, 쓰기/읽기 가능 여부 포함 캐시), 공통 `TrySet`/`TryGet` 추출, 인스턴스 없이 같은 탐색을 하는 `CanResolve(Type, name, write)` 추가 |
| U11-2 | `SongCategoryResourceCollection_GetDataSet_Patch.CreateFallback` | 기증 세트 복사가 문자열 배열 순회라 이름을 오프라인 검증할 수 없음 | `CopyMember(donor, target, name)` 래퍼로 호출부마다 상수 전달 |
| U11-3 | `CustomSongs` (`IsPreferredBaseSong`·`ReadValidDifficulties`·`RegisterSongMeta`) | `HookGuard.Fail`로 끄기만 하고 `IsDisabled`로 단락하지 않음. `IsPreferredBaseSong`은 패턴 수(수백)만큼 불려 업데이트 후 주입 때마다 그만큼 예외 반복 | 키를 `const`로 모으고 세 곳 모두 진입부 `IsDisabled` 단락 |
| U11-4 | `InGameSurface_SendGameEnd_Patch.Prefix` | `IsCustom` 바인딩 실패로 꺼진 뒤에도 모든 곡 결과 전송마다 `IsCustom` 재시도·예외 | 꺼진 뒤에는 `CustomSongs.Current != null`(기존 catch 폴백과 같은 판정)만 사용 |

### 하네스 신규 검사 (`tools/interop-harness/ReflectCheck.cs`)

- **`[Reflect]`**: 모드 IL을 스택 시뮬레이션(선형 스캔 + 분기 대상 스냅숏)해 `SafeSet`/`SafeGet` 호출부마다 (대상 인자의 정적 타입, 이름 상수)를 뽑고, 모드 자신의 `UpdateResilience.CanResolve`를 리플렉션으로 불러 인터롭 타입에서 찾히는지 확인한다. 자기 인자를 그대로 넘기는 메서드는 "이름 전달 래퍼"로 인식(고정점 반복)해 그 호출부를 검사한다. 이름이 상수가 아니거나 대상 정적 타입이 인터롭 타입이 아니면 실패. 자기 검증: `SongInfo.Id` 쓰기 = true, 없는 이름 = false가 아니면 실패(`CanResolve` 퇴행 방지).
- **`[GuardKey]`**: 10차의 수작업 키 대조를 기계화. ① `IsDisabled(키)`의 키가 `[Guarded]` 이름도 `Fail` 키도 아니면 실패(단락이 절대 켜지지 않음), ② `Fail(키)`로 끄는 키를 `IsDisabled`로 단락하는 곳이 없으면 실패(9·10차 U9-3·U10-2·U10-3 유형 — 이번에 U11-3·U11-4 4건 발견), ③ 키가 문자열 상수가 아니면 실패(키를 인자로 받는 공통 헬퍼 제외).
- 한계: 스택 시뮬레이션은 C# 컴파일러 출력(전방 분기·삼항·`??`)에 맞춘 근사다. `calli`나 역방향 분기 안에서 인자를 만드는 경우는 정적 타입을 놓칠 수 있고, 그때는 "정적 타입 불명" 실패로 드러난다(통과로 숨지 않는다).

### 확인 항목 (인게임)

- `[HookGuard] 훅 바인딩 사전 점검 정상 (54개 메서드, …)` — 이번 수정은 직접 경로가 정상이면 폴백을 타지 않으므로 기존 동작과 같아야 한다.
- 커스텀 곡 주입·선곡·플레이·결과 화면 회귀 없음.

## 12차 (2026-11-20): 미디어 길이 오판, 데이터 파일 손상 내성, 하네스 `[Mark]`·`[Fingerprint]` 추가

검증: Release 빌드 경고·오류 0건, 하네스 종료 코드 0 — `[Guarded]` 54개·`[HarmonyPatch]` 26개·`[Required]` 10개·`[Layout]` 281개·`[Body]` 390개 메서드·`[Diag]` 5개·`[NativeField]` 2개·`[BmsFind]` 8건·`[TextSanitizer]` 10건·`[PureLogic]` 실패 0건(Ogg 가짜 페이지 회귀 추가)·`[Reflect]` 35개·`[GuardKey]` 실패 0, 신규 `[Mark]` 4종·신규 `[Fingerprint]` 3항목 실패 0. **인게임 실기 실행은 이 빌드로 미확인.**

### 정확성 · 데이터 내성

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| C12-1 | `MediaDuration.ReadOgg` | 파일 끝 64KB를 역탐색해 처음 만난 `"OggS"`의 granule을 길이로 썼다. 오디오 데이터 속 우연한 `OggS` 바이트나 다른 논리 스트림 페이지를 페이지로 오인할 수 있음. **실측**(수정 전 코드로 새 회귀 테스트): 2초짜리 파일을 22,675초로 읽음 → 커스텀 곡 차트 끝이 그만큼 연장될 수 있었다 | 페이지 헤더 검증(버전 0, 세그먼트 테이블이 버퍼 안) + 첫 페이지의 비트스트림 일련번호 일치만 인정. `PureLogicTests`에 가짜 꼬리 페이지 회귀 추가 |
| C12-2 | `CustomCharts.Process` / `ReadAll` | 차트 스트림 읽기 실패 시 빈 배열로 이어가, 오버라이드 JSON이 없는 **일반 곡**도 빈 차트 스트림으로 바뀜 | `ReadAll`은 실패 시 `null`, 원본을 처음으로 되감을 수 있으면(`CanSeek`) 원본 그대로 반환 |
| C12-3 | `CustomSongs.LoadSongIdMap` / `SaveSongIdMap` | `File.WriteAllText` 직접 덮어쓰기라 저장 도중 종료 시 파일이 잘리고, 다음 실행에서 읽기 실패 → 빈 맵 → 곧바로 덮어써 **모든 커스텀 곡의 `hwa_NNN`·`PatternIndex`가 다시 매겨짐**(손상 파일도 사라짐) | 임시 파일에 쓰고 `File.Replace`로 교체(직전본 `.bak`). 읽기 실패 시 손상 파일을 `.corrupt`로 보관하고 `.bak`에서 복구 후 즉시 재저장 |
| C12-4 | `InGameSurface_SendGameEnd_Patch.Prefix` | 패턴 번호를 못 읽을 때의 폴백이 `CustomSongs.Current`를 썼는데, `Current`는 `PlayDirector.Exit`·선곡 복귀에서 지워진다. `SendGameEnd`와 `Exit`의 순서는 **미실측**이라 커스텀 곡 결과를 일반 곡으로 오판해 서버로 보낼 여지 | 정리에서 지우지 않는 `CustomSongs.LastStarted`(가장 최근 `RequestGameStart`의 곡, 일반 곡이면 null)로 판정 |

### 하네스 신규 검사

- **`[Mark]`** (`ReflectCheck.RunMarkKeys`): `ModHealth.Mark(키)` 상수와 `ReportPlay`의 `s_fired.Contains(키)` 상수를 대조. 확인하는 키를 아무도 Mark하지 않으면 매 플레이 "훅 미호출" 거짓 경고, Mark만 하면 죽은 표시 → 둘 다 실패.
- **`[Fingerprint]`** (`Program.VerifyFingerprint`): `ModHealth.TestedBuildId`·`TestedGameAssemblySize`·`TestedLocateCount` 상수를 설치된 게임의 매니페스트 `buildid`·`GameAssembly.dll` 크기·Locate 엄격 조건 건수와 대조. 업데이트 후 확인 순서 4단계(검증 기준 갱신)를 기계화했다 — 게임이 바뀌었으면 **의도적으로 실패**하며 갱신할 값을 출력한다.

### 확인 항목 (인게임)

- 커스텀 곡 스캔 후 `UserData\PLATiNALAB\song_ids.json`과 `song_ids.json.bak`(두 번째 저장부터)이 생기고 곡 번호가 유지되는지.
- 커스텀 곡 결과 화면: `SendGameEnd 서버 전송 생략` 로그(정상 경로는 `IsCustom`이라 동작 변화 없음).

## 13차 (2026-11-20): Harmony 인자 바인딩 오프라인 검증(`[PatchArgs]`), MP4·UTF-8·BMS·오버레이 경계값 수정

검증: Release 빌드 경고·오류 0건, 하네스 종료 코드 0 — `[Guarded]` 54개·`[HarmonyPatch]` 26개·신규 `[PatchArgs]` 30개 메서드·`[Required]` 10개·`[Layout]` 283개·`[Body]` 390개 메서드·`[Diag]` 5개·`[NativeField]` 2개·`[BmsFind]` 8건·`[TextSanitizer]` 10건·`[PureLogic]` 실패 0건(MP4 길이 미정·비표준 버전, 과장 UTF-8, 탭 구분 BMS 헤더 회귀 추가)·`[Reflect]` 35개·`[GuardKey]`·`[Mark]` 4종·`[Fingerprint]` 3항목 실패 0. **인게임 실기 실행은 이 빌드로 미확인.**

### 업데이트 내성 · 하네스 신규 검사

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| U13-1 | `tools/interop-harness/Program.cs` (`[PatchArgs]`) | `[HarmonyPatch]`는 대상 메서드 존재만 확인했고, `Prefix`/`Postfix`/`Finalizer`의 이름 바인딩 인자(`patternId`, `songId`, `patternStream` 등)와 `__instance`/`__result`/`__0`/`__state`가 대상 시그니처와 맞는지 검증하지 않았다. 게임 업데이트로 인자 이름이 바뀌면 빌드·하네스가 모두 통과한 뒤 런타임 Harmony 패치 시점에 실패한다 | `VerifyPatchMethodParameters` 추가 — 26개 `[HarmonyPatch]` 클래스의 30개 패치 메서드 인자 이름·타입·특수 파라미터 바인딩을 실제 인터롭 `MethodInfo`와 오프라인 대조 |

### 정확성 · 경계값 수정

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| C13-1 | `MediaDuration.ReadMp4` / `FindBox` | ISO/IEC 14496-12의 길이 미정(`0xFFFFFFFF`/`ulong.MaxValue`, fragmented/streamed MP4)을 그대로 초로 나눠 49.7일 등으로 오판 가능, `mvhd` 버전 0/1 외 바이트를 버전 0으로 처리, 64비트 확장 박스 크기(`size == 1`) 경계·오버플로 미검사 | `version` 0/1 명시 검증, `0`/`uint.MaxValue`/`ulong.MaxValue`는 `null` 반환, `FindBox` 범위·오버플로 검사 및 `PureLogicTests` 회귀 추가 |
| C13-2 | `EncodingHelper.IsValidUtf8` | 수동 비트 검사가 과장 UTF-8(`0xC0`/`0xC1`), 서로게이트(`ED A0..BF`), `U+10FFFF` 초과를 허용해 CP949/GBK 유효 바이트(`0xC0 0xA1` 등)를 UTF-8로 오판 가능 | RFC 3629 엄격 `UTF8Encoding(false, throwOnInvalidBytes: true)` 기반으로 교체 + 회귀 테스트 추가 |
| C13-3 | `BmsConverter.Parse` / `Convert` | 헤더 구분자가 공백(`' '`)만 허용돼 탭(`#TITLE\t...`, `#WAV01\t...`) 헤더를 무시, 롱노트 끝 위치가 시작 이하(`pos <= start`)여도 0길이 롱노트 생성 | `HeaderSeparators = { ' ', '\t' }` 지원, `pos <= start` 롱노트는 단노트/경고로 방어 + 회귀 테스트 추가 |
| C13-4 | `PlayOverlay.UpdateJudgmentBar` | 새 판정 틱(`TickN`)을 추가할 때 `s_label`만 `SetAsLastSibling()`을 호출해 `s_labelShadow`가 새 틱 뒤로 가려짐 | `s_labelShadow.transform.SetAsLastSibling()`을 `s_label` 직전에 함께 호출 |

## 14차 (2026-11-22): 재감사 잔여 항목 정리 (v1.2.0)

검증: Release 빌드 경고·오류 0건, 하네스 종료 코드 0 (`[PureLogic]` 실패 0건 — WAV 길이·`#RANDOM` 회귀 추가, 나머지 검사 동일). **인게임 실기 실행은 이 빌드로 미확인.** 오토 플레이를 공식 곡에도 켤 수 있는 범위 문제는 소유자 결정으로 이번 차수에서 제외했다(기록 보호는 여전히 `IsScorable` 안전장치에만 의존, 인게임 미확인).

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| C14-1 | `CustomSongs.ExtendToMediaLength` / `MediaDuration` | 음원 길이를 못 읽으면(Opus, WAV 등) 조용히 `return`해 `endPosition`이 기반 곡 길이 그대로 → BGM·BGA 중간에 결과 화면으로 넘어감 | `MediaDuration.Audio`(RIFF면 WAV 아니면 Ogg) 추가, 음원·영상 길이를 못 읽으면 경고 로그 |
| C14-2 | `BmsConverter.Parse` | `#RANDOM`/`#IF`를 무시해 모든 분기의 오브젝트가 한꺼번에 들어가 노트가 겹침 | 1번 분기만 변환 + 경고 (중첩 `#IF`는 미지원). 회귀 테스트 `BmsRandom` |
| C14-3 | `CustomSongs.IsCustomId` / `IsCustomSong` | 접두사(`hwa_`)만 봐서 공식 곡 ID가 우연히 `hwa_`로 시작하면 잠금·보유 판정이 바뀜 | 등록된 ID(`s_byId`)인지까지 확인. 네이티브 접두사 검사가 안 맞는 일반 곡은 이전처럼 할당 없이 종료 |
| C14-4 | `PlayOverlay` / `ModConfig` | 판정바(24×300)·키 칸(44)이 픽셀 고정이라 4K·HiDPI에서 작음 | `OverlayScale`(0.5~4) 추가, 배치 캐시 키에 배율 포함 |
| C14-5 | `AddressableKeyRedirect.PatchedCount`, `ModHealth.cs`의 `global using` | 읽는 곳이 없는 프로퍼티, 관련 없는 파일에 숨은 전역 using | 삭제 / `mod/GlobalUsings.cs`로 이동 |

### 확인 항목 (인게임)
- 선곡 화면: 일반 곡 잠금 표시가 이전과 같은지, custom 카테고리 곡이 잠기지 않는지 (`IsCustomId` 변경).
- `OverlayScale=2`로 올렸을 때 판정바·키뷰어 배치와 글자 크기.
- WAV `music`을 쓰는 커스텀 곡이 끝까지 재생된 뒤 결과 화면으로 넘어가는지.

## 15차 (2026-11-23): 전체 재감사 (경량)

검증: Release 빌드 경고·오류 0건, 하네스 종료 코드 0 (`[Guarded]` 54·`[HarmonyPatch]` 26·`[PatchArgs]` 30·`[Required]` 10·`[Layout]` 283·`[Body]` 394·`[Reflect]` 35·`[GuardKey]`·`[Mark]`·`[Fingerprint]` 모두 실패 0). **인게임 실기 실행은 이 빌드로 미확인.**

- 병목 점검: `Locate` 훅(네이티브 접두사 검사 후 할당 없이 종료), `PlayOverlay.Update`(비플레이 시 조기 반환, 틱 위치·글자는 버전 변경 시에만 갱신), `ModConfig.Load`(곡 시작 시에만, 수정 시각 비교)를 재확인 — 추가 개선점 없음.
- 전수 감사(`mod/` 22개 파일 정독, 하네스·릴리즈 스크립트 포함) 결과:
  - C15-2 `CustomSongs.ExtendToMediaLength`: 초기 BPM 하나로 `endPosition`을 계산해 BPM이 뒤에서 빨라지는 곡은 길이가 모자라 중간에 끝남 → `BmsConverter.PositionAfterSeconds`(구간별 반영). 회귀 테스트 추가, 첫 구현이 `JsonValue<int>`에서 `TryGetValue<double>` 실패로 하네스에 잡혀 JSON 텍스트 파싱으로 수정.
  - C15-3 `BmsConverter.Convert`: 겹친 노트 중복 제거, 명시 `lanes` 5개 이상이면 앞 4개로 제한 (회귀 테스트 `BmsDuplicate`).
  - C15-4 `CustomMedia.GetCover`: `File.ReadAllBytes` 예외 시 "실패 파일" 표시가 남아 파일 수정 전까지 재시도하지 않음 → 예외 때 표시 제거.
  - 확인만 하고 고치지 않은 것: `InGameSettingManager.Save` Postfix는 원본이 예외를 던지면 실행되지 않아 오토 플레이 값이 복원되지 않을 수 있음(발생 경로 미확인, Finalizer 전환은 인게임 확인 후), `GetCover`의 동시 호출 시 일시적 null(선곡 UI는 메인 스레드라 영향 없음으로 판단).
- C15-1 `Core.OnInitializeMelon`: 시작 로그가 `v1.1.0`으로 하드코딩돼 1.2.0 빌드와 불일치 → `Info.Version` 사용.
- 게임 빌드는 여전히 `25981392`로 동일해 검증 기준 상수 갱신 없음.

## 16차 (2026-11-23): 전체 재감사 → v1.2.1

- C16-1 `CustomSongs.ExtendToMediaLength`/`PrepareChart`/`MakeFirstNoteChart`, `BmsConverter.Convert`: `JsonValue.GetValue<double>()`는 int로 만든 값(예: `bgms`가 없을 때 만들던 `["position"] = 1`)과 문자열 숫자에서 `InvalidOperationException`을 던져, `PrepareChart`의 catch로 빠지면서 길이 연장이 조용히 생략됐다 → `BmsConverter.ReadDouble`(int/double/JsonElement/문자열 공통) 하나로 통일, 새 `bgms`는 `1.0`으로 생성. 회귀 테스트 `ReadDouble`.
- C16-2 `BmsConverter.Parse`: `#ELSE`/`#ELSEIF`를 몰라 1번 분기와 `#ELSE` 분기가 함께 들어갔고, `#IFEND`(일부 에디터의 `#ENDIF` 별칭)는 분기를 닫지 못해 그 뒤 오브젝트가 전부 빠질 수 있었다 → 분기 선택 상태(`branchTaken`) 추적. 회귀 테스트 `BmsElse`.
- C16-3 `BmsConverter.Convert` 중복 제거: (시작, 끝, 레인) 기준이라 단노트 + 같은 자리 롱노트 시작이 둘 다 남았다 → (시작, 레인) 기준으로 합치고 더 긴 노트를 남긴다. 회귀 테스트 `BmsOverlapHold`.
- C16-4 `#BPM 0`·음수가 `initialBpm`으로 들어가던 문제 → 무시하고 기본 130. 회귀 테스트 `BmsZeroBpm`.
- C16-5 `PlayAssist.InGameSettingManager_Save_Patch`: 15차에 "확인만" 했던 Postfix 미실행 문제를 Finalizer로 전환 (반환형 void, `__exception` 없음 → 원본 예외는 그대로 다시 던져진다).
- 병목: 새 코드는 차트 변환(플레이당 1회)에만 있고 핫패스 변경 없음.

## 17차 (2026-11-23): 전체 재감사 → v1.3.0

- C17-1 `Core`/`SafePatcher` (**업데이트 내성, 가장 큰 구멍**): 이 모드만 MelonLoader 자동 `PatchAll`에 의존하고 있었다. Harmony `PatchAll`은 패치 클래스를 차례로 처리하다 하나라도 예외(대상 삭제·오버로드 추가로 모호·인자 이름 변경)가 나면 뒤 클래스를 전부 건너뛴다. 그러면 `ModHealth`가 필수 패치 누락으로 커스텀 곡을 통째로 끄거나, 필수가 아닌 기능(판정바·오토 플레이 등)이 조용히 사라졌다. 문자열 대상 훅(`PlayDirector "Awake"/"OnDestroy"`, `PatternPlayer "ProcessInputEvent"`)은 컴파일 오류로도 드러나지 않는다. → `[assembly: HarmonyDontPatchAll]` + `SafePatcher.ApplyAll`(클래스 단위 try/catch, 형제 모드 sxtg2-update·DEFLATE·PUMP·muse-dash와 같은 방식). 하네스 `[SafePatch]`가 되돌림을 막는다.
- C17-2 `CustomSong.MusicNames`: WAV는 재생(`CreateAudioClip` RIFF 판별)·길이(`MediaDuration.Audio`) 모두 지원하는데 탐색 목록이 `.ogg`뿐이라 `music.wav`가 무시되고 기반 곡 음원이 나왔다. README도 WAV 지원으로 안내하고 있었다 → `.wav` 이름 추가(`.ogg` 우선).
- C17-3 `PlayOverlay` 병목: 인터롭의 `Mathf.*`, `Color`/`Rect`/`Vector2` 생성자, `Rect.x` 등 프로퍼티, `Color.white`는 관리 코드가 아니라 `il2cpp_runtime_invoke`다. 판정바는 틱(최대 32)마다 매 프레임 `Mathf.Clamp01`·`new Color`·`Image.enabled` 게터를 불렀고, `Update`는 매 프레임 `Screen.width/height`·`s_root == null`을 읽었다 → `Rgba`/`Vec2`/`Box`(필드 직접 대입), `MathF`, 관리 플래그(`s_built`·`s_tickEnabled`·`s_labelShown`), 30프레임 간격 재확인. 모양 스프라이트 생성(픽셀마다 `Mathf`+`new Color`, 300×24면 수만 번)도 관리 코드로.
- C17-4 기타 핫패스: `GetDataSet` Postfix가 모든 카테고리 키에서 `__instance == null`(Unity 비교)을 먼저 하던 것을 키 비교 뒤로, `SoundData.FromHEAudioClip`(효과음마다)·`DetermineRunning` 해제(매 프레임)의 `== null`을 `is null`로. `BuildNameBanners` 로그는 `DiagnosticLogs=1`일 때만.
- 확인만 하고 고치지 않은 것: 미리듣기 음원을 메인 스레드에서 `File.ReadAllBytes`로 읽는 비용(파일당 1회, LRU 8개 캐시 — 큰 WAV는 선곡 시 짧은 멈춤이 있을 수 있음. 비동기화는 HEAudio 쪽 스레드 안전성을 실기로 확인한 뒤), BMS 변환 캐시 스탬프에 `info.txt`(lanes)가 빠진 것(`info.txt`는 시작 시 한 번만 읽으므로 재시작 필요 — 기존 동작과 같음).
- 검증: 빌드·하네스는 게임 PC에서 실행(결과는 아래 확인 항목·커밋 메시지 참고). **인게임 실기는 이 빌드로 미확인.**

## 18차 (2026-11-24): v1.3.0 게임 PC 빌드·하네스 실측 검증 + 경계값 보강

검증: Release/Debug 빌드 경고·오류 0건, 하네스 종료 코드 0 (`[SafePatch]` 포함 전 항목 통과). **인게임 실기 실행은 이 빌드로 미확인.**

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| C18-1 | `PlayOverlay.Build` | 17차에서 추가한 관리 플래그 `s_labelShown = false`와 달리 `NewText` 직후 `TextMeshProUGUI.enabled`는 기본 `true`라 첫 판정 전 초기 상태가 어긋남 | `Build()`에서 `s_label.enabled = false; s_labelShadow.enabled = false;` 명시 초기화 |
| C18-2 | `MediaDuration.ReadWav` | WAV 파일에서 `data` 청크가 `fmt `보다 먼저 나오면 `byteRate == 0`으로 즉시 `null` 반환, `fmt ` 청크 끝 경계 미검사 | `byteRate`·`dataLen`을 독립 기록해 순서 무관하게 둘 다 확보되면 계산, `body + 16 <= len` 검사 + 회귀 테스트(`data`-first WAV) 추가 |
| C18-3 | `BmsConverter.Parse` | `#SETRANDOM n`의 지정 분기 값 `n`을 무시하고 항상 1 고정, 디렉티브가 `StartsWith`라 접두사가 겹치는 비표준 헤더 오인 가능, `headerOnly` 스캔 시 `#RANDOM` 경고 중복 출력 | `#SETRANDOM n` 분기 값 반영, 공백/탭/줄끝 경계 검사(`IsDirective`), `headerOnly` 경고 생략 + 회귀 테스트(`BmsSetRandom`) 추가 |
| U18-1 | `SafePatcher.cs` | 17차 클라우드 커밋(`4d32c8c`)에 `using MelonLoader;`가 빠져 `[assembly: HarmonyDontPatchAll]` 컴파일 오류(CS0246), `ApplyAll` 중복 호출 가드 없음 | `using MelonLoader;` 추가, `s_applied` 가드 및 `harmony`/`assembly` null 방어 추가 |

## 19차 (2026-11-25): 오프라인 독립 실행 커밋(decf3b7) 감사 → v1.4.0

대상: 18차 이후 들어온 `decf3b7`(스팀/서버 우회·커튼 패치, 1,067줄)과 그 영향을 받은 기존 경로. 나머지 파일은 18차 결과 위에서 재확인.
검증: Release 빌드 경고·오류 0건, 하네스 종료 코드 0 (신규 `[Offline]`·`JsonMapFile`·`SongOwnership` 포함). `[Offline]`은 게이트 한 줄을 일부러 지운 빌드로 실패하는 것까지 확인(변이 테스트). **인게임 실기는 이 빌드로 미확인.**

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| C19-1 | `SteamBypassPatches` 전체 (38개 클래스) | 우회가 게이트 없이 모든 사용자에게 적용: 스팀으로 정상 실행해도 SDK 초기화·로그인이 생략되고 서버 오류 팝업이 숨겨졌다 | `Offline.Enabled`(config `OfflineMode`, 기본 0, 시작 시 한 번 읽음)일 때만 동작. 하네스 `[Offline]`이 게이트 누락·`[OfflineOnly]` 누락을 잡는다 |
| C19-2 | `SongInfo.IsAvailable/IsNotOwned/IsLock/IsNewSongMarkTarget` | 커스텀 곡 확인을 지워 모든 공식 곡을 무조건 해금 — 소유하지 않은 유료 DLC 곡까지 열림 (게임에 `DlcProductIdCollection` 존재) | 커스텀 곡만 고정값. 공식 곡은 온라인에서 원본 결과를 `SongOwnership`(`UserData\PLATiNALAB\song_ownership.json`)에 기록하고, 오프라인에서는 기록값 재현·기록 없으면 잠김 |
| C19-3 | `OfflineSession` `RequestGameStart`/`RenewGameSession`/`SendGameEnd` | 공식 곡도 항상 로컬 처리 → 온라인 사용자의 공식 곡 기록이 서버로 가지 않음. `CustomSongsAllowed` 확인 삭제, `SendGameEnd` 훅이 꺼지면 커스텀 곡 결과가 서버로 갈 수 있게 됨(`LastStarted` 판정 삭제 — 11차 수정의 회귀) | 공식 곡 로컬 처리는 `OfflineMode=1`일 때만. 커스텀 곡 판정·`LastStarted` 폴백·`CustomSongsAllowed` 확인 복원 |
| C19-4 | `Application.Quit` Prefix | 무조건 `false` → 게임 메뉴의 종료가 동작하지 않음 | 오프라인 모드에서 로그인 완료 전이거나 오류 팝업을 막은 뒤 15초 안일 때만 막음 (`Offline.ShouldBlockQuit`) |
| C19-5 | 오프라인 프로필 | 제작자의 스팀 ID·닉네임이 공개 DLL에 하드코딩 | `OfflineUserId`/`OfflineNickname` 설정 (기본 `PLAYER`) |
| C19-6 | `CurtainBypassPatches` | 온라인에서도 커튼을 강제로 끔(원본 연출 손상 가능). `DetailViewerManager.SetSong`(곡을 넘길 때마다)마다 씬 전체 `FindObjectsOfType` 2회 — 선곡 스크롤 병목 | 커튼 정리는 오프라인 모드만. `SetSong` 경로는 마지막 정리 후 1초 안이면 건너뜀. `SectionController.Initialize`(커스텀 패턴 방어)는 유지 |
| C19-7 | 소유 기록 훅의 곡 ID 읽기 (신규 코드 병목 방지) | 판정 게터는 곡마다 여러 번 불려, 게터(`il2cpp_runtime_invoke`+문자열 할당)로 ID를 읽으면 곡 수만큼 비용이 쌓임 | `CustomSongs.ReadSongIdCached`: 백킹 필드의 IL2CPP 문자열 포인터로 캐시, 포인터가 같아도 내용 재비교(할당 없음)로 주소 재사용 오인 방지, 자체 검증 32건은 게터와 대조. 저장은 변경 3초 뒤 메인 스레드에서 한 번 + 종료 시 |
| C19-8 | `song_ids.json` 읽기·쓰기 | 손상 복구·교체 저장 로직이 `CustomSongs`에만 있어 새 파일에 재사용 불가 | `JsonMapFile`로 공용화(하네스 회귀 테스트: `.bak` 생성, `.corrupt` 보관 후 복구, 백업까지 손상 시 초기화, 최소값 필터) |
| C19-9 | `SongOwnershipStore` 생성자 (하네스가 찾은 신규 코드 결함) | 범위 밖 비트만 있는 항목이 빈 기록으로 남음 | 마스킹 후 0이면 버림 |

설계 결정 (소유자 확인, 2026-11-25): 오프라인 모드는 옵트인, 공식 곡 소유 판정은 온라인 기록 재현. 소유하지 않은 유료 곡을 여는 기능은 공개 릴리즈에 넣지 않는다.

확인 항목 (인게임):
- `OfflineMode=0` + 스팀: 로그인·공식 곡 기록 전송·메뉴 종료가 원본대로인지, 선곡 화면을 연 뒤 종료하면 `song_ownership.json`이 생기는지, 선곡 스크롤 체감 속도(소유 기록 Postfix 추가).
- `OfflineMode=1`: 스팀 없이 시작 → 보유 곡만 열림, 미보유 곡 잠김, 결과 화면, 커튼 정리, 메뉴 종료(로그인 후·오류 15초 후) 동작.

## 20차 (2026-11-25): v1.4.0 전체 재감사 → v1.4.1

대상: `mod/` 27개 파일 중 19차 신규·변경 경로(`OfflineMode`·`SteamBypassPatches`·`CurtainBypassPatches`·`SongOwnership(Store)`·`JsonMapFile`·`ModHealth`·`CustomSongPatches` 보유 판정)를 정독하고, 나머지는 19차 결과 위에서 핫패스만 재확인.
검증: Release 빌드 경고·오류 0건, 하네스 종료 코드 0 (`[HarmonyPatch]` 71·`[PatchArgs]` 78·`[Required]` 10+7·`[Offline]` 44 포함 전 항목 실패 0). 새 `[Required]` 오프라인 대조는 이름 하나를 일부러 틀린 빌드(`SteamAPI.InitExx`)로 실패하는 것까지 확인(변이 테스트). **인게임 실기는 이 빌드로 미확인.**

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| C20-1 | `ModHealth` (업데이트 내성) | 필수 패치 점검이 커스텀 곡 경로만 봤다. `OfflineMode=1`에서 스팀 SDK·로그인 우회 패치가 업데이트로 빠지면(SafePatcher가 그 클래스만 건너뜀) 원인 로그 없이 기동 실패·로그인 대기 | `s_offlineRequiredPatches` 7종을 오프라인 모드일 때만 점검해 `[Health] 오프라인 모드 필수 패치 누락` + 대처법(`OfflineMode=0`) 로그. 커스텀 곡 주입은 끄지 않음. 하네스 `[Required]`가 이름 대조 |
| C20-2 | `Offline.ShouldBlockQuit` (업데이트 내성) | "로그인 완료 전" 판정은 `LoginAndLoadProfile` 우회 성공 또는 `SongSelectDirector.Awake`에서만 풀린다. 업데이트로 두 훅이 모두 꺼지면 메뉴 종료가 영구히 막힘 | 시작 후 180초가 지나면 부팅 완료로 간주 + 경고 로그 |
| C20-3 | `Application.Quit` | `Quit()`만 패치 — 게임이 `Quit(int)`를 쓰면(또는 업데이트로 바꾸면) 오프라인 실행이 오류로 그대로 꺼짐 | `Application_QuitExitCode_Patch` 추가(같은 정책, `[OfflineOnly]`) |
| C20-4 | `CurtainBypassManager` (병목) | 19차 스로틀 후에도 선곡 스크롤 중 1초마다 씬 전체 `FindObjectsOfType` 2회 | 직전 정리에서 잔류 커튼 0개면 `s_sceneClean` → 다음 씬 전환 신호(`Awake`·`OnSceneVisible`·`Leave`)까지 `SetSong` 경로 스캔 생략 |
| C20-5 | `SteamBypassPatches` 로그 (병목) | 오프라인에서는 모든 API 호출이 실패해 더미 응답·오류 팝업 차단 로그가 호출마다 `Latest.log`에 쓰임 (소셜 랭킹은 곡을 넘길 때마다 호출 가능) | 더미 응답 성공 로그 10종은 `Offline.LogOnce`, 오류 팝업 차단 경고 7곳은 `Offline.WarnThrottled`(키당 10초, 생략 건수 표시) |
| C20-6 | `JsonMapFile.Write` (데이터 내성) | `File.Replace`를 지원하지 않는 볼륨(FAT32·네트워크·일부 동기화 폴더)이나 `.bak` 잠김이면 저장 자체가 실패 → `song_ownership.json`은 3초마다 재시도만 반복 | 실패 시 `.bak` 복사 + 덮어쓰기 이동으로 저장, 경고 로그 |

확인만 하고 고치지 않은 것:
- `JsonMapFile.TryRead`는 키를 `OrdinalIgnoreCase`로 읽는다. 대소문자만 다른 공식 곡 ID가 있으면 보유 기록이 합쳐질 수 있으나 확인된 사례 없음 — `song_ids.json`(폴더명, Windows 대소문자 무시)과 공용이라 유지.
- 온라인에서 서버 보유 정보가 오기 전에 판정 게터가 불리면 일시적 값이 기록될 수 있다. 값이 바뀌면 덮어쓰므로 선곡 화면을 한 번 정상 표시한 뒤 종료하면 맞는 값이 남는다(README 권장 순서와 같음).
- 첫 구현의 `ConcurrentDictionary.GetOrAdd` 람다가 `CS0656`을 냈다 → 람다 없는 형태로 작성 (AGENTS.md Dead Ends에 추가).

확인 항목 (인게임): AGENTS.md §7의 20차 항목.

## 21차 (2026-11-26): 20차에서 얕게 본 파일 정독 → v1.4.2

대상: 20차는 오프라인 경로 위주였다. 이번에는 `AddressableKeyRedirect`·`OfflineSession`·`PlayAssist`·`CustomMedia`·`CustomCharts`·`CustomSongs` 판정 경로·`PlayOverlay` 갱신/생성 경로를 정독했다.
검증: Release 빌드 경고·오류 0건, 하네스 종료 코드 0 (`[Layout]` 366·`[Body]` 536 포함 전 항목 실패 0). **인게임 실기는 이 빌드로 미확인.**

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| C21-1 | `CustomSongs.IsCustomSong` (병목) | 접두사 `hwa_`가 맞으면 등록 여부를 보려고 매번 `SongInfo.Id` 게터(`il2cpp_runtime_invoke` + 문자열 할당)를 불렀다. 커스텀 곡은 `IsAvailable/IsNotOwned/IsLock/IsHidden` 판정 게터마다 이 경로를 탄다 | 19차의 포인터 캐시 `ReadSongIdCached`로 읽는다 |
| C21-2 | `CustomSongs.ReadSongIdCached` (병목) | 캐시 조회·저장마다 `lock` 2회. 판정 게터는 로딩 스레드에서도 불려 경합 가능 | `ConcurrentDictionary`로 lock 없이 읽기 |
| C21-3 | `CustomMedia.GetCover` (병목) | 선곡 화면에서 배너가 갱신될 때마다 커스텀 커버마다 `File.GetLastWriteTimeUtc`(디스크 메타데이터 조회) | 같은 커버는 1초 안이면 stat 없이 캐시 반환 (핫리로드는 최대 1초 늦게 반영) |
| C21-4 | `PlayOverlay.Update`/`Build` (업데이트 내성·누수) | 업데이트로 uGUI/TMP 멤버가 바뀌어 `Build()`가 도중에 실패하면, 만들다 만 루트 GameObject가 프레임마다 새로 쌓였다. 갱신이 실패하면 `WarnOnce`로 로그만 줄이고 예외는 매 프레임 계속 던졌다 | `Build` 실패 시 루트 파괴(`DestroyRoot`). 120프레임 연속 오류면 이번 실행 동안 오버레이를 끄고 `[PlayOverlay] … 연속 오류` 오류 로그 |

확인만 하고 고치지 않은 것:
- `CustomCharts.ToStream`은 `MemoryStream` 생성에 실패하면 빈 스트림을 돌려준다(원본으로 되돌리지 않음). 인터롭 `MemoryStream` 생성자가 깨지면 이미 다른 경로도 함께 깨지므로 유지.
- `AddressableKeyRedirect.Prefix`는 커스텀 키마다 치환 문자열을 새로 IL2CPP 문자열로 만든다. 플레이당 수 건이라 캐시하지 않음.
- `InGameSettingManager.Save`·`BGAPlayer.SetClip`의 Unity `== null`은 저장·플레이 시작 때 한 번이라 유지.

확인 항목 (인게임): AGENTS.md §7의 21차 항목.

## 22차 (2026-11-26): 게임 업데이트(Build 26909943) 실측 대응·Addressables 탐색 단락·미리듣기 정리 강화 → v1.4.3

대상: 게임 업데이트로 변경된 바이너리 지문 확인, `AddressableKeyRedirect` 키 탐색 경로, `CustomMedia` 미리듣기 생명주기, `PlayOverlay` 곡 전환 초기화.
검증: Release 빌드 경고·오류 0건, 하네스 종료 코드 0 (`[HarmonyPatch]` 72개·`[PatchArgs]` 79개·`[Fingerprint]` 갱신 통과 등 전 항목 실패 0). **인게임 실기는 이 빌드로 미확인.**

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| C22-1 | `ModHealth.TestedBuildId` / `TestedGameAssemblySize` (지문 갱신) | 게임 업데이트로 Steam 매니페스트 `buildid`가 `26909943`, `GameAssembly.dll` 크기가 `107,589,120`바이트로 변경되어 하네스 `[Fingerprint]` 실패 및 시작 시 경고 발생 | 최신 빌드 기준(`26909943` / `107589120`)으로 갱신하여 지문 검증 통과 |
| C22-2 | `CustomSongs.FindIdIn` (병목) | `Addressables.Locate`에서 모든 에셋(수백~수천 개) 로드마다 불리는데, 접두사 확인 없이 `s_songsByIdLengthDesc` 배열 전체를 선형 탐색 | `text.Contains(IdPrefix)` 단락을 먼저 확인하여 비-커스텀 에셋 로딩 시 O(1)로 배열 순회 완전 생략 |
| C22-3 | `CustomMedia.PreviewSong` / `PreviewSoundController.Stop` (상태 누수 방지) | `PreviewSoundController.Play`만 후킹하고 `Stop`이 없어, 미리듣기가 멈춘 뒤에도 `PreviewSong`이 이전 커스텀 곡으로 잔류해 음원 캐시 방출 차단 및 공식 기반 곡 미리듣기 시 이전 커스텀 음원 재생 위험 존재 | `PreviewSoundController.Stop` Prefix 훅을 추가해 `CustomMedia.PreviewSong = null;`로 안전하게 초기화 |
| C22-4 | `PlayOverlay.Reset` (초기화 안정성) | `Reset()` 시 판정 목록은 비우지만 `s_labelShown` 플래그가 유지되어, 곡 재시작 시 1프레임 동안 이전 곡의 마지막 판정 텍스트가 깜빡일 가능성 존재 | `Reset()`에서 `s_labelShown = false;` 명시적 초기화 |

확인 항목 (인게임): AGENTS.md §7의 22차 항목.

## 23차 (2026-11-27): BMS 변속 호환성·롱노트 키워드 정규화·I/O 및 컬렉션 심층 최적화 → v1.4.4

대상: `BmsConverter` 채널 파싱 및 키음 역할 판정, `CustomCharts` 스탬프 생성 I/O, `MediaDuration` WAV 청크 방어, `OfflineSession` 계산식 널 방어, `PlayOverlay` 틱 만료 처리.
검증: Release 빌드 경고·오류 0건, 하네스 종료 코드 0 (`[HarmonyPatch]` 72개·`[PatchArgs]` 79개·`[Fingerprint]` 전 항목 실패 0). **인게임 실기는 이 빌드로 미확인.**

| # | 위치 | 문제 | 수정 |
|---|---|---|---|
| C23-1 | `BmsConverter.Parse` (BMS 호환성 버그) | `#WAV`에 3자리 키(001~999)가 있으면 `width=3`이 되어, BMS 표준상 항상 2자리 키(`#BPMxx`)인 08 채널(BPM 변경)까지 3자리씩 끊겨 변속이 누락되거나 깨짐 | `int w = (channel == "03" \|\| channel == "08") ? 2 : width;`로 08 채널도 2자리 폭 고정 |
| C23-2 | `BmsConverter.RoleOf` (키음 매칭 누락) | `"hold start"`, `"holdstart"`만 매칭하여 차트 제작자들이 자주 쓰는 언더스코어/하이픈(`hold_start`, `hold-start`, `long_start`, `hold_end`)이 일반 단노트로 처리됨 | 공백·언더스코어·하이픈을 제거한 정규화 문자열(`normalized`)로 검사하여 폭넓은 네이밍 지원 |
| C23-3 | `CustomCharts.StampOf` (I/O 병목) | `File.Exists`와 `File.GetLastWriteTimeUtc`를 연달아 호출하여 파일당 2회씩 시스템 콜 발생 | `File.Exists`를 생략하고 바로 `GetLastWriteTimeUtc` 호출(파일 부재 시 0 처리)로 stat 디스크 시스템 콜 50% 절감 |
| C23-4 | `MediaDuration.ReadWav` (무한 루프 방어) | 비정상 청크나 `size` 오버플로로 `nextPos <= pos`가 되는 경우 무한 루프 위험 | `if (nextPos <= pos) break;` 안전장치 추가 |
| C23-5 | `OfflineSession.ComputeAccuracy` (방어적 코딩) | `request.Judge`가 null일 때 `AccuracyV2Formula` 호출 시 NRE 발생 가능 | `request?.Judge == null ? 0 : ...` 널 가드 추가 |
| C23-6 | `PlayOverlay.UpdateJudgmentBar` (GC/컬렉션 최적화) | 만료된 틱이 전체일 때 `RemoveRange(0, expired)`는 불필요한 배열 요소 복사를 유발 | `expired >= s_history.Count ? s_history.Clear() : ...`로 O(1) 초기화 |

확인 항목 (인게임): AGENTS.md §7의 23차 항목.

## 게임 업데이트 후 확인 순서

1. MelonLoader가 인터롭을 다시 만들었는지 본다.
2. 모드를 다시 빌드한다. 이름·시그니처 변경은 대부분 **컴파일 오류**로 드러난다.
3. `Latest.log`에서 `[Health]` 줄을 본다: 빌드 ID, `GameAssembly.dll` 파일 지문(매니페스트가 거짓일 수 있어 별도 확인), `[Init] ... 실패`, `필수 패치 정상`, Locate 건수 경고. `[HookGuard] 훅 바인딩 사전 점검`(실패한 훅 목록), `[KeyRedirect]`의 패치 목록·`완화 조건` 경고·`네이티브 문자열 검사 자체 검증`, `[Native] ... 자체 검증`/`관리 경로로 전환`, `[SafePatcher] … 적용 실패`(17차부터 자동 PatchAll 대신 클래스 단위 적용 — 실패한 클래스 이름이 그대로 나온다), `OfflineMode=1`이면 `[Health] 오프라인 모드 필수 패치`(20차)도 검색한다.
   - 오프라인으로 먼저 보려면 `tools/interop-harness`에서 `dotnet run -c Release` (게임 실행 불필요, 종료 코드 0이면 통과): 컴파일러 생성 판별 누락·오탐 0, Locate 엄격 조건 3건, `[Guarded]` JIT·`[HarmonyPatch]` 타깃·`[Required]` 실패 0, `[Layout]`·`[Body]`·`[Diag]`·`[NativeField]` 실패 0, `[Reflect]`(리플렉션 폴백 이름이 새 인터롭에서도 찾히는지)·`[GuardKey]` 실패 0을 확인한다. `[Reflect]` 실패는 "직접 경로가 깨졌을 때 받쳐 줄 폴백도 깨졌다"는 뜻이므로 직접 경로가 아직 정상이어도 이름을 고친다. 업데이트로 게임 enum이 struct가 되거나 record가 아니게 되는 등 "재빌드 없이 돌던 모드가 클래스째 깨지는" 변화는 `[Layout]`·`[Body]`가 새 인터롭 기준으로 다시 잡는다.
4. 검증 기준 빌드를 `ModHealth.TestedBuildId`와 `TestedGameAssemblySize`(= `GameAssembly.dll` 바이트 수)에 갱신한다 (Locate 건수가 바뀌었으면 `TestedLocateCount`도). 하네스 `[Fingerprint]`가 갱신할 값을 출력하고, 갱신 전에는 실패한다. 필수 패치 목록(`s_requiredPatches`)을 바꿨다면 하네스 `[Required]`가 통과하는지도 본다.
5. 커스텀 곡을 끝까지 플레이한다: 커버/BGM/BGA → 판정 → 결과 화면. 씬 종료 시 `[Health] ... 핵심 훅 호출 정상`을 확인한다.
