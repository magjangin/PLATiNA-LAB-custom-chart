using HarmonyLib;
using Il2CppPlatinaLab.Main.Director;
using Il2CppPlatinaLab.Main.InGame;
using Il2CppPlatinaLab.Main.InGame.Pattern.Note;
using Il2CppPlatinaLab.Main.InGameSetting;
using Il2CppPlatinaLab.Main.InGameSetting.Type;

namespace PLATiNALAB.Mod;

/// <summary>
/// 오토 플레이 / 올 퍼펙트 (sxtg2 AutoPlayHook / AllPerfectJudgeHook 포팅).
///
/// 오토 플레이: 게임에 오토 플레이 모드가 이미 있다(인게임 설정 Extra = AutoPlay). InGameSettingHelper.IsAutoPlayMode()는
///   Instance.Extra(+0x20).Extra(+0x10) == 1 비교인데 호출부에 인라인돼서 패치가 안 먹는다(직접 호출 없음, 디스어셈블 확인).
///   → 플레이 씬 동안만 ExtraScheme.SetExtra(AutoPlay)로 값을 바꾸고, 씬을 나가면 원래 값으로 돌린다.
///   IsScorable()은 Extra가 AutoPlay/NoDeath/PartyTime이면 false라서 게임이 스스로 기록을 남기지 않는다.
///   InGameSettingManager.Save()가 그 사이에 불리면 원래 값으로 저장되게 Save 앞뒤로 되돌렸다가 다시 건다.
///
/// 올 퍼펙트: PatternPlayer.ProcessJudgeResult(JudgeResult)에서 Judgment를 PerfectHigh로, ProcessTickResult(롱노트 틱)도 PerfectHigh로.
///   서버 랭킹이 있는 게임이라 커스텀 곡(서버에 기록을 보내지 않음, OfflineSession)에서만 동작한다.
/// </summary>
internal static class PlayAssist
{
    private static bool s_overriding;
    private static InGameSettingType.Extra s_savedExtra;
    private static InGameSettingType.Extra s_autoPlayValue;
    private const string AutoPlayName = "AutoPlay";
    private const string AutoPlayHook = "PlayAssist.AutoPlay";

    public static bool AllPerfectActive => ModConfig.AllPerfect && CustomSongs.Current != null;

    public static void ApplyAutoPlay()
    {
        if (!ModConfig.AutoPlay) { RestoreAutoPlay(); return; }
        if (HookGuard.IsDisabled(AutoPlayHook)) return;
        // enum 상수는 컴파일 시 숫자로 박히므로, 게임이 값을 재배치해도 맞도록 런타임에 이름으로 찾는다
        if (!Enum.TryParse(AutoPlayName, out InGameSettingType.Extra autoPlay))
        {
            Core.Log.Error($"[AutoPlay] Extra.{AutoPlayName} 값을 찾지 못해 끕니다");
            RestoreAutoPlay();
            return;
        }

        InGameSettingType.Extra currentExtra;
        try
        {
            if (!TryGetCurrentExtra(out currentExtra))
            {
                Core.Log.Warning("[AutoPlay] InGameSettingManager 없음");
                return;
            }
        }
        catch (Exception e)
        {
            HookGuard.Fail(AutoPlayHook, e);
            return;
        }

        var savedBefore = s_overriding ? s_savedExtra : currentExtra;
        s_autoPlayValue = autoPlay;
        try
        {
            if (!TrySetExtra(autoPlay)) return;
        }
        catch (Exception e)
        {
            HookGuard.Fail(AutoPlayHook, e);
            return;
        }
        s_savedExtra = savedBefore;
        s_overriding = true;

        // 기록 보호는 게임의 IsScorable()이 맡는다. 기록이 남는 상태거나 확인할 수 없으면 오토 플레이를 쓰지 않는다
        bool scorable;
        try { scorable = ReadIsScorable(); }
        catch (Exception e)
        {
            Core.Log.Error($"[AutoPlay] IsScorable 확인 실패로 끕니다: {e.Message}");
            RestoreAutoPlay();
            return;
        }
        if (scorable)
        {
            Core.Log.Error("[AutoPlay] 오토 플레이 상태인데 기록이 남는 설정으로 확인되어 끕니다");
            RestoreAutoPlay();
            return;
        }
        Core.Log.Msg($"[AutoPlay] 켬 (원래 Extra={s_savedExtra})");
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded(AutoPlayHook)]
    private static bool TryGetCurrentExtra(out InGameSettingType.Extra current)
    {
        current = default;
        var extra = InGameSettingManager.Instance?.Extra;
        if (extra == null) return false;
        current = extra.Extra;
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded(AutoPlayHook)]
    private static bool TrySetExtra(InGameSettingType.Extra value)
    {
        var extra = InGameSettingManager.Instance?.Extra;
        if (extra == null) return false;
        extra.SetExtra(value);
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("InGameSettingHelper.IsScorable")]
    private static bool ReadIsScorable() => Il2CppPlatinaLab.Main.InGameSetting.Utilities.InGameSettingHelper.IsScorable();

    public static void RestoreAutoPlay()
    {
        if (!s_overriding) return;
        s_overriding = false;
        if (HookGuard.IsDisabled(AutoPlayHook)) return;
        try
        {
            if (TrySetExtra(s_savedExtra))
                Core.Log.Msg($"[AutoPlay] 끔 → Extra={s_savedExtra}");
        }
        catch (Exception e)
        {
            HookGuard.Fail(AutoPlayHook, e);
        }
    }

    [HarmonyPatch(typeof(PlayDirector), "Awake")]
    private static class PlayDirector_Awake_Patch
    {
        private static void Prefix()
        {
            ModConfig.Load("곡 시작");
            PlayOverlay.Reset();
            PlayOverlay.InPlay = true;
            try { ApplyAutoPlay(); }
            catch (Exception e) { Core.Log.Error($"[AutoPlay] 적용 실패: {e}"); }
        }
    }

    [HarmonyPatch(typeof(PlayDirector), "OnDestroy")]
    private static class PlayDirector_OnDestroy_Patch
    {
        private static void Postfix()
        {
            PlayOverlay.InPlay = false;
            try { RestoreAutoPlay(); }
            catch (Exception e) { Core.Log.Error($"[AutoPlay] 복원 실패: {e}"); }
            ModHealth.ReportPlay();
        }
    }

    [HarmonyPatch(typeof(InGameSettingManager), nameof(InGameSettingManager.Save))]
    private static class InGameSettingManager_Save_Patch
    {
        private const string Hook = "InGameSettingManager.Save";

        // 설정 저장은 오토 플레이와 무관하게 늘 불린다 → 게임 멤버 접근은 별도 메서드로 격리 (HookGuard 설명 참고)
        private static void Prefix(InGameSettingManager __instance) => Set(__instance, s_savedExtra);

        private static void Postfix(InGameSettingManager __instance) => Set(__instance, s_autoPlayValue);

        private static void Set(InGameSettingManager manager, InGameSettingType.Extra value)
        {
            if (!s_overriding || manager == null || HookGuard.IsDisabled(Hook)) return;
            try { SetExtraDirect(manager, value); }
            catch (Exception e) { HookGuard.Fail(Hook, e); }
        }

        [MethodImpl(MethodImplOptions.NoInlining), Guarded("InGameSettingManager.Save")]
        private static void SetExtraDirect(InGameSettingManager manager, InGameSettingType.Extra value) => manager.Extra?.SetExtra(value);
    }

    [HarmonyPatch(typeof(PatternPlayer), nameof(PatternPlayer.ProcessJudgeResult))]
    private static class PatternPlayer_ProcessJudgeResult_Patch
    {
        private const string Hook = "PatternPlayer.ProcessJudgeResult";
        private static int s_logged;
        private static bool s_setFailureLogged;

        /// <summary>
        /// 노트마다 게임 루프 스레드에서 불린다. 판정바도 올 퍼펙트도 꺼져 있으면 아무것도 읽지 않는다
        /// (예전에는 판정바를 꺼도 판정을 모아 두기만 하고 비우지 않았다).
        /// </summary>
        private static void Prefix(JudgeResult judgeResult)
        {
            bool bar = PlayOverlay.InPlay && ModConfig.EnableJudgmentBar;
            bool allPerfect = AllPerfectActive;
            if ((!bar && !allPerfect) || judgeResult == null || HookGuard.IsDisabled(Hook)) return;
            try { Handle(judgeResult, bar, allPerfect); }
            catch (Exception e) { HookGuard.Fail(Hook, e); }
        }

        [MethodImpl(MethodImplOptions.NoInlining), Guarded("PatternPlayer.ProcessJudgeResult")]
        private static void Handle(JudgeResult judgeResult, bool bar, bool allPerfect)
        {
            var judgment = judgeResult.Judgment;
            if (bar)
            {
                var id = judgeResult.Id;
                // 타이밍이 있는 판정만 판정바에 (미스·롱노트 끝 처리는 오차 의미 없음)
                if ((id == JudgeResult.IdType.ProcessShortNote || id == JudgeResult.IdType.StartProcessingLongNote)
                    && judgment != JudgmentType.Miss && judgment != JudgmentType.Undefined)
                {
                    double error = judgeResult.Error;
                    if (s_logged < 5)
                    {
                        s_logged++;
                        Core.Log.Msg($"[JudgmentBar] 판정 {judgment} error={error} offsetMs={judgeResult.TimingOffsetMilliseconds}");
                    }
                    PlayOverlay.RegisterHit(error, judgment);
                }
            }
            if (allPerfect && judgment != JudgmentType.PerfectHigh && judgment != JudgmentType.Undefined)
                ForcePerfect(judgeResult);
        }

        /// <summary>
        /// 1차: 프로퍼티 세터 직접 호출. 2차(세터가 없어졌거나 실패): 이름 기반 리플렉션.
        /// 예전에는 노트마다 두 경로를 모두 실행했다 (같은 세터를 두 번 호출, 리플렉션 비용).
        /// </summary>
        private static void ForcePerfect(JudgeResult judgeResult)
        {
            try
            {
                SetPerfectDirect(judgeResult);
                return;
            }
            catch { /* 리플렉션 폴백 */ }
            if (!UpdateResilience.SafeSetFieldOrProperty(judgeResult, "Judgment", JudgmentType.PerfectHigh) && !s_setFailureLogged)
            {
                s_setFailureLogged = true;
                Core.Log.Warning("[PlayAssist] 올 퍼펙트: JudgeResult.Judgment 설정 실패");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetPerfectDirect(JudgeResult judgeResult) => judgeResult.Judgment = JudgmentType.PerfectHigh;
    }

    [HarmonyPatch(typeof(PatternPlayer), nameof(PatternPlayer.ProcessTickResult))]
    private static class PatternPlayer_ProcessTickResult_Patch
    {
        private static void Prefix(ref JudgmentType tickResult)
        {
            if (AllPerfectActive && tickResult != JudgmentType.Undefined) tickResult = JudgmentType.PerfectHigh;
        }
    }
}
