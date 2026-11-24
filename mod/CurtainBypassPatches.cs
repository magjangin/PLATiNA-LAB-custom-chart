using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPlatinaLab.Main.Catalog.Scheme.Song;
using Il2CppPlatinaLab.Main.Director;
using Il2CppPlatinaLab.Main.InGame.Score.Schema;
using Il2CppPlatinaLab.Main.Result;
using Il2CppPlatinaLab.Main.SceneLoader.Curtain;
using Il2CppPlatinaLab.Main.Select.DetailViewer;
using UnityEngine;

namespace PLATiNALAB.Mod;

/// <summary>
/// 오프라인 환경에서 씬 전환 시 닫힌 ACCESSING.. 아이캐치 커튼이 열리지 않고 멈추는 현상을 해소하고,
/// 선곡 화면(SongSelect) 노출 시 잔류 커튼을 즉시 비활성화한다.
/// </summary>
internal static class CurtainBypassManager
{
    private const string Hook = "CurtainBypassManager.Dismiss";

    public static void DismissAllCurtains()
    {
        if (HookGuard.IsDisabled(Hook)) return;
        try
        {
            DismissInternal();
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("CurtainBypassManager.Dismiss")]
    private static void DismissInternal()
    {
        var airings = UnityEngine.Object.FindObjectsOfType<AiringCurtainController>();
        if (airings is not null)
        {
            for (int i = 0; i < airings.Length; i++)
            {
                var c = airings[i];
                if (c is not null && c.gameObject is not null && c.gameObject.activeSelf)
                {
                    Core.Log.Msg($"[Curtain] 잔류 AiringCurtain 비활성화: {c.gameObject.name}");
                    c.gameObject.SetActive(false);
                }
            }
        }

        var fadings = UnityEngine.Object.FindObjectsOfType<FadingCurtainController>();
        if (fadings is not null)
        {
            for (int i = 0; i < fadings.Length; i++)
            {
                var c = fadings[i];
                if (c is not null && c.gameObject is not null && c.gameObject.activeSelf)
                {
                    Core.Log.Msg($"[Curtain] 잔류 FadingCurtain 비활성화: {c.gameObject.name}");
                    c.gameObject.SetActive(false);
                }
            }
        }
    }
}

[HarmonyPatch(typeof(AiringCurtainController), nameof(AiringCurtainController.Leave))]
internal static class AiringCurtainController_Leave_Patch
{
    private const string Hook = "AiringCurtainController.Leave";

    private static void Postfix(AiringCurtainController __instance)
    {
        if (HookGuard.IsDisabled(Hook)) return;
        try
        {
            HandleLeave(__instance);
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("AiringCurtainController.Leave")]
    private static void HandleLeave(AiringCurtainController instance)
    {
        if (instance is not null && instance.gameObject is not null)
        {
            Core.Log.Msg("[Curtain] AiringCurtainController.Leave 감지 -> 비활성화 보장");
            instance.gameObject.SetActive(false);
        }
    }
}

[HarmonyPatch(typeof(FadingCurtainController), nameof(FadingCurtainController.Leave))]
internal static class FadingCurtainController_Leave_Patch
{
    private const string Hook = "FadingCurtainController.Leave";

    private static void Postfix(FadingCurtainController __instance)
    {
        if (HookGuard.IsDisabled(Hook)) return;
        try
        {
            HandleLeave(__instance);
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("FadingCurtainController.Leave")]
    private static void HandleLeave(FadingCurtainController instance)
    {
        if (instance is not null && instance.gameObject is not null)
        {
            Core.Log.Msg("[Curtain] FadingCurtainController.Leave 감지 -> 비활성화 보장");
            instance.gameObject.SetActive(false);
        }
    }
}

[HarmonyPatch(typeof(SongSelectDirector), nameof(SongSelectDirector.Awake))]
internal static class SongSelectDirector_Awake_Patch
{
    private const string Hook = "SongSelectDirector.Awake";

    private static void Postfix()
    {
        if (HookGuard.IsDisabled(Hook)) return;
        try
        {
            HandleAwake();
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("SongSelectDirector.Awake")]
    private static void HandleAwake()
    {
        Core.Log.Msg("[Curtain] SongSelectDirector.Awake 감지 -> 커튼 정리");
        CurtainBypassManager.DismissAllCurtains();
    }
}

[HarmonyPatch(typeof(SongSelectDirector), nameof(SongSelectDirector.OnSceneVisible))]
internal static class SongSelectDirector_OnSceneVisible_Patch
{
    private const string Hook = "SongSelectDirector.OnSceneVisible";

    private static void Postfix()
    {
        if (HookGuard.IsDisabled(Hook)) return;
        try
        {
            HandleVisible();
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("SongSelectDirector.OnSceneVisible")]
    private static void HandleVisible()
    {
        Core.Log.Msg("[Curtain] SongSelectDirector.OnSceneVisible 감지 -> 커튼 정리");
        CurtainBypassManager.DismissAllCurtains();
    }
}

[HarmonyPatch(typeof(DetailViewerManager), nameof(DetailViewerManager.SetSong))]
internal static class DetailViewerManager_SetSong_Patch
{
    private const string Hook = "DetailViewerManager.SetSong";

    private static void Postfix(SongInfo songInfo)
    {
        if (HookGuard.IsDisabled(Hook)) return;
        try
        {
            HandleSetSong(songInfo);
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("DetailViewerManager.SetSong")]
    private static void HandleSetSong(SongInfo songInfo)
    {
        CurtainBypassManager.DismissAllCurtains();
    }
}

[HarmonyPatch(typeof(SectionController), nameof(SectionController.Initialize))]
internal static class SectionController_Initialize_Patch
{
    private const string Hook = "SectionController.Initialize";

    private static bool Prefix(PatternInfo patternInfo, Il2CppReferenceArray<SectionRecord> sectionRecords)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            return CheckPattern(patternInfo);
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("SectionController.Initialize")]
    private static bool CheckPattern(PatternInfo patternInfo)
    {
        if (patternInfo is not null)
        {
            if (patternInfo.PatternIndex >= 990000 || CustomSongs.IsCustomId(patternInfo.GroupId))
            {
                // 커스텀 곡은 오프라인 섹션 캐시가 없으므로 불필요한 오류 로그 없이 안전하게 건너뜀
                return false;
            }
        }
        return true;
    }
}
