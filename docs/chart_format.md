# PLATiNA LAB 차트(PPP) 형식 & 커스텀 차트 사용법

실측: 2026-10-30, `FourLines_Easy` 덤프 (Build 25981392).

## 사용법
1. 원하는 곡을 한 번 플레이 → `UserData\PLATiNALAB\ChartDump\{패턴키}.json` 생성 (MelonLoader 로그에 `원본 차트: {패턴키}` 출력)
2. 그 파일을 복사해서 수정 → `UserData\PLATiNALAB\CustomCharts\{패턴키}.json` 으로 저장
3. 같은 곡/난이도를 플레이하면 교체됨 (로그: `커스텀 차트 적용`). 파일은 매 플레이마다 다시 읽으므로 게임 재시작 불필요.
   - 원래대로 돌리려면 CustomCharts의 파일을 지우거나 이름을 바꾼다.

패턴키 = `{곡ID}__{FilePath 파일명}`. 예: `wits_biz__FourLines_Easy`. (파일명만으로는 여러 곡이 겹친다)
커스텀 곡(`custom` 카테고리의 Custom Test)의 차트는 `CustomCharts\custom_test.json` — 없으면 첫 노트만 남긴 차트가 자동 생성된다.

## JSON 구조 (System.Text.Json, camelCase)
```json
{
  "initialBpm": 165,
  "ignoreShifts": false,
  "bars":   {},            // { "마디번호": {"numerator":3,"denominator":4} } 박자 변경
  "bpms":   {},            // { "위치": bpm } BPM 변속
  "stops":  {},
  "scrolls": {},
  "bgms": [{"position":1, "sound":"lolikami_165bpm.fmod.ogg", "column":4}],
  "bgaPosition": 0,
  "notes": [
    {"startPosition":2.5, "endPosition":null, "ticks":null, "keys":[1], "key":1, "sound":"", "type":0},
    {"startPosition":4, "endPosition":4.25, "ticks":[4,4.0625,4.125,4.1875], "keys":[1], "key":1, "sound":"", "type":0}
  ],
  "sections": [{"name":"NOTES","startPosition":0}],
  "endPosition": 81
}
```

## 위치(position) 단위
- `마디번호 + 마디 내 비율` (double). 4/4 기준 `2.25` = 2번 마디의 2번째 박.
- BGM은 `position: 1`에서 시작 → 1번 마디 시작이 음원 0초.
- 4/4, BPM 고정일 때 초 환산: `(position - 1) × 4 × 60 / bpm`.
  (BMS tick 환산과 같음: 1마디 = 4박 = 240/bpm 초)

## 노트
- `key`: 레인 1~4 (FourLines 기준). `keys`는 같은 값의 배열(동시 입력용으로 보임) — 둘 다 맞춰 적는다.
- 롱노트: `endPosition`에 끝 위치, `ticks`에 판정 틱 위치 목록. 실측 원본은 시작부터 끝 직전까지 1/16마디 간격.
- 단노트: `endPosition`·`ticks` 모두 `null`.
- `type`: 실측 원본은 전부 0. 다른 값의 의미는 미확인.

## BMS로 차트 만들기 (hwa\ 곡 폴더)
곡 폴더에 `.bms`(또는 `.bme/.bml/.pms`)가 있으면 JSON 차트 대신 BMS를 매 플레이마다 변환해서 쓴다(게임 재시작 불필요).
난이도별로 파일명에 난이도 이름이 들어간 BMS(`hard.bms`, `song_hard.bms` 등)를 쓰고, 없으면 이름순 첫 BMS를 쓴다.

- 위치: `PPP position = BMS 마디 + 1 + tick / 마디 tick 수` → `time = (position - 1) × 240 / bpm`. BMS 000마디 시작 = 음원 0초.
- 노트 종류는 `#WAV` 파일 이름으로 정한다.

| WAV 파일 | PPP 노트 |
| --- | --- |
| `ShortNote.wav` (그 밖의 이름도) | 단노트 |
| `LongNote 홀드 시작.wav` → `LongNote 홀드 끝.wav` (같은 채널) | 롱노트 (`endPosition` + 1/16마디 `ticks`) |
| `PitchNote.wav` | 피치 노트 (`key: 255`, 채널 무관) |

- 표준 롱노트(`#LNTYPE 1`의 채널 51~59, `#LNOBJ`)도 지원.
- 레인: `info.txt`에 `lanes = 16,11,12,13`처럼 레인 1~4 순서의 채널을 적는다. 없으면 쓰인 채널을 BMSE 화면 순서(16, 11, 12, 13, 14, 15, 18, 19)로 앞에서 4개.
- 곡 음원(`music.ogg`)을 가리키는 오브젝트 위치가 BGM 시작 위치가 된다(채널 무관). 없으면 position 1.
- 오브젝트 ID는 2자리·3자리 모두 지원(`#WAV` 키 길이로 판단).
- BPM 변경(채널 03, 08)은 `bpms`로 변환. 박자 변경(#mmm02)·STOP(09)·키음은 미지원(경고 로그).
- 변환 결과는 MelonLoader 로그 `[BMS] ... 노트 N (롱 N, 피치 N), 레인 [...]`으로 확인.

## PitchNote
PPP `key`가 255인 단노트. 게임 `PatternParser.GetNotes`가 key를 그대로 `NoteEntity.HorizontalIndex`에 넣고,
`PatternPlayer.PopulateNotes`가 `HorizontalIndex == 0xFF`면 `PitchNote`, 아니면 `ShortNote(time, HorizontalIndex)`를 만든다 (GameAssembly 디스어셈블).
롱노트(`endPosition` 있음)는 피치가 될 수 없다.
