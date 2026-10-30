using System.Text.Json.Nodes;

namespace PLATiNALAB.Mod;

/// <summary>
/// BMS → PPP 차트(JSON) 변환기 — 뼈대.
/// 위치 규칙: PPP position = BMS 마디 번호 + 1 + 마디 내 비율.
///   (PPP는 BGM을 position 1에 두면 1마디 시작 = 음원 0초. BMS 000마디 ↔ PPP 1마디)
/// 1마디 = 4박 = 240 / bpm 초 (4/4 기준).
///
/// 아직 게임에 연결하지 않았다. 연결 지점 후보: CustomCharts.Process에서 곡 폴더에 chart_*.json이 없고
/// .bms가 있으면 Convert 결과를 chart_{난이도}.json으로 저장.
///
/// TODO:
///  - 채널 → PPP 레인(key 1~4) 매핑 확정 (지금은 1P 11~14 → 1~4 가정)
///  - 롱노트: LNTYPE 1 (채널 51~54 시작/끝 쌍), LNOBJ → endPosition + ticks(1/16마디 간격, 실측 원본과 동일)
///  - 박자 변경(#mmm02) → bars { "마디": {numerator, denominator} }
///  - BPM 변경(채널 03 / 08 + #BPMxx) → bpms { "위치": bpm }
///  - 키음(#WAVxx, 채널 01 BGM 오브젝트)은 미지원: 음원은 music.ogg 하나로 재생
///  - 난이도별 BMS 파일 선택 (#PLAYLEVEL / 파일명 규칙)
/// </summary>
internal static class BmsConverter
{
    /// <summary>BMS 채널 오브젝트 하나 (마디, 채널, 마디 내 비율, 값 2자리 base36).</summary>
    internal readonly record struct BmsObject(int Measure, string Channel, double Fraction, string Value);

    internal sealed class BmsChart
    {
        public string Title;
        public string Artist;
        public double Bpm = 130;
        public int PlayLevel;
        public readonly Dictionary<string, string> Wav = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<BmsObject> Objects = new();
    }

    /// <summary>1P 일반 노트 채널 → PPP 레인. TODO: 실제 키 배치에 맞게 확정.</summary>
    private static readonly Dictionary<string, int> LaneOfChannel = new()
    {
        ["11"] = 1, ["12"] = 2, ["13"] = 3, ["14"] = 4,
    };

    public static BmsChart Parse(string path)
    {
        var chart = new BmsChart();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (!line.StartsWith("#")) continue;

            // #mmmcc:data — 채널 데이터
            if (line.Length > 7 && line[6] == ':' && int.TryParse(line.AsSpan(1, 3), out var measure))
            {
                var channel = line.Substring(4, 2).ToUpperInvariant();
                var data = line[7..].Trim();
                if (channel == "02") continue; // TODO: 박자 변경
                int count = data.Length / 2;
                for (int i = 0; i < count; i++)
                {
                    var value = data.Substring(i * 2, 2);
                    if (value == "00") continue;
                    chart.Objects.Add(new BmsObject(measure, channel, (double)i / count, value));
                }
                continue;
            }

            // 헤더
            int space = line.IndexOf(' ');
            if (space < 0) continue;
            var key = line[1..space].ToUpperInvariant();
            var arg = line[(space + 1)..].Trim();
            if (key == "TITLE") chart.Title = arg;
            else if (key == "ARTIST") chart.Artist = arg;
            else if (key == "BPM" && double.TryParse(arg, out var bpm)) chart.Bpm = bpm;
            else if (key == "PLAYLEVEL" && int.TryParse(arg, out var lv)) chart.PlayLevel = lv;
            else if (key.StartsWith("WAV") && key.Length == 5) chart.Wav[key[3..]] = arg;
        }
        return chart;
    }

    /// <summary>
    /// PPP JSON 생성. bgms[].sound는 CustomSongs.PrepareChart가 기반 곡 파일명으로 바꾸고,
    /// endPosition은 ExtendToMediaLength가 music.ogg/video.mp4 길이에 맞춰 늘린다.
    /// </summary>
    public static JsonObject Convert(BmsChart bms)
    {
        var notes = new JsonArray();
        double last = 1;
        foreach (var o in bms.Objects.OrderBy(o => o.Measure + o.Fraction))
        {
            if (!LaneOfChannel.TryGetValue(o.Channel, out var lane)) continue; // TODO: 롱노트/기타 채널
            double position = o.Measure + 1 + o.Fraction;
            last = Math.Max(last, position);
            notes.Add(new JsonObject
            {
                ["startPosition"] = position,
                ["endPosition"] = null,
                ["ticks"] = null,
                ["keys"] = new JsonArray(lane),
                ["key"] = lane,
                ["sound"] = "",
                ["type"] = 0,
            });
        }

        return new JsonObject
        {
            ["initialBpm"] = bms.Bpm,
            ["ignoreShifts"] = false,
            ["bars"] = new JsonObject(),   // TODO: #mmm02
            ["bpms"] = new JsonObject(),   // TODO: BPM 변경
            ["stops"] = new JsonObject(),
            ["scrolls"] = new JsonObject(),
            ["bgms"] = new JsonArray(new JsonObject { ["position"] = 1, ["sound"] = "music.ogg", ["column"] = 0 }),
            ["bgaPosition"] = 1,
            ["notes"] = notes,
            ["sections"] = new JsonArray(new JsonObject { ["name"] = "NOTES", ["startPosition"] = 0 }),
            ["endPosition"] = Math.Ceiling(last) + 1,
            ["timeTriggeredEvents"] = new JsonArray(),
        };
    }
}
