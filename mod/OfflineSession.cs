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
    private static bool Prefix(int patternId, ref UniTask<SurfaceResult<GameSessionResult>> __result)
    {
        try
        {
            CustomSongs.Current = CustomSongs.ByPatternIndex(patternId);
            if (CustomSongs.Current == null) return true;
            var session = new GameSessionResult();
            if (!UpdateResilience.SafeSetFieldOrProperty(session, "Token", OfflineSession.Token))
                Core.Log.Warning("[OfflineSession] 세션 Token 설정 실패: RenewGameSession이 서버로 갈 수 있음");
            ModHealth.BeginPlay(CustomSongs.Current.Song);
            __result = new UniTask<SurfaceResult<GameSessionResult>>(SurfaceResult<GameSessionResult>.Success(session));
            Core.Log.Msg($"[OfflineSession] RequestGameStart({patternId}) 서버 호출 생략, 로컬 세션 발급");
            return false;
        }
        catch (Exception e)
        {
            Core.Log.Error($"[OfflineSession] RequestGameStart 래퍼 예외 발생: {e}");
            return true;
        }
    }
}

[HarmonyPatch(typeof(InGameSurface), nameof(InGameSurface.RenewGameSession))]
internal static class InGameSurface_RenewGameSession_Patch
{
    private static bool Prefix(string gameToken, ref UniTask<SurfaceResult> __result)
    {
        if (gameToken != OfflineSession.Token) return true;
        __result = new UniTask<SurfaceResult>(SurfaceResult.Success());
        return false;
    }
}

[HarmonyPatch(typeof(InGameSurface), nameof(InGameSurface.SendGameEnd))]
internal static class InGameSurface_SendGameEnd_Patch
{
    private static bool Prefix(GameEndResult request, PatternInfo patternInfo, ref UniTask<SurfaceResult<FreeModePlayRecordV2>> __result)
    {
        if (!CustomSongs.IsCustom(patternInfo)) return true;
        try
        {
            var localRecord = BuildLocalRecord(request);
            if (localRecord != null)
            {
                __result = new UniTask<SurfaceResult<FreeModePlayRecordV2>>(SurfaceResult<FreeModePlayRecordV2>.Success(localRecord));
                int score = Math.Max(request?.Score ?? 0, request?.FinalScore ?? 0);
                Core.Log.Msg($"[OfflineSession] SendGameEnd 서버 전송 생략, 로컬 결과 반환 (score={score})");
            }
            else
            {
                __result = new UniTask<SurfaceResult<FreeModePlayRecordV2>>(SurfaceResult<FreeModePlayRecordV2>.NetworkError());
            }
        }
        catch (Exception e)
        {
            Core.Log.Error($"[OfflineSession] 로컬 결과 생성 실패: {e}");
            __result = new UniTask<SurfaceResult<FreeModePlayRecordV2>>(SurfaceResult<FreeModePlayRecordV2>.NetworkError());
        }
        return false;
    }

    /// <summary>
    /// 서버 응답 없이 결과 화면에 넘길 레코드를 만든다.
    /// RecordUpdateHelper.UpdatePatternRecordAndBuildSnapshot은 세이브에 패턴 990001 기록을 남기므로 쓰지 않고,
    /// 스냅샷은 Record* 메서드로 직접 채운다 (이전 값 0 → 이번 값).
    /// 직접 생성자 호출은 CreateRecordDirect로 분리했다. 같은 메서드에 두면 시그니처가 바뀔 때 JIT 단계에서 메서드 전체가 실패해 Reflection 폴백에 도달하지 못한다.
    /// </summary>
    private static FreeModePlayRecordV2 BuildLocalRecord(GameEndResult request)
    {
        // SectionDetailPanelController NRE 방지: SectionRecords가 null이면 빈 배열 할당
        if (request != null && request.SectionRecords == null)
        {
            try { request.SectionRecords = new Il2CppReferenceArray<SectionRecord>(0); }
            catch { }
        }

        var response = new EndGameResponse();
        response.PlayResult = new PlayResult();
        response.GrantedItems = new Il2CppReferenceArray<Il2CppPlatinaLab.DataModel.ItemBase>(0);
        response.UpdatedAchievements = new Il2CppReferenceArray<UserAchievementState>(0);

        int finalScore = Math.Max(request?.Score ?? 0, request?.FinalScore ?? 0);
        var snapshot = new GameEndResultSnapShot();
        try
        {
            FillSnapshot(snapshot, finalScore);
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FillSnapshot(GameEndResultSnapShot snapshot, int finalScore)
    {
        var zeroMastery = new Il2CppPlatinaLab.Main.Common.Types.Mastery(0);
        snapshot.RecordPatternScore(0, finalScore);
        snapshot.RecordPatternAccuracy(new Il2CppPlatinaLab.Main.Common.Types.Accuracy(0), new Il2CppPlatinaLab.Main.Common.Types.Accuracy(0));
        snapshot.RecordPatternMastery(zeroMastery, zeroMastery);
        snapshot.RecordTotalMastery(snapshot.PlayedGameMode, zeroMastery, zeroMastery);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
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
internal static class Diag_Exit
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
