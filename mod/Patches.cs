using HarmonyLib;
using Il2CppPlatinaLab.Main.Director;
using Il2CppPlatinaLab.Main.InGame;
using Il2CppPlatinaLab.Main.InGame.Pattern;
using Il2CppPlatinaLab.Main.InGame.Pattern.Note;

namespace PLATiNALAB.Mod;

/// <summary>
/// 인게임 패턴 데이터 주입 및 로딩 감지 패치
/// </summary>
[HarmonyPatch(typeof(PatternPlayer), nameof(PatternPlayer.SetPatternDataToPlay))]
internal static class PatternPlayer_SetPatternDataToPlay_Patch
{
    private static void Prefix()
    {
        // 차트 교체는 PatternParser.Parse 후킹에서 끝난다(CustomCharts). 여기서는 파서 이후 단계까지 왔는지만 남긴다.
        var current = CustomSongs.Current;
        Core.Log.Msg($"[PatternPlayer] SetPatternDataToPlay ({(current != null ? $"커스텀 {current.Key}" : "일반 곡")})");
    }
}
