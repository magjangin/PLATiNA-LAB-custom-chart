using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace PLATiNALAB.Mod;

/// <summary>
/// 게임 버전 업데이트 및 컴파일러 백킹 필드/시그니처 변경에 대응하기 위한 업데이트 내성(Update Resilience) 유틸리티.
/// </summary>
internal static class UpdateResilience
{
    private const BindingFlags DeclaredInstanceFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    // 판정 훅(게임 루프 스레드)과 메인 스레드가 함께 쓴다. 못 찾은 결과(null)도 저장해 매번 다시 탐색하지 않는다.
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo> s_propertyCache = new();
    private static readonly ConcurrentDictionary<(Type, string, bool), MemberInfo> s_backingCache = new();

    /// <summary>
    /// 프로퍼티 또는 백킹 멤버를 찾아 안전하게 값을 설정한다.
    /// 1. 상속 계층을 포함한 public/non-public 프로퍼티 setter 시도
    /// 2. 백킹 멤버 이름 후보(_X_k__BackingField, &lt;X&gt;k__BackingField, _x, _X, m_X, X)를 필드 → 프로퍼티 순으로 검색.
    ///    Il2CppInterop은 IL2CPP 필드를 관리 필드가 아니라 같은 이름의 <b>프로퍼티</b>로 노출하므로(예: SongInfo._Id_k__BackingField),
    ///    필드만 찾으면 인터롭 타입에서는 백킹 폴백이 한 번도 성공하지 못한다 (11차 감사에서 발견).
    /// </summary>
    public static bool SafeSetFieldOrProperty(object target, string memberName, object value)
    {
        if (target == null || string.IsNullOrEmpty(memberName)) return false;
        var type = target.GetType();

        var prop = FindProperty(type, memberName);
        if (prop != null && prop.CanWrite && TrySet(prop, target, value)) return true;

        var backing = FindBacking(type, memberName, write: true);
        return backing != null && TrySet(backing, target, value);
    }

    /// <summary>
    /// 프로퍼티 또는 백킹 멤버에서 값을 안전하게 읽어온다.
    /// </summary>
    public static T SafeGetFieldOrProperty<T>(object target, string memberName, T defaultValue = default)
    {
        if (target == null || string.IsNullOrEmpty(memberName)) return defaultValue;
        var type = target.GetType();

        var prop = FindProperty(type, memberName);
        if (prop != null && prop.CanRead && TryGet(prop, target, out T value)) return value;

        var backing = FindBacking(type, memberName, write: false);
        return backing != null && TryGet(backing, target, out value) ? value : defaultValue;
    }

    /// <summary>
    /// 인스턴스 없이 타입만으로 폴백이 이 이름을 찾을 수 있는지 (SafeSet/SafeGet과 같은 탐색).
    /// tools/interop-harness의 [Reflect] 검증이 호출부의 정적 타입·문자열 이름으로 이 메서드를 불러, 이름 오타나
    /// 업데이트로 사라진 멤버를 게임 실행 없이 잡는다 (폴백은 직접 경로가 깨졌을 때만 돌기 때문에 실기에서는 늦게 드러난다).
    /// </summary>
    internal static bool CanResolve(Type type, string memberName, bool write)
    {
        if (type == null || string.IsNullOrEmpty(memberName)) return false;
        var prop = FindProperty(type, memberName);
        if (prop != null && (write ? prop.CanWrite : prop.CanRead)) return true;
        return FindBacking(type, memberName, write) != null;
    }

    private static bool TrySet(MemberInfo member, object target, object value)
    {
        try
        {
            switch (member)
            {
                case PropertyInfo p: p.SetValue(target, ConvertValue(value, p.PropertyType)); return true;
                case FieldInfo f: f.SetValue(target, ConvertValue(value, f.FieldType)); return true;
            }
        }
        catch { }
        return false;
    }

    private static bool TryGet<T>(MemberInfo member, object target, out T value)
    {
        try
        {
            object raw = member switch
            {
                PropertyInfo p => p.GetValue(target),
                FieldInfo f => f.GetValue(target),
                _ => null,
            };
            return TryCast(raw, out value);
        }
        catch { }
        value = default;
        return false;
    }

    private static object ConvertValue(object value, Type targetType)
    {
        if (value == null || targetType.IsInstanceOfType(value)) return value;
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        try
        {
            if (underlying.IsEnum)
                return value is string s ? Enum.Parse(underlying, s, ignoreCase: true) : Enum.ToObject(underlying, value);
            if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(underlying))
                return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
        }
        catch { }
        return value;
    }

    private static bool TryCast<T>(object raw, out T value)
    {
        if (raw is T direct) { value = direct; return true; }
        if (raw != null)
        {
            var underlying = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            try
            {
                if (underlying.IsEnum)
                {
                    object parsed = raw is string s ? Enum.Parse(underlying, s, ignoreCase: true) : Enum.ToObject(underlying, raw);
                    value = (T)parsed;
                    return true;
                }
                if (raw is IConvertible && typeof(IConvertible).IsAssignableFrom(underlying))
                {
                    value = (T)Convert.ChangeType(raw, underlying, CultureInfo.InvariantCulture);
                    return true;
                }
            }
            catch { }
        }
        value = default;
        return false;
    }

    private static PropertyInfo FindProperty(Type type, string memberName) =>
        s_propertyCache.GetOrAdd((type, memberName), FindPropertyUncached);

    private static PropertyInfo FindPropertyUncached((Type Type, string Name) key)
    {
        for (var cur = key.Type; cur != null && cur != typeof(object); cur = cur.BaseType)
        {
            try
            {
                var prop = cur.GetProperty(key.Name, DeclaredInstanceFlags);
                if (prop != null) return prop;
            }
            catch (AmbiguousMatchException) { return null; } // 인덱서 등 같은 이름이 여럿이면 필드로 넘어간다
        }
        return null;
    }

    /// <summary>
    /// 백킹 멤버 이름 후보를 상속 계층까지 필드 → 프로퍼티 순으로 찾는다. 프로퍼티는 쓰기/읽기 가능한 것만 돌려준다.
    /// 인터롭 타입은 IL2CPP 필드를 프로퍼티로, 순수 관리 타입은 필드로 노출하므로 둘 다 본다.
    /// </summary>
    private static MemberInfo FindBacking(Type type, string memberName, bool write) =>
        s_backingCache.GetOrAdd((type, memberName, write), FindBackingUncached);

    private static MemberInfo FindBackingUncached((Type Type, string Name, bool Write) key)
    {
        var name = key.Name;
        var candidates = new[]
        {
            $"_{name}_k__BackingField",
            $"<{name}>k__BackingField",
            $"_{char.ToLowerInvariant(name[0])}{name[1..]}",
            $"_{name}",
            $"m_{name}",
            name,
        };
        for (var cur = key.Type; cur != null && cur != typeof(object); cur = cur.BaseType)
        {
            foreach (var candidate in candidates)
            {
                var field = cur.GetField(candidate, DeclaredInstanceFlags);
                if (field != null && !(key.Write && field.IsInitOnly)) return field;
                PropertyInfo prop;
                try { prop = cur.GetProperty(candidate, DeclaredInstanceFlags); }
                catch (AmbiguousMatchException) { continue; }
                if (prop != null && (key.Write ? prop.CanWrite : prop.CanRead)) return prop;
            }
        }
        return null;
    }

    /// <summary>
    /// Il2Cpp Action을 호출하는 람다를 만들어 before/원본/after 실행을 각각 try-catch로 감싼다.
    /// 인터롭을 통해 관리 코드로 올라온 예외(Il2CppException 등)만 경고로 남기며, 네이티브 크래시(AV 등)는 막지 못한다.
    /// 클로저가 original 프록시를 참조하긴 하지만, 이 래퍼를 GC 수명 문제의 대책으로 검증한 적은 없다.
    /// </summary>
    public static Il2CppSystem.Action WrapAction(Il2CppSystem.Action original, Action before = null, Action after = null)
    {
        if (original == null && before == null && after == null) return null;

        return (Il2CppSystem.Action)(() =>
        {
            try { before?.Invoke(); } catch (Exception e) { Core.Log.Warning($"[Resilience] Action Before 래퍼 예외: {e.Message}"); }
            try { original?.Invoke(); } catch (Exception e) { Core.Log.Warning($"[Resilience] Action 원본 대리자 실행 예외: {e.Message}"); }
            try { after?.Invoke(); } catch (Exception e) { Core.Log.Warning($"[Resilience] Action After 래퍼 예외: {e.Message}"); }
        });
    }
}
