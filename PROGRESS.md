# PLATiNA LAB IL2CPP 분석 및 모드 진행 상황

## 환경 정보
- **게임명:** PLATiNA LAB (Steam App ID: `3430470`, Build ID: `25981392`)
- **Unity 버전:** 6000.3.25f1 (IL2CPP, metadata v39)
- **모드 로더:** MelonLoader 0.7.3 Open-Beta (Runtime: net6)

---

## 완료된 마일스톤
- **1~2단계 (메타데이터·디컴파일):** `dummy_dll` 및 `src_dummy` (`ilspycmd` 정적 소스) 확보 완료.
- **3~4단계 (후킹 분석 및 모드 구축):** `mod/PLATiNALAB.Mod.csproj` 빌드 및 `Mods\` 자동 배포 구축 완료.
- **커스텀 차트 & 커스텀 곡:** PPP JSON 스트림 교체, `hwa\` 폴더 기반 커스텀 곡 주입, BMS → PPP 변환(단노트/롱노트/LNOBJ/피치노트), 커버·음원(OGG)·BGA(MP4) 교체 및 길이 자동 연장 구현 완료.
- **플레이 보조 & 오버레이:** `SaveCustomKey\config.txt` 설정 핫리로드, 오토 플레이, 올 퍼펙트(커스텀 곡 전용), 판정바, 키뷰어 구현 완료.
- **오프라인 검증 하네스:** `tools/interop-harness`로 인터롭 타입·`[Guarded]` JIT(54개)·`[HarmonyPatch]`(26개)·필드 레이아웃·본문 IL(4규칙)·순수 로직 회귀 테스트 자동 검증 중 (9차 감사 완료).

상세 사용법은 [README.md](README.md), 기술 인수인계 및 규칙은 [AGENTS.md](AGENTS.md)를 참고하세요.
