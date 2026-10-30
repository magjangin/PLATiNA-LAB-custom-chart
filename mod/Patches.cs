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

/// <summary>
/// 곡/패턴 잠금 여부 검사 패치
/// </summary>
[HarmonyPatch(typeof(SongSelectDirector), nameof(SongSelectDirector.IsLock))]
internal static class SongSelectDirector_IsLock_Patch
{
    private static void Postfix(ref bool __result)
    {
        // 필요시 모든 곡 해금: __result = false;
    }
}

/// <summary>
/// DLC/미보유 곡 여부 검사 패치
/// </summary>
[HarmonyPatch(typeof(SongSelectDirector), nameof(SongSelectDirector.IsNotOwned))]
internal static class SongSelectDirector_IsNotOwned_Patch
{
    private static void Postfix(ref bool __result)
    {
        // 필요시 미보유 우회: __result = false;
    }
}
