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

        return failed;
    }
}
