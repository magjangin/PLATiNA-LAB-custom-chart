# Changelog

형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/)를 따르고, 버전은 `mod/Core.cs`의 `MelonInfo`와 같습니다.
검증 기준 게임: PLATiNA LAB Build `25981392` (`GameAssembly.dll` 96,829,440바이트), MelonLoader 0.7.3.

## [Unreleased]

## [1.4.1] - 2026-11-25

### 변경 (업데이트 내성)
- `OfflineMode=1`에서 오프라인 기동에 꼭 필요한 패치(스팀 SDK 초기화·로그인·인증 토큰·미로그인 팝업·종료)가 게임 업데이트로 빠지면 시작 로그에 `[Health] 오프라인 모드 필수 패치 누락`과 대처법(`OfflineMode=0`)을 남긴다. 하네스 `[Required]`가 이 목록의 이름도 대조한다.
- 오프라인 모드의 "로그인 완료 전" 종료 차단에 시작 후 180초 상한을 뒀다. 업데이트로 로그인 우회·선곡 화면 훅이 모두 꺼지면 메뉴 종료가 영영 막힐 수 있었다.
- `Application.Quit(int)` 오버로드도 `Quit()`과 같은 정책으로 막는다.
- `song_ids.json`·`song_ownership.json` 저장에서 `File.Replace`를 지원하지 않는 볼륨(FAT32·네트워크 드라이브 등)이나 `.bak`이 잠긴 경우 덮어쓰기 저장으로 넘어간다 (예전에는 저장 자체가 실패).

### 성능
- 오프라인 모드에서 선곡 화면 스크롤 중 1초마다 씬 전체 `FindObjectsOfType`를 두 번 돌던 커튼 정리를, 직전 정리에서 잔류 커튼이 없었으면 다음 씬 전환까지 건너뛴다.
- 오프라인 모드의 더미 응답 로그(소셜·보상·해금·로그인)는 훅마다 한 번만, 서버 오류 팝업 차단 경고는 종류마다 10초에 한 번(생략 건수 표시)만 남긴다. 모든 API 호출이 실패하는 오프라인 환경에서 `Latest.log` 쓰기가 몰리던 것을 줄였다.

## [1.4.0] - 2026-11-25

### 추가
- **오프라인 독립 실행 모드 (`config.txt` `OfflineMode=1`, 기본 꺼짐)**: 스팀 클라이언트·로그인·게임 서버 없이 실행. 스팀 SDK·로그인·보상·소셜 API 우회, 서버 오류 팝업 차단, 프롤로그·튜토리얼 건너뛰기, 닫힌 채 남는 씬 전환 커튼 정리. 닉네임·ID는 `OfflineNickname`/`OfflineUserId`.
- 공식 곡 보유·해금 기록(`UserData\PLATiNALAB\song_ownership.json`): 온라인 실행 때 게임이 답한 값을 기록하고, 오프라인 모드에서 그대로 재현한다. 기록이 없는 곡은 잠긴 상태로 보인다.
- 하네스 `[Offline]` 검사(오프라인 전용 패치의 게이트 누락 검출)와 회귀 테스트 `JsonMapFile`·`SongOwnership`.

### 변경
- 직전 커밋(decf3b7)에서 모든 사용자에게 무조건 걸리던 스팀/서버 우회를 `OfflineMode=1`일 때만 동작하도록 바꿨다. 꺼져 있으면(기본) v1.3.0과 같이 스팀·서버는 게임 원본, 커스텀 곡만 로컬 처리.
- 모든 공식 곡(유료 DLC 포함)을 무조건 해금하던 동작을 제거했다. 오프라인에서는 온라인에서 기록한 보유 상태만 재현한다.
- 오프라인 모드에서 `Application.Quit`은 로그인 전·서버 오류 팝업을 막은 직후 15초 동안만 막는다 (예전에는 항상 막혀 게임 메뉴로 종료할 수 없었다).
- `song_ids.json` 읽기·저장을 공용 `JsonMapFile`로 옮겼다 (동작 동일: 교체 저장, `.bak`, 손상 시 `.corrupt` 보관 후 복구).

### 수정
- `SendGameEnd` 훅이 꺼지면(게임 업데이트 등) 커스텀 곡 결과가 서버로 갈 수 있던 회귀 (`LastStarted` 판정 복원).
- 필수 패치가 빠져 커스텀 곡 주입이 꺼진 상태에서도 커스텀 곡 로컬 세션을 만들던 회귀 (`CustomSongsAllowed` 확인 복원).
- 공개 DLL에 제작자의 스팀 ID·닉네임이 하드코딩돼 있던 문제.

### 성능
- 선곡 화면에서 곡을 넘길 때마다 씬 전체 `FindObjectsOfType`을 두 번 돌던 커튼 정리를 오프라인 모드에서만, 1초에 한 번으로 제한.
- 보유 기록용 곡 ID는 게터 대신 IL2CPP 문자열 포인터 캐시로 읽고, 파일 저장은 변경 3초 뒤 한 번으로 묶는다.

## [1.3.0] - 2026-11-23

### 변경 (업데이트 내성)
- MelonLoader 자동 `PatchAll`을 끄고(`[assembly: HarmonyDontPatchAll]`) `SafePatcher`가 `[HarmonyPatch]` 클래스를 하나씩 격리해 적용한다. 예전에는 게임 업데이트로 훅 하나(대상 메서드 삭제·오버로드 모호·인자 이름 변경)가 실패하면 그 뒤 패치가 전부 빠져, 무관한 기능까지(필수 패치가 뒤에 있으면 커스텀 곡 전체가) 꺼졌다. 실패한 클래스는 `[SafePatcher] … 적용 실패` 로그로 남고, 필수 패치 누락은 이전처럼 `[Health]`가 판단한다.
- 하네스 `[SafePatch]` 검사 추가: 자동 `PatchAll`이 다시 켜지거나 `SafePatcher`가 빠지면 실패한다.

### 수정
- 곡 폴더의 `music.wav`(`audio.wav`·`song.wav`·`bgm.wav`·`音乐.wav`·`音频.wav`)를 찾지 못해 기반 곡 음원이 나오던 문제. WAV 재생·길이 읽기는 이미 지원했지만 파일 탐색 목록에 `.ogg`만 있었다. 같은 이름이면 `.ogg`가 우선.
- `MediaDuration.ReadWav`에서 `data` 청크가 `fmt ` 청크보다 먼저 오는 WAV도 길이를 읽도록 보강, BMS `#SETRANDOM n` 분기 값 반영 및 `PlayOverlay` 초기 라벨 `enabled` 상태 동기화 (18차).

### 성능
- 판정바·키뷰어: 매 프레임 틱(최대 32개)마다 부르던 `Mathf.*`, `new Color/Rect/Vector2`, `Rect.x` 같은 Unity 값 형식 헬퍼(전부 `il2cpp_runtime_invoke`)를 `MathF`·필드 직접 대입으로 바꿨다. `Image.enabled` 게터와 오버레이 `== null`·`Screen.width/height`도 매 프레임 대신 관리 플래그와 30프레임 간격 확인으로 줄였다. 모양 스프라이트(알약·다이아몬드) 생성도 픽셀마다 IL2CPP 호출을 하지 않는다.
- `SongCategoryResourceCollection.GetDataSet` 훅이 모든 카테고리 키에서 먼저 하던 Unity `== null` 비교를 키 비교 뒤로 옮기고, `SoundData.FromHEAudioClip`·`DetermineRunning` 해제 경로의 `== null`을 참조 비교로 바꿨다.
- 선곡 화면을 열 때마다 남던 `SongListBuilder.BuildNameBanners` 로그를 `DiagnosticLogs=1`일 때만 남긴다.

## [1.2.1] - 2026-11-23

### 수정
- 음원·영상 길이에 맞춰 `endPosition`을 늘릴 때 초기 BPM 하나로만 계산해, 뒤쪽에서 BPM이 빨라지는 곡이 중간에 끝나던 문제 (`BmsConverter.PositionAfterSeconds`가 BPM 변속을 구간별로 반영).
- BMS에서 같은 위치·레인에 겹친 노트가 그대로 변환되던 문제 (하나로 합침), `info.txt` `lanes`를 5개 이상 적으면 key 5 이상 노트가 생기던 문제 (앞 4개만 사용).
- 커버 파일을 읽다 실패(잠김·저장 중)하면 파일을 수정하기 전까지 다시 시도하지 않던 문제.
- `chart.json`에 `bgms`가 없거나 `initialBpm`·`endPosition`·`bgms[].position`이 문자열 숫자면 음원·영상 길이 연장이 예외로 통째로 빠져 곡이 기반 곡 길이에서 끝나던 문제 (`BmsConverter.ReadDouble`로 형식과 무관하게 읽음).
- BMS `#ELSE`/`#ELSEIF` 분기 노트가 1번 분기와 함께 들어가 겹치던 문제, `#IFEND`가 분기를 닫지 못해 뒤쪽 노트가 모두 빠질 수 있던 문제.
- 같은 위치·레인에 단노트와 롱노트 시작이 겹치면 둘 다 남던 문제 (더 긴 노트 하나로 합침).
- `#BPM 0`(또는 음수)이 그대로 `initialBpm`이 되던 문제 (기본 130 유지).
- 오토 플레이 중 설정 저장(`InGameSettingManager.Save`)이 예외로 끝나면 오토 플레이 값이 다시 걸리지 않던 문제 (Postfix → Finalizer).

### 변경
- 시작 로그의 버전 문자열을 `MelonInfo`에서 읽도록 수정 (1.2.0인데 `v1.1.0`으로 찍히던 하드코딩 제거).
- 하네스 회귀 테스트 추가: `BmsOverlapHold`, `BmsElse`, `BmsZeroBpm`, `ReadDouble`.

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
