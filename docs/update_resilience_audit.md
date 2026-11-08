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

## 게임 업데이트 후 확인 순서

1. MelonLoader가 인터롭을 다시 만들었는지 본다.
2. 모드를 다시 빌드한다. 이름·시그니처 변경은 대부분 **컴파일 오류**로 드러난다.
3. `Latest.log`에서 `[Health]` 줄을 본다: 빌드 ID, `필수 패치 정상`. `Failed to HarmonyInit PatchAll`도 검색한다.
4. 검증 기준 빌드를 `ModHealth.TestedBuildId`에 갱신한다.
5. 커스텀 곡을 끝까지 플레이한다: 커버/BGM/BGA → 판정 → 결과 화면. 씬 종료 시 `[Health] ... 핵심 훅 호출 정상`을 확인한다.
