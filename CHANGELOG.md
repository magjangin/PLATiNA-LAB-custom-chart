# Changelog

형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/)를 따르고, 버전은 `mod/Core.cs`의 `MelonInfo`와 같습니다.
검증 기준 게임: PLATiNA LAB Build `25981392` (`GameAssembly.dll` 96,829,440바이트), MelonLoader 0.7.3.

## [1.2.0] - 2026-11-22

### 추가
- `config.txt`에 `OverlayScale`(기본 1, 0.5~4): 판정바·키뷰어의 막대·글자·키 칸 크기 배율. 4K 등 고해상도에서 작아 보이는 문제 대응.
- BMS `#RANDOM` / `#IF` / `#ENDIF` 처리: 항상 1번 분기만 변환하고 경고 로그를 남긴다 (예전에는 모든 분기가 한꺼번에 들어가 노트가 겹쳤다).
- WAV 음원 길이 읽기(`MediaDuration.Audio`): RIFF 헤더면 WAV, 아니면 Ogg Vorbis로 길이를 읽어 `endPosition`을 늘린다.
- 하네스 회귀 테스트: WAV 길이, `#RANDOM` 분기.
- `CHANGELOG.md`, `tools/release.ps1`(빌드 → 하네스 → 압축 → GitHub 릴리즈).

### 변경
- 음원·영상 길이를 읽지 못하면 경고 로그를 남긴다 (예전에는 조용히 기반 곡 길이에서 곡이 끝났다).
- `CustomSongs.IsCustomId`가 `hwa_` 접두사에 더해 등록된 커스텀 곡 ID인지까지 확인한다 (공식 곡 ID가 우연히 `hwa_`로 시작해도 잠금·보유 판정이 바뀌지 않는다). 일반 곡은 접두사 검사에서 할당 없이 끝난다.
- `global using`을 `mod/GlobalUsings.cs`로 이동, 쓰이지 않던 `AddressableKeyRedirect.PatchedCount` 삭제.

## [1.1.0] - 2026-11-18 ~ 2026-11-20

### 추가
- 곡 번호 영속화(`UserData\PLATiNALAB\song_ids.json`): 폴더를 추가·삭제해도 기존 곡의 ID·패턴 번호가 바뀌지 않는다. 임시 파일 교체 저장, `.bak` 직전본, 손상 시 `.corrupt` 보관.
- BMS 헤더(`#TITLE`, `#ARTIST`, `#BPM`, `#PLAYLEVEL`)로 `info.txt`에 없는 메타 자동 보완.
- 설정 파일 오류 경고(알 수 없는 키, 잘못된 값, 범위 밖 값).
- 진단 로그 패치를 `DiagnosticLogs=1`일 때만 수동 적용 (`mod/DiagnosticPatches.cs`).
- 오프라인 하네스 `PureLogicTests`(BMS 변환, 인코딩 감지, 미디어 길이).
- 8~13차 업데이트 내성 감사: `[PatchArgs]`·`[Reflect]`·`[GuardKey]`·`[Mark]`·`[Fingerprint]` 등 기계 검사 (`docs/update_resilience_audit.md`).

### 수정
- MP4 길이 미정·비표준 `mvhd`, 과장 UTF-8 오판, 탭 구분 BMS 헤더, 판정 라벨 그림자 순서, Ogg 길이 오판.

## [1.0.0] - 2026-10-30 ~ 2026-11-16

- 커스텀 카테고리·`hwa\` 폴더 곡, 커버/음원/BGA 교체, BMS → PPP 변환, 오프라인 세션(서버 호출 우회), 판정바·키뷰어·오토 플레이·올 퍼펙트, 다국어 인코딩, 업데이트 내성(`HookGuard`, `ModHealth`) 초기 구현과 1~7차 감사.
