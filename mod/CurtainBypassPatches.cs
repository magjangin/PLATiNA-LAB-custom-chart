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
/// 커튼 정리는 OfflineMode=1(<see cref="Offline.Enabled"/>)일 때만 한다 — 온라인에서는 게임 원본 연출 그대로 (19차 감사).
/// SectionController.Initialize 방어는 커스텀 곡 전용이라 모드와 무관하게 동작한다.
/// </summary>
internal static class CurtainBypassManager
{
    private const string Hook = "CurtainBypassManager.Dismiss";

    /// <summary>
    /// 곡을 넘길 때마다(DetailViewerManager.SetSong) 씬 전체 FindObjectsOfType을 두 번 돌던 것을 줄인다.
    /// 잔류 커튼은 씬 전환 직후에만 생기므로, 곡 변경으로 인한 정리는 마지막 정리 후 1초 안이면 건너뛴다.
    /// </summary>
    private const long ThrottleMs = 1_000;
    private static long s_lastScanTick = long.MinValue / 2;

    public static void DismissAllCurtains(bool throttled = false)
    {
        if (!Offline.Enabled || HookGuard.IsDisabled(Hook)) return;
        long now = Environment.TickCount64;
        if (throttled && now - Interlocked.Read(ref s_lastScanTick) < ThrottleMs) return;
        Interlocked.Exchange(ref s_lastScanTick, now);
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

[OfflineOnly]
[HarmonyPatch(typeof(AiringCurtainController), nameof(AiringCurtainController.Leave))]
internal static class AiringCurtainController_Leave_Patch
{
    private const string Hook = "AiringCurtainController.Leave";

    private static void Postfix(AiringCurtainController __instance)
    {
        if (!Offline.Enabled || HookGuard.IsDisabled(Hook)) return;
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

[OfflineOnly]
[HarmonyPatch(typeof(FadingCurtainController), nameof(FadingCurtainController.Leave))]
internal static class FadingCurtainController_Leave_Patch
{
    private const string Hook = "FadingCurtainController.Leave";

    private static void Postfix(FadingCurtainController __instance)
    {
        if (!Offline.Enabled || HookGuard.IsDisabled(Hook)) return;
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

[OfflineOnly]
[HarmonyPatch(typeof(SongSelectDirector), nameof(SongSelectDirector.Awake))]
internal static class SongSelectDirector_Awake_Patch
{
    private const string Hook = "SongSelectDirector.Awake";

    private static void Postfix()
    {
        if (!Offline.Enabled || HookGuard.IsDisabled(Hook)) return;
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
        Offline.NoteBootCompleted();
        CurtainBypassManager.DismissAllCurtains();
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(SongSelectDirector), nameof(SongSelectDirector.OnSceneVisible))]
internal static class SongSelectDirector_OnSceneVisible_Patch
{
    private const string Hook = "SongSelectDirector.OnSceneVisible";

    private static void Postfix()
    {
        if (!Offline.Enabled || HookGuard.IsDisabled(Hook)) return;
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

[OfflineOnly]
[HarmonyPatch(typeof(DetailViewerManager), nameof(DetailViewerManager.SetSong))]
internal static class DetailViewerManager_SetSong_Patch
{
    private const string Hook = "DetailViewerManager.SetSong";

    private static void Postfix(SongInfo songInfo)
    {
        if (!Offline.Enabled || HookGuard.IsDisabled(Hook)) return;
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
        CurtainBypassManager.DismissAllCurtains(throttled: true);
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
