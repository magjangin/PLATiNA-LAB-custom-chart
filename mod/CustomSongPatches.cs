using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppPlatinaLab.DataModel.SongCategory;
using Il2CppPlatinaLab.Main.Catalog;
using Il2CppPlatinaLab.Main.Catalog.Scheme.Song;
using Il2CppPlatinaLab.Main.Select.Category;
using Il2CppPlatinaLab.Main.Select.Category.Cache;

namespace PLATiNALAB.Mod;

[HarmonyPatch(typeof(SongListBuilder), nameof(SongListBuilder.BuildNameBanners))]
internal static class SongListBuilder_BuildNameBanners_Patch
{
    private static void Prefix()
    {
        // 선곡 화면을 열 때마다 불린다 → 진단 로그를 켰을 때만 남긴다 (주입 결과는 Inject가 따로 기록)
        if (ModConfig.DiagnosticLogs) Core.Log.Msg("[CustomSongs] SongListBuilder.BuildNameBanners");
        CustomSongs.InjectGuarded();
    }
}

// 커스텀 곡은 게임 카탈로그/보유 정보 사전에 없어서 원본 판정이 KeyNotFound를 던진다 → 직접 고정값을 돌려준다
[HarmonyPatch(typeof(SongInfo), nameof(SongInfo.IsHiddenFromDefaultList), MethodType.Getter)]
internal static class SongInfo_IsHidden_Patch
{
    private static bool Prefix(SongInfo __instance, ref bool __result)
    {
        if (!CustomSongs.IsCustomSong(__instance)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(PatternInfo), nameof(PatternInfo.GetPatternFileName))]
internal static class PatternInfo_GetPatternFileName_Patch
{
    private const string Hook = "PatternInfo.GetPatternFileName";
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> s_logged = new();

    private static void Postfix(PatternInfo __instance, ref string __result)
    {
        var baseName = CustomSongs.s_baseFileName;
        if (baseName == null || __instance is null || HookGuard.IsDisabled(Hook)) return;
        try
        {
            if (!CustomSongs.IsCustom(__instance)) return;
            if (__result != baseName && s_logged.TryAdd(__result ?? "", 0))
                Core.Log.Msg($"[CustomSongs] GetPatternFileName '{__result}' -> '{baseName}'");
            __result = baseName;
        }
        catch (Exception e) { HookGuard.Fail(Hook, e, required: true); }
    }
}

// --- 보유·해금 판정 (모든 곡에서 자주 불림) ---
// 커스텀 곡: 게임 보유 사전에 없어 원본이 KeyNotFound → 항상 사용 가능·보유·해금.
// 공식 곡: 온라인(OfflineMode=0)이면 원본 그대로 실행하고 결과를 SongOwnership에 기록한다.
//          오프라인(OfflineMode=1)이면 서버 보유 정보가 없으므로 기록값을 재현한다 (기록 없으면 잠김).
// decf3b7은 모든 공식 곡을 무조건 해금해 소유하지 않은 유료 DLC 곡까지 열었다 → 19차 감사에서 기록 재현으로 교체.

[HarmonyPatch(typeof(SongInfo), nameof(SongInfo.IsAvailable))]
internal static class SongInfo_IsAvailable_Patch
{
    private static bool Prefix(SongInfo __instance, ref bool __result, out string __state)
    {
        __state = null;
        if (CustomSongs.IsCustomSong(__instance)) { __result = true; return false; }
        return SongOwnership.Prefix(CustomSongs.ReadSongIdCached(__instance), SongOwnershipStore.Available, ref __result, out __state);
    }

    private static void Postfix(bool __result, string __state) => SongOwnership.Record(__state, SongOwnershipStore.Available, __result);
}

[HarmonyPatch(typeof(SongInfo), nameof(SongInfo.IsNotOwned))]
internal static class SongInfo_IsNotOwned_Patch
{
    private static bool Prefix(SongInfo __instance, ref bool __result, out string __state)
    {
        __state = null;
        if (CustomSongs.IsCustomSong(__instance)) { __result = false; return false; }
        return SongOwnership.Prefix(CustomSongs.ReadSongIdCached(__instance), SongOwnershipStore.NotOwned, ref __result, out __state);
    }

    private static void Postfix(bool __result, string __state) => SongOwnership.Record(__state, SongOwnershipStore.NotOwned, __result);
}

[HarmonyPatch(typeof(SongInfo), nameof(SongInfo.IsLock))]
internal static class SongInfo_IsLock_Patch
{
    private const string Hook = "EncryptedBool";

    private static bool Prefix(SongInfo __instance, ref Il2CppPlatinaLab.Common.EncryptedPrimitives.EncryptedBool __result, out string __state)
    {
        __state = null;
        if (HookGuard.IsDisabled(Hook)) return true;
        try
        {
            if (CustomSongs.IsCustomSong(__instance)) { __result = CustomSongs.EncryptedFalse(); return false; }
            bool locked = false;
            if (SongOwnership.Prefix(CustomSongs.ReadSongIdCached(__instance), SongOwnershipStore.Lock, ref locked, out __state)) return true;
            __result = CustomSongs.EncryptedOf(locked);
            return false;
        }
        catch (Exception e)
        {
            HookGuard.Fail(Hook, e);
            __state = null;
            return true;
        }
    }

    private static void Postfix(Il2CppPlatinaLab.Common.EncryptedPrimitives.EncryptedBool __result, string __state)
    {
        if (__state == null || HookGuard.IsDisabled(Hook)) return;
        try { SongOwnership.Record(__state, SongOwnershipStore.Lock, CustomSongs.DecodeEncrypted(__result)); }
        catch (Exception e) { HookGuard.Fail(Hook, e); }
    }
}

[HarmonyPatch(typeof(SongInfo), nameof(SongInfo.IsNewSongMarkTarget))]
internal static class SongInfo_IsNewSongMarkTarget_Patch
{
    /// <summary>커스텀 곡과 오프라인 모드의 공식 곡은 NEW 표시 정보(서버)가 없다 → false. 온라인 공식 곡은 원본.</summary>
    private static bool Prefix(SongInfo __instance, ref Il2CppPlatinaLab.Common.EncryptedPrimitives.EncryptedBool __result)
    {
        if (HookGuard.IsDisabled("EncryptedBool")) return true;
        if (!Offline.Enabled && !CustomSongs.IsCustomSong(__instance)) return true;
        try { __result = CustomSongs.EncryptedFalse(); }
        catch (Exception e)
        {
            HookGuard.Fail("EncryptedBool", e);
            return true;
        }
        return false;
    }
}

/// <summary>
/// 카테고리 UI(탭 버튼, 선곡 화면 상단 라벨 등)는 SongCategoryResourceCollection.GetDataSet(key)의 리소스 세트로 그려진다.
/// custom은 세트가 없어 상단 라벨이 흰 판(스프라이트 없는 Image)으로 나왔다(실측).
/// ALL 카테고리 세트에서 배경/색을 빌리고 이름 칸들은 RenderType.Text + DisplayedName "CUSTOM"으로 만든 세트를 돌려줘
/// 게임 자체 경로로 다른 카테고리와 같은 모양으로 그리게 한다.
/// </summary>
[HarmonyPatch(typeof(SongCategoryResourceCollection), nameof(SongCategoryResourceCollection.GetDataSet))]
internal static class SongCategoryResourceCollection_GetDataSet_Patch
{
    private const string Hook = "SongCategoryResourceCollection.GetDataSet";
    private const string DonorKey = "all";
    private const string DisplayedName = "CUSTOM";
    private static SongCategoryResourceDataSet s_dataSet;

    private static void Postfix(SongCategoryResourceCollection __instance, string categoryKey, ref SongCategoryResourceDataSet __result)
    {
        // 카테고리 UI를 그릴 때마다 모든 카테고리 키로 불린다. 문자열 비교(관리 코드)를 먼저 해서
        // custom이 아닌 대부분의 호출은 Unity Object의 == null(IL2CPP 호출) 없이 끝낸다.
        if (categoryKey != CustomSongs.CategoryTag || __instance is null || HookGuard.IsDisabled(Hook)) return;
        try
        {
            if (s_dataSet is null || s_dataSet.WasCollected || s_dataSet == null)
                s_dataSet = CreateOrFallback(__instance);
            if (s_dataSet != null) __result = s_dataSet;
        }
        catch (Exception e)
        {
            if (!HookGuard.Fail(Hook, e)) Core.Log.Error($"[CustomSongs] custom 카테고리 리소스 생성 실패: {e}");
        }
    }

    private const string CreateDirectHook = "SongCategoryResourceCollection.CreateDirect";

    private static SongCategoryResourceDataSet CreateOrFallback(SongCategoryResourceCollection collection)
    {
        if (!HookGuard.IsDisabled(CreateDirectHook))
        {
            try
            {
                return Create(collection);
            }
            catch (Exception e)
            {
                HookGuard.Fail(CreateDirectHook, e);
            }
        }
        return CreateFallback(collection);
    }

    // Create 실패 시 GetDataSet 훅 전체가 아니라 CreateDirect만 꺼서 리플렉션 폴백(CreateFallback)에 도달하게 한다.
    [MethodImpl(MethodImplOptions.NoInlining), Guarded(CreateDirectHook)]
    private static SongCategoryResourceDataSet Create(SongCategoryResourceCollection collection)
    {
        var donor = collection.GetDataSet(DonorKey);
        if (donor == null && collection._categoryResourceDataSet?.Length > 0) donor = collection._categoryResourceDataSet[0];

        var ds = UnityEngine.ScriptableObject.CreateInstance(Il2CppType.Of<SongCategoryResourceDataSet>()).Cast<SongCategoryResourceDataSet>();
        ds.hideFlags = UnityEngine.HideFlags.DontUnloadUnusedAsset;
        ds._categoryKey = CustomSongs.CategoryTag;
        ds._displayedName = DisplayedName;
        if (donor != null)
        {
            ds._carouselBackground = donor._carouselBackground;
            ds._detailFocusBackground = donor._detailFocusBackground;
            ds._categoryColor = donor._categoryColor;
        }
        ds._songBanner = TextResource();
        ds._smallLabel = TextResource();
        ds._booth = TextResource();
        ds._eyeCatchV1 = TextResource();
        ds._detailBanner = TextResource();
        Core.Log.Msg($"[CustomSongs] custom 카테고리 리소스 세트 생성 (배경/색 차용: {donor?._categoryKey ?? "없음"})");
        return ds;
    }

    // 폴백 경로도 게임 타입을 직접 쓴다. 보고용 이름으로 [Guarded]를 달아 시작 시 사전 JIT로 점검만 한다 (IsDisabled로 끄지 않음).
    [MethodImpl(MethodImplOptions.NoInlining), Guarded("SongCategoryResourceCollection.CreateFallback")]
    private static SongCategoryResourceDataSet CreateFallback(SongCategoryResourceCollection collection)
    {
        var donor = collection.GetDataSet(DonorKey);
        var ds = UnityEngine.ScriptableObject.CreateInstance(Il2CppType.Of<SongCategoryResourceDataSet>()).Cast<SongCategoryResourceDataSet>();
        ds.hideFlags = UnityEngine.HideFlags.DontUnloadUnusedAsset;
        UpdateResilience.SafeSetFieldOrProperty(ds, "categoryKey", CustomSongs.CategoryTag);
        UpdateResilience.SafeSetFieldOrProperty(ds, "displayedName", DisplayedName);
        if (donor != null)
        {
            // 이름을 호출부에 문자열 상수로 두어야 하네스 [Reflect]가 정적 타입과 함께 검증한다 (배열 순회는 검증 불가)
            CopyMember(donor, ds, "carouselBackground");
            CopyMember(donor, ds, "detailFocusBackground");
            CopyMember(donor, ds, "categoryColor");
        }
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var resType = typeof(CategoryResource);
        foreach (var p in typeof(SongCategoryResourceDataSet).GetProperties(flags))
        {
            if (p.CanWrite && p.PropertyType == resType)
                try { p.SetValue(ds, TextResource()); } catch { }
        }
        foreach (var f in typeof(SongCategoryResourceDataSet).GetFields(flags))
        {
            if (f.FieldType == resType)
                try { f.SetValue(ds, TextResource()); } catch { }
        }
        Core.Log.Msg("[CustomSongs] custom 카테고리 리소스 세트 리플렉션 폴백 생성");
        return ds;
    }

    /// <summary>기증 세트의 멤버를 이름으로 복사 (하네스 [Reflect]는 이 메서드를 이름 전달 래퍼로 인식해 호출부 문자열을 검증한다).</summary>
    private static void CopyMember(object donor, object target, string memberName)
    {
        var v = UpdateResilience.SafeGetFieldOrProperty<object>(donor, memberName);
        if (v != null) UpdateResilience.SafeSetFieldOrProperty(target, memberName, v);
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("SongCategoryResourceCollection.TextResource")]
    private static CategoryResource TextResource()
    {
        var r = new CategoryResource();
        try { SetTextRenderTypeDirect(r); }
        catch { UpdateResilience.SafeSetFieldOrProperty(r, "resourceRenderType", CategoryResource.RenderType.Text); }
        return r;
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("SongCategoryResourceCollection.SetTextRenderTypeDirect")]
    private static void SetTextRenderTypeDirect(CategoryResource r) =>
        r._resourceRenderType = CategoryResource.RenderType.Text;
}

/// <summary>
/// 게임은 모든 노트가 처리되면 PatternPlayer.DetermineRunning()에서 _isLastNoteProcessingTriggered(+0x60)를 세우고
/// EventQueue에 클리어 이벤트(3=PERFECT / 4=FULL COMBO / 5=CLEAR, GameAssembly 0x2CF5381~0x2CF53AC 실측)를 넣어
/// 곧바로 클리어 연출(PERFECT DECODE)로 넘어간다. 1노트 커스텀 차트는 첫 노트 직후에 끝나버리므로,
/// DetermineRunning 동안만 플래그를 true로 세워 그 블록을 건너뛰게 하고, 곡 끝(차트 길이 - 여유)에 도달하면 막지 않는다.
/// DetermineRunning은 입력 처리 스레드에서도 불리므로 Update에 걸면 막히지 않는다(실측).
/// </summary>
[HarmonyPatch(typeof(Il2CppPlatinaLab.Main.InGame.PatternPlayer), nameof(Il2CppPlatinaLab.Main.InGame.PatternPlayer.DetermineRunning))]
internal static class PatternPlayer_DetermineRunning_HoldEnd_Patch
{
    private const string Hook = "PatternPlayer.DetermineRunning";
    private const int EndMarginMicroSec = 3_000_000;

    /// <summary>
    /// 곡(PatternPlayer)마다 한 번만 정하는 값: 커스텀 패턴인지, 보류를 풀 시각.
    /// 예전에는 매 프레임(+ 입력 스레드) PlayPattern 게터(runtime invoke)·PatternIndex·_patternData·DurationMicroSec를 다시 읽었다.
    /// 메인·입력 두 스레드가 읽으므로 한 객체로 바꿔 끼운다. 새 곡 시작(RequestGameStart)마다 비운다.
    /// </summary>
    private sealed class HoldState
    {
        public readonly IntPtr Player;
        public readonly bool Custom;
        public readonly long EndAt;
        public HoldState(IntPtr player, bool custom, long endAt) { Player = player; Custom = custom; EndAt = endAt; }
    }
    private static volatile HoldState s_state;

    public static void ResetCache() => s_state = null;

    private static void Prefix(Il2CppPlatinaLab.Main.InGame.PatternPlayer __instance, out bool __state)
    {
        __state = false;
        // 매 프레임 + 입력 스레드에서 불린다. 커스텀 곡을 시작하지 않았으면 게임 객체를 건드리지 않는다 (일반 곡 빠른 경로).
        if (CustomSongs.Current == null || __instance == null || HookGuard.IsDisabled(Hook)) return;
        try { __state = TryHold(__instance); }
        catch (Exception e) { HookGuard.Fail(Hook, e); }
    }

    // Harmony는 정상 실행 때도 Postfix와 Finalizer를 모두 부르므로 Finalizer 하나로만 플래그를 복원해 IL2CPP 호출을 절반으로 줄인다.
    private static void Finalizer(Il2CppPlatinaLab.Main.InGame.PatternPlayer __instance, bool __state) => Release(__instance, __state);

    private static void Release(Il2CppPlatinaLab.Main.InGame.PatternPlayer player, bool held)
    {
        if (!held || player is null) return; // 매 프레임 경로: Unity == null(IL2CPP 호출) 대신 참조 비교
        try { ClearFlag(player); }
        catch (Exception e) { HookGuard.Fail(Hook, e); }
    }

    // 게임 필드 직접 접근은 별도 메서드로 둔다 (HookGuard 설명 참고)
    [MethodImpl(MethodImplOptions.NoInlining), Guarded("PatternPlayer.DetermineRunning")]
    private static bool TryHold(Il2CppPlatinaLab.Main.InGame.PatternPlayer player)
    {
        var ptr = player.Pointer;
        var state = s_state;
        if (state == null || state.Player != ptr)
        {
            state = CreateState(player, ptr);
            if (state == null) return false; // 차트가 아직 안 들어옴 → 다음 호출에서 다시 확인
            s_state = state;
        }
        if (!state.Custom || player._isLastNoteProcessingTriggered) return false;
        if (player.CurrentTimeMicroseconds >= state.EndAt) return false;
        player._isLastNoteProcessingTriggered = true;
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("PatternPlayer.DetermineRunning")]
    private static HoldState CreateState(Il2CppPlatinaLab.Main.InGame.PatternPlayer player, IntPtr ptr)
    {
        var pattern = player.PlayPattern;
        if (pattern is null) return null; // 패턴이 아직 안 정해짐 → "일반 곡"으로 굳히지 않고 다음 호출에서 다시 확인
        if (!CustomSongs.IsCustom(pattern)) return new HoldState(ptr, false, 0);
        var data = player._patternData;
        if (data == null) return null;
        int duration = data.DurationMicroSec;
        long endAt = (long)duration - EndMarginMicroSec;
        Core.Log.Msg($"[CustomSongs] 클리어 연출 보류: {endAt / 1_000_000.0:F1}s까지 (차트 길이 {duration / 1_000_000.0:F1}s)");
        return new HoldState(ptr, true, endAt);
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("PatternPlayer.DetermineRunning")]
    private static void ClearFlag(Il2CppPlatinaLab.Main.InGame.PatternPlayer player) => player._isLastNoteProcessingTriggered = false;
}

[HarmonyPatch(typeof(CategoryCacheFactory), nameof(CategoryCacheFactory.CreateAll))]
internal static class CategoryCacheFactory_CreateAll_Patch
{
    private const string Hook = "CategoryCacheFactory.CreateAll";

    private static void Postfix(Il2CppSystem.Collections.Generic.List<AbstractPreFilteredCache> __result)
    {
        // 필수 패치가 빠져 주입을 껐으면 빈 카테고리도 만들지 않는다
        if (__result == null || !ModHealth.CustomSongsAllowed || HookGuard.IsDisabled(Hook)) return;
        CustomSongs.InjectGuarded();
        if (!ModHealth.CustomSongsAllowed) return;
        try { AddCategory(__result); }
        catch (Exception e)
        {
            if (!HookGuard.Fail(Hook, e)) Core.Log.Error($"[CustomSongs] 카테고리 추가 실패: {e}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("CategoryCacheFactory.CreateAll")]
    private static void AddCategory(Il2CppSystem.Collections.Generic.List<AbstractPreFilteredCache> list)
    {
        foreach (var c in list)
            if (c != null && c.Key == CustomSongs.CategoryTag) return;
        var cache = new TagCategoryCache(CustomSongs.CategoryTag, CustomSongs.CategoryTag);
        list.Add(cache);
        Core.Log.Msg($"[CustomSongs] 카테고리 '{CustomSongs.CategoryTag}' 추가 (총 {list.Count}개)");
        var keys = new List<string>();
        foreach (var c in list)
            if (c != null) keys.Add($"{c.Key}({c.GetType().Name}:{c.SongCategoryKey})");
        Core.Log.Msg($"[CustomSongs] 카테고리 목록: {string.Join(", ", keys)}");
    }
}
