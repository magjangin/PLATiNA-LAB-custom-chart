using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;

namespace PLATiNALAB.Mod;

/// <summary>
/// 커스텀 곡의 리소스(차트 .ppp 등) 주소는 게임이 SongInfo.Id로 만들기 때문에 "hwa_001" 등이 들어간 키가 된다.
/// Addressables의 IResourceLocator.Locate(object key, ...) 구현체들에 Prefix를 걸어
/// 문자열 키 속 커스텀 곡 ID를 기반 곡 ID로 바꿔 원본 리소스를 찾게 한다.
/// PlayDirector.LoadGame은 LoadTargetPattern 안에 인라인돼 Harmony로 잡히지 않으므로(실측) 이 지점을 쓴다.
/// Locate는 인터페이스 경유 호출이라 인라인되지 않는다.
///
/// 구현체 탐색 (Il2CppInterop은 IL2CPP 인터페이스를 C# 인터페이스로 남기지 않으므로 이름/시그니처로 찾는다):
///   1. 이름에 "Addressables"가 든 어셈블리 (업데이트로 어셈블리 이름이 조금 바뀌어도 찾도록)
///   2. 컴파일러 생성 타입·메서드(람다 캐시 __c, 클로저 __c__DisplayClass, 상태 머신 _X_d__N 등)와
///      인터페이스 프록시(인터롭은 IResourceLocator를 일반 클래스로 만든다)는 InteropNames로 제외
///   3. 엄격 조건: bool Locate(Il2CppSystem.Object key, Il2CppSystem.Type, out IList) — 실측 3건
///      (DynamicResourceLocator, ResourceLocationMap, ContentCatalogData+ResourceLocator)
///   4. 엄격 조건으로 하나도 못 찾으면 완화 조건(첫 인자가 Il2CppSystem.Object인 Locate)으로 다시 찾고 경고
/// 첫 인자 타입을 탐색 단계에서 확인하므로 Prefix는 인자 이름 대신 위치(__0)로 받는다 → 게임이 인자 이름을 바꿔도 패치가 유지된다.
/// </summary>
internal static class AddressableKeyRedirect
{
    private static readonly object s_logLock = new();
    private static readonly HashSet<string> s_loggedRedirects = new();

    public static int PatchedCount { get; private set; }

    public static void Apply(HarmonyLib.Harmony harmony)
    {
        // 인터롭 어셈블리는 지연 로드되므로 OnInitializeMelon 시점엔 명시적으로 로드해야 한다
        try { Assembly.Load("Unity.Addressables"); }
        catch (Exception e) { Core.Log.Warning($"[KeyRedirect] Unity.Addressables 로드 실패 (다른 이름의 어셈블리에서 계속 찾음): {e.Message}"); }

        var candidates = FindLocateMethods(strict: true, out int skippedGenerated, out var rejected);
        if (candidates.Count == 0)
        {
            Core.Log.Warning($"[KeyRedirect] 정확한 Locate 시그니처를 찾지 못해 완화 조건으로 다시 찾습니다. 거른 후보: {string.Join(", ", rejected)}");
            candidates = FindLocateMethods(strict: false, out skippedGenerated, out _);
        }

        var prefix = new HarmonyMethod(typeof(AddressableKeyRedirect), nameof(Prefix));
        int count = 0;
        foreach (var m in candidates)
        {
            try
            {
                harmony.Patch(m, prefix);
                count++;
                Core.Log.Msg($"[KeyRedirect] 패치 성공: {m.DeclaringType?.FullName}.Locate({Describe(m)})");
            }
            catch (Exception e)
            {
                Core.Log.Warning($"[KeyRedirect] {m.DeclaringType?.FullName}.Locate 패치 실패: {e.Message}");
            }
        }
        PatchedCount = count;
        if (skippedGenerated > 0) Core.Log.Msg($"[KeyRedirect] 컴파일러 생성 타입/메서드의 Locate {skippedGenerated}건 제외");
        if (count == 0) Core.Log.Warning("[KeyRedirect] IResourceLocator 구현체를 찾지 못함");
    }

    private static List<MethodInfo> FindLocateMethods(bool strict, out int skippedGenerated, out List<string> rejected)
    {
        var found = new List<MethodInfo>();
        var seen = new HashSet<MethodInfo>();
        rejected = new List<string>();
        skippedGenerated = 0;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            string asmName;
            try { asmName = asm.GetName().Name; } catch { continue; }
            if (asmName == null || asmName.IndexOf("Addressables", StringComparison.OrdinalIgnoreCase) < 0) continue;

            foreach (var t in InteropNames.SafeGetTypes(asm))
            {
                if (t == null || t.IsInterface || t.ContainsGenericParameters) continue;
                MethodInfo[] methods;
                try { methods = t.GetMethods(flags); } catch { continue; }

                foreach (var m in methods)
                {
                    if (m.Name != "Locate" || m.IsAbstract || m.ContainsGenericParameters) continue;
                    // 람다 캐시·클로저·상태 머신 안의 메서드는 구현체가 아니다
                    if (InteropNames.IsCompilerGenerated(m)) { skippedGenerated++; continue; }
                    // IResourceLocator 자체도 인터롭에선 일반 클래스로 나온다 → 구현 없는 인터페이스 프록시는 제외
                    if (InteropNames.IsIl2CppInterfaceOrAbstract(m)) continue;
                    if (!IsLocateSignature(m, strict))
                    {
                        rejected.Add($"{t.Name}.Locate({Describe(m)})");
                        continue;
                    }
                    if (seen.Add(m)) found.Add(m);
                }
            }
        }
        return found;
    }

    /// <summary>bool Locate(Il2CppSystem.Object key, Il2CppSystem.Type type, out IList&lt;IResourceLocation&gt; locations).</summary>
    private static bool IsLocateSignature(MethodInfo m, bool strict)
    {
        ParameterInfo[] ps;
        try { ps = m.GetParameters(); } catch { return false; }
        if (ps.Length < 1 || ps[0].ParameterType != typeof(Il2CppSystem.Object)) return false;
        if (!strict) return ps.Length <= 4;
        return m.ReturnType == typeof(bool)
               && ps.Length == 3
               && ps[1].ParameterType.Name == "Type"
               && ps[2].ParameterType.IsByRef;
    }

    private static string Describe(MethodInfo m)
    {
        try { return string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)); }
        catch { return "?"; }
    }

    /// <summary>
    /// __0 = key. 첫 인자가 Il2CppSystem.Object인 메서드만 패치하므로 위치로 받아도 타입이 맞는다.
    /// 게임의 모든 Addressables 로드(효과음·UI·차트 등)가 지나가므로, 커스텀 곡 키가 아니면 할당 없이 끝낸다:
    /// 관리 문자열로 바꾸기 전에 IL2CPP 문자열의 UTF-16 버퍼에서 접두사(hwa_)를 직접 찾는다.
    /// </summary>
    private static void Prefix(ref Il2CppSystem.Object __0)
    {
        var baseId = CustomSongs.s_baseSongId;
        if (baseId == null || !CustomSongs.Injected) return;
        var key = __0;
        if (key == null) return;
        try
        {
            var ptr = key.Pointer;
            if (ptr == IntPtr.Zero) return;
            var classPtr = IL2CPP.il2cpp_object_get_class(ptr);
            if (classPtr == IntPtr.Zero || classPtr != Il2CppClassPointerStore<string>.NativeClassPtr) return;
            string s = null;
            if (s_useNativeScan)
            {
                if (s_selfChecks < SelfCheckCount)
                {
                    // 처음 몇 건은 네이티브 검사와 관리 문자열 검사를 둘 다 해서 결과를 맞춰 본다.
                    // 다르면(인터롭/IL2CPP 문자열 배치 변경 등) 이후로는 관리 문자열 경로만 쓴다.
                    s = IL2CPP.Il2CppStringToManaged(ptr);
                    bool expected = s != null && s.Contains(CustomSongs.IdPrefix, StringComparison.Ordinal);
                    if (NativeContains(ptr, CustomSongs.IdPrefix) != expected)
                    {
                        s_useNativeScan = false;
                        Core.Log.Warning($"[KeyRedirect] 네이티브 문자열 검사 결과가 '{s}'에서 다름 → 관리 문자열 검사로 전환");
                    }
                    else if (Interlocked.Increment(ref s_selfChecks) == SelfCheckCount)
                        Core.Log.Msg($"[KeyRedirect] 네이티브 문자열 검사 자체 검증 {SelfCheckCount}건 일치");
                }
                else if (!NativeContains(ptr, CustomSongs.IdPrefix)) return;
            }
            s ??= IL2CPP.Il2CppStringToManaged(ptr);
            if (s == null || !s.Contains(CustomSongs.IdPrefix, StringComparison.Ordinal)) return;
            var song = CustomSongs.FindIdIn(s);
            if (song == null) return;
            var redirected = s.Replace(song.Id, baseId, StringComparison.Ordinal);
            var newPtr = IL2CPP.ManagedStringToIl2Cpp(redirected);
            if (newPtr == IntPtr.Zero) return;
            __0 = new Il2CppSystem.Object(newPtr);
            ModHealth.Mark("Locate");
            bool first;
            lock (s_logLock) first = s_loggedRedirects.Add(s);
            if (first) Core.Log.Msg($"[KeyRedirect] '{s}' -> '{redirected}'");
        }
        catch (Exception e) { HookGuard.Fail("IResourceLocator.Locate", e); }
    }

    private const int SelfCheckCount = 32;
    private static volatile bool s_useNativeScan = true;
    private static int s_selfChecks;

    /// <summary>IL2CPP 문자열(UTF-16)에 needle이 있는지 관리 문자열을 만들지 않고 확인한다.</summary>
    private static unsafe bool NativeContains(IntPtr il2cppString, string needle)
    {
        int length = IL2CPP.il2cpp_string_length(il2cppString);
        if (length < needle.Length) return false;
        char* chars = IL2CPP.il2cpp_string_chars(il2cppString);
        if (chars == null) return false;
        return new ReadOnlySpan<char>(chars, length).IndexOf(needle.AsSpan(), StringComparison.Ordinal) >= 0;
    }
}
