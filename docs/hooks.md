# PLATiNA LAB 핵심 후킹 후보 분석 (Hooks)

Unity 6 (6000.3.25f1) IL2CPP 기반 메인 로직 어셈블리(`Il2CppPlatinaLab.Main.dll`, `Il2CppPlatinaLab.DataModel.dll`) 정적 분석 결과입니다.

---

## 1. 차트 파싱 및 인게임 주입 (Pattern & InGame)

| 클래스 (Namespace 포함) | 메서드 시그니처 | 역할 및 모딩 활용 의도 |
| :--- | :--- | :--- |
| `Il2CppPlatinaLab.Main.InGame.Pattern.PatternParser` | `PatternData Parse(PatternPlayer patternPlayer, Stream patternStream)` | 게임 내 패턴 스트림(PPP) 파싱 진입점. 커스텀 BMS/차트 파서 주입 및 오버라이드 후보. |
| `Il2CppPlatinaLab.Main.InGame.Pattern.PatternParser` | `PatternData Parse(Stream patternStream)` | 스트림 기반 파싱 오버로드. |
| `Il2CppPlatinaLab.Main.InGame.Pattern.PatternParser` | `PatternData Parse(PPPScheme pppObject)` | 역직렬화된 PPP 객체 기반 패턴 데이터 생성기. |
| `Il2CppPlatinaLab.Main.InGame.PatternPlayer` | `void SetPatternDataToPlay(PatternData patternData)` | 인게임 시작 시 재생할 차트 데이터 할당. 커스텀 차트 인젝션 핵심 타깃. |
| `Il2CppPlatinaLab.Main.InGame.PatternPlayer` | `void SetMainTrackToPlay(SoundData soundData)` | 인게임 BGM 트랙 할당. 커스텀 음원 주입 타깃. |
| `Il2CppPlatinaLab.Main.InGame.PatternPlayer` | `void EvaluateVideo(int lastTimeMicroSec, int currentTimeMicroSec)` | BGA(동영상) 재생 및 시간 동기화 평가 루프. 커스텀 MP4/BGA 오버라이드 타깃. |

---

## 2. 판정 및 스코어링 (Judgement & Score)

| 클래스 (Namespace 포함) | 메서드 시그니처 | 역할 및 모딩 활용 의도 |
| :--- | :--- | :--- |
| `Il2CppPlatinaLab.Main.InGame.PatternPlayer` | `bool ProcessJudgeResult(JudgeResult judgeResult)` | 단노트/롱노트 판정 결과 처리 루프. All-Perfect 및 판정 보정 모드 핵심 타깃. |
| `Il2CppPlatinaLab.Main.InGame.PatternPlayer` | `void ProcessTickResult(JudgmentType tickResult)` | 롱노트 틱 판정 처리. |
| `Il2CppPlatinaLab.Main.InGame.PatternPlayer` | `void UpdateFastSlow(JudgmentType judgmentType, double error, int key)` | 밀림/당김(Fast/Slow) 오차 오프셋 표시 및 업데이트. |
| `Il2CppPlatinaLab.Main.InGame.Score.ScoreRecorder` | 스코어 집계 및 콤보 기록 관련 메서드 | 게임 결과 스냅샷 및 스코어 조작 검증. |

*참고 (`JudgmentType` 열거형)*:
- `Undefined` (0)
- `PerfectHigh` (1) - 최상위 판정
- `Perfect` (2)
- `Great` (3)
- `Good` (4)
- `Miss` (5)

---

## 3. 곡 선택 및 해금 바이패스 (Song Select & Unlock)

| 클래스 (Namespace 포함) | 메서드 시그니처 | 역할 및 모딩 활용 의도 |
| :--- | :--- | :--- |
| `Il2CppPlatinaLab.Main.Director.SongSelectDirector` | `bool IsLock()` | 곡 및 패턴 잠금 여부 검사. `false` 반환 시 잠금 해제 가능. |
| `Il2CppPlatinaLab.Main.Director.SongSelectDirector` | `bool IsNotOwned()` | DLC/미보유 곡 검사. `false` 반환 시 미보유 차단 우회 가능. |
| `Il2CppPlatinaLab.Main.Director.SongSelectDirector` | `UniTaskVoid TryGameStart()` | 선곡 후 인게임 진입 트리거. |
| `Il2CppPlatinaLab.Main.Select.PatternList.PatternListManager` | `void SelectPattern(PatternInfo patternInfo)` | 난이도 및 패턴 선택 변경 처리. |
