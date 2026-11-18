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

## 게임 업데이트 후 확인 순서

1. MelonLoader가 인터롭을 다시 만들었는지 본다.
2. 모드를 다시 빌드한다. 이름·시그니처 변경은 대부분 **컴파일 오류**로 드러난다.
3. `Latest.log`에서 `[Health]` 줄을 본다: 빌드 ID, `GameAssembly.dll` 파일 지문(매니페스트가 거짓일 수 있어 별도 확인), `[Init] ... 실패`, `필수 패치 정상`, Locate 건수 경고. `[HookGuard] 훅 바인딩 사전 점검`(실패한 훅 목록), `[KeyRedirect]`의 패치 목록·`완화 조건` 경고·`네이티브 문자열 검사 자체 검증`, `[Native] ... 자체 검증`/`관리 경로로 전환`, `Failed to HarmonyInit PatchAll`도 검색한다.
   - 오프라인으로 먼저 보려면 `tools/interop-harness`에서 `dotnet run -c Release` (게임 실행 불필요, 종료 코드 0이면 통과): 컴파일러 생성 판별 누락·오탐 0, Locate 엄격 조건 3건, `[Guarded]` JIT·`[HarmonyPatch]` 타깃·`[Required]` 실패 0, `[Layout]`·`[Body]` 실패 0을 확인한다. 업데이트로 게임 enum이 struct가 되거나 record가 아니게 되는 등 "재빌드 없이 돌던 모드가 클래스째 깨지는" 변화는 `[Layout]`·`[Body]`가 새 인터롭 기준으로 다시 잡는다.
4. 검증 기준 빌드를 `ModHealth.TestedBuildId`와 `TestedGameAssemblySize`(= `GameAssembly.dll` 바이트 수)에 갱신한다. 필수 패치 목록(`s_requiredPatches`)을 바꿨다면 하네스 `[Required]`가 통과하는지도 본다.
5. 커스텀 곡을 끝까지 플레이한다: 커버/BGM/BGA → 판정 → 결과 화면. 씬 종료 시 `[Health] ... 핵심 훅 호출 정상`을 확인한다.
