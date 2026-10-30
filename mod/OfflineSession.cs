using HarmonyLib;
using Il2CppCysharp.Threading.Tasks;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPlatinaLab.Main.Catalog.Scheme.Song;
using Il2CppPlatinaLab.Main.Endpoint;
using Il2CppPlatinaLab.Main.Endpoint.Model;
using Il2CppPlatinaLab.Main.InGame.Score;
using Il2CppPlatinaLab.Main.InGame.Score.Schema;
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
        CustomSongs.Current = CustomSongs.ByPatternIndex(patternId);
        if (CustomSongs.Current == null) return true;
        var session = new GameSessionResult();
        session._Token_k__BackingField = OfflineSession.Token;
        __result = new UniTask<SurfaceResult<GameSessionResult>>(SurfaceResult<GameSessionResult>.Success(session));
        Core.Log.Msg($"[OfflineSession] RequestGameStart({patternId}) 서버 호출 생략, 로컬 세션 발급");
        return false;
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
            __result = new UniTask<SurfaceResult<FreeModePlayRecordV2>>(SurfaceResult<FreeModePlayRecordV2>.Success(BuildLocalRecord(request)));
            Core.Log.Msg($"[OfflineSession] SendGameEnd 서버 전송 생략, 로컬 결과 반환 (score={request?.FinalScore})");
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
    /// </summary>
    private static FreeModePlayRecordV2 BuildLocalRecord(GameEndResult request)
    {
        var response = new EndGameResponse();
        response._PlayResult_k__BackingField = new PlayResult();
        response._GrantedItems_k__BackingField = new Il2CppReferenceArray<Il2CppPlatinaLab.DataModel.ItemBase>(0);
        response._UpdatedAchievements_k__BackingField = new Il2CppReferenceArray<UserAchievementState>(0);

        var snapshot = new GameEndResultSnapShot();
        var zeroMastery = new Il2CppPlatinaLab.Main.Common.Types.Mastery(0);
        snapshot.RecordPatternScore(0, request?.FinalScore ?? 0);
        snapshot.RecordPatternAccuracy(new Il2CppPlatinaLab.Main.Common.Types.Accuracy(0), new Il2CppPlatinaLab.Main.Common.Types.Accuracy(0));
        snapshot.RecordPatternMastery(zeroMastery, zeroMastery);
        snapshot.RecordTotalMastery(snapshot.PlayedGameMode, zeroMastery, zeroMastery);

        return new FreeModePlayRecordV2(request, snapshot, response, null, null, new Il2CppSystem.Collections.Generic.List<Il2CppPlatinaLab.Main.Common.FeatureId>());
    }
}

// --- 게임 종료 경로 진단 (지난 플레이에서 SendGameEnd까지 가지 못함) ---

[HarmonyPatch(typeof(Il2CppPlatinaLab.Main.Director.PlayDirector), nameof(Il2CppPlatinaLab.Main.Director.PlayDirector.TrySaveScoreAndGameOver))]
internal static class Diag_TrySave
{
    private static void Prefix() => Core.Log.Msg("[Diag] PlayDirector.TrySaveScoreAndGameOver");
}

[HarmonyPatch(typeof(Il2CppPlatinaLab.Main.Director.PlayDirector), nameof(Il2CppPlatinaLab.Main.Director.PlayDirector.Exit))]
internal static class Diag_Exit
{
    private static void Prefix(bool isGameOver) => Core.Log.Msg($"[Diag] PlayDirector.Exit(isGameOver={isGameOver})");
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
