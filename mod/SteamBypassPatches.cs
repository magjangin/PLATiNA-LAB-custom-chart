using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Il2CppCysharp.Threading.Tasks;
using Il2CppHighendGames.GamePlatform;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPlatinaLab.Main;
using Il2CppPlatinaLab.Main.Common.Dialog;
using Il2CppPlatinaLab.Main.Director;
using Il2CppPlatinaLab.Main.Endpoint;
using Il2CppPlatinaLab.Main.Endpoint.Model;
using Il2CppPlatinaLab.Main.Repository;
using Il2CppPlatinaLab.Main.Reward;
using Il2CppPlatinaLab.Main.Select.PersonalAgendaToastBoard;
using Il2CppPlatinaLab.Main.Story;
using Il2CppPlatinaLab.Main.Tutorial;
using Il2CppSteamworks;
using Il2CppSystem.Collections.Generic;

namespace PLATiNALAB.Mod;

/// <summary>
/// 오프라인 독립 실행 환경에서 스팀 클라이언트 없이 게임이 온전히 기동되도록 Steamworks SDK 및 플랫폼을 우회한다.
/// </summary>
[HarmonyPatch(typeof(SteamAPI), nameof(SteamAPI.Init))]
internal static class SteamAPI_Init_Patch
{
    private static bool Prefix(ref bool __result)
    {
        __result = true;
        Core.Log.Msg("[SteamBypass] SteamAPI.Init -> true (우회)");
        return false;
    }
}

[HarmonyPatch(typeof(SteamAPI), nameof(SteamAPI.InitEx))]
internal static class SteamAPI_InitEx_Patch
{
    private static bool Prefix(ref ESteamAPIInitResult __result, ref string OutSteamErrMsg)
    {
        __result = ESteamAPIInitResult.k_ESteamAPIInitResult_OK;
        OutSteamErrMsg = string.Empty;
        Core.Log.Msg("[SteamBypass] SteamAPI.InitEx -> OK (우회)");
        return false;
    }
}

[HarmonyPatch(typeof(SteamAPI), nameof(SteamAPI.RestartAppIfNecessary))]
internal static class SteamAPI_RestartAppIfNecessary_Patch
{
    private static bool Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(SteamAPI), nameof(SteamAPI.IsSteamRunning))]
internal static class SteamAPI_IsSteamRunning_Patch
{
    private static bool Prefix(ref bool __result)
    {
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(SteamAPI), nameof(SteamAPI.RunCallbacks))]
internal static class SteamAPI_RunCallbacks_Patch
{
    private static bool Prefix()
    {
        return false;
    }
}

[HarmonyPatch(typeof(SteamUser), nameof(SteamUser.GetSteamID))]
internal static class SteamUser_GetSteamID_Patch
{
    private static unsafe bool Prefix(ref CSteamID __result)
    {
        ulong val = 76561199502858707UL;
        __result = *(CSteamID*)&val;
        return false;
    }
}

[HarmonyPatch(typeof(SteamUser), nameof(SteamUser.BLoggedOn))]
internal static class SteamUser_BLoggedOn_Patch
{
    private static bool Prefix(ref bool __result)
    {
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(SteamUser), nameof(SteamUser.GetAuthTicketForWebApi))]
internal static class SteamUser_GetAuthTicketForWebApi_Patch
{
    private static unsafe bool Prefix(ref HAuthTicket __result)
    {
        uint val = 1;
        __result = *(HAuthTicket*)&val;
        return false;
    }
}

[HarmonyPatch(typeof(InteropHelp), nameof(InteropHelp.TestIfAvailableClient))]
internal static class InteropHelp_TestIfAvailableClient_Patch
{
    private static bool Prefix()
    {
        return false;
    }
}

[HarmonyPatch(typeof(Platform), nameof(Platform.PlatformUserId), MethodType.Getter)]
internal static class Platform_PlatformUserId_Patch
{
    private static bool Prefix(ref string __result)
    {
        __result = "76561199502858707";
        return false;
    }
}

[HarmonyPatch(typeof(Platform), nameof(Platform.GetAuthToken))]
internal static class Platform_GetAuthToken_Patch
{
    private const string Hook = "Platform.GetAuthToken";

    private static bool Prefix(ref UniTask<string> __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = GetAuthTokenLocal();
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("Platform.GetAuthToken")]
    private static UniTask<string> GetAuthTokenLocal()
    {
        return UniTask.FromResult("platinalab-offline-auth-token");
    }
}

[HarmonyPatch(typeof(LoginProcedure), nameof(LoginProcedure.LoginAndLoadProfile))]
internal static class LoginProcedure_LoginAndLoadProfile_Patch
{
    private const string Hook = "LoginProcedure.LoginAndLoadProfile";

    private static bool Prefix(ref UniTask<LoginResultType> __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            SetupOfflineSession();
            __result = SuccessResult();
            Core.Log.Msg("[SteamBypass] LoginProcedure.LoginAndLoadProfile -> Success (오프라인 세션 설정 완료)");
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("LoginProcedure.LoginAndLoadProfile")]
    private static void SetupOfflineSession()
    {
        try
        {
            var db = RuntimeDatabase.Instance;
            if (db is not null)
            {
                if (db.UserId is not null) db.UserId.Value = "76561199502858707";
                if (db.UserNickname is not null) db.UserNickname.Value = "화영왕";
                if (db.IsUserNicknameValid is not null) db.IsUserNicknameValid.Value = true;
                if (db.UserSessionToken is not null) db.UserSessionToken.Value = "platinalab-offline-session";
                if (db.IsUserProfileLoaded is not null) db.IsUserProfileLoaded.Value = true;
            }
        }
        catch (Exception e)
        {
            Core.Log.Warning($"[SteamBypass] RuntimeDatabase 세션 설정 실패(계속 진행): {e.Message}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("LoginProcedure.LoginAndLoadProfile")]
    private static UniTask<LoginResultType> SuccessResult()
    {
        return UniTask.FromResult(LoginResultType.Success);
    }
}

[HarmonyPatch(typeof(LoginSurface), nameof(LoginSurface.LoginSteam))]
internal static class LoginSurface_LoginSteam_Patch
{
    private const string Hook = "LoginSurface.LoginSteam";

    private static bool Prefix(ref UniTask<SurfaceResult<LoginOutcome>> __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = SuccessResult();
            Core.Log.Msg("[SteamBypass] LoginSurface.LoginSteam -> 가짜 LoginOutcome 반환");
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("LoginSurface.LoginSteam")]
    private static UniTask<SurfaceResult<LoginOutcome>> SuccessResult()
    {
        var outcome = new LoginOutcome(true, "화영왕", true);
        return UniTask.FromResult(SurfaceResult<LoginOutcome>.Success(outcome));
    }
}

[HarmonyPatch(typeof(UserSurface), nameof(UserSurface.GetUserProfile))]
internal static class UserSurface_GetUserProfile_Patch
{
    private const string Hook = "UserSurface.GetUserProfile";

    private static bool Prefix(ref UniTask<SurfaceResult<UserProfile>> __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = SuccessResult();
            Core.Log.Msg("[SteamBypass] UserSurface.GetUserProfile -> 가짜 UserProfile 반환");
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("UserSurface.GetUserProfile")]
    private static UniTask<SurfaceResult<UserProfile>> SuccessResult()
    {
        var profile = new UserProfile();
        profile.Nickname = "화영왕";
        profile.PlayerCode = "HWAPYEONG";
        return UniTask.FromResult(SurfaceResult<UserProfile>.Success(profile));
    }
}

[HarmonyPatch(typeof(UserSurface), nameof(UserSurface.GetUnlockedFeatures))]
internal static class UserSurface_GetUnlockedFeatures_Patch
{
    private const string Hook = "UserSurface.GetUnlockedFeatures";

    private static bool Prefix(ref UniTask<SurfaceResult<Il2CppStructArray<int>>> __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = SuccessResult();
            Core.Log.Msg("[SteamBypass] UserSurface.GetUnlockedFeatures -> 해금 기능 더미 반환");
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("UserSurface.GetUnlockedFeatures")]
    private static UniTask<SurfaceResult<Il2CppStructArray<int>>> SuccessResult()
    {
        var arr = new Il2CppStructArray<int>(0);
        return UniTask.FromResult(SurfaceResult<Il2CppStructArray<int>>.Success(arr));
    }
}

[HarmonyPatch(typeof(ContentsSurface), nameof(ContentsSurface.GetContentsAvailability))]
internal static class ContentsSurface_GetContentsAvailability_Patch
{
    private const string Hook = "ContentsSurface.GetContentsAvailability";

    private static bool Prefix(ref UniTask<SurfaceResult<CatalogPermissionResponse>> __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = SuccessResult();
            Core.Log.Msg("[SteamBypass] ContentsSurface.GetContentsAvailability -> 성공 가짜 응답 반환");
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("ContentsSurface.GetContentsAvailability")]
    private static UniTask<SurfaceResult<CatalogPermissionResponse>> SuccessResult()
    {
        var resp = new CatalogPermissionResponse();
        return UniTask.FromResult(SurfaceResult<CatalogPermissionResponse>.Success(resp));
    }
}

[HarmonyPatch(typeof(LoginInitialize), nameof(LoginInitialize.Init))]
internal static class LoginInitialize_Init_Patch
{
    private const string Hook = "LoginInitialize.Init";

    private static bool Prefix(ref UniTask __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = CompletedTask();
            Core.Log.Msg("[SteamBypass] LoginInitialize.Init -> 완료 처리 (오프라인)");
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("LoginInitialize.Init")]
    private static UniTask CompletedTask() => UniTask.CompletedTask;
}

[HarmonyPatch(typeof(HttpRequestInterceptor), nameof(HttpRequestInterceptor.IsCriticalError))]
internal static class HttpRequestInterceptor_IsCriticalError_Patch
{
    private static bool Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(HttpRequestInterceptor), nameof(HttpRequestInterceptor.HandleCriticalError))]
internal static class HttpRequestInterceptor_HandleCriticalError_Patch
{
    private const string Hook = "HttpRequestInterceptor.HandleCriticalError";

    private static bool Prefix(int errorCode, ref UniTask __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = CompletedTask();
            Core.Log.Warning($"[SteamBypass] HttpRequestInterceptor.HandleCriticalError({errorCode}) 차단 (오프라인 유지)");
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("HttpRequestInterceptor.HandleCriticalError")]
    private static UniTask CompletedTask() => UniTask.CompletedTask;
}

[HarmonyPatch(typeof(ErrorDialogCollection), nameof(ErrorDialogCollection.FatalAPIServerError))]
internal static class ErrorDialogCollection_FatalAPIServerError_Patch
{
    private const string Hook = "ErrorDialogCollection.FatalAPIServerError";

    private static bool Prefix(ref UniTask __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = CompletedTask();
            Core.Log.Warning("[SteamBypass] FatalAPIServerError 팝업 차단 (오프라인 유지)");
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("ErrorDialogCollection.FatalAPIServerError")]
    private static UniTask CompletedTask() => UniTask.CompletedTask;
}

[HarmonyPatch(typeof(ErrorDialogCollection), nameof(ErrorDialogCollection.APIServerError))]
internal static class ErrorDialogCollection_APIServerError_Patch
{
    private static bool Prefix()
    {
        Core.Log.Warning("[SteamBypass] APIServerError 팝업 차단 (오프라인 유지)");
        return false;
    }
}

[HarmonyPatch(typeof(ErrorDialogCollection), nameof(ErrorDialogCollection.ShowErrorForCode), new Type[] { typeof(int) })]
internal static class ErrorDialogCollection_ShowErrorForCode_Patch
{
    private const string Hook = "ErrorDialogCollection.ShowErrorForCode";

    private static bool Prefix(int errorCode, ref UniTask __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = CompletedTask();
            Core.Log.Warning($"[SteamBypass] ShowErrorForCode({errorCode}) 팝업 차단 (오프라인 유지)");
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("ErrorDialogCollection.ShowErrorForCode")]
    private static UniTask CompletedTask() => UniTask.CompletedTask;
}

[HarmonyPatch(typeof(ErrorDialogCollection), nameof(ErrorDialogCollection.ShowErrorForCode), new Type[] { typeof(SurfaceResult) })]
internal static class ErrorDialogCollection_ShowErrorForCode_SurfaceResult_Patch
{
    private const string Hook = "ErrorDialogCollection.ShowErrorForCode.SurfaceResult";

    private static bool Prefix(ref UniTask __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = CompletedTask();
            Core.Log.Warning("[SteamBypass] ShowErrorForCode(SurfaceResult) 팝업 차단 (오프라인 유지)");
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("ErrorDialogCollection.ShowErrorForCode.SurfaceResult")]
    private static UniTask CompletedTask() => UniTask.CompletedTask;
}

[HarmonyPatch(typeof(ErrorDialogCollection), nameof(ErrorDialogCollection.ShowNotLoggedInDialogAndRestart))]
internal static class ErrorDialogCollection_ShowNotLoggedInDialogAndRestart_Patch
{
    private const string Hook = "ErrorDialogCollection.ShowNotLoggedInDialogAndRestart";

    private static bool Prefix(ref UniTask __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = CompletedTask();
            Core.Log.Warning("[SteamBypass] ShowNotLoggedInDialogAndRestart 팝업 차단 (오프라인 유지)");
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("ErrorDialogCollection.ShowNotLoggedInDialogAndRestart")]
    private static UniTask CompletedTask() => UniTask.CompletedTask;
}

[HarmonyPatch(typeof(ErrorDialogCollection), nameof(ErrorDialogCollection.ShowMaintenanceDialogAndQuit))]
internal static class ErrorDialogCollection_ShowMaintenanceDialogAndQuit_Patch
{
    private const string Hook = "ErrorDialogCollection.ShowMaintenanceDialogAndQuit";

    private static bool Prefix(ref UniTask __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = CompletedTask();
            Core.Log.Warning("[SteamBypass] ShowMaintenanceDialogAndQuit 팝업 차단 (오프라인 유지)");
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("ErrorDialogCollection.ShowMaintenanceDialogAndQuit")]
    private static UniTask CompletedTask() => UniTask.CompletedTask;
}

[HarmonyPatch(typeof(RewardManager), nameof(RewardManager.Load))]
internal static class RewardManager_Load_Patch
{
    private const string Hook = "RewardManager.Load";

    private static bool Prefix(RewardManager __instance, ref UniTask __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            EnsureRewardMap(__instance);
            __result = CompletedTask();
            Core.Log.Msg("[SteamBypass] RewardManager.Load -> 성공 완료 (오프라인)");
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("RewardManager.Load")]
    private static void EnsureRewardMap(RewardManager instance)
    {
        try
        {
            if (instance is not null && instance._newRewardMap is null)
            {
                instance._newRewardMap = new Il2CppSystem.Collections.Generic.Dictionary<int, Il2CppPlatinaLab.DataModel.Reward>();
            }
        }
        catch (Exception e)
        {
            Core.Log.Warning($"[SteamBypass] EnsureRewardMap 실패: {e.Message}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("RewardManager.Load")]
    private static UniTask CompletedTask() => UniTask.CompletedTask;
}

[HarmonyPatch(typeof(RewardManager), nameof(RewardManager.FetchAllUnlockStates))]
internal static class RewardManager_FetchAllUnlockStates_Patch
{
    private const string Hook = "RewardManager.FetchAllUnlockStates";

    private static bool Prefix(ref UniTask __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = CompletedTask();
            Core.Log.Msg("[SteamBypass] RewardManager.FetchAllUnlockStates -> 성공 완료 (오프라인)");
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("RewardManager.FetchAllUnlockStates")]
    private static UniTask CompletedTask() => UniTask.CompletedTask;
}

[HarmonyPatch(typeof(PersonalAgendaToastBoardFriendsPage), nameof(PersonalAgendaToastBoardFriendsPage.FetchFocusInfoViaAPI))]
internal static class PersonalAgendaToastBoardFriendsPage_FetchFocusInfoViaAPI_Patch
{
    private const string Hook = "PersonalAgendaToastBoardFriendsPage.FetchFocusInfoViaAPI";

    private static bool Prefix(ref UniTask<Il2CppReferenceArray<Il2CppSystem.ValueTuple<string, Il2CppSystem.Nullable<int>>>> __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = SuccessResult();
            Core.Log.Msg("[SteamBypass] PersonalAgendaToastBoardFriendsPage.FetchFocusInfoViaAPI -> 빈 배열 반환");
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("PersonalAgendaToastBoardFriendsPage.FetchFocusInfoViaAPI")]
    private static UniTask<Il2CppReferenceArray<Il2CppSystem.ValueTuple<string, Il2CppSystem.Nullable<int>>>> SuccessResult()
    {
        var arr = new Il2CppReferenceArray<Il2CppSystem.ValueTuple<string, Il2CppSystem.Nullable<int>>>(0);
        return UniTask.FromResult(arr);
    }
}

[HarmonyPatch(typeof(SocialSurface), nameof(SocialSurface.GetScoreRecordsOfFocusedUsers))]
internal static class SocialSurface_GetScoreRecordsOfFocusedUsers_Patch
{
    private const string Hook = "SocialSurface.GetScoreRecordsOfFocusedUsers";

    private static bool Prefix(ref UniTask<SurfaceResult<Il2CppReferenceArray<SocialFocusedUserScoreRecordEntry>>> __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = SuccessResult();
            Core.Log.Msg("[SteamBypass] SocialSurface.GetScoreRecordsOfFocusedUsers -> 빈 배열 반환");
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("SocialSurface.GetScoreRecordsOfFocusedUsers")]
    private static UniTask<SurfaceResult<Il2CppReferenceArray<SocialFocusedUserScoreRecordEntry>>> SuccessResult()
    {
        var arr = new Il2CppReferenceArray<SocialFocusedUserScoreRecordEntry>(0);
        return UniTask.FromResult(SurfaceResult<Il2CppReferenceArray<SocialFocusedUserScoreRecordEntry>>.Success(arr));
    }
}

[HarmonyPatch(typeof(SocialSurface), nameof(SocialSurface.GetFollowingList))]
internal static class SocialSurface_GetFollowingList_Patch
{
    private const string Hook = "SocialSurface.GetFollowingList";

    private static bool Prefix(ref UniTask<SurfaceResult<Il2CppReferenceArray<SocialUserFollowingListEntry>>> __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = SuccessResult();
            Core.Log.Msg("[SteamBypass] SocialSurface.GetFollowingList -> 빈 배열 반환");
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("SocialSurface.GetFollowingList")]
    private static UniTask<SurfaceResult<Il2CppReferenceArray<SocialUserFollowingListEntry>>> SuccessResult()
    {
        var arr = new Il2CppReferenceArray<SocialUserFollowingListEntry>(0);
        return UniTask.FromResult(SurfaceResult<Il2CppReferenceArray<SocialUserFollowingListEntry>>.Success(arr));
    }
}

[HarmonyPatch(typeof(UnityEngine.Application), nameof(UnityEngine.Application.Quit), new Type[] { })]
internal static class Application_Quit_Patch
{
    private static bool Prefix()
    {
        Core.Log.Warning("[SteamBypass] Application.Quit() 호출 감지 -> 오프라인 보존을 위해 무시합니다.");
        return false;
    }
}

[HarmonyPatch(typeof(StoryManager), nameof(StoryManager.IsPrologueFirstHalfRead), MethodType.Getter)]
internal static class StoryManager_IsPrologueFirstHalfRead_Patch
{
    private static bool Prefix(ref bool __result)
    {
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(StoryManager), nameof(StoryManager.IsPrologueSecondHalfRead), MethodType.Getter)]
internal static class StoryManager_IsPrologueSecondHalfRead_Patch
{
    private static bool Prefix(ref bool __result)
    {
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(StoryManager), nameof(StoryManager.IsStoryRead))]
internal static class StoryManager_IsStoryRead_Patch
{
    private static bool Prefix(ref bool __result)
    {
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(TitleDirector), nameof(TitleDirector.LoadPrologueScene))]
internal static class TitleDirector_LoadPrologueScene_Patch
{
    private const string Hook = "TitleDirector.LoadPrologueScene";

    private static bool Prefix(TitleDirector __instance, ref UniTask __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = SkipPrologue(__instance);
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("TitleDirector.LoadPrologueScene")]
    private static UniTask SkipPrologue(TitleDirector instance)
    {
        Core.Log.Msg("[SteamBypass] TitleDirector.LoadPrologueScene 스킵 -> MainMenu로 직행");
        instance.LoadNextScene("MainMenu");
        return UniTask.CompletedTask;
    }
}

[HarmonyPatch(typeof(TutorialLauncher), nameof(TutorialLauncher.Enter))]
internal static class TutorialLauncher_Enter_Patch
{
    private static bool Prefix()
    {
        Core.Log.Msg("[SteamBypass] TutorialLauncher.Enter 차단 (튜토리얼 스킵)");
        return false;
    }
}

[HarmonyPatch(typeof(StorySequenceDirector), nameof(StorySequenceDirector.AskTutorialAndProceed))]
internal static class StorySequenceDirector_AskTutorialAndProceed_Patch
{
    private const string Hook = "StorySequenceDirector.AskTutorialAndProceed";

    private static bool Prefix(ref UniTask __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = CompletedTask();
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("StorySequenceDirector.AskTutorialAndProceed")]
    private static UniTask CompletedTask() => UniTask.CompletedTask;
}

[HarmonyPatch(typeof(StorySequenceDirector), nameof(StorySequenceDirector.LoadTutorialScene))]
internal static class StorySequenceDirector_LoadTutorialScene_Patch
{
    private const string Hook = "StorySequenceDirector.LoadTutorialScene";

    private static bool Prefix(ref UniTask __result)
    {
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = CompletedTask();
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("StorySequenceDirector.LoadTutorialScene")]
    private static UniTask CompletedTask() => UniTask.CompletedTask;
}
