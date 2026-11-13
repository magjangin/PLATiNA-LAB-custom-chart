using HarmonyLib;
using Il2CppCysharp.Threading.Tasks;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPlatinaLab.Main.Catalog.Scheme.Song;
using Il2CppPlatinaLab.Main.Endpoint;
using Il2CppPlatinaLab.Main.Endpoint.Model;
using Il2CppPlatinaLab.Main.InGame.Score;
using Il2CppPlatinaLab.Main.InGame.Score.Schema;
using Il2CppPlatinaLab.Main.InGame.Section;
using Il2CppPlatinaLab.Main.SceneLoader;

namespace PLATiNALAB.Mod;

/// <summary>
/// 커스텀 곡은 서버에 없는 패턴 번호라 RequestGameStart가 "서버와의 통신 과정에서 오류" 팝업으로 거부된다.
/// 커스텀 패턴일 때만 서버 호출을 건너뛰고 로컬에서 완료된 UniTask를 돌려준다.
/// 점수는 서버로 보내지 않는다 (원곡/랭킹 오염 방지).
/// </summary>
internal static class OfflineSession
{
    public const string Token = "platinalab-mod-custom-offline";
}

[HarmonyPatch(typeof(InGameSurface), nameof(InGameSurface.RequestGameStart))]
internal static class InGameSurface_RequestGameStart_Patch
{
    private const string Hook = "InGameSurface.RequestGameStart";

    /// <summary>
    /// 모든 곡의 게임 시작에서 불린다. 로컬 세션을 만드는 게임 타입 참조는 StartLocal로 격리해서,
    /// 업데이트로 그 타입이 바뀌어도 일반 곡 시작은 원본대로 진행된다 (HookGuard 설명 참고).
    /// </summary>
    private static bool Prefix(int patternId, ref UniTask<SurfaceResult<GameSessionResult>> __result)
    {
        var chart = CustomSongs.ByPatternIndex(patternId);
        CustomSongs.Current = chart;
        if (chart == null) return true;
        if (!ModHealth.CustomSongsAllowed || HookGuard.IsDisabled(Hook))
        {
            // 서버 우회가 안 되는 상태: 원본으로 넘긴다 (서버가 없는 패턴 번호를 거부하고 오류 팝업, 기록은 남지 않음)
            CustomSongs.Current = null;
            Core.Log.Error($"[OfflineSession] 커스텀 곡 로컬 세션을 만들 수 없는 상태라 원본 경로로 넘깁니다 (pattern {patternId})");
            return true;
        }
        try
        {
            chart.Song.RefreshMedia(); // 곡 폴더 파일을 바꿨으면 이번 플레이부터 반영
            __result = StartLocal(chart);
            Core.Log.Msg($"[OfflineSession] RequestGameStart({patternId}) 서버 호출 생략, 로컬 세션 발급");
            return false;
        }
        catch (Exception e)
        {
            if (!HookGuard.Fail(Hook, e, required: true)) Core.Log.Error($"[OfflineSession] RequestGameStart 래퍼 예외 발생: {e}");
            CustomSongs.Current = null;
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("InGameSurface.RequestGameStart", required: true)]
    private static UniTask<SurfaceResult<GameSessionResult>> StartLocal(CustomChart chart)
    {
        var session = new GameSessionResult();
        if (!UpdateResilience.SafeSetFieldOrProperty(session, "Token", OfflineSession.Token))
            Core.Log.Warning("[OfflineSession] 세션 Token 설정 실패: RenewGameSession이 서버로 갈 수 있음");
        var result = new UniTask<SurfaceResult<GameSessionResult>>(SurfaceResult<GameSessionResult>.Success(session));
        ModHealth.BeginPlay(chart.Song);
        return result;
    }
}

[HarmonyPatch(typeof(InGameSurface), nameof(InGameSurface.RenewGameSession))]
internal static class InGameSurface_RenewGameSession_Patch
{
    private const string Hook = "InGameSurface.RenewGameSession";

    private static bool Prefix(string gameToken, ref UniTask<SurfaceResult> __result)
    {
        if (gameToken != OfflineSession.Token) return true;
        try
        {
            __result = RenewLocal();
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e, required: true);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("InGameSurface.RenewGameSession", required: true)]
    private static UniTask<SurfaceResult> RenewLocal() => new(SurfaceResult.Success());
}

[HarmonyPatch(typeof(InGameSurface), nameof(InGameSurface.SendGameEnd))]
internal static class InGameSurface_SendGameEnd_Patch
{
    private const string Hook = "InGameSurface.SendGameEnd";

    /// <summary>
    /// 모든 곡의 결과 전송에서 불린다. 결과·UniTask 생성은 별도 메서드로 격리해서, 업데이트로 그 타입이 바뀌어도
    /// 일반 곡의 서버 기록 전송은 원본대로 진행된다. 커스텀 곡은 어떤 경우에도 서버로 보내지 않는다.
    /// </summary>
    private static bool Prefix(GameEndResult request, PatternInfo patternInfo, ref UniTask<SurfaceResult<FreeModePlayRecordV2>> __result)
    {
        bool custom;
        try { custom = CustomSongs.IsCustom(patternInfo); }
        catch (Exception e)
        {
            // 패턴 번호를 못 읽으면 커스텀인지 모른다 → 지금 커스텀 곡을 플레이 중이었다면 서버로 보내지 않는다
            HookGuard.Fail(Hook, e, required: true);
            custom = CustomSongs.Current != null;
        }
        if (!custom) return true;

        try
        {
            var localRecord = BuildLocalRecord(request, patternInfo);
            if (localRecord != null)
            {
                __result = Success(localRecord);
                Core.Log.Msg($"[OfflineSession] SendGameEnd 서버 전송 생략, 로컬 결과 반환 (score={FinalScore(request)})");
                return false;
            }
        }
        catch (Exception e)
        {
            if (!HookGuard.Fail(Hook, e)) Core.Log.Error($"[OfflineSession] 로컬 결과 생성 실패: {e}");
        }

        try { __result = NetworkError(); }
        catch (Exception e) { HookGuard.Fail(Hook, e); }
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("InGameSurface.SendGameEnd", required: true)]
    private static UniTask<SurfaceResult<FreeModePlayRecordV2>> Success(FreeModePlayRecordV2 record) =>
        new(SurfaceResult<FreeModePlayRecordV2>.Success(record));

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("InGameSurface.SendGameEnd", required: true)]
    private static UniTask<SurfaceResult<FreeModePlayRecordV2>> NetworkError() =>
        new(SurfaceResult<FreeModePlayRecordV2>.NetworkError());

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int FinalScore(GameEndResult request) => Math.Max(request?.Score ?? 0, request?.FinalScore ?? 0);

    /// <summary>
    /// 서버 응답 없이 결과 화면에 넘길 레코드를 만든다.
    /// RecordUpdateHelper.UpdatePatternRecordAndBuildSnapshot은 세이브에 패턴 990001 기록을 남기므로 쓰지 않고,
    /// 스냅샷은 Record* 메서드로 직접 채운다 (이전 값 0 → 이번 값).
    /// 직접 생성자 호출은 CreateRecordDirect로 분리했다. 같은 메서드에 두면 시그니처가 바뀔 때 JIT 단계에서 메서드 전체가 실패해 Reflection 폴백에 도달하지 못한다.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining), Guarded("OfflineSession.BuildLocalRecord")]
    private static FreeModePlayRecordV2 BuildLocalRecord(GameEndResult request, PatternInfo patternInfo)
    {
        // SectionDetailPanelController NRE 방지: SectionRecords가 null이면 빈 배열 할당
        try { EnsureSectionRecords(request); }
        catch { }

        var response = CreateEndGameResponse();

        int finalScore = FinalScore(request);
        var snapshot = new GameEndResultSnapShot();

        // 서버 응답에 들어 있던 정확도/P.A.T.C.H.를 게임의 계산식으로 직접 구한다 (없으면 결과 화면이 0%, 랭크 C로 나옴)
        int accuracy = 0, mastery = 0;
        try { accuracy = ComputeAccuracy(request); }
        catch (Exception e) { Core.Log.Warning($"[OfflineSession] 정확도 계산 실패 (0으로 진행): {e.Message}"); }

        try { mastery = ComputeMastery(patternInfo, accuracy); }
        catch (Exception e) { Core.Log.Warning($"[OfflineSession] P.A.T.C.H. 계산 실패 (0으로 진행): {e.Message}"); }

        if (response?.PlayResult != null)
        {
            bool acc = UpdateResilience.SafeSetFieldOrProperty(response.PlayResult, "NormalizedAccuracy", accuracy);
            bool mas = UpdateResilience.SafeSetFieldOrProperty(response.PlayResult, "MasteryNormalized", mastery);
            Core.Log.Msg($"[OfflineSession] 로컬 판정 계산: accuracy={accuracy} mastery={mastery} (PlayResult 설정 acc={acc}, mastery={mas})");
        }

        try
        {
            FillSnapshot(snapshot, finalScore, accuracy, mastery);
        }
        catch (Exception e)
        {
            Core.Log.Warning($"[OfflineSession] GameEndResultSnapShot 기록 예외 (기본값 진행): {e.Message}");
        }

        var emptyFeatureList = new Il2CppSystem.Collections.Generic.List<Il2CppPlatinaLab.Main.Common.FeatureId>();

        // 1차: 표준 직접 생성자 호출
        try
        {
            return CreateRecordDirect(request, snapshot, response, emptyFeatureList);
        }
        catch (Exception e)
        {
            Core.Log.Warning($"[OfflineSession] FreeModePlayRecordV2 직접 생성 실패, 동적 생성자 폴백: {e.Message}");
        }

        // 2차: Reflection을 통한 동적 생성자 탐색
        foreach (var ctor in typeof(FreeModePlayRecordV2).GetConstructors())
        {
            var parms = ctor.GetParameters();
            // 인터롭 공통 생성자(IntPtr)와 복사 생성자는 레코드를 새로 만들 수 없다
            if (parms.Length == 1 && (parms[0].ParameterType == typeof(IntPtr) || parms[0].ParameterType == typeof(FreeModePlayRecordV2))) continue;
            try
            {
                var args = new object[parms.Length];
                for (int i = 0; i < parms.Length; i++)
                {
                    var pType = parms[i].ParameterType;
                    if (pType.IsAssignableFrom(typeof(GameEndResult))) args[i] = request;
                    else if (pType.IsAssignableFrom(typeof(GameEndResultSnapShot))) args[i] = snapshot;
                    else if (pType.IsAssignableFrom(typeof(EndGameResponse))) args[i] = response;
                    else if (pType.IsAssignableFrom(typeof(Il2CppSystem.Collections.Generic.List<Il2CppPlatinaLab.Main.Common.FeatureId>))) args[i] = emptyFeatureList;
                    else args[i] = pType.IsValueType ? Activator.CreateInstance(pType) : null;
                }
                if (ctor.Invoke(args) is FreeModePlayRecordV2 record)
                {
                    Core.Log.Msg($"[OfflineSession] FreeModePlayRecordV2 동적 생성 성공 (파라미터 {parms.Length}개)");
                    return record;
                }
            }
            catch (Exception ex)
            {
                Core.Log.Warning($"[OfflineSession] FreeModePlayRecordV2 생성자(파라미터 {parms.Length}개) 실패: {ex.GetBaseException().Message}");
            }
        }

        Core.Log.Error("[OfflineSession] FreeModePlayRecordV2를 만들 수 있는 생성자가 없음");
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("OfflineSession.EnsureSectionRecords")]
    private static void EnsureSectionRecords(GameEndResult request)
    {
        if (request != null && request.SectionRecords == null)
            request.SectionRecords = new Il2CppReferenceArray<SectionRecord>(0);
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("OfflineSession.CreateEndGameResponse")]
    private static EndGameResponse CreateEndGameResponse()
    {
        var response = new EndGameResponse();
        response.PlayResult = new PlayResult();
        try { SetEmptyResponseArrays(response); }
        catch (Exception e) { Core.Log.Warning($"[OfflineSession] EndGameResponse 빈 배열 초기화 실패: {e.Message}"); }
        return response;
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("OfflineSession.ResponseArrays")]
    private static void SetEmptyResponseArrays(EndGameResponse response)
    {
        response.GrantedItems = new Il2CppReferenceArray<Il2CppPlatinaLab.DataModel.ItemBase>(0);
        response.UpdatedAchievements = new Il2CppReferenceArray<UserAchievementState>(0);
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("결과 스냅샷 기록(FillSnapshot)")]
    private static void FillSnapshot(GameEndResultSnapShot snapshot, int finalScore, int accuracy, int mastery)
    {
        var zeroAccuracy = Il2CppPlatinaLab.Main.Common.Types.Accuracy.CreateFrom(0);
        var zeroMastery = Il2CppPlatinaLab.Main.Common.Types.Mastery.CreateFrom(0);
        snapshot.RecordPatternScore(0, finalScore);
        snapshot.RecordPatternAccuracy(zeroAccuracy, Il2CppPlatinaLab.Main.Common.Types.Accuracy.CreateFrom(accuracy));
        snapshot.RecordPatternMastery(zeroMastery, Il2CppPlatinaLab.Main.Common.Types.Mastery.CreateFrom(mastery));
        // 플레이어 전체 P.A.T.C.H.는 커스텀 곡으로 바뀌면 안 되므로 0 → 0 유지
        snapshot.RecordTotalMastery(snapshot.PlayedGameMode, zeroMastery, zeroMastery);
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("정확도 계산(ComputeAccuracy)")]
    private static int ComputeAccuracy(GameEndResult request) =>
        Il2CppPlatinaLab.Main.Mastery.Utility.AccuracyV2Formula.GetAccuracyV2(request.Judge, request.WasPaused).Total;

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("P.A.T.C.H. 계산(ComputeMastery)")]
    private static int ComputeMastery(PatternInfo patternInfo, int accuracy)
    {
        var m = Il2CppPlatinaLab.Main.Mastery.Utility.MasteryV2Formula.CalcFromPatternInfo(patternInfo, accuracy);
        try { return ReadMasteryRawDirect(m); }
        catch { return UpdateResilience.SafeGetFieldOrProperty(m, "ScaledRawValue", 0); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ReadMasteryRawDirect(Il2CppPlatinaLab.Main.Common.Types.Mastery m) =>
        m._ScaledRawValue_k__BackingField;

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("결과 레코드 직접 생성(CreateRecordDirect)")]
    private static FreeModePlayRecordV2 CreateRecordDirect(GameEndResult request, GameEndResultSnapShot snapshot, EndGameResponse response,
        Il2CppSystem.Collections.Generic.List<Il2CppPlatinaLab.Main.Common.FeatureId> features)
    {
        return new FreeModePlayRecordV2(request, snapshot, response, null, null, features);
    }
}

// --- 게임 종료 경로 진단 ---

[HarmonyPatch(typeof(Il2CppPlatinaLab.Main.Director.PlayDirector), nameof(Il2CppPlatinaLab.Main.Director.PlayDirector.TrySaveScoreAndGameOver))]
internal static class Diag_TrySave
{
    private static void Prefix() => Core.Log.Msg("[Diag] PlayDirector.TrySaveScoreAndGameOver");
}

[HarmonyPatch(typeof(Il2CppPlatinaLab.Main.Director.PlayDirector), nameof(Il2CppPlatinaLab.Main.Director.PlayDirector.Exit))]
internal static class PlayDirector_Exit_Patch
{
    private static void Prefix(bool isGameOver) => Core.Log.Msg($"[Diag] PlayDirector.Exit(isGameOver={isGameOver})");

    private static void Postfix()
    {
        try
        {
            PlayOverlay.InPlay = false;
            PlayAssist.RestoreAutoPlay();
            CustomSongs.Current = null;
        }
        catch (Exception e)
        {
            Core.Log.Warning($"[PlayDirector.Exit] 나가기 상태 정리 오류: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(Il2CppPlatinaLab.Main.Common.Dialog.ErrorDialogCollection), nameof(Il2CppPlatinaLab.Main.Common.Dialog.ErrorDialogCollection.APIServerError))]
internal static class Diag_APIServerError
{
    private static void Prefix() => Core.Log.Warning("[Diag] ErrorDialogCollection.APIServerError");
}

[HarmonyPatch(typeof(Il2CppPlatinaLab.Main.InGameSetting.Utilities.InGameSettingHelper), nameof(Il2CppPlatinaLab.Main.InGameSetting.Utilities.InGameSettingHelper.IsScorable))]
internal static class Diag_IsScorable
{
    private static void Postfix(bool __result) => Core.Log.Msg($"[Diag] IsScorable={__result}");
}
