using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using PLATiNALAB.Mod;

internal static class PureLogicTests
{
    public static bool RunAll()
    {
        int failed = 0;
        var tmp = Path.Combine(Path.GetTempPath(), "platinalab_harness_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            failed += TestBmsConverter(tmp);
            failed += TestEncodingHelper(tmp);
            failed += TestMediaDuration(tmp);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
        Console.WriteLine($"[PureLogic] 실패 {failed}건");
        return failed == 0;
    }

    private static int TestBmsConverter(string tmp)
    {
        int failed = 0;
        var bmsPath = Path.Combine(tmp, "test_hard.bms");
        File.WriteAllText(bmsPath, string.Join('\n',
            "#TITLE Test   Song",
            "#ARTIST Don ' t Stop",
            "#BPM 150",
            "#PLAYLEVEL 9",
            "#LNOBJ 04",
            "#WAV01 ShortNote.wav",
            "#WAV02 LongNote 홀드 시작.wav",
            "#WAV03 LongNote 홀드 끝.wav",
            "#WAV04 PitchNote.wav",
            "#WAV05 music.ogg",
            "#WAV06 PitchNote.wav",
            "#BPM01 180",
            "#BPM02 0",
            "#00001:0500",
            "#00116:01000200",
            "#00116:00000003",
            "#00108:0102",
            "#00211:0104",
            "#00252:01000100",
            "#00313:0600"));

        var hdr = BmsConverter.ParseHeader(bmsPath);
        if (hdr.Title != "Test Song" || hdr.Artist != "Don't Stop" || Math.Abs(hdr.Bpm - 150) > 0.001 || hdr.PlayLevel != 9)
        {
            Console.WriteLine($"   [FAIL BmsHeader] title='{hdr.Title}' artist='{hdr.Artist}' bpm={hdr.Bpm} lv={hdr.PlayLevel}");
            failed++;
        }

        var bms = BmsConverter.Parse(bmsPath);
        var baseChart = new JsonObject
        {
            ["bgms"] = new JsonArray { new JsonObject { ["position"] = 1, ["sound"] = "base.fmod.ogg", ["column"] = 0 } },
            ["notes"] = new JsonArray(),
            ["sections"] = new JsonArray(),
        };
        var ppp = BmsConverter.Convert(bms, "music.ogg", new[] { "16", "11", "12", "13" }, baseChart);
        var notes = ppp["notes"]?.AsArray();
        // 기대: #00116 단노트(01) + 롱노트(02~03), #00211 LNOBJ 롱노트(01~04), #00252 LNTYPE1 롱노트(01~01), #00313 피치노트(04 -> key 255) = 총 5노트
        if (notes == null || notes.Count != 5)
        {
            Console.WriteLine($"   [FAIL BmsConvert] 노트 수={notes?.Count} (기대 5)");
            return failed + 1;
        }
        int holds = notes.Count(n => n!["endPosition"] != null);
        int pitches = notes.Count(n => n!["key"]?.GetValue<int>() == BmsConverter.PitchKey);
        if (holds != 3 || pitches != 1)
        {
            Console.WriteLine($"   [FAIL BmsConvert] holds={holds} (기대 3), pitches={pitches} (기대 1)");
            failed++;
        }
        var bpms = ppp["bpms"]?.AsObject();
        if (bpms == null || bpms.Count != 1 || Math.Abs((bpms["2"]?.GetValue<double>() ?? 0) - 180) > 0.001)
        {
            Console.WriteLine($"   [FAIL BmsConvert] bpms 필터링 실패: {bpms?.ToJsonString()}");
            failed++;
        }

        // 난이도 정규화 다국어 회귀
        var diffCases = new (string input, string expected)[]
        {
            ("", "easy"), ("EZ", "easy"), ("简单", "easy"), ("하드", "hard"),
            ("专家", "over"), ("SP", "plus"), ("테스트", "testpattern"), ("custom_diff", "custom_diff"),
        };
        foreach (var (input, expected) in diffCases)
        {
            var actual = BmsConverter.NormalizeDifficulty(input);
            if (actual != expected)
            {
                Console.WriteLine($"   [FAIL NormalizeDifficulty] '{input}' -> '{actual}' (기대 '{expected}')");
                failed++;
            }
        }
        // 탭 구분자 BMS 헤더(#TITLE\t..., #WAV01\t...) 지원 회귀
        var tabBmsPath = Path.Combine(tmp, "tab_header.bms");
        File.WriteAllText(tabBmsPath, "#TITLE\tTab Song\n#ARTIST\tTab Artist\n#BPM\t140\n#WAV01\tShortNote.wav\n#00116:0100\n");
        var tabHdr = BmsConverter.ParseHeader(tabBmsPath);
        if (tabHdr.Title != "Tab Song" || tabHdr.Artist != "Tab Artist" || Math.Abs(tabHdr.Bpm - 140) > 0.001)
        {
            Console.WriteLine($"   [FAIL BmsTabHeader] title='{tabHdr.Title}' artist='{tabHdr.Artist}' bpm={tabHdr.Bpm}");
            failed++;
        }
        // #RANDOM/#IF: 1번 분기만 변환해야 한다 (분기마다 노트가 있으면 겹치지 않아야 함)
        var randPath = Path.Combine(tmp, "random.bms");
        File.WriteAllText(randPath, string.Join('\n',
            "#BPM 120",
            "#WAV01 ShortNote.wav",
            "#RANDOM 2",
            "#IF 1",
            "#00111:01",
            "#ENDIF",
            "#IF 2",
            "#00112:01",
            "#ENDIF",
            "#ENDRANDOM",
            "#00113:01"));
        var randChart = BmsConverter.Parse(randPath);
        var randChannels = randChart.Objects.Select(o => o.Channel).OrderBy(c => c).ToArray();
        if (randChannels.Length != 2 || randChannels[0] != "11" || randChannels[1] != "13")
        {
            Console.WriteLine($"   [FAIL BmsRandom] 채널={string.Join(",", randChannels)} (기대 11,13)");
            failed++;
        }

        // 같은 위치·레인에 겹친 노트는 하나로, 4개를 넘는 명시 레인은 앞 4개만
        var dupPath = Path.Combine(tmp, "dup.bms");
        File.WriteAllText(dupPath, string.Join('\n',
            "#BPM 120", "#WAV01 ShortNote.wav",
            "#00111:01", "#00111:01", "#00112:01"));
        var dupPpp = BmsConverter.Convert(BmsConverter.Parse(dupPath), null, new[] { "11", "12", "13", "14", "15" }, null);
        int dupNotes = dupPpp["notes"]!.AsArray().Count;
        if (dupNotes != 2)
        {
            Console.WriteLine($"   [FAIL BmsDuplicate] 노트 수={dupNotes} (기대 2)");
            failed++;
        }

        // 단노트와 롱노트 시작이 같은 자리면 하나로 합치고 롱노트를 남긴다 (16차)
        var overlapPath = Path.Combine(tmp, "overlap.bms");
        File.WriteAllText(overlapPath, string.Join('\n',
            "#BPM 120", "#WAV01 ShortNote.wav", "#WAV02 LongNote 홀드 시작.wav", "#WAV03 LongNote 홀드 끝.wav",
            "#00111:01", "#00111:02", "#00211:03"));
        var overlapNotes = BmsConverter.Convert(BmsConverter.Parse(overlapPath), null, new[] { "11" }, null)["notes"]!.AsArray();
        if (overlapNotes.Count != 1 || overlapNotes[0]!["endPosition"] == null)
        {
            Console.WriteLine($"   [FAIL BmsOverlapHold] 노트 수={overlapNotes.Count} (기대 1, 롱노트)");
            failed++;
        }

        // #ELSE/#ELSEIF/#IFEND: 1번 분기를 골랐으면 나머지 분기는 넣지 않는다 (16차)
        var elsePath = Path.Combine(tmp, "else.bms");
        File.WriteAllText(elsePath, string.Join('\n',
            "#BPM 0", "#WAV01 ShortNote.wav",
            "#RANDOM 3",
            "#IF 1", "#00111:01",
            "#ELSEIF 2", "#00112:01",
            "#ELSE", "#00113:01",
            "#IFEND",
            "#IF 2", "#00114:01",
            "#ELSE", "#00115:01",
            "#ENDIF",
            "#00118:01"));
        var elseChart = BmsConverter.Parse(elsePath);
        var elseChannels = string.Join(",", elseChart.Objects.Select(o => o.Channel).OrderBy(c => c));
        if (elseChannels != "11,15,18")
        {
            Console.WriteLine($"   [FAIL BmsElse] 채널={elseChannels} (기대 11,15,18)");
            failed++;
        }
        if (Math.Abs(elseChart.Bpm - 130) > 1e-9)
        {
            Console.WriteLine($"   [FAIL BmsZeroBpm] #BPM 0 → {elseChart.Bpm} (기대 기본값 130)");
            failed++;
        }

        // JSON 숫자 읽기: int로 만든 값·문자열 숫자도 읽어야 한다 (GetValue<double>은 int 값에서 예외)
        if (BmsConverter.ReadDouble(JsonValue.Create(1), -1) != 1 || BmsConverter.ReadDouble(JsonNode.Parse("2.5"), -1) != 2.5 ||
            BmsConverter.ReadDouble(JsonValue.Create("3"), -1) != 3 || BmsConverter.ReadDouble(null, -1) != -1)
        {
            Console.WriteLine("   [FAIL ReadDouble] int/JsonElement/문자열/null 값 읽기");
            failed++;
        }

        // 길이 → endPosition 계산: BPM 변속 구간별 반영
        // 120BPM = 마디당 2초. 시작 위치 1에서 4초 → 위치 3.
        double flat = BmsConverter.PositionAfterSeconds(1, 4, 120, null);
        // 위치 2에서 240BPM으로 변속: 첫 2초에 1마디(→위치 2), 남은 2초는 마디당 1초 → 위치 4
        var change = new JsonObject { ["2"] = 240 };
        double fast = BmsConverter.PositionAfterSeconds(1, 4, 120, change);
        // 시작 위치(1)에서 이미 적용된 변속은 초기 BPM 대신 쓴다
        double already = BmsConverter.PositionAfterSeconds(2, 2, 120, change);
        if (Math.Abs(flat - 3) > 1e-9 || Math.Abs(fast - 4) > 1e-9 || Math.Abs(already - 4) > 1e-9)
        {
            Console.WriteLine($"   [FAIL PositionAfterSeconds] flat={flat} (기대 3) fast={fast} (기대 4) already={already} (기대 4)");
            failed++;
        }
        return failed;
    }

    private static int TestEncodingHelper(string tmp)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        int failed = 0;
        var cases = new (string name, Encoding enc, string text)[]
        {
            ("utf8_bom.txt", new UTF8Encoding(true), "테스트 中文 日本語"),
            ("utf8.txt", new UTF8Encoding(false), "테스트 中文 日本語"),
            ("gbk.txt", Encoding.GetEncoding(936), "测试曲名 谱师 难度"),
            ("sjis.txt", Encoding.GetEncoding(932), "どりーむもーど テスト"),
            ("cp949.txt", Encoding.GetEncoding(949), "한글 제목 테스트"),
        };
        foreach (var (name, enc, text) in cases)
        {
            var p = Path.Combine(tmp, name);
            File.WriteAllBytes(p, enc.GetBytes(text));
            var lines = EncodingHelper.ReadAllLinesSafe(p);
            if (lines.Length != 1 || lines[0] != text)
            {
                Console.WriteLine($"   [FAIL Encoding:{name}] '{(lines.Length > 0 ? lines[0] : "")}' (기대 '{text}')");
                failed++;
            }
        }
        // 과장 UTF-8 시퀀스(0xC0 0xA1 — CP949/GBK 유효 바이트)가 UTF-8로 오판되지 않아야 한다
        var overlongBytes = new byte[] { 0xC0, 0xA1, 0xB0, 0xA1 };
        if (EncodingHelper.DetectEncoding(overlongBytes).CodePage == 65001)
        {
            Console.WriteLine("   [FAIL Encoding:overlong] 과장 UTF-8 시퀀스(0xC0 0xA1)를 UTF-8로 오판");
            failed++;
        }
        return failed;
    }

    private static int TestMediaDuration(string tmp)
    {
        int failed = 0;

        // 1) 최소 OGG Vorbis: 앞 페이지에 \x01vorbis 헤더(44100Hz), 마지막 페이지에 granule=88200 (2.0s)
        var oggPath = Path.Combine(tmp, "test.ogg");
        var ogg = new byte[128];
        Encoding.ASCII.GetBytes("OggS").CopyTo(ogg, 0);
        ogg[28] = 1;
        Encoding.ASCII.GetBytes("vorbis").CopyTo(ogg, 29);
        BinaryPrimitives.WriteInt32LittleEndian(ogg.AsSpan(40, 4), 44100);
        Encoding.ASCII.GetBytes("OggS").CopyTo(ogg, 64);
        BinaryPrimitives.WriteInt64LittleEndian(ogg.AsSpan(70, 8), 88200);
        File.WriteAllBytes(oggPath, ogg);
        var oggSec = MediaDuration.Ogg(oggPath);
        if (oggSec == null || Math.Abs(oggSec.Value - 2.0) > 0.001)
        {
            Console.WriteLine($"   [FAIL MediaDuration.Ogg] {oggSec} (기대 2.0)");
            failed++;
        }

        // 1-b) 마지막 진짜 페이지 뒤에 일련번호가 다른 가짜 "OggS"(오디오 데이터 속 우연한 바이트 등)가 있어도 무시해야 한다
        var oggFakePath = Path.Combine(tmp, "test_fake_tail.ogg");
        var oggFake = new byte[160];
        ogg.CopyTo(oggFake, 0);
        Encoding.ASCII.GetBytes("OggS").CopyTo(oggFake, 128);
        BinaryPrimitives.WriteInt64LittleEndian(oggFake.AsSpan(134, 8), 999_999_999);
        BinaryPrimitives.WriteUInt32LittleEndian(oggFake.AsSpan(142, 4), 0x1234);
        File.WriteAllBytes(oggFakePath, oggFake);
        var fakeSec = MediaDuration.Ogg(oggFakePath);
        if (fakeSec == null || Math.Abs(fakeSec.Value - 2.0) > 0.001)
        {
            Console.WriteLine($"   [FAIL MediaDuration.Ogg] 가짜 OggS 꼬리: {fakeSec} (기대 2.0)");
            failed++;
        }

        // 2) 최소 MP4: moov(48B) > mvhd(40B, 내용 32B: version 0, timescale=1000, duration=5000 -> 5.0s)
        var mp4Path = Path.Combine(tmp, "test.mp4");
        var mp4 = new byte[48];
        BinaryPrimitives.WriteUInt32BigEndian(mp4.AsSpan(0, 4), 48);
        Encoding.ASCII.GetBytes("moov").CopyTo(mp4, 4);
        BinaryPrimitives.WriteUInt32BigEndian(mp4.AsSpan(8, 4), 40);
        Encoding.ASCII.GetBytes("mvhd").CopyTo(mp4, 12);
        BinaryPrimitives.WriteUInt32BigEndian(mp4.AsSpan(28, 4), 1000);
        BinaryPrimitives.WriteUInt32BigEndian(mp4.AsSpan(32, 4), 5000);
        File.WriteAllBytes(mp4Path, mp4);
        var mp4Sec = MediaDuration.Mp4(mp4Path);
        if (mp4Sec == null || Math.Abs(mp4Sec.Value - 5.0) > 0.001)
        {
            Console.WriteLine($"   [FAIL MediaDuration.Mp4] {mp4Sec} (기대 5.0)");
            failed++;
        }

        // 2-b) MP4 길이 미정(duration=0xFFFFFFFF, fragmented/streamed) 및 알 수 없는 버전(2)은 null 반환
        var mp4UndefPath = Path.Combine(tmp, "test_undef.mp4");
        var mp4Undef = (byte[])mp4.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(mp4Undef.AsSpan(32, 4), uint.MaxValue);
        File.WriteAllBytes(mp4UndefPath, mp4Undef);
        if (MediaDuration.Mp4(mp4UndefPath) != null)
        {
            Console.WriteLine($"   [FAIL MediaDuration.Mp4] 길이 미정(0xFFFFFFFF)에서 null이 아닌 값 반환");
            failed++;
        }
        var mp4BadVerPath = Path.Combine(tmp, "test_badver.mp4");
        var mp4BadVer = (byte[])mp4.Clone();
        mp4BadVer[16] = 2; // mvhd version = 2 (비표준)
        File.WriteAllBytes(mp4BadVerPath, mp4BadVer);
        if (MediaDuration.Mp4(mp4BadVerPath) != null)
        {
            Console.WriteLine($"   [FAIL MediaDuration.Mp4] 알 수 없는 mvhd 버전(2)에서 null이 아닌 값 반환");
            failed++;
        }

        // 2-c) WAV: fmt(byteRate=176400) + data(352800B) = 2.0s. Audio()가 RIFF 헤더로 WAV를 알아본다.
        var wavPath = Path.Combine(tmp, "test.wav");
        var wav = new byte[12 + 24 + 8 + 352800];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4, 4), (uint)(wav.Length - 8));
        Encoding.ASCII.GetBytes("WAVE").CopyTo(wav, 8);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(wav, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(20, 2), 1);       // PCM
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(22, 2), 2);       // 채널
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(24, 4), 44100);   // 샘플레이트
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(28, 4), 176400);  // byteRate
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(32, 2), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(34, 2), 16);
        Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(40, 4), 352800);
        File.WriteAllBytes(wavPath, wav);
        var wavSec = MediaDuration.Audio(wavPath);
        if (wavSec == null || Math.Abs(wavSec.Value - 2.0) > 0.001)
        {
            Console.WriteLine($"   [FAIL MediaDuration.Audio] WAV {wavSec} (기대 2.0)");
            failed++;
        }
        // 2-d) WAV data 청크가 fmt 청크보다 앞에 와도(표준 허용) 길이를 읽어야 한다
        var wavDataFirstPath = Path.Combine(tmp, "test_data_first.wav");
        var wavDf = new byte[12 + 8 + 352800 + 24];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(wavDf, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(wavDf.AsSpan(4, 4), (uint)(wavDf.Length - 8));
        Encoding.ASCII.GetBytes("WAVE").CopyTo(wavDf, 8);
        Encoding.ASCII.GetBytes("data").CopyTo(wavDf, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(wavDf.AsSpan(16, 4), 352800);
        int fmtOff = 12 + 8 + 352800;
        Encoding.ASCII.GetBytes("fmt ").CopyTo(wavDf, fmtOff);
        BinaryPrimitives.WriteUInt32LittleEndian(wavDf.AsSpan(fmtOff + 4, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(wavDf.AsSpan(fmtOff + 8, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(wavDf.AsSpan(fmtOff + 10, 2), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(wavDf.AsSpan(fmtOff + 12, 4), 44100);
        BinaryPrimitives.WriteUInt32LittleEndian(wavDf.AsSpan(fmtOff + 16, 4), 176400);
        BinaryPrimitives.WriteUInt16LittleEndian(wavDf.AsSpan(fmtOff + 20, 2), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(wavDf.AsSpan(fmtOff + 22, 2), 16);
        File.WriteAllBytes(wavDataFirstPath, wavDf);
        var wavDfSec = MediaDuration.Audio(wavDataFirstPath);
        if (wavDfSec == null || Math.Abs(wavDfSec.Value - 2.0) > 0.001)
        {
            Console.WriteLine($"   [FAIL MediaDuration.Audio] WAV data-first {wavDfSec} (기대 2.0)");
            failed++;
        }
        // 2-e) BMS #SETRANDOM 2는 2번 분기를 선택해야 한다
        var setRndPath = Path.Combine(tmp, "setrandom.bms");
        File.WriteAllText(setRndPath, "#BPM 120\n#WAV01 ShortNote.wav\n#SETRANDOM 2\n#IF 1\n#00111:0100\n#ELSEIF 2\n#00211:0100\n#ENDIF\n");
        var setRnd = BmsConverter.Parse(setRndPath);
        if (setRnd.Objects.Count != 1 || setRnd.Objects[0].Measure != 2)
        {
            Console.WriteLine($"   [FAIL BmsSetRandom] count={setRnd.Objects.Count}, measure={(setRnd.Objects.Count > 0 ? setRnd.Objects[0].Measure : -1)} (기대 1, 2)");
            failed++;
        }
        var oggAudioSec = MediaDuration.Audio(oggPath);
        if (oggAudioSec == null || Math.Abs(oggAudioSec.Value - 2.0) > 0.001)
        {
            Console.WriteLine($"   [FAIL MediaDuration.Audio] Ogg {oggAudioSec} (기대 2.0)");
            failed++;
        }

        // 3) 손상된/빈 파일은 예외 없이 null 반환
        var badPath = Path.Combine(tmp, "corrupt.bin");
        File.WriteAllBytes(badPath, new byte[] { 0, 1, 2, 3, 4 });
        if (MediaDuration.Ogg(badPath) != null || MediaDuration.Mp4(badPath) != null)
        {
            Console.WriteLine("   [FAIL MediaDuration] 손상된 파일에서 null이 아닌 값 반환");
            failed++;
        }

        return failed;
    }
}
