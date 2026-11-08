using System.Reflection;
using Il2CppInterop.Runtime;

namespace PLATiNALAB.Mod;

/// <summary>
/// 게임 버전 업데이트 및 컴파일러 백킹 필드/시그니처 변경에 대응하기 위한 업데이트 내성(Update Resilience) 유틸리티.
/// </summary>
internal static class UpdateResilience
{
    private static readonly Dictionary<(Type, string), FieldInfo> s_fieldCache = new();
    private static readonly Dictionary<(Type, string), PropertyInfo> s_propertyCache = new();

    /// <summary>
    /// 프로퍼티 또는 백킹 필드를 찾아 안전하게 값을 설정한다.
    /// 1. 일치하는 public/non-public 프로퍼티 setter 시도
    /// 2. 컴파일러 백킹 필드 형식들(_<name>_k__BackingField, <name>k__BackingField, _{name}, m_{name}, {name}) 순차 검색
    /// </summary>
    public static bool SafeSetFieldOrProperty(object target, string memberName, object value)
    {
        if (target == null) return false;
        var type = target.GetType();

        // 1. 프로퍼티 시도
        var propKey = (type, memberName);
        if (!s_propertyCache.TryGetValue(propKey, out var prop))
        {
            prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null) s_propertyCache[propKey] = prop;
        }

        if (prop != null && prop.CanWrite)
        {
            try
            {
                prop.SetValue(target, value);
                return true;
            }
            catch { }
        }

        // 2. 필드 시도 (다양한 백킹 필드 네이밍 규칙 순차 탐색)
        var fieldKey = (type, memberName);
        if (!s_fieldCache.TryGetValue(fieldKey, out var field))
        {
            var candidates = new[]
            {
                $"_{memberName}_k__BackingField",
                $"<{memberName}>k__BackingField",
                $"_{char.ToLowerInvariant(memberName[0])}{memberName[1..]}",
                $"_{memberName}",
                $"m_{memberName}",
                memberName
            };

            foreach (var name in candidates)
            {
                field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) break;
            }

            if (field != null) s_fieldCache[fieldKey] = field;
        }

        if (field != null)
        {
            try
            {
                field.SetValue(target, value);
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
        if (target == null) return defaultValue;
        var type = target.GetType();

        // 1. 프로퍼티 시도
        var propKey = (type, memberName);
        if (!s_propertyCache.TryGetValue(propKey, out var prop))
        {
            prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null) s_propertyCache[propKey] = prop;
        }

        if (prop != null && prop.CanRead)
        {
            try
            {
                var val = prop.GetValue(target);
                if (val is T tVal) return tVal;
            }
            catch { }
        }

        // 2. 필드 시도
        var fieldKey = (type, memberName);
        if (!s_fieldCache.TryGetValue(fieldKey, out var field))
        {
            var candidates = new[]
            {
                $"_{memberName}_k__BackingField",
                $"<{memberName}>k__BackingField",
                $"_{char.ToLowerInvariant(memberName[0])}{memberName[1..]}",
                $"_{memberName}",
                $"m_{memberName}",
                memberName
            };

            foreach (var name in candidates)
            {
                field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) break;
            }

            if (field != null) s_fieldCache[fieldKey] = field;
        }

        if (field != null)
        {
            try
            {
                var val = field.GetValue(target);
                if (val is T tVal) return tVal;
            }
            catch { }
        }

        return defaultValue;
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
