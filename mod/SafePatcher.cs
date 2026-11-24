using System.Reflection;
using HarmonyLib;
using MelonLoader;

// MelonLoader의 자동 PatchAll을 끈다. 자동 PatchAll은 패치 클래스를 차례로 적용하다가 하나라도 예외가 나면
// (게임 업데이트로 대상 메서드가 사라짐·오버로드가 생겨 모호해짐·인자 이름 변경) 그 뒤 클래스를 전부 건너뛴다.
// 대신 SafePatcher.ApplyAll이 클래스마다 따로 적용해 깨진 훅만 빠지게 한다 (17차 감사, 형제 모드들과 같은 방식).
[assembly: HarmonyDontPatchAll]

namespace PLATiNALAB.Mod;

/// <summary>
/// [HarmonyPatch] 클래스를 하나씩 격리해 적용한다. 한 클래스의 실패가 다른 훅(특히 필수 패치)을 끌고 가지 않는다.
/// 필수 패치가 빠졌는지는 그 뒤 ModHealth.CheckRequiredPatches가 판단해 커스텀 곡 주입을 끈다.
/// </summary>
internal static class SafePatcher
{
    public static int Applied { get; private set; }
    public static IReadOnlyList<string> Failed => s_failed;
    private static readonly List<string> s_failed = new();
    private static bool s_applied;

    public static void ApplyAll(HarmonyLib.Harmony harmony, Assembly assembly)
    {
        if (s_applied || harmony == null || assembly == null) return;
        s_applied = true;
        int applied = 0;
        foreach (var type in InteropNames.SafeGetTypes(assembly))
        {
            if (type == null || !IsPatchClass(type)) continue;
            try
            {
                harmony.CreateClassProcessor(type).Patch();
                applied++;
            }
            catch (Exception e)
            {
                var root = e;
                while (root is TargetInvocationException or TypeInitializationException && root.InnerException != null) root = root.InnerException;
                s_failed.Add(type.FullName);
                Core.Log.Error($"[SafePatcher] {type.FullName} 적용 실패 — 이 훅만 건너뜁니다: {root.GetType().Name}: {root.Message}");
            }
        }
        Applied = applied;
        if (s_failed.Count == 0) Core.Log.Msg($"[SafePatcher] 패치 클래스 {applied}개 모두 적용");
        else Core.Log.Error($"[SafePatcher] 패치 클래스 {applied}개 적용, 실패 {s_failed.Count}개: {string.Join(", ", s_failed)}");
    }

    private static bool IsPatchClass(Type type)
    {
        try { return type.IsDefined(typeof(HarmonyPatch), inherit: false); }
        catch { return false; }
    }
}
