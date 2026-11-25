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
/// 모든 Prefix는 OfflineMode=1(<see cref="Offline.Enabled"/>)일 때만 동작하고, 꺼져 있으면 게임 원본을 그대로 실행한다.
/// 닉네임·사용자 ID는 config.txt의 OfflineNickname / OfflineUserId (예전에는 제작자 값이 하드코딩돼 있었다).
/// </summary>
[OfflineOnly]
[HarmonyPatch(typeof(SteamAPI), nameof(SteamAPI.Init))]
internal static class SteamAPI_Init_Patch
{
    private static bool Prefix(ref bool __result)
    {
        if (!Offline.Enabled) return true;
        __result = true;
        Core.Log.Msg("[SteamBypass] SteamAPI.Init -> true (우회)");
        return false;
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(SteamAPI), nameof(SteamAPI.InitEx))]
internal static class SteamAPI_InitEx_Patch
{
    private static bool Prefix(ref ESteamAPIInitResult __result, ref string OutSteamErrMsg)
    {
        if (!Offline.Enabled) return true;
        __result = ESteamAPIInitResult.k_ESteamAPIInitResult_OK;
        OutSteamErrMsg = string.Empty;
        Core.Log.Msg("[SteamBypass] SteamAPI.InitEx -> OK (우회)");
        return false;
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(SteamAPI), nameof(SteamAPI.RestartAppIfNecessary))]
internal static class SteamAPI_RestartAppIfNecessary_Patch
{
    private static bool Prefix(ref bool __result)
    {
        if (!Offline.Enabled) return true;
        __result = false;
        return false;
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(SteamAPI), nameof(SteamAPI.IsSteamRunning))]
internal static class SteamAPI_IsSteamRunning_Patch
{
    private static bool Prefix(ref bool __result)
    {
        if (!Offline.Enabled) return true;
        __result = true;
        return false;
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(SteamAPI), nameof(SteamAPI.RunCallbacks))]
internal static class SteamAPI_RunCallbacks_Patch
{
    private static bool Prefix()
    {
        if (!Offline.Enabled) return true;
        return false;
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(SteamUser), nameof(SteamUser.GetSteamID))]
internal static class SteamUser_GetSteamID_Patch
{
    private static unsafe bool Prefix(ref CSteamID __result)
    {
        if (!Offline.Enabled) return true;
        ulong val = Offline.SteamId64;
        __result = *(CSteamID*)&val;
        return false;
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(SteamUser), nameof(SteamUser.BLoggedOn))]
internal static class SteamUser_BLoggedOn_Patch
{
    private static bool Prefix(ref bool __result)
    {
        if (!Offline.Enabled) return true;
        __result = true;
        return false;
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(SteamUser), nameof(SteamUser.GetAuthTicketForWebApi))]
internal static class SteamUser_GetAuthTicketForWebApi_Patch
{
    private static unsafe bool Prefix(ref HAuthTicket __result)
    {
        if (!Offline.Enabled) return true;
        uint val = 1;
        __result = *(HAuthTicket*)&val;
        return false;
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(InteropHelp), nameof(InteropHelp.TestIfAvailableClient))]
internal static class InteropHelp_TestIfAvailableClient_Patch
{
    private static bool Prefix()
    {
        if (!Offline.Enabled) return true;
        return false;
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(Platform), nameof(Platform.PlatformUserId), MethodType.Getter)]
internal static class Platform_PlatformUserId_Patch
{
    private static bool Prefix(ref string __result)
    {
        if (!Offline.Enabled) return true;
        __result = Offline.UserId;
        return false;
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(Platform), nameof(Platform.GetAuthToken))]
internal static class Platform_GetAuthToken_Patch
{
    private const string Hook = "Platform.GetAuthToken";

    private static bool Prefix(ref UniTask<string> __result)
    {
        if (!Offline.Enabled) return true;
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

[OfflineOnly]
[HarmonyPatch(typeof(LoginProcedure), nameof(LoginProcedure.LoginAndLoadProfile))]
internal static class LoginProcedure_LoginAndLoadProfile_Patch
{
    private const string Hook = "LoginProcedure.LoginAndLoadProfile";

    private static bool Prefix(ref UniTask<LoginResultType> __result)
    {
        if (!Offline.Enabled) return true;
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            SetupOfflineSession();
            __result = SuccessResult();
            Offline.NoteBootCompleted();
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
                if (db.UserId is not null) db.UserId.Value = Offline.UserId;
                if (db.UserNickname is not null) db.UserNickname.Value = Offline.Nickname;
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

[OfflineOnly]
[HarmonyPatch(typeof(LoginSurface), nameof(LoginSurface.LoginSteam))]
internal static class LoginSurface_LoginSteam_Patch
{
    private const string Hook = "LoginSurface.LoginSteam";

    private static bool Prefix(ref UniTask<SurfaceResult<LoginOutcome>> __result)
    {
        if (!Offline.Enabled) return true;
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
        var outcome = new LoginOutcome(true, Offline.Nickname, true);
        return UniTask.FromResult(SurfaceResult<LoginOutcome>.Success(outcome));
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(UserSurface), nameof(UserSurface.GetUserProfile))]
internal static class UserSurface_GetUserProfile_Patch
{
    private const string Hook = "UserSurface.GetUserProfile";

    private static bool Prefix(ref UniTask<SurfaceResult<UserProfile>> __result)
    {
        if (!Offline.Enabled) return true;
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
        profile.Nickname = Offline.Nickname;
        profile.PlayerCode = "OFFLINE";
        return UniTask.FromResult(SurfaceResult<UserProfile>.Success(profile));
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(UserSurface), nameof(UserSurface.GetUnlockedFeatures))]
internal static class UserSurface_GetUnlockedFeatures_Patch
{
    private const string Hook = "UserSurface.GetUnlockedFeatures";

    private static bool Prefix(ref UniTask<SurfaceResult<Il2CppStructArray<int>>> __result)
    {
        if (!Offline.Enabled) return true;
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

[OfflineOnly]
[HarmonyPatch(typeof(ContentsSurface), nameof(ContentsSurface.GetContentsAvailability))]
internal static class ContentsSurface_GetContentsAvailability_Patch
{
    private const string Hook = "ContentsSurface.GetContentsAvailability";

    private static bool Prefix(ref UniTask<SurfaceResult<CatalogPermissionResponse>> __result)
    {
        if (!Offline.Enabled) return true;
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

[OfflineOnly]
[HarmonyPatch(typeof(LoginInitialize), nameof(LoginInitialize.Init))]
internal static class LoginInitialize_Init_Patch
{
    private const string Hook = "LoginInitialize.Init";

    private static bool Prefix(ref UniTask __result)
    {
        if (!Offline.Enabled) return true;
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

[OfflineOnly]
[HarmonyPatch(typeof(HttpRequestInterceptor), nameof(HttpRequestInterceptor.IsCriticalError))]
internal static class HttpRequestInterceptor_IsCriticalError_Patch
{
    private static bool Prefix(ref bool __result)
    {
        if (!Offline.Enabled) return true;
        __result = false;
        return false;
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(HttpRequestInterceptor), nameof(HttpRequestInterceptor.HandleCriticalError))]
internal static class HttpRequestInterceptor_HandleCriticalError_Patch
{
    private const string Hook = "HttpRequestInterceptor.HandleCriticalError";

    private static bool Prefix(int errorCode, ref UniTask __result)
    {
        if (!Offline.Enabled) return true;
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = CompletedTask();
            Core.Log.Warning($"[SteamBypass] HttpRequestInterceptor.HandleCriticalError({errorCode}) 차단 (오프라인 유지)");
            Offline.NoteBlockedError();
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

[OfflineOnly]
[HarmonyPatch(typeof(ErrorDialogCollection), nameof(ErrorDialogCollection.FatalAPIServerError))]
internal static class ErrorDialogCollection_FatalAPIServerError_Patch
{
    private const string Hook = "ErrorDialogCollection.FatalAPIServerError";

    private static bool Prefix(ref UniTask __result)
    {
        if (!Offline.Enabled) return true;
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = CompletedTask();
            Core.Log.Warning("[SteamBypass] FatalAPIServerError 팝업 차단 (오프라인 유지)");
            Offline.NoteBlockedError();
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

[OfflineOnly]
[HarmonyPatch(typeof(ErrorDialogCollection), nameof(ErrorDialogCollection.APIServerError))]
internal static class ErrorDialogCollection_APIServerError_Patch
{
    private static bool Prefix()
    {
        if (!Offline.Enabled) return true;
        Core.Log.Warning("[SteamBypass] APIServerError 팝업 차단 (오프라인 유지)");
        Offline.NoteBlockedError();
        return false;
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(ErrorDialogCollection), nameof(ErrorDialogCollection.ShowErrorForCode), new Type[] { typeof(int) })]
internal static class ErrorDialogCollection_ShowErrorForCode_Patch
{
    private const string Hook = "ErrorDialogCollection.ShowErrorForCode";

    private static bool Prefix(int errorCode, ref UniTask __result)
    {
        if (!Offline.Enabled) return true;
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = CompletedTask();
            Core.Log.Warning($"[SteamBypass] ShowErrorForCode({errorCode}) 팝업 차단 (오프라인 유지)");
            Offline.NoteBlockedError();
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

[OfflineOnly]
[HarmonyPatch(typeof(ErrorDialogCollection), nameof(ErrorDialogCollection.ShowErrorForCode), new Type[] { typeof(SurfaceResult) })]
internal static class ErrorDialogCollection_ShowErrorForCode_SurfaceResult_Patch
{
    private const string Hook = "ErrorDialogCollection.ShowErrorForCode.SurfaceResult";

    private static bool Prefix(ref UniTask __result)
    {
        if (!Offline.Enabled) return true;
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = CompletedTask();
            Core.Log.Warning("[SteamBypass] ShowErrorForCode(SurfaceResult) 팝업 차단 (오프라인 유지)");
            Offline.NoteBlockedError();
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

[OfflineOnly]
[HarmonyPatch(typeof(ErrorDialogCollection), nameof(ErrorDialogCollection.ShowNotLoggedInDialogAndRestart))]
internal static class ErrorDialogCollection_ShowNotLoggedInDialogAndRestart_Patch
{
    private const string Hook = "ErrorDialogCollection.ShowNotLoggedInDialogAndRestart";

    private static bool Prefix(ref UniTask __result)
    {
        if (!Offline.Enabled) return true;
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = CompletedTask();
            Core.Log.Warning("[SteamBypass] ShowNotLoggedInDialogAndRestart 팝업 차단 (오프라인 유지)");
            Offline.NoteBlockedError();
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

[OfflineOnly]
[HarmonyPatch(typeof(ErrorDialogCollection), nameof(ErrorDialogCollection.ShowMaintenanceDialogAndQuit))]
internal static class ErrorDialogCollection_ShowMaintenanceDialogAndQuit_Patch
{
    private const string Hook = "ErrorDialogCollection.ShowMaintenanceDialogAndQuit";

    private static bool Prefix(ref UniTask __result)
    {
        if (!Offline.Enabled) return true;
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            __result = CompletedTask();
            Core.Log.Warning("[SteamBypass] ShowMaintenanceDialogAndQuit 팝업 차단 (오프라인 유지)");
            Offline.NoteBlockedError();
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

[OfflineOnly]
[HarmonyPatch(typeof(RewardManager), nameof(RewardManager.Load))]
internal static class RewardManager_Load_Patch
{
    private const string Hook = "RewardManager.Load";

    private static bool Prefix(RewardManager __instance, ref UniTask __result)
    {
        if (!Offline.Enabled) return true;
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

[OfflineOnly]
[HarmonyPatch(typeof(RewardManager), nameof(RewardManager.FetchAllUnlockStates))]
internal static class RewardManager_FetchAllUnlockStates_Patch
{
    private const string Hook = "RewardManager.FetchAllUnlockStates";

    private static bool Prefix(ref UniTask __result)
    {
        if (!Offline.Enabled) return true;
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

[OfflineOnly]
[HarmonyPatch(typeof(PersonalAgendaToastBoardFriendsPage), nameof(PersonalAgendaToastBoardFriendsPage.FetchFocusInfoViaAPI))]
internal static class PersonalAgendaToastBoardFriendsPage_FetchFocusInfoViaAPI_Patch
{
    private const string Hook = "PersonalAgendaToastBoardFriendsPage.FetchFocusInfoViaAPI";

    private static bool Prefix(ref UniTask<Il2CppReferenceArray<Il2CppSystem.ValueTuple<string, Il2CppSystem.Nullable<int>>>> __result)
    {
        if (!Offline.Enabled) return true;
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

[OfflineOnly]
[HarmonyPatch(typeof(SocialSurface), nameof(SocialSurface.GetScoreRecordsOfFocusedUsers))]
internal static class SocialSurface_GetScoreRecordsOfFocusedUsers_Patch
{
    private const string Hook = "SocialSurface.GetScoreRecordsOfFocusedUsers";

    private static bool Prefix(ref UniTask<SurfaceResult<Il2CppReferenceArray<SocialFocusedUserScoreRecordEntry>>> __result)
    {
        if (!Offline.Enabled) return true;
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

[OfflineOnly]
[HarmonyPatch(typeof(SocialSurface), nameof(SocialSurface.GetFollowingList))]
internal static class SocialSurface_GetFollowingList_Patch
{
    private const string Hook = "SocialSurface.GetFollowingList";

    private static bool Prefix(ref UniTask<SurfaceResult<Il2CppReferenceArray<SocialUserFollowingListEntry>>> __result)
    {
        if (!Offline.Enabled) return true;
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

/// <summary>
/// 오프라인 모드에서 게임이 로그인·서버 실패로 스스로 종료하는 것만 막는다. 사용자가 메뉴에서 누른 종료는 허용한다
/// (decf3b7에서는 모든 종료를 막아 게임 메뉴로 끌 수 없었다). 판단 기준은 <see cref="Offline.ShouldBlockQuit"/>.
/// </summary>
[OfflineOnly]
[HarmonyPatch(typeof(UnityEngine.Application), nameof(UnityEngine.Application.Quit), new Type[] { })]
internal static class Application_Quit_Patch
{
    private static bool Prefix()
    {
        if (!Offline.ShouldBlockQuit(out var reason)) return true;
        Core.Log.Warning($"[SteamBypass] Application.Quit() 차단 ({reason}) — 오프라인 실행 유지. 다시 종료하려면 잠시 뒤 메뉴에서 종료하세요.");
        return false;
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(StoryManager), nameof(StoryManager.IsPrologueFirstHalfRead), MethodType.Getter)]
internal static class StoryManager_IsPrologueFirstHalfRead_Patch
{
    private static bool Prefix(ref bool __result)
    {
        if (!Offline.Enabled) return true;
        __result = true;
        return false;
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(StoryManager), nameof(StoryManager.IsPrologueSecondHalfRead), MethodType.Getter)]
internal static class StoryManager_IsPrologueSecondHalfRead_Patch
{
    private static bool Prefix(ref bool __result)
    {
        if (!Offline.Enabled) return true;
        __result = true;
        return false;
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(StoryManager), nameof(StoryManager.IsStoryRead))]
internal static class StoryManager_IsStoryRead_Patch
{
    private static bool Prefix(ref bool __result)
    {
        if (!Offline.Enabled) return true;
        __result = true;
        return false;
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(TitleDirector), nameof(TitleDirector.LoadPrologueScene))]
internal static class TitleDirector_LoadPrologueScene_Patch
{
    private const string Hook = "TitleDirector.LoadPrologueScene";

    private static bool Prefix(TitleDirector __instance, ref UniTask __result)
    {
        if (!Offline.Enabled) return true;
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

[OfflineOnly]
[HarmonyPatch(typeof(TutorialLauncher), nameof(TutorialLauncher.Enter))]
internal static class TutorialLauncher_Enter_Patch
{
    private static bool Prefix()
    {
        if (!Offline.Enabled) return true;
        Core.Log.Msg("[SteamBypass] TutorialLauncher.Enter 차단 (튜토리얼 스킵)");
        return false;
    }
}

[OfflineOnly]
[HarmonyPatch(typeof(StorySequenceDirector), nameof(StorySequenceDirector.AskTutorialAndProceed))]
internal static class StorySequenceDirector_AskTutorialAndProceed_Patch
{
    private const string Hook = "StorySequenceDirector.AskTutorialAndProceed";

    private static bool Prefix(ref UniTask __result)
    {
        if (!Offline.Enabled) return true;
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

[OfflineOnly]
[HarmonyPatch(typeof(StorySequenceDirector), nameof(StorySequenceDirector.LoadTutorialScene))]
internal static class StorySequenceDirector_LoadTutorialScene_Patch
{
    private const string Hook = "StorySequenceDirector.LoadTutorialScene";

    private static bool Prefix(ref UniTask __result)
    {
        if (!Offline.Enabled) return true;
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
