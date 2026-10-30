# PLATiNA LAB IL2CPP 분석 및 모드 스켈레톤 진행 상황

## 환경 정보
- **게임명:** PLATiNA LAB
- **App ID:** 3430470 (Steam Build ID: 25981392)
- **대상 실행 파일:** `H:\steam\steamapps\common\PLATiNA LAB\PLATiNA LAB.exe`
- **Unity 버전:** 6000.3.25f1 (IL2CPP, metadata v39)
- **모드 로더:** MelonLoader 0.7.3 Open-Beta (Runtime: net6)
- **작업 디렉터리:** `H:\source\repos\PLATiNA LAB-unity-il2cpp`

---

## 단계별 진행 상황

- [완료] 1단계: IL2CPP 메타데이터 덤프 — `H:\source\repos\PLATiNA LAB-unity-il2cpp\dummy_dll` — standalone Il2CppDumper/Cpp2IL은 v39 미지원이었으나 MelonLoader 내장 Cpp2IL v2022.1.0-pre-release.21(+StrippedCodeRegSupport)이 정상 생성한 Dummy DLL을 가져와 확보.
- [완료] 2단계: 시그니처 탐색 — `H:\source\repos\PLATiNA LAB-unity-il2cpp\src_dummy` — ilspycmd를 사용하여 `Il2CppPlatinaLab.Main.dll` 및 `Il2CppPlatinaLab.DataModel.dll` 전체 C# 스켈레톤 디컴파일 완료.
- [완료] 3단계: 로직 분석 및 후킹 후보 도출 — `H:\source\repos\PLATiNA LAB-unity-il2cpp\docs\hooks.md` — 차트 파싱(PatternParser), 인게임 플레이(PatternPlayer), 판정(JudgeResult), 선곡 해금(SongSelectDirector) 타깃 정리.
- [완료] 4단계: MelonLoader 0.7.3 모드 스켈레톤 구축 — `H:\source\repos\PLATiNA LAB-unity-il2cpp\mod` — .NET 6 기반 MelonMod 프로젝트 빌드 성공 (`dotnet build` 오류 CS0656 NullableContextAttribute 해결: Nullable disable 설정) 및 `Mods\` 자동 배포.
- [완료] 5단계: 실행 검증 — `H:\steam\steamapps\common\PLATiNA LAB\MelonLoader\Latest.log` — 실제 게임 기동 후 `[PLATiNA_LAB_Custom_Mod] PLATiNA LAB Mod Skeleton Loaded!` 로그 출력 및 1 Mod 정상 로드 확인.
