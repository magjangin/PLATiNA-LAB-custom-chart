using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Il2CppInterop.Runtime;

namespace PLATiNALAB.Mod;

/// <summary>
/// 게임 버전 업데이트 및 컴파일러 백킹 필드/시그니처 변경에 대응하기 위한 업데이트 내성(Update Resilience) 유틸리티.
/// </summary>
internal static class UpdateResilience
{
    private const BindingFlags DeclaredInstanceFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    // 판정 훅(게임 루프 스레드)과 메인 스레드가 함께 쓴다. 못 찾은 결과(null)도 저장해 매번 다시 탐색하지 않는다.
    private static readonly ConcurrentDictionary<(Type, string), FieldInfo> s_fieldCache = new();
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo> s_propertyCache = new();

    /// <summary>
    /// 프로퍼티 또는 백킹 필드를 찾아 안전하게 값을 설정한다.
    /// 1. 상속 계층을 포함한 public/non-public 프로퍼티 setter 시도
    /// 2. 컴파일러 백킹 필드 형식들(_<name>_k__BackingField, <name>k__BackingField, _{name}, m_{name}, {name}) 순차 검색
    /// </summary>
    public static bool SafeSetFieldOrProperty(object target, string memberName, object value)
    {
        if (target == null || string.IsNullOrEmpty(memberName)) return false;
        var type = target.GetType();

        var prop = FindProperty(type, memberName);
        if (prop != null && prop.CanWrite)
        {
            try
            {
                prop.SetValue(target, ConvertValue(value, prop.PropertyType));
                return true;
            }
            catch { }
        }

        var field = FindField(type, memberName);
        if (field != null)
        {
            try
            {
                field.SetValue(target, ConvertValue(value, field.FieldType));
                return true;
            }
            catch { }
        }

        return false;
    }

    /// <summary>
    /// 프로퍼티 또는 필드에서 값을 안전하게 읽어온다.
    /// </summary>
    public static T SafeGetFieldOrProperty<T>(object target, string memberName, T defaultValue = default)
    {
        if (target == null || string.IsNullOrEmpty(memberName)) return defaultValue;
        var type = target.GetType();

        var prop = FindProperty(type, memberName);
        if (prop != null && prop.CanRead)
        {
            try
            {
                if (TryCast<T>(prop.GetValue(target), out var tVal)) return tVal;
            }
            catch { }
        }

        var field = FindField(type, memberName);
        if (field != null)
        {
            try
            {
                if (TryCast<T>(field.GetValue(target), out var tVal)) return tVal;
            }
            catch { }
        }

        return defaultValue;
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
        if (raw != null && raw is IConvertible && typeof(IConvertible).IsAssignableFrom(typeof(T)))
        {
            try
            {
                value = (T)Convert.ChangeType(raw, typeof(T), CultureInfo.InvariantCulture);
                return true;
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

    /// <summary>인터롭 백킹 필드(_X_k__BackingField), 원래 백킹 필드(&lt;X&gt;k__BackingField), 흔한 명명 규칙 순으로 상속 계층까지 찾는다.</summary>
    private static FieldInfo FindField(Type type, string memberName) =>
        s_fieldCache.GetOrAdd((type, memberName), FindFieldUncached);

    private static FieldInfo FindFieldUncached((Type Type, string Name) key)
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
                if (field != null) return field;
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
