using System.Globalization;
using System.Text.Json.Nodes;

namespace PLATiNALAB.Mod;

/// <summary>
/// BMS → PPP 차트(JSON) 변환기. 곡 폴더에 .bms가 있으면 CustomCharts.Process가 매 플레이마다 변환한다(핫리로드).
///
/// 위치: PPP position = BMS 마디 + 1 + tick / 마디 tick 수.
///   (BMS 000마디 시작 ↔ PPP position 1. 1마디 = 4박이므로 time = tick × 240 / bpm / 마디 tick 수)
///
/// 노트 종류는 #WAV 파일 이름으로 정한다 (hwa\ 곡 폴더 규칙):
///   ShortNote.wav → 단노트, "LongNote 홀드 시작.wav" ~ "LongNote 홀드 끝.wav" → 같은 채널에서 쌍을 이루는 롱노트,
///   PitchNote.wav → 피치 노트(key 255). 그 밖의 이름은 단노트.
///   표준 LNTYPE 1(채널 51~59, 61~69)과 #LNOBJ도 롱노트로 처리한다.
/// PitchNote: 게임 PopulateNotes(RVA 0x2CF72F0)가 HorizontalIndex == 0xFF인 단노트를 PitchNote로 만든다.
///   GetNotes(RVA 0x2D0C180)는 PPP key를 그대로 HorizontalIndex에 넣으므로 PPP key = 255.
///
/// 레인: info.txt `lanes = 16,11,12,13`(레인 1~4 순서의 채널)로 지정. 없으면 쓰인 1P 노트 채널을
///   BMSE 화면 순서(16, 11, 12, 13, 14, 15, 18, 19)로 앞에서 4개.
/// 오브젝트 ID 폭: #WAV 키 길이(2자리 base36 또는 3자리)로 판단.
/// BGM 시작: music.ogg(곡 폴더 음원)를 가리키는 오브젝트의 위치. 없으면 position 1.
///
/// 미지원 (경고 로그): 박자 변경(#mmm02, PPP bars의 비 4/4 위치 의미 미확인), STOP(09), 키음.
/// </summary>
internal static class BmsConverter
{
    /// <summary>BMS 채널 오브젝트 하나 (마디, 채널, 마디 내 비율, 오브젝트 ID).</summary>
    internal readonly record struct BmsObject(int Measure, string Channel, double Fraction, string Value)
    {
        public double Position => Measure + 1 + Fraction;
    }

    internal sealed class BmsChart
    {
        public string Path;
        public string Title;
        public string Artist;
        public double Bpm = 130;
        public int PlayLevel;
        public string LnObj;
        public readonly Dictionary<string, string> Wav = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, double> BpmTable = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<BmsObject> Objects = new();
        public readonly HashSet<int> MeterChanges = new();
    }

    private enum Role { Short, HoldStart, HoldEnd, Pitch }

    public const int PitchKey = 255;
    private static readonly string[] DefaultChannelOrder = { "16", "11", "12", "13", "14", "15", "18", "19" };
    private static readonly string[] Extensions = { ".bms", ".bme", ".bml", ".pms" };

    /// <summary>난이도에 쓸 BMS: 파일명에 난이도 이름이 들어간 것 → 없으면 이름순 첫 BMS.</summary>
    public static string FindFor(string folder, string difficulty)
    {
        var files = Directory.GetFiles(folder)
            .Where(f => Extensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var f in files)
            if (System.IO.Path.GetFileNameWithoutExtension(f).Contains(difficulty, StringComparison.OrdinalIgnoreCase))
                return f;
        return files.FirstOrDefault();
    }

    public static BmsChart Parse(string path)
    {
        var chart = new BmsChart { Path = path };
        var dataLines = new List<(int measure, string channel, string data)>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim().TrimStart('﻿');
            if (!line.StartsWith("#")) continue;

            // #mmmcc:data — 채널 데이터 (오브젝트 폭은 헤더를 다 읽은 뒤 정한다)
            if (line.Length > 7 && line[6] == ':' && int.TryParse(line.AsSpan(1, 3), out var measure))
            {
                dataLines.Add((measure, line.Substring(4, 2).ToUpperInvariant(), line[7..].Trim()));
                continue;
            }

            int space = line.IndexOf(' ');
            if (space < 0) continue;
            var key = line[1..space].ToUpperInvariant();
            var arg = line[(space + 1)..].Trim();
            if (key == "TITLE") chart.Title = arg;
            else if (key == "ARTIST") chart.Artist = arg;
            else if (key == "BPM" && TryDouble(arg, out var bpm)) chart.Bpm = bpm;
            else if (key == "PLAYLEVEL" && int.TryParse(arg, out var lv)) chart.PlayLevel = lv;
            else if (key == "LNOBJ") chart.LnObj = arg.ToUpperInvariant();
            else if (key.StartsWith("WAV") && key.Length > 3) chart.Wav[key[3..]] = arg;
            else if (key.StartsWith("BPM") && key.Length > 3 && TryDouble(arg, out var b)) chart.BpmTable[key[3..]] = b;
        }

        int width = chart.Wav.Count > 0 ? chart.Wav.Keys.Max(k => k.Length) : 2;
        foreach (var (measure, channel, data) in dataLines)
        {
            if (channel == "02") { chart.MeterChanges.Add(measure); continue; }
            int w = channel == "03" ? 2 : width; // 03은 16진 BPM 값 2자리
            int count = data.Length / w;
            for (int i = 0; i < count; i++)
            {
                var value = data.Substring(i * w, w).ToUpperInvariant();
                if (value.Trim('0').Length == 0) continue;
                chart.Objects.Add(new BmsObject(measure, channel, (double)i / count, value));
            }
        }
        return chart;
    }

    /// <summary>
    /// PPP JSON 생성. bgms[].sound는 CustomSongs.PrepareChart가 기반 곡 파일명으로 바꾸고,
    /// endPosition은 ExtendToMediaLength가 music.ogg/video.mp4 길이에 맞춰 늘린다.
    /// </summary>
    public static JsonObject Convert(BmsChart bms, string musicFileName, IList<string> laneChannels, JsonObject baseChart)
    {
        var log = System.IO.Path.GetFileName(bms.Path);

        // BGM 시작 위치: 곡 음원을 가리키는 오브젝트
        double bgmPosition = 1;
        var bgm = bms.Objects.Where(o => IsMusic(bms, o.Value, musicFileName)).OrderBy(o => o.Position).ToList();
        if (bgm.Count > 0) bgmPosition = bgm[0].Position;

        // 레인 채널
        var lanes = laneChannels?.Where(c => c.Length == 2).Select(c => c.ToUpperInvariant()).ToList();
        if (lanes == null || lanes.Count == 0)
        {
            var used = bms.Objects.Select(o => NoteChannel(o.Channel)).ToHashSet();
            lanes = DefaultChannelOrder.Where(used.Contains).ToList();
            if (lanes.Count > 4)
                Core.Log.Warning($"[BMS] {log}: 노트 채널 {lanes.Count}개 ({string.Join(",", lanes)}) → 앞 4개만 사용. info.txt `lanes = ..`로 지정 가능");
            lanes = lanes.Take(4).ToList();
        }
        var laneOf = new Dictionary<string, int>();
        for (int i = 0; i < lanes.Count; i++) laneOf[lanes[i]] = i + 1;

        var notes = new List<(double start, double? end, int key)>();
        var openHold = new Dictionary<string, (double pos, int lane)>();   // 채널 → 홀드 시작 (WAV 이름 규칙)
        var openLnType = new Dictionary<string, (double pos, int lane)>(); // LN 채널 → 시작 (LNTYPE 1)
        var lastShort = new Dictionary<string, int>();        // 채널 → 마지막 단노트 인덱스 (LNOBJ)
        int ignored = 0, unpaired = 0;

        foreach (var o in bms.Objects.OrderBy(o => o.Position).ThenBy(o => o.Channel))
        {
            if (IsMusic(bms, o.Value, musicFileName)) continue;
            var channel = NoteChannel(o.Channel);
            if (!laneOf.TryGetValue(channel, out var lane)) { ignored++; continue; }
            double pos = o.Position;

            if (channel != o.Channel) // 51~59 / 61~69: LNTYPE 1 시작·끝 쌍
            {
                if (openLnType.Remove(channel, out var s)) notes.Add((s.pos, pos, lane));
                else openLnType[channel] = (pos, lane);
                continue;
            }
            if (bms.LnObj != null && o.Value == bms.LnObj)
            {
                if (lastShort.Remove(channel, out var idx)) notes[idx] = (notes[idx].start, pos, notes[idx].key);
                else unpaired++;
                continue;
            }

            switch (RoleOf(bms, o.Value))
            {
                case Role.HoldStart:
                    if (openHold.Remove(channel, out var prev)) { notes.Add((prev.pos, null, lane)); unpaired++; }
                    openHold[channel] = (pos, lane);
                    break;
                case Role.HoldEnd:
                    if (openHold.Remove(channel, out var start)) notes.Add((start.pos, pos, lane));
                    else unpaired++;
                    break;
                case Role.Pitch:
                    notes.Add((pos, null, PitchKey));
                    break;
                default:
                    lastShort[channel] = notes.Count;
                    notes.Add((pos, null, lane));
                    break;
            }
        }
        // 끝이 없는 홀드는 단노트로
        foreach (var s in openHold.Values.Concat(openLnType.Values)) { notes.Add((s.pos, null, s.lane)); unpaired++; }

        var noteArray = new JsonArray();
        double last = bgmPosition;
        foreach (var n in notes.OrderBy(n => n.start).ThenBy(n => n.key))
        {
            last = Math.Max(last, n.end ?? n.start);
            noteArray.Add(new JsonObject
            {
                ["startPosition"] = n.start,
                ["endPosition"] = n.end,
                ["ticks"] = n.end is double e ? Ticks(n.start, e) : null,
                ["keys"] = new JsonArray(n.key),
                ["key"] = n.key,
                ["sound"] = "",
                ["type"] = 0,
            });
        }

        // BPM 변경: 03(16진 정수), 08(#BPMxx 참조)
        var bpms = new JsonObject();
        foreach (var o in bms.Objects.OrderBy(o => o.Position))
        {
            double? bpm = o.Channel == "03" && int.TryParse(o.Value, NumberStyles.HexNumber, null, out var hex) ? hex
                : o.Channel == "08" && bms.BpmTable.TryGetValue(o.Value, out var t) ? t : null;
            if (bpm != null) bpms[o.Position.ToString("R", CultureInfo.InvariantCulture)] = bpm;
        }

        if (bms.MeterChanges.Count > 0)
            Core.Log.Warning($"[BMS] {log}: 박자 변경(#mmm02, 마디 {string.Join(",", bms.MeterChanges.OrderBy(m => m))})은 미지원 → 4/4로 변환");
        if (bms.Objects.Any(o => o.Channel == "09"))
            Core.Log.Warning($"[BMS] {log}: STOP(09)은 미지원 → 무시");
        if (unpaired > 0)
            Core.Log.Warning($"[BMS] {log}: 짝이 맞지 않는 롱노트 오브젝트 {unpaired}개 (끝 없는 시작은 단노트로, 시작 없는 끝은 무시)");

        int holds = notes.Count(n => n.end != null), pitches = notes.Count(n => n.key == PitchKey);
        Core.Log.Msg($"[BMS] {log}: 노트 {notes.Count} (롱 {holds}, 피치 {pitches}), 레인 [{string.Join(",", lanes.Select((c, i) => $"{i + 1}={c}"))}], " +
                     $"bpm {bms.Bpm}, BGM 시작 {bgmPosition}, 무시한 오브젝트 {ignored}");

        return new JsonObject
        {
            ["initialBpm"] = bms.Bpm,
            ["ignoreShifts"] = false,
            ["bars"] = new JsonObject(),
            ["bpms"] = bpms,
            ["stops"] = new JsonObject(),
            ["scrolls"] = new JsonObject(),
            ["bgms"] = new JsonArray(new JsonObject { ["position"] = bgmPosition, ["sound"] = musicFileName ?? "music.ogg", ["column"] = 0 }),
            ["bgaPosition"] = baseChart?["bgaPosition"]?.GetValue<double>() ?? 0,
            ["notes"] = noteArray,
            ["sections"] = new JsonArray(new JsonObject { ["name"] = "NOTES", ["startPosition"] = 0 }),
            ["endPosition"] = Math.Ceiling(last) + 1,
            ["timeTriggeredEvents"] = new JsonArray(),
        };
    }

    /// <summary>롱노트 판정 틱: 시작부터 끝 직전까지 1/16마디 간격 (실측 원본과 동일).</summary>
    private static JsonArray Ticks(double start, double end)
    {
        var ticks = new JsonArray();
        for (int i = 0; start + i / 16.0 < end - 1e-9; i++) ticks.Add(start + i / 16.0);
        return ticks;
    }

    /// <summary>LN 채널(5x/6x)을 일반 노트 채널(1x/2x)로.</summary>
    private static string NoteChannel(string channel) => channel[0] switch
    {
        '5' => "1" + channel[1],
        '6' => "2" + channel[1],
        _ => channel,
    };

    private static Role RoleOf(BmsChart bms, string value)
    {
        if (!bms.Wav.TryGetValue(value, out var file)) return Role.Short;
        var name = System.IO.Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
        if (name.Contains("pitch")) return Role.Pitch;
        if (name.Contains("홀드 시작") || name.Contains("hold start")) return Role.HoldStart;
        if (name.Contains("홀드 끝") || name.Contains("hold end")) return Role.HoldEnd;
        return Role.Short;
    }

    private static bool IsMusic(BmsChart bms, string value, string musicFileName) =>
        musicFileName != null && bms.Wav.TryGetValue(value, out var file) &&
        string.Equals(System.IO.Path.GetFileName(file), musicFileName, StringComparison.OrdinalIgnoreCase);

    private static bool TryDouble(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}
