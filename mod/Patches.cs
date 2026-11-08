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
    private static void Prefix(PatternPlayer __instance, PatternData patternData)
    {
        Core.Log.Msg("[PatternPlayer] SetPatternDataToPlay 호출됨 — 커스텀 차트 주입 대기 지점");
    }
}
