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

## 게임 업데이트 후 확인 순서

1. MelonLoader가 인터롭을 다시 만들었는지 본다.
2. 모드를 다시 빌드한다. 이름·시그니처 변경은 대부분 **컴파일 오류**로 드러난다.
3. `Latest.log`에서 `[Health]` 줄을 본다: 빌드 ID, `필수 패치 정상`, Locate 건수 경고. `[KeyRedirect]`의 패치 목록과 `완화 조건` 경고, `Failed to HarmonyInit PatchAll`도 검색한다.
4. 검증 기준 빌드를 `ModHealth.TestedBuildId`에 갱신한다.
5. 커스텀 곡을 끝까지 플레이한다: 커버/BGM/BGA → 판정 → 결과 화면. 씬 종료 시 `[Health] ... 핵심 훅 호출 정상`을 확인한다.
