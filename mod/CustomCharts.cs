using System.Text;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPlatinaLab.Main.InGame;
using Il2CppPlatinaLab.Main.InGame.Pattern;
using MelonLoader.Utils;

namespace PLATiNALAB.Mod;

/// <summary>
/// 커스텀 차트 로더.
/// 게임 패턴은 PPPScheme JSON 스트림으로 PatternParser.Parse에 들어온다.
/// - 원본 차트는 UserData/PLATiNALAB/ChartDump/{key}.json 으로 덤프 (템플릿 용도)
/// - UserData/PLATiNALAB/CustomCharts/{key}.json 이 있으면 그 내용으로 스트림을 교체
/// 파일은 매 플레이마다 다시 읽으므로 게임 재시작 없이 수정 → 재플레이로 확인 가능.
/// </summary>
internal static class CustomCharts
{
    public static readonly string Root = Path.Combine(MelonEnvironment.UserDataDirectory, "PLATiNALAB");
    public static readonly string DumpDir = Path.Combine(Root, "ChartDump");
    public static readonly string CustomDir = Path.Combine(Root, "CustomCharts");

    // Parse(PatternPlayer, Stream)이 내부에서 Parse(Stream)을 부를 경우 중복 처리 방지
    [ThreadStatic] internal static bool Handling;

    public static void Init()
    {
        Directory.CreateDirectory(DumpDir);
        Directory.CreateDirectory(CustomDir);
        Core.Log.Msg($"[CustomCharts] Dump: {DumpDir}");
        Core.Log.Msg($"[CustomCharts] Custom: {CustomDir}");
    }

    public static string KeyOf(PatternPlayer player)
    {
        try
        {
            var info = player?.PlayPattern;
            if (info == null) return null;
            var custom = CustomSongs.Of(info);
            if (custom != null) return custom.Key;
            var raw = info.FilePath;
            if (string.IsNullOrEmpty(raw)) raw = info.GetPatternFileName();
            if (string.IsNullOrEmpty(raw)) return null;
            // 패턴 파일명은 곡마다 고유하지 않다 (실측: 서로 다른 곡이 모두 FourLines_Easy.ppp) → 곡 ID를 붙인다.
            // 리소스 주소도 Data/Patterns/{songId}/{file} 구조.
            var file = Path.GetFileNameWithoutExtension(raw.Replace('\\', '/'));
            return Sanitize($"{info.GroupId}__{file}");
        }
        catch (Exception e)
        {
            Core.Log.Warning($"[CustomCharts] 패턴 키 조회 실패: {e.Message}");
            return null;
        }
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        return sb.ToString();
    }

    private static byte[] ReadAll(Il2CppSystem.IO.Stream stream)
    {
        var ms = new Il2CppSystem.IO.MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray().ToArray();
    }

    private static Il2CppSystem.IO.Stream ToStream(byte[] bytes) =>
        new Il2CppSystem.IO.MemoryStream(new Il2CppStructArray<byte>(bytes));

    /// <summary>원본 스트림을 덤프하고, 커스텀 파일이 있으면 교체된 스트림을 돌려준다.</summary>
    public static Il2CppSystem.IO.Stream Process(string key, Il2CppSystem.IO.Stream original)
    {
        if (original == null) return original;
        byte[] bytes = ReadAll(original);

        var name = key ?? $"unknown_{bytes.Length}";
        var customPath = Path.Combine(CustomDir, name + ".json");

        var song = CustomSongs.ByKey(key);
        if (song != null)
        {
            // hwa\ 곡 폴더: chart.json이 없으면 기반 차트에서 첫 노트만 남긴 템플릿을 만들어 둔다
            if (!File.Exists(song.ChartPath))
            {
                try
                {
                    File.WriteAllBytes(song.ChartPath, CustomSongs.MakeFirstNoteChart(bytes));
                    Core.Log.Msg($"[CustomCharts] 첫 노트 차트 생성: {song.ChartPath}");
                }
                catch (Exception e) { Core.Log.Error($"[CustomCharts] 첫 노트 차트 생성 실패: {e.Message}"); }
            }
            try
            {
                var chart = CustomSongs.PrepareChart(File.ReadAllBytes(song.ChartPath), bytes, song.Song);
                Core.Log.Msg($"[CustomCharts] 커스텀 곡 차트 적용: {song.Key} '{song.Song.Title}' ({chart.Length} bytes)");
                return ToStream(chart);
            }
            catch (Exception e)
            {
                Core.Log.Error($"[CustomCharts] {song.ChartPath} 읽기/파싱 실패, 기반 차트 사용: {e.Message}");
                return ToStream(bytes);
            }
        }
        else
        {
            var dumpPath = Path.Combine(DumpDir, name + ".json");
            try
            {
                if (!File.Exists(dumpPath)) File.WriteAllBytes(dumpPath, bytes);
            }
            catch (Exception e) { Core.Log.Warning($"[CustomCharts] 덤프 실패: {e.Message}"); }
        }

        if (key != null && File.Exists(customPath))
        {
            try
            {
                var custom = File.ReadAllBytes(customPath);
                Core.Log.Msg($"[CustomCharts] 커스텀 차트 적용: {name} ({custom.Length} bytes)");
                return ToStream(custom);
            }
            catch (Exception e) { Core.Log.Error($"[CustomCharts] 커스텀 차트 읽기 실패, 원본 사용: {e.Message}"); }
        }
        else
        {
            Core.Log.Msg($"[CustomCharts] 원본 차트: {name} ({bytes.Length} bytes) — 교체하려면 CustomCharts/{name}.json");
        }
        return ToStream(bytes);
    }
}

[HarmonyPatch(typeof(PatternParser), nameof(PatternParser.Parse), typeof(PatternPlayer), typeof(Il2CppSystem.IO.Stream))]
internal static class PatternParser_Parse_PlayerStream_Patch
{
    private static void Prefix(PatternPlayer patternPlayer, ref Il2CppSystem.IO.Stream patternStream)
    {
        try
        {
            patternStream = CustomCharts.Process(CustomCharts.KeyOf(patternPlayer), patternStream);
        }
        catch (Exception e) { Core.Log.Error($"[CustomCharts] Parse(Player,Stream) 처리 실패: {e}"); }
        CustomCharts.Handling = true;
    }

    private static void Finalizer() => CustomCharts.Handling = false;
}

[HarmonyPatch(typeof(PatternParser), nameof(PatternParser.Parse), typeof(Il2CppSystem.IO.Stream))]
internal static class PatternParser_Parse_Stream_Patch
{
    private static void Prefix(ref Il2CppSystem.IO.Stream patternStream)
    {
        if (CustomCharts.Handling) return;
        try
        {
            patternStream = CustomCharts.Process(null, patternStream);
        }
        catch (Exception e) { Core.Log.Error($"[CustomCharts] Parse(Stream) 처리 실패: {e}"); }
    }
}
