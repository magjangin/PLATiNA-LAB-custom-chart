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

    public static bool AllPerfectActive => ModConfig.AllPerfect && CustomSongs.Current != null;

    public static void ApplyAutoPlay()
    {
        if (!ModConfig.AutoPlay) { RestoreAutoPlay(); return; }
        var extra = InGameSettingManager.Instance?.Extra;
        if (extra == null) { Core.Log.Warning("[AutoPlay] InGameSettingManager 없음"); return; }
        if (!s_overriding)
        {
            s_savedExtra = extra.Extra;
            s_overriding = true;
        }
        extra.SetExtra(InGameSettingType.Extra.AutoPlay);
        Core.Log.Msg($"[AutoPlay] 켬 (원래 Extra={s_savedExtra})");
    }

    public static void RestoreAutoPlay()
    {
        if (!s_overriding) return;
        s_overriding = false;
        var extra = InGameSettingManager.Instance?.Extra;
        if (extra == null) return;
        extra.SetExtra(s_savedExtra);
        Core.Log.Msg($"[AutoPlay] 끔 → Extra={s_savedExtra}");
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
        }
    }

    [HarmonyPatch(typeof(InGameSettingManager), nameof(InGameSettingManager.Save))]
    private static class InGameSettingManager_Save_Patch
    {
        private static void Prefix(InGameSettingManager __instance)
        {
            if (s_overriding) __instance.Extra?.SetExtra(s_savedExtra);
        }

        private static void Postfix(InGameSettingManager __instance)
        {
            if (s_overriding) __instance.Extra?.SetExtra(InGameSettingType.Extra.AutoPlay);
        }
    }

    [HarmonyPatch(typeof(PatternPlayer), nameof(PatternPlayer.ProcessJudgeResult))]
    private static class PatternPlayer_ProcessJudgeResult_Patch
    {
        private static int s_logged;

        private static void Prefix(JudgeResult judgeResult)
        {
            try
            {
                if (judgeResult == null) return;
                var id = judgeResult.Id;
                var judgment = judgeResult.Judgment;
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
                if (AllPerfectActive && judgment != JudgmentType.PerfectHigh && judgment != JudgmentType.Undefined)
                    judgeResult.Judgment = JudgmentType.PerfectHigh;
            }
            catch (Exception e) { Core.Log.Warning($"[PlayAssist] 판정 처리 오류: {e.Message}"); }
        }
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
