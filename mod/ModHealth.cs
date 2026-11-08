global using System.Runtime.CompilerServices;

using System.Text.RegularExpressions;
using MelonLoader.Utils;

namespace PLATiNALAB.Mod;

/// <summary>
/// 게임 업데이트 후 문제를 조기에 드러내는 점검. 패치 대상은 문자열로만 비교해서, 대상이 사라져도 이 클래스는 JIT 단계에서 실패하지 않는다.
/// - 시작 시 Steam Build ID를 검증 기준과 비교해 로그로 남긴다.
/// - 필수 패치가 빠졌으면 커스텀 곡 주입을 막는다 (커스텀 곡이 서버로 요청되거나 로딩에서 멈추는 것을 방지).
/// - 커스텀 곡 플레이마다 핵심 훅이 실제로 호출됐는지 확인한다 (패치는 성공해도 IL2CPP 인라인으로 호출이 안 될 수 있음).
/// </summary>
internal static class ModHealth
{
    private const string TestedBuildId = "25981392";
    private const string AppManifest = "appmanifest_3430470.acf";

    /// <summary>하나라도 없으면 커스텀 곡을 안전하게 플레이할 수 없는 패치 (선언 타입.메서드 이름).</summary>
    private static readonly string[] s_requiredPatches =
    {
        "InGameSurface.RequestGameStart",
        "InGameSurface.RenewGameSession",
        "InGameSurface.SendGameEnd",
        "PatternParser.Parse",
        "SongListBuilder.BuildNameBanners",
        "SongInfo.IsAvailable",
        "SongInfo.IsNotOwned",
        "SongInfo.IsLock",
        "SongInfo.IsNewSongMarkTarget",
        "SongInfo.get_IsHiddenFromDefaultList",
    };

    private static readonly object s_lock = new();
    private static readonly HashSet<string> s_fired = new();
    private static CustomSong s_playing;
    private static bool s_checked;

    public static bool CustomSongsAllowed { get; private set; } = true;

    public static void CheckOnStart()
    {
        LogBuildId();
        CheckRequiredPatches();
    }

    private static void LogBuildId()
    {
        try
        {
            var manifest = Path.GetFullPath(Path.Combine(MelonEnvironment.GameRootDirectory, "..", "..", AppManifest));
            if (!File.Exists(manifest))
            {
                Core.Log.Msg($"[Health] Steam 매니페스트 없음, 게임 빌드 확인 생략 ({manifest})");
                return;
            }
            var match = Regex.Match(File.ReadAllText(manifest), "\"buildid\"\\s+\"(\\d+)\"");
            if (!match.Success) { Core.Log.Warning("[Health] 매니페스트에서 buildid를 찾지 못함"); return; }
            var build = match.Groups[1].Value;
            if (build == TestedBuildId) Core.Log.Msg($"[Health] 게임 빌드 {build} (검증된 빌드)");
            else Core.Log.Warning($"[Health] 게임 빌드 {build}: 모드 검증 기준 {TestedBuildId}와 다릅니다. 문제가 있으면 모드를 다시 빌드하세요.");
        }
        catch (Exception e) { Core.Log.Warning($"[Health] 게임 빌드 확인 실패: {e.Message}"); }
    }

    private static void CheckRequiredPatches()
    {
        if (s_checked) return;
        s_checked = true;
        try
        {
            var patched = new HashSet<string>();
            int locate = 0;
            foreach (var m in Core.Patcher.GetPatchedMethods())
            {
                patched.Add($"{m.DeclaringType?.Name}.{m.Name}");
                if (m.Name == "Locate") locate++;
            }

            var missing = new List<string>();
            foreach (var name in s_requiredPatches)
                if (!patched.Contains(name)) missing.Add(name);
            if (locate == 0) missing.Add("IResourceLocator.Locate");

            if (missing.Count == 0)
            {
                Core.Log.Msg($"[Health] 필수 패치 정상 ({s_requiredPatches.Length}종 + Locate {locate}건, 전체 {patched.Count}개 메서드)");
                return;
            }
            CustomSongsAllowed = false;
            Core.Log.Error($"[Health] 필수 패치 누락으로 커스텀 곡 주입을 끕니다: {string.Join(", ", missing)}");
        }
        catch (Exception e)
        {
            // 점검 자체의 실패로 정상 기능을 막지 않는다
            Core.Log.Warning($"[Health] 필수 패치 점검 실패(주입은 유지): {e.Message}");
        }
    }

    public static void BeginPlay(CustomSong song)
    {
        lock (s_lock)
        {
            s_playing = song;
            s_fired.Clear();
        }
    }

    public static void Mark(string hook)
    {
        lock (s_lock)
        {
            if (s_playing != null) s_fired.Add(hook);
        }
    }

    /// <summary>플레이 씬 종료 시 호출. 이번 커스텀 곡에서 호출되지 않은 핵심 훅을 경고한다.</summary>
    public static void ReportPlay()
    {
        CustomSong song;
        var missing = new List<string>();
        lock (s_lock)
        {
            song = s_playing;
            s_playing = null;
            if (song == null) return;
            if (!s_fired.Contains("Locate")) missing.Add("리소스 주소 치환(Locate)");
            if (!s_fired.Contains("Parse")) missing.Add("차트 교체(PatternParser.Parse)");
            if (song.MusicPath != null && !s_fired.Contains("Music")) missing.Add("음원 교체(SoundData.FromHEAudioClip)");
            if (song.VideoPath != null && !s_fired.Contains("Video")) missing.Add("BGA 교체(BGAPlayer.SetClip)");
        }
        if (missing.Count == 0) Core.Log.Msg($"[Health] {song.Id} 핵심 훅 호출 정상");
        else Core.Log.Warning($"[Health] {song.Id}에서 호출되지 않은 훅(도중에 나간 경우 무시): {string.Join(", ", missing)}");
    }
}
