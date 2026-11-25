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
- **오프라인 독립 실행 모드 (v1.4.0, 옵트인):** `OfflineMode=1`일 때만 스팀·로그인·서버 우회, 공식 곡 보유 판정은 온라인 기록(`song_ownership.json`) 재현. 미보유 유료 곡은 열지 않는다.
- **오프라인 검증 하네스:** `tools/interop-harness`로 인터롭 타입·`[Guarded]` JIT(86개)·`[HarmonyPatch]`(71개)·`[PatchArgs]`(78개 메서드)·`[Required]`(필수 10개 + 오프라인 필수 7개)·`[Layout]`·`[Body]`·`[Diag]`(5개)·`[NativeField]`(2개)·`[Reflect]`(35개 호출부)·`[GuardKey]`·`[Mark]`·`[Fingerprint]`·`[SafePatch]`·`[Offline]`(오프라인 전용 패치 44개 게이트)·순수 로직 회귀 테스트 자동 검증 중 (20차 감사 완료, v1.4.1).

상세 사용법은 [README.md](README.md), 기술 인수인계 및 규칙은 [AGENTS.md](AGENTS.md)를 참고하세요.
