using System.Collections.Concurrent;
using System.Reflection;

namespace PLATiNALAB.Mod;

/// <summary>
/// 일반 곡에도 도는 훅의 예외 격리.
///
/// Il2CppInterop의 native→managed 트램펄린은 훅에서 올라온 예외를 잡아 "During invoking native->managed trampoline"으로
/// 로그하고 원본 대신 기본값을 돌려준다 (Il2CppInterop.HarmonySupport 문자열 실측). 그래서 Prefix 본문이 업데이트로 사라진
/// 멤버를 직접 참조하면(JIT 단계 MissingMemberException) 그 게임 메서드가 커스텀 곡뿐 아니라 일반 곡에서도 망가진다.
///
/// 규칙: 일반 곡에서도 불리는 훅은 Prefix/Postfix 본문에서 게임 멤버를 직접 참조하지 않고,
/// 그 부분을 [MethodImpl(NoInlining)] 별도 메서드로 빼서 try 안에서 호출한다. 실패하면 여기에 보고한다.
///   - 바인딩 실패(MissingMember/TypeLoad/BadImageFormat): 그 훅을 꺼서 게임 원본 동작만 남긴다 (매 호출 예외 반복 방지).
///     필수 훅이면 커스텀 곡 주입도 끈다.
///   - 그 밖의 예외: 훅은 유지하고 같은 예외는 한 번만 경고한다.
/// </summary>
internal static class HookGuard
{
    private static readonly ConcurrentDictionary<string, string> s_disabled = new();
    private static readonly ConcurrentDictionary<string, byte> s_warned = new();

    /// <summary>바인딩 실패로 꺼진 훅이 하나라도 있으면 true (평상시 사전 조회를 건너뛰기 위한 빠른 경로).</summary>
    private static volatile bool s_any;

    public static bool IsDisabled(string hook) => s_any && s_disabled.ContainsKey(hook);

    public static IReadOnlyCollection<string> Disabled => s_disabled.Keys.ToArray();

    /// <summary>훅 예외 보고. 바인딩 실패면 그 훅을 끄고 true를 돌려준다.</summary>
    public static bool Fail(string hook, Exception e, bool required = false)
    {
        var root = Unwrap(e);
        if (IsBindingFailure(root))
        {
            if (s_disabled.TryAdd(hook, root.GetType().Name))
            {
                s_any = true;
                Core.Log.Error($"[HookGuard] {hook} 훅을 끕니다 ({root.GetType().Name}: {root.Message}). " +
                               "게임 업데이트로 멤버가 바뀐 것으로 보입니다 — 게임 원본 동작은 유지되며, 모드를 다시 빌드해야 합니다.");
                if (required) ModHealth.DisableCustomSongs($"{hook} 바인딩 실패");
            }
            return true;
        }
        if (s_warned.TryAdd($"{hook}|{root.GetType().FullName}|{root.Message}", 0))
            Core.Log.Warning($"[HookGuard] {hook} 처리 중 예외 (훅 유지, 같은 예외는 다시 표시하지 않음): {root.GetType().Name}: {root.Message}");
        return false;
    }

    /// <summary>JIT 바인딩 단계의 실패 = 컴파일 시점 인터롭과 런타임 인터롭이 어긋남. 재시도해도 계속 실패한다.</summary>
    public static bool IsBindingFailure(Exception e) =>
        e is MissingMemberException or TypeLoadException or BadImageFormatException;

    /// <summary>
    /// [Guarded] 메서드를 시작 시 미리 JIT해서 바인딩 실패를 첫 호출 전에 찾는다.
    /// 실측(순수 .NET 6, 멤버를 지운 라이브러리로 바꿔치기): 없는 메서드·필드·프로퍼티를 참조하는 메서드는
    /// RuntimeHelpers.PrepareMethod에서 MissingMethodException/MissingFieldException을 던진다.
    /// 실패한 훅은 꺼 두므로 곡을 플레이하다가 처음 알게 되는 일이 없고, 필수 경로면 커스텀 곡 주입을 시작부터 막는다.
    /// </summary>
    public static void Precheck(Assembly assembly)
    {
        int checkedCount = 0, failed = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var type in InteropNames.SafeGetTypes(assembly))
            {
                MethodInfo[] methods;
                try { methods = type.GetMethods(flags); } catch { continue; }
                foreach (var m in methods)
                {
                    GuardedAttribute attr;
                    try { attr = m.GetCustomAttribute<GuardedAttribute>(); } catch { continue; }
                    if (attr == null || m.ContainsGenericParameters) continue;
                    checkedCount++;
                    try { RuntimeHelpers.PrepareMethod(m.MethodHandle); }
                    catch (Exception e)
                    {
                        failed++;
                        Fail(attr.Hook, e, attr.Required);
                    }
                }
            }
        }
        catch (Exception e) { Core.Log.Warning($"[HookGuard] 사전 점검 자체가 실패 (실행 중 감지로 계속): {e.Message}"); }
        if (failed == 0) Core.Log.Msg($"[HookGuard] 훅 바인딩 사전 점검 정상 ({checkedCount}개 메서드, {sw.ElapsedMilliseconds}ms)");
        else Core.Log.Error($"[HookGuard] 훅 바인딩 사전 점검: {checkedCount}개 중 {failed}개 실패 → 해당 훅 꺼짐: {string.Join(", ", Disabled)}");
    }

    private static Exception Unwrap(Exception e)
    {
        while (e is TargetInvocationException or TypeInitializationException && e.InnerException != null) e = e.InnerException;
        return e;
    }
}

/// <summary>
/// 게임 멤버를 직접 참조하는 격리 메서드 표시. HookGuard.Precheck가 시작 시 미리 JIT해서
/// 바인딩이 깨졌으면 <see cref="Hook"/> 이름의 훅을 끈다. <see cref="Required"/>면 커스텀 곡 주입도 끈다.
/// 폴백 경로가 따로 있는 메서드는 Hook에 설명 이름을 넣어 보고만 하게 한다(IsDisabled로 확인하는 곳이 없음).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class GuardedAttribute : Attribute
{
    public string Hook { get; }
    public bool Required { get; }

    public GuardedAttribute(string hook, bool required = false)
    {
        Hook = hook;
        Required = required;
    }
}
