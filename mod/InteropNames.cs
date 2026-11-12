using System.Reflection;
using System.Text.RegularExpressions;

namespace PLATiNALAB.Mod;

/// <summary>
/// 컴파일러가 만든 타입·메서드(람다 캐시, 클로저, async/iterator 상태 머신, 로컬 함수, 익명 타입)를 가려낸다.
/// 런타임에 메서드를 이름으로 찾아 패치할 때(AddressableKeyRedirect) 이런 멤버를 실제 구현체로 오인하지 않게 한다.
///
/// Il2CppInterop은 C#에서 쓸 수 없는 이름을 바꾼다(실측, Unity.Addressables / Il2CppPlatinaLab.Main 인터롭):
///   &lt;&gt;c → __c,  &lt;&gt;c__114`1 → __c__114`1,  &lt;&gt;c__DisplayClass102_0 → __c__DisplayClass102_0,
///   &lt;Start&gt;d__15 → _Start_d__15,  &lt;&gt;f__AnonymousType0`2 → __f__AnonymousType0`2,
///   &lt;&lt;BuildItems&gt;b__17_0&gt;d → __BuildItems_b__17_0_d (async 람다의 상태 머신),
///   일부 async 로컬 함수 상태 머신 → ValueTypeCompilerGeneratedNPrivateSealed…Unique (인터롭이 이름을 새로 합성),
///   람다 본문 &lt;get_AllLocations&gt;b__8_0 → _get_AllLocations_b__8_0.
/// 이름이 바뀐 타입에는 [ObfuscatedName("원래 전체 이름")]이 붙으므로 원래 이름의 '&lt;'를 1차 기준으로 쓰고,
/// 속성이 없거나 형식이 바뀐 경우를 대비해 바뀐 이름 패턴을 2차 기준으로 쓴다.
/// 이 클래스는 인터롭 속성 타입을 직접 참조하지 않는다(이름 비교만) → 속성이 사라져도 JIT 단계에서 실패하지 않는다.
/// </summary>
internal static class InteropNames
{
    private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    /// <summary>인터롭이 바꾼 컴파일러 생성 타입 이름 (중첩 타입 이름 하나 기준).</summary>
    private static readonly Regex[] s_typePatterns =
    {
        new(@"^__c(__\d+)?(`\d+)?$", Opts),                    // <>c, <>c__N`1 (람다 캐시)
        new(@"^__c__DisplayClass\d+_\d+(`\d+)?$", Opts),        // <>c__DisplayClassN_M (클로저)
        new(@"^_.+_d__\d+(`\d+)?$", Opts),                      // <Method>d__N (async/iterator 상태 머신)
        new(@"^__.+_[bg]__.+_d(`\d+)?$", Opts),                 // <<Method>b__N_M>d (async 람다/로컬 함수의 상태 머신)
        new(@"^(ValueType|Object|Class)CompilerGenerated", Opts), // 인터롭이 이름을 새로 합성한 컴파일러 생성 타입
                                                                  // (실측: <<Awake>g__PreloadLazyObjects|14_0>d → ValueTypeCompilerGeneratedNPrivateSealed…Unique)
        new(@"^__f__AnonymousType\d*(`\d+)?$", Opts),           // <>f__AnonymousTypeN (익명 타입)
        new(@"^__o__\d+", Opts),                                // <>o__N (dynamic 호출 사이트)
        new(@"^__O(`\d+)?$", Opts),                             // <>O (정적 메서드 그룹 대리자 캐시)
        new(@"^__z__", Opts),                                   // <>z__ReadOnly… (컬렉션 식 지원 타입)
        new(@"_e__FixedBuffer$", Opts),                         // <field>e__FixedBuffer (fixed 배열 필드)
        new(@"PrivateImplementationDetails", Opts),             // <PrivateImplementationDetails>
        new(@"^__StaticArrayInitTypeSize", Opts),
        new(@"^MethodInfoStoreGeneric_", Opts),                 // 인터롭이 만든 제네릭 메서드 정보 저장소
    };

    /// <summary>인터롭이 바꾼 컴파일러 생성 메서드 이름.</summary>
    private static readonly Regex[] s_methodPatterns =
    {
        new(@"_b__\d+(_\d+)?$", Opts),                          // <Method>b__N_M (람다 본문)
        new(@"_g__.+_\d+(_\d+)?$", Opts),                       // <Method>g__Local|N_M (로컬 함수)
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> s_typeCache = new();

    /// <summary>[ObfuscatedName]/[OriginalName]에 남은 원래 이름. 없으면 null.</summary>
    public static string OriginalName(MemberInfo member)
    {
        if (member == null) return null;
        try
        {
            foreach (var data in member.GetCustomAttributesData())
            {
                var attrName = data.AttributeType.Name;
                if (attrName != "ObfuscatedNameAttribute" && attrName != "OriginalNameAttribute") continue;
                // 생성자 인자 중 마지막 문자열이 이름 (ObfuscatedName(name), OriginalName(asm, ns, name) 형태 모두 대응)
                for (int i = data.ConstructorArguments.Count - 1; i >= 0; i--)
                    if (data.ConstructorArguments[i].Value is string s && s.Length > 0) return s;
            }
        }
        catch { /* 속성 메타데이터를 못 읽으면 이름 패턴으로만 판단 */ }
        return null;
    }

    /// <summary>타입 자신 또는 감싸는 타입 중 하나라도 컴파일러 생성이면 true.</summary>
    public static bool IsCompilerGenerated(Type type)
    {
        if (type == null) return false;
        if (s_typeCache.TryGetValue(type, out var cached)) return cached;
        bool result = false;
        for (var cur = type; cur != null && !result; cur = cur.DeclaringType)
            result = IsCompilerGeneratedSelf(cur);
        s_typeCache[type] = result;
        return result;
    }

    /// <summary>메서드 자신이 컴파일러 생성(람다 본문, 로컬 함수)이거나 컴파일러 생성 타입에 속하면 true.</summary>
    public static bool IsCompilerGenerated(MethodBase method)
    {
        if (method == null) return false;
        if (IsCompilerGenerated(method.DeclaringType)) return true;
        var name = method.Name;
        if (name.IndexOf('<') >= 0) return true;
        if (HasCompilerGeneratedAttribute(method)) return true;
        var original = OriginalName(method);
        if (original != null && original.IndexOf('<') >= 0) return true;
        foreach (var p in s_methodPatterns)
            if (p.IsMatch(name)) return true;
        return false;
    }

    /// <summary>
    /// IL2CPP 쪽에서 구현이 없는 메서드(인터페이스·추상 메서드의 인터롭 프록시)면 true.
    /// Il2CppInterop은 IL2CPP 인터페이스를 C# 인터페이스가 아니라 Il2CppObjectBase를 직접 상속하는 일반 클래스로 만들고
    /// 메서드를 가상 호출 래퍼로 채운다 (실측: UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator).
    /// 그래서 C# 리플렉션의 IsInterface/IsAbstract로는 걸러지지 않는다. 두 가지 흔적으로 판단한다:
    ///   1. 실제 IL2CPP 클래스 프록시는 Il2CppSystem.Object를 상속한다. 인터페이스 프록시는 상속하지 않는다.
    ///   2. 네이티브 메서드 정보 필드 이름(NativeMethodInfoPtr_{메서드}_..._Abstract_...)에 Abstract가 들어 있다.
    /// </summary>
    public static bool IsIl2CppInterfaceOrAbstract(MethodInfo method)
    {
        var type = method?.DeclaringType;
        if (type == null) return false;
        if (type.IsInterface || method.IsAbstract) return true;
        if (!DerivesFromIl2CppObject(type)) return true;
        try
        {
            int total = 0, abstracts = 0;
            var prefix = $"NativeMethodInfoPtr_{method.Name}_";
            foreach (var f in type.GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (!f.Name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                total++;
                if (f.Name.Contains("_Abstract_", StringComparison.Ordinal)) abstracts++;
            }
            // 같은 이름의 오버로드가 섞여 있으면 어느 쪽인지 모르므로 전부 Abstract일 때만 거른다
            return total > 0 && abstracts == total;
        }
        catch { return false; }
    }

    private static bool DerivesFromIl2CppObject(Type type)
    {
        for (var cur = type; cur != null; cur = cur.BaseType)
            if (cur.FullName == "Il2CppSystem.Object") return true;
        return false;
    }

    private static bool IsCompilerGeneratedSelf(Type t)
    {
        var name = t.Name;
        if (name.IndexOf('<') >= 0) return true;                // 인터롭을 거치지 않은 관리 타입
        if (HasCompilerGeneratedAttribute(t)) return true;

        var original = OriginalName(t);
        if (original != null)
        {
            // "Ns.Outer+<>c" → 마지막 중첩 이름만 본다 (바깥 타입의 제네릭 인자 등에 든 '<'와 구분)
            int plus = original.LastIndexOf('+');
            var last = plus >= 0 ? original[(plus + 1)..] : original;
            if (last.IndexOf('<') >= 0) return true;
        }

        foreach (var p in s_typePatterns)
            if (p.IsMatch(name)) return true;
        return false;
    }

    private static bool HasCompilerGeneratedAttribute(MemberInfo member)
    {
        try { return member.IsDefined(typeof(CompilerGeneratedAttribute), false); }
        catch { return false; }
    }

    /// <summary>ReflectionTypeLoadException이 나도 읽힌 타입은 돌려준다 (인터롭 일부 타입 로드 실패 대비).</summary>
    public static Type[] SafeGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException e)
        {
            var loaded = new List<Type>();
            foreach (var t in e.Types) if (t != null) loaded.Add(t);
            Core.Log.Warning($"[InteropNames] {asm.GetName().Name}: 타입 {e.Types.Length - loaded.Count}개 로드 실패, 나머지 {loaded.Count}개로 진행");
            return loaded.ToArray();
        }
        catch (Exception e)
        {
            Core.Log.Warning($"[InteropNames] {asm.GetName().Name} 타입 목록 읽기 실패: {e.Message}");
            return Array.Empty<Type>();
        }
    }
}
