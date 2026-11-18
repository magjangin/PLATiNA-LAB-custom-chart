using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Il2CppPlatinaLab.Main.Common.Dialog;
using Il2CppPlatinaLab.Main.Director;
using Il2CppPlatinaLab.Main.InGame;
using Il2CppPlatinaLab.Main.InGameSetting.Utilities;
using Il2CppPlatinaLab.Main.Select.Category.Cache;

namespace PLATiNALAB.Mod;

/// <summary>
/// 진단용 상세 로그 패치. DiagnosticLogs=1 일 때만 수동 적용된다.
/// 평소에는 게임 메서드를 후킹하지 않아 업데이트 노출면과 로그 부하를 줄인다.
/// </summary>
internal static class DiagnosticPatches
{
    private static bool s_applied;

    public static void Apply(HarmonyLib.Harmony harmony)
    {
        if (s_applied || harmony == null) return;
        s_applied = true;

        TryPatch(harmony, typeof(PlayDirector), nameof(PlayDirector.TrySaveScoreAndGameOver),
            prefix: nameof(LogTrySave));
        TryPatch(harmony, typeof(ErrorDialogCollection), nameof(ErrorDialogCollection.APIServerError),
            prefix: nameof(LogApiServerError));
        TryPatch(harmony, typeof(InGameSettingHelper), nameof(InGameSettingHelper.IsScorable),
            postfix: nameof(LogIsScorable));
        TryPatch(harmony, typeof(PatternPlayer), nameof(PatternPlayer.SetPatternDataToPlay),
            prefix: nameof(LogSetPatternData));
        TryPatch(harmony, typeof(AbstractPreFilteredCache), nameof(AbstractPreFilteredCache.UpdatePostProcessData),
            postfix: nameof(LogUpdatePostProcess));

        Core.Log.Msg("[Diag] 진단용 로그 패치 적용 완료 (DiagnosticLogs=1)");
    }

    private static void TryPatch(HarmonyLib.Harmony harmony, Type targetType, string methodName, string prefix = null, string postfix = null)
    {
        try
        {
            var target = AccessTools.Method(targetType, methodName);
            if (target == null)
            {
                Core.Log.Warning($"[Diag] 대상 메서드 없음: {targetType.Name}.{methodName}");
                return;
            }
            var pre = prefix == null ? null : new HarmonyMethod(typeof(DiagnosticPatches), prefix);
            var post = postfix == null ? null : new HarmonyMethod(typeof(DiagnosticPatches), postfix);
            harmony.Patch(target, prefix: pre, postfix: post);
        }
        catch (Exception e)
        {
            Core.Log.Warning($"[Diag] 패치 실패 ({targetType.Name}.{methodName}): {e.Message}");
        }
    }

    private static void LogTrySave() => Core.Log.Msg("[Diag] PlayDirector.TrySaveScoreAndGameOver");
    private static void LogApiServerError() => Core.Log.Warning("[Diag] ErrorDialogCollection.APIServerError");
    private static void LogIsScorable(bool __result) => Core.Log.Msg($"[Diag] IsScorable={__result}");

    private static void LogSetPatternData()
    {
        var current = CustomSongs.Current;
        Core.Log.Msg($"[PatternPlayer] SetPatternDataToPlay ({(current != null ? $"커스텀 {current.Key}" : "일반 곡")})");
    }

    private static void LogUpdatePostProcess(AbstractPreFilteredCache __instance)
    {
        const string Hook = "AbstractPreFilteredCache.UpdatePostProcessData";
        if (__instance == null || !CustomSongs.Injected || HookGuard.IsDisabled(Hook)) return;
        try { LogCustomCategory(__instance); }
        catch (Exception e) { HookGuard.Fail(Hook, e); }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("AbstractPreFilteredCache.UpdatePostProcessData")]
    private static void LogCustomCategory(AbstractPreFilteredCache cache)
    {
        if (cache.Key != CustomSongs.CategoryTag) return;
        Core.Log.Msg($"[CustomSongs] custom 카테고리 raw={cache.RawData?.Count} post={cache.PostProcessData?.Count}");
    }
}
