# 업데이트 내성 감사 (2026-11-08)

기준: 게임 Build `25981392`, MelonLoader 0.7.3, 커밋 `52bc8cb` 시점의 `mod/`.
이 문서는 감사 기록이다. 아래 문제들은 **코드를 고치지 않았고** 주석과 문서만 실제 동작에 맞췄다.

근거 표기: **실측**(실행·디컴파일로 확인) / **판독**(코드를 읽어 판단) / **일반 동작**(라이브러리의 알려진 동작, 이번에 재현은 안 함).
실제 게임 업데이트를 겪어 본 것은 아니다. 인게임 실행 없이 정적 확인과 순수 .NET 6.0.36 실험으로만 판단했다.

## 핵심 발견: 같은 메서드 안의 `try`는 누락 멤버를 잡지 못한다

모드 DLL은 컴파일 시점의 인터롭을 참조한다. 업데이트로 멤버가 사라지면 그 멤버를 **직접 참조하는 메서드는 JIT 단계에서 실패**하고, 메서드 본문은 시작되지 않는다.

**실측:** 생성자·메서드·필드를 제거한 라이브러리로 바꿔치기하고 실행했다. 세 경우 모두 메서드의 첫 줄도 실행되지 않았고, 내부 `catch`도 폴백도 돌지 않았으며, 호출자의 `try`에서만 잡혔다.

```
[ctor]   OUTER caught: MissingMethodException
[method] OUTER caught: MissingMethodException
[field]  OUTER caught: MissingFieldException
```

폴백이 작동하려면 위험한 직접 참조가 **폴백과 다른 메서드**에 있어야 한다(`[MethodImpl(NoInlining)]`으로 분리하거나 리플렉션만 사용).
누락이 호출 대상의 **본문 안쪽**에 있는 경우는 호출 지점에서 예외가 나므로 잡힌다. 실제로 겪은 `ImageConversion.LoadImage`의 `ReadOnlySpan` 문제가 이 경우다(판독).

## 높음

| # | 위치 | 문제 | 업데이트 후 증상 | 권장 |
|---|---|---|---|---|
| H1 | `OfflineSession.BuildLocalRecord` | `new FreeModePlayRecordV2(...)` 직접 호출과 리플렉션 폴백이 같은 메서드에 있다. 생성자 시그니처가 바뀌면 폴백에 도달하지 못한다(실측 기반). | `SendGameEnd` Prefix의 `catch`로 빠져 `NetworkError` 반환, 결과 화면 실패 | 직접 호출을 별도 메서드로 분리 |
| H2 | `CustomSongs.SetSongField`, `SetPatternField` | `_Id_k__BackingField` 등 직접 폴백이 같은 메서드에 있어, 그 멤버가 사라지면 앞의 `SafeSetFieldOrProperty`까지 실행되지 않는다(실측 기반). | 커스텀 곡 주입 실패 | `switch` 폴백을 별도 메서드로 분리 |
| H3 | `PlayAssist.ApplyAutoPlay` | 오토 플레이는 커스텀 곡에 한정되지 않고 기록 보호는 게임의 `IsScorable()`에 맡긴다. `InGameSettingType.Extra.AutoPlay`(현재 1)는 컴파일 시 숫자로 박힌다(C# 상수). 게임이 enum을 재배치하면 조용히 다른 값이 설정된다. | 랭킹 서버가 있는 곡에서 의도와 다른 설정이 적용될 수 있음 | 적용 후 `IsScorable()==false` 확인, 아니면 복원 |

`CustomMedia.LoadImage`, `SetUrl`도 H1과 같은 구조다. `LoadImage_Injected`, `set_url_Injected`, `MarshalNotNull`, `ManagedSpanWrapper` 중 하나가 사라지면 1차 시도까지 함께 실패한다.

## 중간

| # | 내용 | 근거 | 권장 |
|---|---|---|---|
| M1 | `[HarmonyPatch]` 34개 중 인자 타입을 지정한 것은 3개뿐이다. 게임에 오버로드가 추가되면 해당 패치가 `AmbiguousMatchException`으로 실패한다. | 실측(AccessTools 디컴파일) | 인자 타입 명시 |
| M2 | Prefix 인자 이름(`patternId`, `gameToken`, `audioClip`, `afterDispose`, `tickResult` 등)이 게임 쪽 이름과 일치해야 한다. 이름이 바뀌면 그 패치 클래스가 실패한다. | 일반 동작 | `__0` 등 위치 기반 인자 검토 |
| M3 | 부분 실패에 대한 방어가 없다. `Locate` 패치가 0건이어도 경고만 남기고 커스텀 곡은 목록에 뜬다. `RequestGameStart` 패치만 실패하면 커스텀 곡이 서버로 요청된다. | 판독 | 필수 패치 집합을 확인하고 실패 시 커스텀 곡 주입 중단 |
| M4 | IL2CPP 인라인으로 패치는 성공하고 **호출만 안 되는** 경우를 감지하지 못한다. 과거 사례: `LoadGame`, `IsAutoPlayMode`, `Interpret`, `SetMainTrackToPlay`. | 판독 + AGENTS.md Dead Ends | 곡 시작~결과까지 훅별 호출 체크 로그 |
| M5 | `[MelonGame(null, null)]`이고 런타임에 게임 빌드를 확인하거나 기록하지 않는다. 로그만으로는 업데이트 때문인지 구분하기 어렵다. | 판독 | 시작 시 게임 버전을 로그에 남기고 기준 빌드와 비교 |

## 낮음

- **L1:** `SafeSetFieldOrProperty`의 반환값을 무시한다(`OfflineSession.cs:34`, `PlayAssist.cs:118`). Token 주입이 실패하면 `RenewGameSession` Prefix의 토큰 비교를 통과하지 못해 서버 호출로 이어진다.
- **L2:** `BuildLocalRecord`의 생성자 순회는 전체가 하나의 `try`라, 한 후보가 예외를 내면 다음 후보를 시도하지 않는다. 인터롭 공통 `(IntPtr)` 생성자는 `IntPtr.Zero`면 `NullReferenceException`을 낸다(실측). 지금은 실제 생성자가 먼저라 문제가 드러나지 않는다.
- **L3:** `SafeSet/GetFieldOrProperty`는 찾기에 실패한 결과를 캐시하지 않아 호출마다 리플렉션을 다시 수행한다.
- **L4:** `AddressableKeyRedirect`의 `<>` 필터는 일치 타입이 0개다(실측). 결과에는 영향이 없다.
- **L5:** `WrapAction`은 예외 격리일 뿐 업데이트 내성과는 무관하다.
- **L6:** 빈 Postfix(`IsLock`, `IsNotOwned`), 호출되지 않는 `SongListBuilder.Build`, 진단용 `Diag_*`는 업데이트 시 실패 로그만 늘린다.

## 잘 되어 있는 것

- **패치 클래스별 격리:** `MelonBase.HarmonyInit`이 클래스마다 `try/catch`를 건다. 패치 하나가 실패해도 나머지는 적용된다(실측).
- **자동 생성 이름·주소 비의존:** 런타임 코드에 `Method_Private_...` 이름과 RVA가 없다. RVA는 주석에만 있고 업데이트 후에는 무효가 된다.
- **데이터 자동 탐색:** 기반 곡 선택, 유효 난이도 키, `Locate` 구현체 모두 런타임에 찾는다.
- **값 검증:** `JudgmentTimingMap`은 범위와 순서를 검사하고 이상하면 기본값을 쓴다.
- **안전한 실패 방향:** 올 퍼펙트는 `CustomSongs.Current != null`일 때만 켜진다. `RequestGameStart` 패치가 실패하면 자동으로 꺼진다.

## 게임 업데이트 후 확인 순서

1. MelonLoader가 인터롭을 다시 만들었는지 본다.
2. 모드를 다시 빌드한다. 이름·시그니처 변경은 대부분 **컴파일 오류**로 드러난다. JIT 실패 문제(H1, H2)도 이 단계에서 예방된다.
3. `Latest.log`에서 `Failed to HarmonyInit PatchAll`과 `[KeyRedirect] 패치 성공` 건수(현재 3건)를 확인한다.
4. enum 값을 재확인한다(`Extra`, `JudgmentType`). 재빌드하면 새 값으로 다시 컴파일된다.
5. 커스텀 곡을 끝까지 플레이한다: 커버/BGM/BGA → 판정 → 결과 화면. 호출이 안 되는 훅을 찾는 유일한 방법이다.
